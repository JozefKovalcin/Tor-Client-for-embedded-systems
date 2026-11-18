"""Isolate and invoke the existing native Windows source recipe."""

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import zipfile

from .install import msys_path, native_environment, operation_lock
from .scan import MSYS, PROFILES, ROOT, _require_pe, inspect_source, ordinary_path, scan, sha256, source_manifest


NATIVE = ROOT / "windows-native"
RECIPE_FILES = (
    "build-windows.sh", "source-manifest.json", "RUNTIME.md",
    "scripts/prepare.py", "scripts/package.sh", "scripts/package.py",
    "scripts/start-tor.cmd", "scripts/smoke-test.ps1",
)


def stage_archives(source_cache, snapshot, sources):
    required = sum(ordinary_path(source_cache / item["archive"]).stat().st_size for item in sources)
    if shutil.disk_usage(snapshot.parent).free < required:
        raise ValueError("Insufficient disk space for the private archive snapshot")
    snapshot.mkdir()
    for item in sources:
        original = ordinary_path(source_cache / item["archive"])
        dest = snapshot / item["archive"]
        with original.open("rb") as src, dest.open("xb") as dst:
            shutil.copyfileobj(src, dst)
        inspect_source(snapshot, item)


def stage_recipe(destination):
    for relative in RECIPE_FILES:
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ordinary_path(NATIVE / relative), target)
    for folder in ("config", "patches"):
        source = ordinary_path(NATIVE / folder, directory=True)
        for item in source.iterdir():
            if item.is_file():
                target = destination / folder / item.name
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ordinary_path(item), target)
    for patch in source_manifest()["tor_source_patches"]:
        if sha256(destination / patch["file"]) != patch["sha256"]:
            raise ValueError("Copied patch hash differs from the Windows source lock")


def json_file(path, limit=4 * 1024 * 1024, encoding="utf-8"):
    with ordinary_path(path).open("rb") as stream:
        data = stream.read(limit + 1)
    if len(data) > limit:
        raise ValueError("Manifest exceeds the size limit")
    try:
        value = json.loads(data.decode(encoding))
    except (json.JSONDecodeError, UnicodeError) as error:
        raise ValueError("Malformed build metadata") from error
    if not isinstance(value, dict):
        raise ValueError("Manifest must be an object")
    return value


def write_record(path, value):
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as stream:
        json.dump(value, stream, indent=2, sort_keys=True, allow_nan=False)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())
    Path(stream.name).replace(path)


def resolve_record_path(folder, relative):
    if not isinstance(relative, str) or len(relative) > 300:
        raise ValueError("Invalid inventory path")
    portable = relative.replace("\\", "/")
    if not portable or portable.startswith("/") or ":" in portable or any(p in ("", ".", "..") for p in portable.split("/")):
        raise ValueError("Inventory path escapes the package")
    return folder.joinpath(*portable.split("/"))


def verify_artifacts(package):
    package = ordinary_path(package, directory=True)
    manifest = json_file(package / "package-report.json")
    inventory = manifest.get("files")
    if manifest.get("status") != "PASS" or not isinstance(inventory, dict) or "tor.exe" not in inventory:
        raise ValueError("The reviewed PE packager did not produce a valid inventory")
    if sha256(package / "source-manifest.json") != sha256(NATIVE / "source-manifest.json"):
        raise ValueError("Artifact source provenance differs from the reviewed pin")
    files, binaries = set(), set()
    for relative, expected in inventory.items():
        path = ordinary_path(resolve_record_path(package, relative))
        key = path.relative_to(package).as_posix().lower()
        if key in files or not isinstance(expected, dict) or type(expected.get("bytes")) is not int:
            raise ValueError("Malformed or duplicate inventory entry")
        if path.stat().st_size != expected["bytes"] or sha256(path) != expected.get("sha256"):
            raise ValueError("Artifact size or SHA-256 changed")
        files.add(key)
        if path.suffix.lower() in (".exe", ".dll"):
            if path.parent != package:
                raise ValueError("Runtime binaries must be in the package root")
            _require_pe(path)
            binaries.add(key)
    pe = manifest.get("pe")
    if not isinstance(pe, dict) or set(pe) != binaries or any(
            not isinstance(item, dict) or item.get("machine") != "AMD64" or item.get("format") != "PE32+"
            or not all(item.get(flag) is True for flag in ("dynamic_base", "nx_compat", "high_entropy_va"))
            for item in pe.values()):
        raise ValueError("PE inventory or hardening differs from the reviewed packager output")
    for item in source_manifest()["sources"]:
        name = "licenses/" + item["name"] + "-LICENSE.txt"
        if name.lower() not in files or sha256(package / name) != item["license_sha256"]:
            raise ValueError("The artifact license inventory differs from the source lock")
    for entry in package.rglob("*"):
        ordinary_path(entry, directory=entry.is_dir())
        if entry.is_file() and entry != package / "package-report.json" and entry.relative_to(package).as_posix().lower() not in files:
            raise ValueError("Unlisted file in the verified runtime package")
    return manifest


def monitor(command, env, cwd, log_path, stages=False):
    with log_path.open("xb") as log, subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, env=env, cwd=cwd) as process:
        for data in iter(lambda: process.stdout.readline(65536), b""):
            log.write(data)
            line = data.decode("utf-8", "replace").rstrip()
            if stages and line.startswith("VERIFIED PATCH "):
                print("[phase] compiling verified sources", flush=True)
            elif stages and line.startswith("PASS build and PE/package checks."):
                print("[phase] verifying artifacts", flush=True)
            print(line, flush=True)
        return process.wait()


def build(profile, source_cache, workspace):
    if os.name != "nt" or profile not in PROFILES:
        raise ValueError("The reviewed build recipe requires native Windows and a supported profile")
    workspace = ordinary_path(workspace, directory=True)
    source_cache = ordinary_path(source_cache, directory=True)
    with operation_lock(workspace, ".build-lock"):
        report = scan(profile, source_cache, workspace)
        if report["status"] != "AVAILABLE":
            raise ValueError("Preflight did not accept all required dependencies and verified inputs")
        builds = workspace / "builds"
        builds.mkdir(exist_ok=True)
        ordinary_path(builds, directory=True)
        run = Path(tempfile.mkdtemp(prefix="run-", dir=builds))
        recipe, archives = run / "pipeline", run / "archives"
        stage_recipe(recipe)
        stage_archives(source_cache, archives, source_manifest()["sources"])
        record_path = run / "build-record.json"
        record = {"schemaVersion": 1, "id": run.name, "profile": profile, "linkage": PROFILES[profile],
                  "startedUtc": datetime.now(timezone.utc).isoformat(), "state": "INTERRUPTED",
                  "compilerExit": None, "smokeState": "NOT RUN",
                  "sourceManifestSha256": sha256(recipe / "source-manifest.json"),
                  "recipeSha256": sha256(recipe / "build-windows.sh"),
                  "recipeFiles": {path.relative_to(recipe).as_posix(): sha256(path)
                                  for path in sorted(recipe.rglob("*")) if path.is_file()},
                  "compilerFlags": [line for line in (recipe / "build-windows.sh").read_text(encoding="utf-8").splitlines()
                                    if line.startswith(("export CFLAGS=", "export LDFLAGS="))],
                  "toolchain": {row["id"]: row["detectedVersion"] for row in report["dependencies"]},
                  "version": next(s["version"] for s in source_manifest()["sources"] if s["name"] == "tor"),
                  "package": "pipeline/dist" if profile == "optimized-static" else "pipeline/dist-optimized-shared"}
        write_record(record_path, record)
        env = {**native_environment(), "PROFILE": "optimized", "LINKAGE": PROFILES[profile],
               "TOR_WINDOWS_OFFLINE": "1", "TOR_WINDOWS_WORK": msys_path(run / "work"),
               "TOR_WINDOWS_CACHE": msys_path(archives), "JOBS": str(min(os.cpu_count() or 1, 4))}
        bash = ordinary_path(MSYS / "usr/bin/bash.exe")
        print("[phase] source and recipe snapshot ready", flush=True)
        print(f"Build ID {run.name}", flush=True)
        command = [str(bash), "--noprofile", "--norc", msys_path(recipe / "build-windows.sh")]
        code = monitor(command, env, run, run / "manager-build.log", stages=True)
        record["compilerExit"] = code & 0xffffffff
        record["state"] = "FAILED"
        if code == 0:
            try:
                package = run / record["package"]
                record["artifacts"] = verify_artifacts(package)["files"]
                record["packageReportSha256"] = sha256(package / "package-report.json")
                record["state"] = "BUILT"
            except (OSError, ValueError) as error:
                write_record(record_path, record)
                raise ValueError("Native build output failed artifact verification") from error
        write_record(record_path, record)
        return code


def get_record(workspace, build_id):
    if not isinstance(build_id, str) or not re.fullmatch(r"run-[a-z0-9_]{6,24}", build_id):
        raise ValueError("Select a build from this workspace")
    run = ordinary_path(Path(workspace) / "builds" / build_id, directory=True)
    record = json_file(run / "build-record.json")
    expected = {"optimized-static": "pipeline/dist", "optimized-shared": "pipeline/dist-optimized-shared"}
    if record.get("id") != build_id or record.get("profile") not in expected or record.get("package") != expected[record["profile"]]:
        raise ValueError("Build record does not match the selected profile and location")
    return run, record


def list_builds(workspace):
    parent = Path(workspace) / "builds"
    if not parent.exists():
        return {"builds": []}
    result = []
    for item in sorted(ordinary_path(parent, directory=True).iterdir(), key=lambda item: item.name, reverse=True):
        try:
            run, record = get_record(workspace, item.name)
            if record["state"] == "BUILT":
                package = run / record["package"]
                if record.get("packageReportSha256") != sha256(package / "package-report.json") or \
                        verify_artifacts(package)["files"] != record.get("artifacts"):
                    raise ValueError("Artifact inventory changed")
            result.append(record)
        except (OSError, ValueError, KeyError):
            result.append({"id": item.name, "state": "UNVERIFIED"})
    return {"builds": result}


def smoke_passed(result, version):
    required = {"--version", "--help", "shipped_configs", "compiled_modules",
                "invalid_config_rejected", "startup_and_writable_state", "clean_shutdown", "restart"}
    rows = result.get("tests", [])
    passed = {row["id"]: row["detail"] for row in rows if row["status"] == "PASS"}
    skipped = {row["id"] for row in rows if row["status"] == "SKIPPED"}
    return (len(passed) + len(skipped) == len(rows) and required.issubset(passed)
            and {"public_bootstrap", "socks_remote_dns"}.issubset(skipped)
            and bool(re.match(r"Tor version " + re.escape(version) + r"(?:\.|\s|$)", passed["--version"])))


def candidate(workspace, build_id):
    run, record = get_record(workspace, build_id)
    if record.get("schemaVersion") != 1 or record.get("state") != "BUILT" or record.get("compilerExit") != 0 or record.get("smokeState") != "PASS":
        raise ValueError("Only a locally built artifact with a successful offline smoke test can be approved")
    package = run / record["package"]
    package_hash = sha256(package / "package-report.json")
    version = next(source["version"] for source in source_manifest()["sources"] if source["name"] == "tor")
    license_lock = sha256(NATIVE / "source-manifest.json")
    if (version != record.get("version") or license_lock != record.get("sourceManifestSha256")
            or package_hash != record.get("packageReportSha256") or package_hash != record.get("smokePackageSha256")
            or sha256(NATIVE / "build-windows.sh") != record.get("recipeSha256")):
        raise ValueError("Build and offline smoke do not share the reviewed source, recipe and artifact identity")
    report = verify_artifacts(package)
    evidence = resolve_record_path(run, record.get("smokeEvidence"))
    if (report["files"] != record.get("artifacts") or sha256(evidence) != record.get("smokeEvidenceSha256")
            or not smoke_passed(json_file(evidence, encoding="utf-8-sig"), version)):
        raise ValueError("Artifact or offline acceptance evidence changed")
    binary_pins = {name.lower(): item["sha256"] for name, item in report["files"].items()
                   if name.lower() in report["pe"]}
    return {"id": build_id, "version": version, "profile": record["profile"], "packagePath": str(package),
            "reportSha256": package_hash, "sourceSha256": license_lock,
            "files": [{"name": name, "sha256": digest} for name, digest in sorted(binary_pins.items())]}


def export_runtime(workspace, build_id):
    workspace = ordinary_path(workspace, directory=True)
    with operation_lock(workspace, ".build-lock"):
        approved = candidate(workspace, build_id)
        package = Path(approved["packagePath"])
        report_path = package / "package-report.json"
        with ordinary_path(report_path).open("rb") as stream:
            report_bytes = stream.read(4 * 1024 * 1024 + 1)
        if hashlib.sha256(report_bytes).hexdigest() != approved["reportSha256"]:
            raise ValueError("Package report changed before export")
        inventory = json.loads(report_bytes)["files"]
        output = workspace / "exports"
        output.mkdir(exist_ok=True)
        output = ordinary_path(output, directory=True)
        destination = output / (f"tor-{approved['version']}-{approved['profile']}-{build_id}.zip")
        if destination.exists():
            raise ValueError("The export already exists; move it before exporting the same build again")
        pending = None
        try:
            with tempfile.NamedTemporaryFile(dir=output, prefix="pending-runtime-", suffix=".part", delete=False) as stream:
                pending = Path(stream.name)
                with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
                    archive.writestr("package-report.json", report_bytes)
                    for relative, expected in sorted(inventory.items()):
                        item = ordinary_path(resolve_record_path(package, relative))
                        name = item.relative_to(package).as_posix()
                        digest = hashlib.sha256()
                        count = 0
                        with item.open("rb") as source, archive.open(name, "w") as target:
                            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                                count += len(chunk)
                                if count > expected["bytes"]:
                                    raise ValueError("Runtime file grew during export")
                                digest.update(chunk)
                                target.write(chunk)
                        if count != expected["bytes"] or digest.hexdigest() != expected["sha256"]:
                            raise ValueError("Runtime file changed during export")
                stream.flush()
                os.fsync(stream.fileno())
            digest = sha256(pending)
            # Publish atomically without replacing an existing export, including a concurrent file.
            os.link(pending, destination)
        finally:
            if pending is not None:
                pending.unlink(missing_ok=True)
        return {"path": str(destination), "sha256": digest}


def smoke(workspace, build_id):
    if os.name != "nt":
        raise ValueError("Native Windows is required for artifact smoke testing")
    workspace = ordinary_path(workspace, directory=True)
    with operation_lock(workspace, ".build-lock"):
        run, record = get_record(workspace, build_id)
        package = run / record["package"]
        manifest = verify_artifacts(package)
        package_hash = sha256(package / "package-report.json")
        if record.get("state") != "BUILT" or record.get("packageReportSha256") != package_hash or \
                manifest["files"] != record.get("artifacts"):
            raise ValueError("An intact native build is required before offline smoke")
        # The existing smoke workflow uses a fresh private state scope and receives no Online flag.
        test = Path(tempfile.mkdtemp(prefix="smoke-", dir=run))
        staged = test / "package"
        for relative in (*manifest["files"], "package-report.json"):
            target = resolve_record_path(staged, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            with ordinary_path(resolve_record_path(package, relative)).open("rb") as src, target.open("xb") as dst:
                shutil.copyfileobj(src, dst)
        if verify_artifacts(staged) != manifest or sha256(staged / "package-report.json") != package_hash:
            raise ValueError("Smoke test snapshot differs from the verified package")
        script = test / "scripts/smoke-test.ps1"
        script.parent.mkdir()
        shutil.copyfile(ordinary_path(NATIVE / "scripts/smoke-test.ps1"), script)
        env = native_environment()
        powershell = ordinary_path(Path(env.get("SystemRoot", "C:/Windows")) / "System32/WindowsPowerShell/v1.0/powershell.exe")
        record["smokeState"] = "NOT RUN"
        record["smokeExit"] = None
        write_record(run / "build-record.json", record)
        command = [str(powershell), "-NoProfile", "-NonInteractive", "-File", str(script), "-Dist", str(staged)]
        code = monitor(command, env, test, test / "offline-smoke.log")
        evidence = list((test / "runtime").glob("smoke-*/results.json"))
        if code == 0:
            result = json_file(evidence[0], encoding="utf-8-sig") if len(evidence) == 1 else {}
            if not smoke_passed(result, record["version"]) or verify_artifacts(staged) != manifest:
                code = 2
            else:
                record["smokeEvidence"] = evidence[0].relative_to(run).as_posix()
                record["smokeEvidenceSha256"] = sha256(evidence[0])
                record["smokePackageSha256"] = package_hash
        record["smokeState"] = "PASS" if code == 0 else "FAIL"
        record["smokeExit"] = code & 0xffffffff
        write_record(run / "build-record.json", record)
        return code
