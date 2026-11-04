"""Shared result schema, immutable baseline storage and report helpers (stdlib only)."""
import csv
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = 1


def utcnow():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    data = json.dumps(value, indent=2, sort_keys=True, allow_nan=False) + "\n"
    fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=".writing-")
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
            f.write(data)
        os.replace(tmp, path)
    finally:
        if Path(tmp).exists():
            Path(tmp).unlink()


def git_metadata():
    def git(*args):
        return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True, stderr=subprocess.DEVNULL).strip()
    try:
        return {"commit": git("rev-parse", "HEAD"),
                "dirty": bool(git("status", "--porcelain", "--untracked-files=normal")),
                "diff_sha256": hashlib.sha256(git("diff", "HEAD", "--", ".").encode()).hexdigest()}
    except (OSError, subprocess.CalledProcessError):
        return {"commit": None, "dirty": True, "diff_sha256": None}


def validate_result(result, policy):
    problems = []
    if result.get("schema_version") != SCHEMA:
        problems.append("unsupported result schema")
    if result.get("build", {}).get("status") != "PASS":
        problems.append("complete build did not pass")
    rows = result.get("tests", [])
    tests = {}
    for row in rows:
        name = row.get("id")
        if name in tests:
            problems.append("duplicate test: " + str(name))
        tests[name] = row
        if row.get("status") not in ("PASS", "FAIL", "SKIPPED", "BLOCKED"):
            problems.append("invalid test status: " + str(name))
        if row.get("status") == "FAIL":
            problems.append("failed test: " + str(name))
    for name in policy["required_tests"]:
        if tests.get(name, {}).get("status") != "PASS":
            problems.append("mandatory test not passed: " + name)
    for name in policy["required_metrics"]:
        value = result.get("metrics", {}).get(name)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            problems.append("missing/invalid measurement: " + name)
    meta = result.get("metadata", {})
    for field in ("architecture", "buildroot_version", "tor_version", "kernel_version",
                  "compiler", "compiler_flags", "configuration_id", "source_provenance_sha256",
                  "input_sha256", "policy_sha256"):
        if not meta.get(field):
            problems.append("missing provenance: " + field)
    if not meta.get("git", {}).get("commit"):
        problems.append("missing Git commit")
    if not result.get("environment"):
        problems.append("missing measurement environment")
    return problems


def resolve_result(name):
    path = ROOT / "reports/baseline" if str(name) == "baseline" else Path(name)
    if path.is_dir():
        path = path / "results.json"
    result = read_json(path)
    if path.parent.name == "baseline":
        expected = (path.parent / "results.sha256").read_text().strip()
        if sha256(path) != expected:
            raise ValueError("baseline integrity check failed")
    return result


def seal_baseline(result, directory=None, policy=None):
    policy = policy or read_json(ROOT / "config/policy.json")
    problems = validate_result(result, policy)
    if result.get("metadata", {}).get("git", {}).get("dirty"):
        problems.append("baseline requires a clean committed source tree")
    if problems:
        raise ValueError("baseline refused: " + "; ".join(problems))
    path = Path(directory) if directory else ROOT / "reports/baseline"
    # Exclusive directory creation: even a partial or corrupt baseline is never replaced.
    path.mkdir(parents=True, exist_ok=False)
    write_json(path / "results.json", result)
    with (path / "results.sha256").open("x", encoding="utf-8") as f:
        f.write(sha256(path / "results.json") + "\n")
    for child in path.iterdir():
        child.chmod(0o444)


def write_csv(path, rows, fields):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def publish(result, comparison, reports=None):
    reports = Path(reports) if reports else ROOT / "reports"
    reports.mkdir(parents=True, exist_ok=True)
    run = reports / "benchmarks" / result["run_id"]
    run.mkdir(parents=True, exist_ok=True)
    write_json(run / "results.json", result)
    write_json(run / "comparison.json", comparison)
    write_csv(run / "metrics.csv", [dict(metric=k, value=v, run_id=result["run_id"],
                                           timestamp=result["timestamp"],
                                           commit=result["metadata"].get("git", {}).get("commit"))
                                      for k, v in result["metrics"].items()],
              ["run_id", "timestamp", "commit", "metric", "value"])
    write_csv(run / "tests.csv", result["tests"], ["id", "mode", "status", "detail", "seconds"])
    write_json(reports / "test_results.json", {"run_id": result["run_id"], "tests": result["tests"]})
    status = comparison["status"]
    summary = ["# Optimization validation", "", f"Run: `{result['run_id']}`", "",
               f"**{status}** — {result['change']}", "",
               f"Build: {result['build']['status']}. {result['build'].get('detail', '')}", "",
               f"Commit: `{result['metadata'].get('git', {}).get('commit')}`; "
               f"dirty: `{result['metadata'].get('git', {}).get('dirty')}`.", "",
               "Hardware behavior: unverified. Measurements describe only the recorded test environment.", "",
               "## Test results", "", "| Test | Mode | Status | Detail |", "|---|---|---|---|"]
    for t in result["tests"]:
        detail = str(t.get("detail", "")).replace("|", "\\|").replace("\n", " ")
        summary.append(f"| {t['id']} | {t['mode']} | {t['status']} | {detail} |")
    (reports / "test_summary.md").write_text("\n".join(summary) + "\n", encoding="utf-8")
    summary += ["", "## Measurements", "", "Null means unavailable; bytes use base 1024 for KiB conversions.", "",
                "| Metric | Candidate | Baseline | Delta | Reduction % |", "|---|---:|---:|---:|---:|"]
    diffs = {d["metric"]: d for d in comparison.get("differences", [])}
    for name, value in sorted(result["metrics"].items()):
        d = diffs.get(name, {})
        summary.append(f"| {name} | {value} | {d.get('baseline')} | {d.get('delta')} | {d.get('reduction_percent')} |")
    summary += ["", "## Decision", ""] + ["- " + reason for reason in comparison.get("reasons", [])]
    summary += ["", "Positive reduction means lower resource use. Throughput improvement has the opposite sign.",
                "Public Tor timing varies with network conditions; one run does not establish a causal speedup.",
                "Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.",
                "Logs and complete provenance are in the run directory. Private runtime state is never exported."]
    content = "\n".join(summary) + "\n"
    (run / "report.md").write_text(content, encoding="utf-8")
    (reports / "latest.md").write_text(content, encoding="utf-8")
    history = reports / "optimization-history.csv"
    fields = ["timestamp", "run_id", "commit", "configuration_id", "change", "status", "build_status",
              "tor_executable_bytes", "complete_image_bytes", "idle_rss_bytes", "peak_process_rss_bytes"]
    row = {**{k: result.get(k) for k in ("timestamp", "run_id", "change")},
           "commit": result["metadata"].get("git", {}).get("commit"),
           "configuration_id": result["metadata"].get("configuration_id"),
           "status": status, "build_status": result["build"]["status"], **result["metrics"]}
    exists = history.exists() and history.stat().st_size > 0
    with history.open("a", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fields, extrasaction="ignore")
        if not exists:
            writer.writeheader()
        writer.writerow(row)
