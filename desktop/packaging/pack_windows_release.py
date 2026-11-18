#!/usr/bin/env python3
"""Assemble a local self-contained Windows x64 candidate from locked inputs."""

import argparse
import base64
import hashlib
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from .pack_gui_preview import (ASSET_INVENTORY, PROJECT, PROJECT_ASSEMBLIES, check_package_assets,
                               member_path, notices, read_file, x64_pe)


REPO = PROJECT.parents[1]
PACKAGING = Path(__file__).resolve().parent
NET_PACK = "Microsoft.NETCore.App.Runtime.win-x64/11.0.0"
TARGET = ".NETCoreApp,Version=v11.0/win-x64"


def sha(data):
    return hashlib.sha256(data).hexdigest()


def locked_archives(feed, reviewed, runtime_packs):
    archives = {}
    for row in [*reviewed["packages"], *runtime_packs["packages"]]:
        package = row["id"]
        version = row["version"]
        data = read_file(feed, f"{package.lower()}.{version}.nupkg")
        if base64.b64encode(hashlib.sha512(data).digest()).decode() != row["signedArchiveSha512Base64"]:
            raise ValueError(f"Reviewed package archive changed: {package}")
        archives[f"{package}/{version}"] = data
    return archives


def developer_files():
    for pattern in (
        "scripts/windows_developer/*.py", "scripts/dependencies/*.py", "scripts/contracts.py",
        "config/contracts/v1/*.json", "config/dependencies/manifest-v1.json",
        "windows-native/build-windows.sh", "windows-native/source-manifest.json", "windows-native/RUNTIME.md",
        "windows-native/scripts/prepare.py", "windows-native/scripts/package.sh", "windows-native/scripts/package.py",
        "windows-native/scripts/start-tor.cmd", "windows-native/scripts/smoke-test.ps1",
        "windows-native/config/*", "windows-native/patches/*",
    ):
        yield from (path.relative_to(REPO).as_posix() for path in sorted(REPO.glob(pattern)) if path.is_file())


def add_artifact(artifacts, publish, name, expected=None):
    data = read_file(publish, name)
    if name in artifacts or (expected is not None and data != expected):
        raise ValueError(f"Published file differs from its verified source or collides: {name}")
    artifacts[name] = data


def add_managed_publish(artifacts, publish, archives, inventory, lock, native):
    deps = json.loads(artifacts["Tor.Desktop.deps.json"])
    runtime = json.loads(artifacts["Tor.Desktop.runtimeconfig.json"])["runtimeOptions"]
    if (deps["runtimeTarget"]["name"] != TARGET or "framework" in runtime or "frameworks" in runtime
            or {"name": "Microsoft.NETCore.App", "version": "11.0.0"} not in runtime.get("includedFrameworks", [])):
        raise ValueError("Expected an offline self-contained win-x64 publish")
    target = deps["targets"][TARGET]
    assets = {key: [(name, member_path(name).name) for name in (*row.get("runtime", {}), *row.get("native", {}))
                    if not name.lower().endswith(".pdb")] for key, row in target.items()}
    check_package_assets(deps, assets, json.loads(ASSET_INVENTORY.read_bytes()))
    locked_packages = lock["dependencies"]["net11.0"] | lock["dependencies"]["net11.0/win-x64"]
    project_files = set()
    found_framework = False
    for package, row in target.items():
        kind = deps["libraries"][package]["type"]
        if kind == "project":
            for _, name in assets[package]:
                if name not in PROJECT_ASSEMBLIES:
                    raise ValueError(f"Unexpected project output: {name}")
                add_artifact(artifacts, publish, name)
                project_files.add(name)
            continue
        if kind == "runtimepack":
            if package != "runtimepack." + NET_PACK:
                raise ValueError(f"Unreviewed framework in publish: {package}")
            with zipfile.ZipFile(io.BytesIO(archives[NET_PACK])) as pack:
                paths = {member_path(file.attrib["Path"]).name: file.attrib["Path"]
                         for file in ET.fromstring(pack.read("data/RuntimeList.xml"))}
                if paths.keys() != {name for _, name in assets[package]}:
                    raise ValueError("Framework deployment differs from the signed RuntimeList")
                for name, path in paths.items():
                    add_artifact(artifacts, publish, name, pack.read(path))
                for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
                    artifacts[f"licenses/{NET_PACK.split('/')[0]}/{name}"] = pack.read(name)
            native.update(row["native"])
            found_framework = True
            continue
        if kind != "package":
            raise ValueError(f"Unexpected dependency type: {kind}")
        if not assets[package]:
            continue
        name, version = package.split("/")
        pin = locked_packages[name]
        if (pin["resolved"] != version or pin["contentHash"] != inventory[name]["nugetContentHash"]
                or deps["libraries"][package]["sha512"] != "sha512-" + pin["contentHash"]):
            raise ValueError(f"RID lock differs from the verified package: {package}")
        with zipfile.ZipFile(io.BytesIO(archives[package])) as archive:
            for source, leaf in assets[package]:
                add_artifact(artifacts, publish, leaf, archive.read(source))
                if source in row.get("native", {}):
                    native.add(leaf)
            for leaf, data in notices(archive, name).items():
                artifacts[f"licenses/{name}/{leaf}"] = data
    if project_files != PROJECT_ASSEMBLIES or not found_framework:
        raise ValueError("Self-contained project or framework assembly inventory is incomplete")


def add_tor(artifacts, runtime):
    report = (REPO / "windows-native/evidence/package-static.json").read_bytes()
    manifest = json.loads(report)
    inventory = manifest["files"]
    for name in sorted(inventory):
        data = read_file(runtime, name)
        if sha(data) != inventory[name]["sha256"] or len(data) != inventory[name]["bytes"]:
            raise ValueError(f"Bundled Tor file differs from the reviewed package: {name}")
        artifacts[f"tor-runtime/{name}"] = data
    if artifacts["tor-runtime/source-manifest.json"] != (REPO / "windows-native/source-manifest.json").read_bytes():
        raise ValueError("Bundled Tor source lock differs from the current recipe")
    artifacts["tor-runtime/package-report.json"] = report


def native_dependencies(artifacts, publish, runtime, native, objdump, system32):
    resolved = {}
    for name in sorted(native | {"tor-runtime/tor.exe", "tor-runtime/libwinpthread-1.dll"}):
        x64_pe(artifacts[name], name)
        parent = "tor-runtime/" if name.startswith("tor-runtime/") else ""
        root = runtime if parent else publish
        path = root / member_path(name).name
        # Recheck the inspected file so the dependency evidence describes the archived bytes.
        result = subprocess.run([str(objdump), "-p", str(path)], capture_output=True, text=True, check=True, timeout=10)
        if path.read_bytes() != artifacts[name]:
            raise ValueError(f"Native PE changed during inspection: {name}")
        imports = sorted(set(re.findall(r"DLL Name:\s*(\S+)", result.stdout)), key=str.casefold)
        package = {key.lower() for key in artifacts}
        for dll in imports:
            lower = dll.lower()
            if (not re.fullmatch(r"[a-z0-9_.-]+\.dll", lower) or lower.startswith(("msys-", "cygwin"))
                    or not ((parent + lower).lower() in package or lower.startswith(("api-ms-win-", "ext-ms-win-"))
                            or (system32 / lower).is_file())):
                raise ValueError(f"Unresolved or disallowed native import: {name} / {dll}")
        resolved[name] = imports
    return resolved


def write_zip(destination, artifacts):
    if destination.exists():
        raise FileExistsError(destination)
    with tempfile.NamedTemporaryFile(dir=destination.parent, prefix="pending-release-", delete=False) as handle:
        pending = Path(handle.name)
    try:
        with zipfile.ZipFile(pending, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, data in sorted(artifacts.items()):
                archive.writestr(name, data)
        with zipfile.ZipFile(pending) as archive:
            if archive.namelist() != sorted(artifacts) or archive.testzip() is not None or any(
                    archive.read(name) != data for name, data in artifacts.items()):
                raise ValueError("Archive verification failed")
        os.link(pending, destination)
    finally:
        pending.unlink(missing_ok=True)


def pack(publish, feed, runtime, destination, version, objdump, system32):
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", version):
        raise ValueError("Provide a version number for the local release candidate")
    publish, feed, runtime = (path.resolve(strict=True) for path in (publish, feed, runtime))
    lock_path = PROJECT / "packages.win-x64.lock.json"
    lock_bytes = lock_path.read_bytes()
    inventory_bytes = (PROJECT / "resolved-package-inventory.json").read_bytes()
    packs_bytes = (PACKAGING / "windows-runtime-packs.json").read_bytes()
    reviewed = json.loads(inventory_bytes)
    archives = locked_archives(feed, reviewed, json.loads(packs_bytes))
    artifacts = {}
    for name in ("Tor.Desktop.exe", "Tor.Desktop.deps.json", "Tor.Desktop.runtimeconfig.json"):
        add_artifact(artifacts, publish, name)
    native = {"Tor.Desktop.exe"}
    add_managed_publish(artifacts, publish, archives, {item["id"]: item for item in reviewed["packages"]},
                        json.loads(lock_bytes), native)
    for relative in developer_files():
        add_artifact(artifacts, publish, "developer/" + relative, read_file(REPO, relative))
    add_tor(artifacts, runtime)
    imports = native_dependencies(artifacts, publish, runtime, native, objdump.resolve(strict=True), system32.resolve(strict=True))
    artifacts.update({
        "README.md": (PACKAGING / "WINDOWS-RELEASE.md").read_bytes(),
        "provenance/managed-and-native-imports.json": (json.dumps(imports, indent=2) + "\n").encode(),
        "provenance/packages.lock.json": (PROJECT / "packages.lock.json").read_bytes(),
        "provenance/packages.win-x64.lock.json": lock_bytes,
        "provenance/resolved-package-inventory.json": inventory_bytes,
        "provenance/windows-runtime-packs.json": packs_bytes,
    })
    subprocess.run(["git", "-C", str(REPO), "diff", "--quiet", "HEAD", "--", "desktop", "scripts", "config", "windows-native"], check=True)
    source = subprocess.check_output(["git", "-C", str(REPO), "rev-parse", "HEAD"], text=True).strip()
    metadata = {"schemaVersion": 1, "version": version, "sourceCommit": source, "runtimeIdentifier": "win-x64",
                "selfContained": True, "cleanHostQualification": "NOT RUN", "publicTorQualification": "NOT RUN",
                "files": {name: {"bytes": len(data), "sha256": sha(data)} for name, data in sorted(artifacts.items())}}
    artifacts["manifest.json"] = (json.dumps(metadata, indent=2) + "\n").encode()
    write_zip(destination, artifacts)
    return {"path": str(destination), "bytes": destination.stat().st_size,
            "sha256": sha(destination.read_bytes()), "inventoryEntries": len(artifacts), "sourceCommit": source}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    for arg in ("publish", "feed", "tor-runtime", "out", "objdump", "system32"):
        parser.add_argument("--" + arg, type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    print(json.dumps(pack(args.publish, args.feed, args.tor_runtime, args.out, args.version, args.objdump, args.system32)))
