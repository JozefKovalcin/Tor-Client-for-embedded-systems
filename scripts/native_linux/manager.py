"""Linux Developer adapter. The executor still owns build validation and cancellation."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import threading
import time

from . import executor, packaging, preflight


RUN_ID = re.compile(r"run-[0-9a-f]{32}")
PIN_FILES = ("config/native-linux/v1.json", "config/dependencies/manifest-v1.json",
             "config/sources.lock.json", "windows-native/source-manifest.json",
             *(f"config/upstream/{name}.hash" for name in ("tor", "libopenssl", "libevent", "libzlib", "libseccomp")))


def read_ref(run, name):
    digest = packaging.stable_bytes(packaging.scoped_file(run, name), 65).decode("ascii").strip()
    if not preflight.SHA256.fullmatch(digest):
        raise ValueError("Invalid saved build result digest")
    return digest


def manifest_at(run):
    raw = packaging.stable_bytes(packaging.scoped_file(run, "build-manifest.json"), packaging.MAX_MANIFEST_BYTES)
    return hashlib.sha256(raw).hexdigest(), json.loads(raw)


def list_builds(workspace):
    rows = []
    for run in workspace.iterdir():
        if not RUN_ID.fullmatch(run.name):
            continue
        try:
            digest, manifest = manifest_at(run)
            status = manifest["status"]
            if status == "PASS" and read_ref(run, "manager-build.sha256") != digest:
                raise ValueError("Build record changed")
            smoke = run / "manager-smoke.sha256"
            rows.append({"id": run.name, "state": "BUILT" if status == "PASS" else status,
                         "startedAtUtc": manifest["startedAtUtc"],
                         "version": next(item["version"] for item in manifest["inputs"]["sources"] if item["name"] == "tor"),
                         "profile": "native-linux", "path": str(run / "prefix/bin/tor"),
                         "smokeState": "PASS" if smoke.exists() and read_ref(run, smoke.name) == digest else "NOT RUN",
                         "buildManifestSha256": digest, "outputs": manifest["outputs"]})
        except (OSError, ValueError, TypeError, KeyError, StopIteration):
            rows.append({"id": run.name, "state": "UNVERIFIED"})
    return {"builds": sorted(rows, key=lambda row: (row.get("startedAtUtc", ""), row["id"]), reverse=True)}


def smoke(run, digest, source, cancel):
    plan = packaging.package_plan(run, digest, root=source)
    pin = plan["files"][0]
    binary = packaging.stable_bytes(packaging.scoped_file(run, pin["sourceRef"]), packaging.MAX_ELF_BYTES)
    if hashlib.sha256(binary).hexdigest() != pin["sha256"]:
        raise ValueError("The verified Tor executable changed before launch")
    with tempfile.TemporaryDirectory(prefix="smoke-", dir=run) as scratch:
        private = Path(scratch)
        tor = private / "tor"
        tor.write_bytes(binary)
        tor.chmod(0o700)
        pid_file = private / "pid"
        config = private / "torrc"
        config.write_text("# Offline smoke defaults\n", encoding="ascii")
        environment = {"PATH": "/usr/bin:/bin", "HOME": str(private), "LC_ALL": "C"}
        common = [str(tor), "--defaults-torrc", str(config), "-f", str(config),
                  "--DataDirectory", str(private / "state"), "--PidFile", str(pid_file),
                  "--RunAsDaemon", "0", "--DisableNetwork", "1", "--SocksPort", "0"]
        version = subprocess.check_output([str(tor), "--version"], cwd=private, env=environment, timeout=10,
                                          text=True).splitlines()[0]
        subprocess.run([*common, "--verify-config"], cwd=private, env=environment, timeout=10, check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if cancel.is_set():
            raise InterruptedError("Offline launch cancelled")
        with subprocess.Popen(common, cwd=private, env=environment, stdout=subprocess.DEVNULL,
                              stderr=subprocess.DEVNULL) as process:
            try:
                deadline = time.monotonic() + 15
                while not pid_file.exists():
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise ValueError("Verified Tor did not start offline")
                    if cancel.wait(0.05):
                        raise InterruptedError("Offline launch cancelled")
                if pid_file.read_text().strip() != str(process.pid) or process.poll() is not None:
                    raise ValueError("Offline PID evidence differs from the owned process")
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        raise
            if process.returncode != 0:
                raise ValueError(f"Offline Tor exited with code {process.returncode}")
    marker = run / "manager-smoke.sha256"
    if marker.exists():
        if read_ref(run, marker.name) != digest:
            raise ValueError("Saved smoke result belongs to a different build")
    else:
        with marker.open("x", encoding="ascii") as file:
            file.write(digest + "\n")
    return {"version": version, "exitCode": 0, "status": "PASS"}


def operation(args, cancelled):
    source, workspace = Path(args.source), Path(args.workspace)
    if not source.is_absolute() or not workspace.is_absolute():
        raise ValueError("Use absolute native Linux paths")
    for name in PIN_FILES:
        path = packaging.scoped_file(source, name)
        if packaging.stable_bytes(path, 1024 * 1024) != (preflight.ROOT / name).read_bytes():
            raise ValueError("The selected checkout differs from the Manager's Linux policy or source pins")
    with executor._open_workspace_parent(workspace):
        if args.action == "list":
            return list_builds(workspace)
        if not RUN_ID.fullmatch(args.run):
            raise ValueError("Expected a Manager build ID")
        run = workspace / args.run
        if args.action == "scan":
            return executor.execution_plan(str(source), str(run), args.cache, root=source)
        if args.action == "build":
            result = executor.execute_build(str(source), str(run), args.cache, approved_input_sha256=args.approval,
                                            cancellation=cancelled, root=source,
                                            observe=lambda row: print(row["phase"] + " · " + row["state"], file=sys.stderr, flush=True))
            if result["status"] == "PASS":
                with (run / "manager-build.sha256").open("x", encoding="ascii") as output:
                    output.write(manifest_at(run)[0] + "\n")
            return {"status": result["status"], "path": str(run)}
        digest = read_ref(run, "manager-build.sha256")
        if args.action == "smoke":
            return smoke(run, digest, source, cancelled)
        if read_ref(run, "manager-smoke.sha256") != digest:
            raise ValueError("Run the offline smoke test on this exact build before exporting")
        return packaging.create_archive(run, digest, workspace / (args.run + "-tor.tar.gz"), root=source)["packageArtifact"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("scan", "build", "list", "smoke", "export"))
    for name in ("source", "workspace", "cache", "run", "approval"):
        parser.add_argument("--" + name, default="")
    args = parser.parse_args()
    cancelled = threading.Event()

    def watch_parent():
        os.read(sys.stdin.fileno(), 1)
        cancelled.set()

    threading.Thread(target=watch_parent, daemon=True).start()
    try:
        print(json.dumps(operation(args, cancelled), ensure_ascii=True, allow_nan=False))
        return 0
    except (OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError) as error:
        print(type(error).__name__ + ": " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
