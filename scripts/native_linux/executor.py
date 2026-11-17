"""Opt-in native Linux source executor; planning is read-only by default.

No downloader, installer, firmware builder or Tor runtime invocation exists here.
The only accepted recipe is compiled in below; W3 reports are freshly collected,
never accepted from a caller as authorization. Real compilation needs a separate
operator approval bound to executionInputSha256.
"""
import argparse
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import signal
import stat
import subprocess
import sys
import tarfile
import threading
import time
import uuid

from scripts.contracts import require_valid
from . import preflight as native


ADAPTER_ID = "native-linux-source-executor-v1"
CFLAGS = "-O2 -fPIC -fstack-protector-strong"
CPPFLAGS = "-D_FORTIFY_SOURCE=3"
LDFLAGS = "-Wl,-z,relro,-z,now,-z,noexecstack"
SOURCE_ORDER = ("zlib", "openssl", "libevent", "libseccomp", "tor")
MAX_MEMBERS = 200000
MAX_FILE_BYTES = 512 * 1024 * 1024
MAX_LOG_BYTES = 16 * 1024 * 1024
COMMAND_TIMEOUT = 1800
BUILD_TIMEOUT = 4 * 3600


class BuildBlocked(ValueError):
    """An execution prerequisite or the exact approval is absent."""


def utc_now():
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def write_json(path, value):
    """Write only inside a newly owned run, retaining failed evidence."""
    temporary = path.with_suffix(path.suffix + ".new")
    with temporary.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, sort_keys=True, indent=2, allow_nan=False)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def _tool_hashes(plan):
    hashes = {}
    for name, observation in plan["toolchainManifest"].items():
        if name == "gcc-target":
            continue
        path = Path(observation.get("path") or "")
        if observation.get("status") != "AVAILABLE" or not path.is_absolute():
            raise BuildBlocked("required classified tool has no executable path: " + name)
        resolved = path.resolve(strict=True)
        if not stat.S_ISREG(resolved.stat().st_mode):
            raise BuildBlocked("tool is not a regular file: " + name)
        hashes[name] = {"path": str(resolved), "sha256": native.hash_file(resolved)}
    return hashes


def execution_plan(source_dir, output_dir, archive_cache, *, root=native.ROOT):
    """Fresh W3 environment/source inspection plus the fixed Linux recipe hash."""
    plan = native.preflight(source_dir, output_dir, archive_cache, root=root)
    if not plan["readyForBuildPlan"]:
        return {"schemaVersion": 1, "kind": ADAPTER_ID, "executionStatus": "NOT RUN",
                "readyForExecution": False, "preflight": plan,
                "executionInputSha256": None, "buildRequest": None, "approvalRequired": True}
    require_valid("BuildRequest", plan["buildRequest"])
    policy = native._load_json(Path(root) / "config/native-linux/v1.json")
    pins, _, _ = native.load_source_pins(root, policy)
    tools = _tool_hashes(plan)
    inputs = {"preflightInputSha256": plan["inputSha256"], "recipeVersion": 1,
              "adapterSha256": native.hash_file(Path(__file__)), "tools": tools,
              "sources": pins, "sourceOrder": list(SOURCE_ORDER), "patches": [],
              "flags": {"CFLAGS": CFLAGS, "CPPFLAGS": CPPFLAGS, "LDFLAGS": LDFLAGS},
              "maxJobs": policy["maxJobs"], "sourceDateEpoch": plan["inputMetadata"]["sourceDateEpoch"]}
    digest = native.canonical_hash(inputs)
    request = dict(plan["buildRequest"], mode="build", adapterId=ADAPTER_ID,
                   requestId="native-build-request-" + digest[:20], inputSha256=digest,
                   toolchainManifestSha256=native.canonical_hash(tools))
    require_valid("BuildRequest", request)
    return {"schemaVersion": 1, "kind": ADAPTER_ID, "executionStatus": "NOT RUN",
            "readyForExecution": True, "approvalRequired": True,
            "executionInputSha256": digest, "inputs": inputs, "buildRequest": request,
            "preflight": plan}


def _verify_tools(tools):
    for name, item in tools.items():
        if native.hash_file(item["path"]) != item["sha256"]:
            raise BuildBlocked("tool bytes changed since approval: " + name)


def _same_file(metadata):
    value = (metadata.st_dev, metadata.st_ino, metadata.st_mode, metadata.st_size,
             metadata.st_mtime_ns)
    return value + ((metadata.st_ctime_ns,) if os.name == "posix" else ())


def snapshot_source(cache, snapshots, pin):
    """Copy one descriptor into a private immutable-by-policy verified snapshot."""
    name = pin["archive"]
    if PurePosixPath(name).name != name or "\\" in name or name in ("", ".", ".."):
        raise ValueError("unsafe source archive name")
    source_path, destination = Path(cache) / name, Path(snapshots) / name
    before = source_path.stat(follow_symlinks=False)
    if not stat.S_ISREG(before.st_mode):
        raise ValueError("source must be a regular file, never a symlink")
    if before.st_size <= 0 or before.st_size > MAX_FILE_BYTES:
        raise ValueError("compressed source snapshot byte budget exceeded")
    fd = os.open(source_path, os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0))
    digest = hashlib.sha256()
    with os.fdopen(fd, "rb", buffering=0) as source, destination.open("xb") as target:
        opened = os.fstat(source.fileno())
        if _same_file(opened) != _same_file(before):
            raise ValueError("source replaced before snapshot")
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
            target.write(block)
        if (_same_file(os.fstat(source.fileno())) != _same_file(opened) or
                _same_file(source_path.stat(follow_symlinks=False)) != _same_file(before)):
            raise ValueError("source changed during snapshot")
    if digest.hexdigest() != pin["sha256"] or native.hash_file(destination) != pin["sha256"]:
        raise ValueError("snapshot SHA-256 differs from pinned archive")
    destination.chmod(0o400)
    return destination


def extract_snapshot(snapshot, source_root, pin, *, byte_budget):
    """Extract only unique regular files/directories under the pinned top directory.

    No tarfile.extract API, links, devices, ownership or archive permissions are
    trusted. Rejecting links is intentionally conservative for an unqualified
    source release. A failed run is retained rather than cleaned or reused.
    """
    if native.hash_file(snapshot) != pin["sha256"]:
        raise ValueError("snapshot changed before extraction")
    seen, total = set(), 0
    root = Path(source_root).resolve(strict=True)
    with Path(snapshot).open("rb", buffering=0) as stream:
        with tarfile.open(fileobj=stream, mode="r:*") as archive:
            for member in archive:
                if len(seen) >= MAX_MEMBERS:
                    raise ValueError("archive member budget exceeded")
                path = PurePosixPath(member.name)
                if (not member.name or member.name.startswith("/") or "\\" in member.name or
                        ":" in member.name or "\x00" in member.name or
                        any(part in ("", ".", "..") for part in member.name.rstrip("/").split("/")) or
                        path.parts[0] != pin["directory"] or member.name.rstrip("/") in seen):
                    raise ValueError("unsafe, duplicate or unexpected archive member")
                seen.add(member.name.rstrip("/"))
                destination = root.joinpath(*path.parts)
                if root not in destination.parents:
                    raise ValueError("archive escape")
                if not member.isdir() and not member.isfile():
                    raise ValueError("archive links and special files refused")
                if member.isdir():
                    destination.mkdir(mode=0o700, parents=True, exist_ok=True)
                    continue
                total += member.size
                if member.size < 0 or member.size > MAX_FILE_BYTES or total > byte_budget:
                    raise ValueError("expanded source byte budget exceeded")
                destination.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                content = archive.extractfile(member)
                if content is None:
                    raise ValueError("unreadable source member")
                copied = 0
                with content, destination.open("xb") as target:
                    for block in iter(lambda: content.read(1024 * 1024), b""):
                        copied += len(block)
                        if copied > member.size:
                            raise ValueError("member size mismatch")
                        target.write(block)
                if copied != member.size:
                    raise ValueError("truncated member")
                destination.chmod(0o700 if member.mode & 0o111 else 0o600)
        if native._hash_stream(stream) != pin["sha256"]:
            raise ValueError("snapshot changed during extraction")
    directory = root / pin["directory"]
    license_path = directory / pin["license_file"]
    if native.hash_file(license_path) != pin["license_sha256"]:
        raise ValueError("extracted license differs from pin")
    return directory, total


@dataclass(frozen=True)
class Command:
    name: str
    argv: tuple
    cwd: str
    environment: dict


def build_commands(plan, output):
    """Fixed source-built, private-prefix recipe; no supplied shell fragments."""
    inputs = plan["inputs"]
    pins = {pin["name"]: pin for pin in inputs["sources"]}
    if set(pins) != set(SOURCE_ORDER):
        raise ValueError("unsupported native source set")
    tools = inputs["tools"]
    output = Path(output)
    prefix = output / "prefix"
    env = {"PATH": "/usr/bin:/bin", "LANG": "C", "LC_ALL": "C", "TZ": "UTC",
           "HOME": str(output / "home"), "TMPDIR": str(output / "tmp"),
           "SOURCE_DATE_EPOCH": str(inputs["sourceDateEpoch"]),
           "CFLAGS": CFLAGS, "CPPFLAGS": CPPFLAGS + " -I" + str(prefix / "include"),
           "LDFLAGS": LDFLAGS + " -L" + str(prefix / "lib"),
           "PKG_CONFIG_LIBDIR": str(prefix / "lib/pkgconfig"), "PKG_CONFIG_PATH": "",
           "CONFIG_SITE": "/dev/null", "MAKEFLAGS": "", "MFLAGS": ""}
    for name, variable in (("gcc", "CC"), ("ar", "AR"), ("ranlib", "RANLIB"),
                           ("pkg-config", "PKG_CONFIG"), ("perl", "PERL")):
        env[variable] = tools[name]["path"]
    make, bash, perl = [tools[name]["path"] for name in ("make", "bash", "perl")]
    prefix_arg = "--prefix=" + str(prefix)
    recipes = {
        "zlib": (bash, "./configure", prefix_arg, "--static"),
        "openssl": (perl, "./Configure", "linux-x86_64", "no-shared", "no-tests", prefix_arg, "--libdir=lib"),
        "libevent": (bash, "./configure", prefix_arg, "--disable-shared", "--enable-static",
                     "--disable-samples", "--disable-libevent-regress"),
        "libseccomp": (bash, "./configure", prefix_arg, "--disable-shared", "--enable-static"),
        "tor": (bash, "./configure", prefix_arg, "--with-libevent-dir=" + str(prefix),
                "--with-openssl-dir=" + str(prefix), "--with-zlib-dir=" + str(prefix),
                "--enable-static-libevent", "--enable-static-openssl", "--enable-static-zlib",
                "--enable-gcc-hardening", "--enable-linker-hardening", "--enable-pic", "--enable-seccomp",
                "--disable-module-relay", "--disable-module-dirauth", "--disable-asciidoc",
                "--disable-systemd", "--disable-lzma", "--disable-zstd", "--disable-libscrypt")}
    commands = []
    for name in SOURCE_ORDER:
        cwd = str(output / "sources" / pins[name]["directory"])
        environment = dict(env)
        if name == "tor":
            environment["CFLAGS"] += " -fPIE"
            environment["LDFLAGS"] += " -pie"
        commands.append(Command(name + "-configure", recipes[name], cwd, environment))
        commands.append(Command(name + "-make", (make, "-j" + str(inputs["maxJobs"])), cwd, environment))
        commands.append(Command(name + "-install", (make, "-j1", "install_sw" if name == "openssl" else "install"), cwd, environment))
    return commands


class ProcessRunner:
    """Bounded output/deadline and POSIX process-group teardown, no shell."""
    def __init__(self, *, timeout=COMMAND_TIMEOUT, max_log_bytes=MAX_LOG_BYTES, grace_seconds=2):
        self.timeout, self.max_log_bytes, self.grace_seconds = timeout, max_log_bytes, grace_seconds

    def run(self, command, log_path, cancellation):
        # Claim the log before spawning: denied/existing logs cannot orphan a
        # child. All subsequent failure paths converge on process teardown.
        process, reader, reason, error_type = None, None, None, None
        overflow, read_error = threading.Event(), []
        with Path(log_path).open("xb") as log:
            try:
                process = subprocess.Popen(list(command.argv), cwd=command.cwd, env=command.environment,
                                           stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                           stderr=subprocess.STDOUT, shell=False,
                                           start_new_session=os.name == "posix")
                def copy_output():
                    try:
                        count = 0
                        for block in iter(lambda: process.stdout.read(4096), b""):
                            remaining = self.max_log_bytes - count
                            if remaining > 0:
                                log.write(block[:remaining])
                                count += min(remaining, len(block))
                            if len(block) > remaining:
                                overflow.set()
                    except OSError as exc:
                        read_error.append(type(exc).__name__)
                reader = threading.Thread(target=copy_output, daemon=True)
                reader.start()
                started = time.monotonic()
                while process.poll() is None:
                    if cancellation.is_set() or overflow.is_set() or read_error or time.monotonic() - started >= self.timeout:
                        reason = ("cancelled" if cancellation.is_set() else "log-budget" if overflow.is_set()
                                  else "log-write-failure" if read_error else "timeout")
                        break
                    time.sleep(0.02)
            except (OSError, RuntimeError) as exc:
                reason, error_type = "runner-error", type(exc).__name__
            finally:
                if process is not None:
                    # Also terminate descendants retaining stdout after normal
                    # parent exit; the Linux adapter owns this process group.
                    self._stop(process)
                    if reader is not None and reader.ident is not None:
                        reader.join(self.grace_seconds + 1)
                    if reader is not None and reader.is_alive():
                        reason, error_type = "runner-error", "OutputPipeDidNotClose"
                    process.stdout.close()
            code = process.returncode if process is not None else None
            if read_error:
                reason, error_type = "log-write-failure", read_error[0]
            if overflow.is_set():
                reason = reason or "log-budget"
        return {"returnCode": code, "reason": reason,
                "rawExitCode": code if code is not None and code >= 0 else None,
                "terminationSignal": -code if code is not None and code < 0 else None,
                "logSha256": native.hash_file(log_path), "errorType": error_type}

    def _stop(self, process):
        def send(value):
            try:
                if os.name == "posix":
                    os.killpg(process.pid, value)
                elif process.poll() is None:
                    process.terminate()
            except ProcessLookupError:
                pass
        send(signal.SIGTERM)
        deadline = time.monotonic() + self.grace_seconds
        while process.poll() is None and time.monotonic() < deadline:
            time.sleep(0.02)
        if os.name == "posix":
            send(signal.SIGKILL)
        elif process.poll() is None:
            process.kill()
        process.wait(timeout=self.grace_seconds + 1)


class Progress:
    def __init__(self, task_id, path):
        self.task_id, self.path, self.sequence = task_id, Path(path), 0

    def emit(self, state, phase, message, *, result=None, reason=None):
        self.sequence += 1
        value = {"schemaVersion": 1, "taskId": self.task_id, "parentTaskId": None,
                 "sequence": self.sequence, "timestampUtc": utc_now(), "phase": phase,
                 "state": state, "completed": None, "total": None, "unit": None,
                 "message": message, "cancellationSupported": state in ("queued", "running", "cancelling"),
                 "retryOfTaskId": None, "rawExitCode": (result or {}).get("rawExitCode"),
                 "terminationSignal": (result or {}).get("terminationSignal"),
                 "reasonCode": reason, "logRef": "native-build/" + self.task_id + "/events.jsonl"}
        require_valid("TaskProgress", value)
        with self.path.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(value, allow_nan=False, sort_keys=True) + "\n")
            stream.flush()
        return value


@contextmanager
def workspace_reservation(parent):
    """One native source writer per workspace parent, released on runner exit."""
    if os.name != "posix":
        raise BuildBlocked("native Linux executor requires POSIX locks/process groups")
    import fcntl
    descriptor = os.open(Path(parent) / ".tor-native-build.lock",
                         os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    try:
        if not stat.S_ISREG(os.fstat(descriptor).st_mode):
            raise BuildBlocked("invalid workspace lock")
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise BuildBlocked("another source build owns this workspace reservation") from exc
        yield
    finally:
        os.close(descriptor)


def _run_verified(plan, output, archive_cache, cancellation, *, runner=None):
    """Internal engine; public entry obtains fresh qualification and approval."""
    runner = runner or ProcessRunner()
    output = Path(output)
    output.mkdir(mode=0o700)  # Atomic refusal to reuse existing/failed output.
    for folder in ("sources", "snapshots", "prefix", "logs", "home", "tmp"):
        (output / folder).mkdir(mode=0o700)
    # Input hashes identify reusable inputs; task IDs identify distinct attempts.
    task_id = "native-build-" + uuid.uuid4().hex
    progress = Progress(task_id, output / "events.jsonl")
    manifest = {"schemaVersion": 1, "kind": ADAPTER_ID, "taskId": task_id,
                "executionInputSha256": plan["executionInputSha256"], "inputs": plan["inputs"],
                "buildRequest": plan["buildRequest"],
                "startedAtUtc": utc_now(), "finishedAtUtc": None, "status": "RUNNING",
                "commands": [], "outputs": [], "qualification": "build only; runtime/online/ELF acceptance NOT RUN"}
    write_json(output / "build-manifest.json", manifest)
    progress.emit("queued", "sources", "Approved native build queued")
    progress.emit("running", "sources", "Verify private archive snapshots and extract sources")
    try:
        used = 0
        budget = plan["preflight"]["buildRequest"]["diskBudgetBytes"]
        for pin in plan["inputs"]["sources"]:
            if cancellation.is_set():
                raise InterruptedError("cancelled before source preparation")
            snapshot = snapshot_source(archive_cache, output / "snapshots", pin)
            _, size = extract_snapshot(snapshot, output / "sources", pin, byte_budget=budget - used)
            used += size
        started = time.monotonic()
        for index, command in enumerate(build_commands(plan, output)):
            if cancellation.is_set():
                raise InterruptedError("cancelled before next command")
            if time.monotonic() - started >= BUILD_TIMEOUT:
                raise TimeoutError("build wall-clock budget exceeded")
            _verify_tools(plan["inputs"]["tools"])
            if shutil.disk_usage(output).free < budget:
                raise BuildBlocked("native disk headroom below approved reservation")
            progress.emit("running", command.name, "Running fixed native source recipe; progress indeterminate")
            result = runner.run(command, output / "logs" / f"{index:02d}-{command.name}.log", cancellation)
            manifest["commands"].append({"name": command.name, "argv": list(command.argv), **result})
            write_json(output / "build-manifest.json", manifest)
            if result["returnCode"] != 0 or result["reason"]:
                state = "cancelled" if result["reason"] == "cancelled" else "failed"
                manifest["status"] = "CANCELLED" if state == "cancelled" else "FAIL"
                progress.emit(state, command.name, "Native command ended; original exit/signal retained",
                              result=result, reason=result["reason"] or "process-failure")
                break
        else:
            tor = output / "prefix/bin/tor"
            if not tor.is_file() or tor.is_symlink() or tor.stat().st_size == 0:
                raise ValueError("successful commands did not produce a regular nonempty Tor executable")
            manifest["outputs"] = [{"pathRef": "prefix/bin/tor", "bytes": tor.stat().st_size,
                                     "sha256": native.hash_file(tor)}]
            manifest["status"] = "PASS"
            progress.emit("succeeded", "build", "Source commands completed; runtime/package qualification NOT RUN", result={"rawExitCode": 0})
    except InterruptedError:
        manifest["status"] = "CANCELLED"
        progress.emit("cancelled", "build", "Cancellation observed; partial output retained", reason="cancelled")
    except (OSError, ValueError, TimeoutError, RuntimeError) as exc:
        manifest["status"] = "BLOCKED" if isinstance(exc, BuildBlocked) else "FAIL"
        manifest["errorType"] = type(exc).__name__
        progress.emit("blocked" if isinstance(exc, BuildBlocked) else "failed", "build",
                      "Native build stopped; partial output and logs retained", reason="execution-prerequisite" if isinstance(exc, BuildBlocked) else "executor-failure")
    finally:
        manifest["finishedAtUtc"] = utc_now()
        write_json(output / "build-manifest.json", manifest)
    return manifest


def execute_build(source_dir, output_dir, archive_cache, *, approved_input_sha256=None,
                  cancellation=None, root=native.ROOT):
    """Execute only after fresh qualification and explicit exact-input approval."""
    if sys.platform != "linux":
        raise BuildBlocked("source execution requires the selected native Linux host")
    if not native.SHA256.fullmatch(approved_input_sha256 or ""):
        raise BuildBlocked("explicit SHA-256-scoped source compilation approval required")
    output = Path(output_dir)
    if not output.parent.is_dir():
        raise BuildBlocked("create and review the native workspace parent before approval")
    with workspace_reservation(output.parent):
        plan = execution_plan(source_dir, output_dir, archive_cache, root=root)
        if not plan["readyForExecution"] or plan["executionInputSha256"] != approved_input_sha256:
            raise BuildBlocked("fresh qualification or approved input digest differs")
        return _run_verified(plan, output, archive_cache, cancellation or threading.Event())


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-dir", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--archive-cache", required=True)
    parser.add_argument("--execute", action="store_true", help="source compilation; separate explicit approval required")
    parser.add_argument("--approved-input-sha256")
    args = parser.parse_args(argv)
    cancellation = threading.Event()
    def cancel(signum, frame):
        cancellation.set()
    try:
        if not args.execute:
            result = execution_plan(args.source_dir, args.output_dir, args.archive_cache)
            code = 0 if result["readyForExecution"] else 1
        else:
            for value in (signal.SIGINT, signal.SIGTERM):
                signal.signal(value, cancel)
            result = execute_build(args.source_dir, args.output_dir, args.archive_cache,
                                   approved_input_sha256=args.approved_input_sha256, cancellation=cancellation)
            code = 0 if result["status"] == "PASS" else 1
    except (OSError, ValueError, KeyError, TypeError) as exc:
        result = {"schemaVersion": 1, "kind": ADAPTER_ID, "executionStatus": "NOT RUN",
                  "status": "BLOCKED", "reason": type(exc).__name__ + ": " + str(exc)}
        code = 2
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return code


if __name__ == "__main__":
    sys.dont_write_bytecode = True
    raise SystemExit(main())
