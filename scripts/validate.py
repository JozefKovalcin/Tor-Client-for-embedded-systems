#!/usr/bin/env python3
"""One-command build, target tests, metrics, baseline comparison and reports."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import time
import uuid

from benchmark import collect, source_provenance
from bootstrap import source_tree
from common import (ROOT, SCHEMA, git_metadata, publish, read_json, resolve_result,
                    seal_baseline, sha256, utcnow, validate_result, write_json)
from compare import compare


class Blocked(Exception):
    pass


def check_host():
    if sys.platform != "linux":
        raise Blocked("Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner")
    if re.search(r"\s", str(ROOT)):
        raise Blocked("Buildroot does not support whitespace in source/output paths; clone into e.g. ~/embedded-tor")
    for program in ("bash", "make", "gcc", "g++", "bc", "bison", "flex", "perl", "tar",
                    "cpio", "rsync", "patch", "file", "unzip", "wget"):
        if not shutil.which(program):
            raise Blocked("missing build prerequisite: " + program)


def input_manifest():
    paths = []
    for folder in ("config", "br2-external", "scripts", "tests"):
        paths += [p for p in (ROOT / folder).rglob("*") if p.is_file() and "__pycache__" not in p.parts]
    return {str(p.relative_to(ROOT)).replace("\\", "/"): sha256(p) for p in sorted(paths)}


def snapshot_inputs(run, manifest):
    for name in manifest:
        path = Path(name)
        if (path.name in ("hs_ed25519_secret_key", "private_key", "control_auth_cookie", ".env")
                or path.suffix.lower() in (".pem", ".key", ".cookie", ".pfx", ".p12")):
            raise ValueError("secret-like build input refused: " + name)
    with tarfile.open(run / "source-inputs.tar", "x") as archive:
        for name in sorted(manifest):
            archive.add(ROOT / name, arcname=name, recursive=False)


def run_logged(command, path, env=None):
    print("Running: " + " ".join(str(x) for x in command), flush=True)
    with path.open("w", encoding="utf-8") as f:
        f.write("Command: " + json.dumps([str(x) for x in command]) + "\n")
        f.flush()
        subprocess.run(command, cwd=ROOT, env=env, stdout=f, stderr=subprocess.STDOUT, check=True)


def check_resolved_config(requested, resolved):
    text = resolved.read_text()
    for line in requested.read_text().splitlines():
        if line.startswith("BR2_") and "=" in line:
            key, value = line.split("=", 1)
            expanded = value.replace("$(BR2_EXTERNAL_EMBEDDED_TOR_PATH)", str(ROOT / "br2-external"))
            if not any(f"{key}={v}" in text.splitlines() for v in (value, expanded)):
                raise ValueError("Kconfig dropped/changed requested setting: " + key)
        elif line.startswith("# BR2_") and line.endswith(" is not set"):
            key = line.split()[1]
            if re.search(r"^" + re.escape(key) + r"=", text, re.M):
                raise ValueError("Kconfig enabled a disabled setting: " + key)


def emulation(output, profile, mode, run):
    emulator = shutil.which(profile["qemu"])
    if not emulator:
        raise Blocked("missing " + profile["qemu"] + "; install the QEMU system package")
    environment = dict(profile["environment"])
    environment.update({"qemu_version": subprocess.check_output([emulator, "--version"], text=True).splitlines()[0],
                        "host_platform": platform.platform(),
                        "host_cpu_sha256": sha256(Path("/proc/cpuinfo")),
                        "host_memory_bytes": os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES"),
                        "test_mode": mode,
                        "timeouts": {k: profile[k] for k in ("bootstrap_timeout_seconds", "connection_timeout_seconds", "idle_seconds")}})
    # Dynamic MHz and bogomips must not make otherwise identical hosts incomparable.
    cpu = Path("/proc/cpuinfo").read_text()
    cpu = "\n".join(line for line in cpu.splitlines() if not line.startswith(("cpu MHz", "bogomips")))
    environment["host_cpu_sha256"] = hashlib.sha256(cpu.encode()).hexdigest()
    images = output / "images"
    command = [emulator, "-machine", environment["machine"], "-cpu", environment["cpu"],
               "-accel", environment["accelerator"], "-m", str(environment["memory_mib"]),
               "-smp", str(environment["cpus"]), "-nographic", "-monitor", "none", "-no-reboot",
               "-kernel", str(images / profile["artifacts"]["kernel"]),
               "-drive", f"file={images / profile['artifacts']['rootfs']},if=virtio,format=raw,snapshot=on",
               "-append", "root=/dev/vda rw console=ttyS0 tor_validate=" + mode +
               f" tor_bootstrap_timeout={profile['bootstrap_timeout_seconds']}" +
               f" tor_connection_timeout={profile['connection_timeout_seconds']}" +
               f" tor_idle_seconds={profile['idle_seconds']}",
               "-netdev", "user,id=net0,restrict=" + ("on" if mode == "offline" else "off"),
               "-device", "virtio-net-pci,netdev=net0", "-device", "virtio-rng-pci"]
    write_json(run / "qemu-command.json", command)
    log = run / "qemu.log"
    with log.open("wb") as f:
        process = subprocess.Popen(command, stdout=f, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + profile["timeout_seconds"]
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    break
                # Read only tail to avoid unbounded log processing.
                with log.open("rb") as reader:
                    reader.seek(max(0, log.stat().st_size - 128))
                    if b"TOR_VALIDATION_END" in reader.read():
                        break
                time.sleep(1)
            else:
                raise Blocked("QEMU boot/integration deadline exceeded; serial log preserved")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
    data = log.read_text(encoding="utf-8", errors="replace")
    match = re.search(r"TOR_VALIDATION_JSON=(\{[^\r\n]+\})\r?\nTOR_VALIDATION_END", data)
    if not match:
        raise Blocked("target produced no complete test result; see QEMU log")
    result = json.loads(match.group(1))
    write_json(run / "target-tests.json", result)
    return result, environment


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", default="qemu-x86_64")
    parser.add_argument("--mode", choices=("offline", "online"), default="online")
    parser.add_argument("--change", default="Configuration validation")
    parser.add_argument("--record-baseline", action="store_true", help="seal first fully validated clean build; never replace")
    parser.add_argument("--checks-only", action="store_true", help="run fast checks and report INCOMPLETE; does not accept builds")
    parser.add_argument("--jobs", type=int, default=min(8, os.cpu_count() or 1))
    parser.add_argument("--resume-run", help="resume a compatible completed build after a host reporting fix")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-zA-Z0-9_-]+", args.profile) or args.jobs < 1:
        parser.error("invalid profile or jobs")
    run_id = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + uuid.uuid4().hex[:8]
    run = ROOT / "reports/benchmarks" / run_id
    run.mkdir(parents=True)
    try:
        policy = read_json(ROOT / "config/policy.json")
        for key in ("required_tests", "required_metrics", "maximum_regression_percent", "higher_is_better"):
            if key not in policy:
                raise ValueError("missing policy field: " + key)
    except (OSError, ValueError, TypeError) as exc:
        result = {"schema_version": SCHEMA, "run_id": run_id, "timestamp": utcnow(), "change": args.change,
                  "build": {"status": "FAIL", "detail": "invalid validation policy: " + str(exc)},
                  "tests": [{"id": "policy_validation", "mode": "offline", "status": "FAIL", "detail": str(exc)}],
                  "metrics": {}, "environment": {}, "metadata": {"git": git_metadata(), "configuration_id": args.profile}}
        publish(result, {"status": "INCOMPLETE", "reasons": [result["build"]["detail"]], "differences": []}, ROOT / "reports")
        return 1
    manifest = input_manifest()
    write_json(run / "inputs.json", manifest)
    input_sha = sha256(run / "inputs.json")
    result = {"schema_version": SCHEMA, "run_id": run_id, "timestamp": utcnow(), "change": args.change,
              "build": {"status": "BLOCKED", "detail": "build not started"},
              "tests": [], "metrics": {name: None for name in policy["required_metrics"]},
              "environment": {}, "metadata": {"git": git_metadata(), "configuration_id": args.profile,
                  "input_sha256": input_sha, "policy_sha256": sha256(ROOT / "config/policy.json")}}
    comparison = None
    try:
        profile = read_json(ROOT / "config/profiles" / (args.profile + ".json"))
        lock = read_json(ROOT / "config/sources.lock.json")
        snapshot_inputs(run, manifest)
        result["metadata"].update({"architecture": profile["architecture"],
                                  "buildroot_version": lock["buildroot"]["version"],
                                  "tor_version": lock["tor_version"], "kernel_version": lock["kernel_version"]})
        run_logged([sys.executable, str(ROOT / "scripts/check.py")], run / "checks.log")
        result["tests"].append({"id": "repository_checks", "mode": "offline", "status": "PASS", "detail": "see checks.log"})
        if args.checks_only:
            raise Blocked("checks-only run; image and target functionality were not tested")
        check_host()
        if args.record_baseline and (ROOT / "reports/baseline").exists():
            raise Blocked("baseline already exists; refusing replacement")
        if args.record_baseline:
            if result["metadata"]["git"].get("dirty"):
                raise Blocked("baseline requires a clean committed source tree")
            tag = subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--verify", "refs/tags/baseline"],
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if tag.returncode == 0 or (ROOT / "build/baseline").exists():
                raise Blocked("baseline tag or preserved image directory exists; refusing replacement")
        source = source_tree()
        # A change to ANY versioned build/test input gets a new output directory.
        # No make clean, deletion, or reuse across incompatible toolchains.
        output = ROOT / "build/embedded" / args.profile / input_sha[:20]
        if args.resume_run:
            if not re.fullmatch(r"[A-Za-z0-9_-]+", args.resume_run):
                raise ValueError("invalid resume run identifier")
            previous_run = ROOT / "reports/benchmarks" / args.resume_run
            previous = read_json(previous_run / "results.json")
            old_inputs = read_json(previous_run / "inputs.json")
            # These host-only files cannot change the target build inputs.
            allowed = {"scripts/validate.py", "tests/test_pipeline.py"}
            changed = {name for name in set(old_inputs) | set(manifest)
                       if old_inputs.get(name) != manifest.get(name)}
            if changed - allowed:
                raise ValueError("resume refused: target inputs changed: " + ", ".join(sorted(changed - allowed)))
            output = (ROOT / previous["build"]["output_directory"]).resolve()
            if not output.is_relative_to((ROOT / "build/embedded" / args.profile).resolve()) or not (output / ".config").is_file():
                raise ValueError("resume output is missing or outside the selected profile")
            result["metadata"]["resumed_build_from"] = {
                "run_id": args.resume_run, "input_sha256": previous["metadata"]["input_sha256"],
                "git": previous["metadata"]["git"], "host_only_changes": sorted(changed)}
        output.mkdir(parents=True, exist_ok=True)
        result["build"]["output_directory"] = str(output.relative_to(ROOT))
        env = dict(os.environ, SOURCE_DATE_EPOCH=str(lock["source_date_epoch"]), TZ="UTC", LC_ALL="C", LANG="C")
        env.pop("BR2_DL_DIR", None)
        downloads = ROOT / "downloads/packages"
        base = ["make", "--no-print-directory", "-C", str(source), "O=" + str(output),
                "BR2_EXTERNAL=" + str(ROOT / "br2-external"), "BR2_DL_DIR=" + str(downloads)]
        config = ROOT / "br2-external/configs" / profile["defconfig"]
        run_logged([*base, profile["defconfig"]], run / "configure.log", env)
        check_resolved_config(config, output / ".config")
        shutil.copy2(output / ".config", run / "buildroot.config")
        started = time.monotonic()
        result["build"]["status"] = "FAIL"
        run_logged([*base, "-j" + str(args.jobs), "all"], run / "build.log", env)
        result["build"]["seconds"] = time.monotonic() - started
        result["metrics"].update(collect(output, profile, run))
        result["metadata"]["source_provenance_sha256"] = source_provenance(output, source, downloads, run)
        target_name = subprocess.check_output([*base, "-s", "printvars", "VARS=GNU_TARGET_NAME"], env=env, text=True).strip()
        if not target_name.startswith("GNU_TARGET_NAME="):
            raise ValueError("cannot identify configured target compiler")
        compiler = output / "host/bin" / (target_name.split("=", 1)[1] + "-gcc")
        if not compiler.is_file():
            raise ValueError("configured target compiler is missing")
        result["metadata"]["compiler"] = subprocess.check_output([str(compiler), "--version"], text=True).splitlines()[0]
        vars_cmd = [*base, "-s", "printvars", "VARS=TARGET_CFLAGS TARGET_LDFLAGS TOR_CONF_OPTS GNU_TARGET_NAME", "QUOTED_VARS=YES"]
        flags = subprocess.check_output(vars_cmd, env=env, text=True)
        result["metadata"]["compiler_flags"] = flags.strip()
        (run / "compiler-flags.txt").write_text(flags, encoding="utf-8")
        for kind in ("linux-*", "busybox-*"):
            configs = list((output / "build").glob(kind + "/.config"))
            if len(configs) != 1:
                raise ValueError("cannot identify resolved config: " + kind)
            shutil.copy2(configs[0], run / (kind.replace("-*", "") + ".config"))
        readelf = compiler.with_name(compiler.name[:-3] + "readelf")
        elf = subprocess.check_output([str(readelf), "-hlWd", str(output / "target/usr/bin/tor")], text=True)
        (run / "tor-elf.txt").write_text(elf, encoding="utf-8")
        if not (re.search(r"Type:\s+DYN", elf) and "GNU_RELRO" in elf and "BIND_NOW" in elf):
            raise ValueError("target Tor ELF is missing required PIE/RELRO/NOW hardening")
        stack = next((line for line in elf.splitlines() if "GNU_STACK" in line), "")
        if not stack or "RWE" in stack:
            raise ValueError("target Tor ELF has missing/unsafe stack permissions")
        result["tests"].append({"id": "elf_hardening", "mode": "offline", "status": "PASS", "detail": "PIE, RELRO, NOW and non-executable stack observed"})
        result["build"].update(status="PASS", detail="make all succeeded; all required boot artifacts and ELF hardening verified")
        target, environment = emulation(output, profile, args.mode, run)
        if not re.search(r"\b" + re.escape(lock["tor_version"]) + r"\b", target.get("tor_version_output", "")):
            raise ValueError("target Tor version differs from pinned version")
        if target.get("kernel_version") != lock["kernel_version"] or target.get("architecture") != profile["architecture"]:
            raise ValueError("booted target kernel or architecture differs from selected profile")
        result["tests"].extend(target["tests"])
        result["metrics"].update({k: v for k, v in target["metrics"].items() if not isinstance(v, dict)})
        result["environment"] = environment
        if input_manifest() != manifest:
            raise ValueError("source/configuration inputs changed during validation; result cannot be accepted")
        if args.record_baseline:
            problems = validate_result(result, policy)
            if problems:
                raise Blocked("baseline refused: " + "; ".join(problems))
            baseline_images = ROOT / "build/baseline"
            baseline_images.mkdir(exist_ok=False)
            preserved = []
            for role, name in profile["artifacts"].items():
                src = output / "images" / name
                dest = baseline_images / name
                with src.open("rb") as source_file, dest.open("xb") as dest_file:
                    shutil.copyfileobj(source_file, dest_file)
                if sha256(dest) != sha256(src):
                    raise ValueError("baseline image copy checksum mismatch")
                dest.chmod(0o444)
                preserved.append({"role": role, "path": str(dest.relative_to(ROOT)),
                                  "sha256": sha256(dest), "bytes": dest.stat().st_size})
            write_json(baseline_images / "artifacts.json", preserved)
            result["metadata"]["baseline_artifacts"] = preserved
            seal_baseline(result, policy=policy)
            subprocess.run(["git", "-C", str(ROOT), "tag", "baseline", result["metadata"]["git"]["commit"]], check=True)
            comparison = {"status": "BASELINE", "reasons": ["first fully validated build sealed; no optimization claim"], "differences": []}
    except Blocked as exc:
        if result["build"]["status"] != "PASS":
            result["build"]["status"] = "BLOCKED"
        result["build"]["detail"] = str(exc)
    except (Exception, KeyboardInterrupt) as exc:
        result["build"].update(status="FAIL", detail=type(exc).__name__ + ": " + str(exc))
        (run / "failure.txt").write_text(result["build"]["detail"] + "\n", encoding="utf-8")
    for name in policy["required_tests"]:
        if not any(row["id"] == name for row in result["tests"]):
            result["tests"].append({"id": name, "mode": "online" if name in
                                   ("socks5", "public_bootstrap", "onion_v3", "two_node", "bidirectional_integrity", "reconnection") else "offline",
                                   "status": "BLOCKED", "detail": result["build"]["detail"]})
    if comparison is None:
        try:
            comparison = compare(resolve_result("baseline"), result, policy, sha256(ROOT / "config/policy.json"))
        except (OSError, ValueError) as exc:
            comparison = {"status": "INCOMPLETE", "reasons": ["no usable immutable baseline: " + str(exc),
                          *validate_result(result, policy)], "differences": []}
    publish(result, comparison, ROOT / "reports")
    print(f"{comparison['status']}: reports/latest.md (run {run_id})", flush=True)
    return 0 if comparison["status"] in ("ACCEPTED", "BASELINE") else 1


if __name__ == "__main__":
    # OS-managed advisory lock is released on crashes. Never remove user files
    # or allow concurrent writes to the same output directory/history.
    lock_path = ROOT / "build/validation.lock"
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    with lock_path.open("a+b") as lock_file:
        try:
            if os.name == "nt":
                import msvcrt
                lock_file.seek(0)
                if not lock_file.read(1):
                    lock_file.write(b"0")
                    lock_file.flush()
                lock_file.seek(0)
                msvcrt.locking(lock_file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(lock_file, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            print("Another validation owns this workspace; no files were changed", file=sys.stderr)
            raise SystemExit(2)
        raise SystemExit(main())
