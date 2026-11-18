"""Read-only UCRT64 recipe preflight using the W2 and W1 contracts."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import stat
import struct
import subprocess
import tarfile

from scripts.contracts import require_valid
from scripts.dependencies.checker import ReadOnlyProbe, _classified_tool, _record, _version_tuple, dependency_check


ROOT = Path(__file__).resolve().parents[2]
MSYS = Path("C:/msys64")
PROFILES = {"optimized-static": "static", "optimized-shared": "shared"}
TOOLS = ("bash", "msys2-gcc", "msys2-gcc-target", "make", "ld", "ar", "ranlib", "strip", "pkg-config", "perl", "tar", "patch")
EXTRA = ("windres", "objdump", "nm", "dlltool", "curl", "sha256sum", "pacman", "cygpath")
BOOTSTRAP_URL = "https://www.msys2.org/docs/installer/"
DOTNET_URL = "https://dotnet.microsoft.com/download/dotnet/11.0"


def ordinary_path(path, *, directory=False):
    """Inspect local paths without following symlinks or Windows reparse points."""
    path = Path(path)
    if not path.is_absolute() or ".." in path.parts or path.drive.startswith("\\"):
        raise ValueError("A local absolute path is required")
    for component in (*reversed(path.parents), path):
        info = component.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise PermissionError("Reparse points are not accepted")
    if directory and not path.is_dir() or not directory and not path.is_file():
        raise ValueError("Unexpected file type")
    return path


def sha256(path):
    with ordinary_path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def source_manifest():
    return json.loads((ROOT / "windows-native/source-manifest.json").read_text(encoding="utf-8"))


def inspect_source(cache, source):
    archive = ordinary_path(cache / source["archive"])
    if sha256(archive) != source["sha256"]:
        raise ValueError("Archive SHA-256 differs from the source lock")
    name = f"{source['directory']}/{source['license_file']}"
    with tarfile.open(archive, "r:*") as bundle:
        matches = [item for item in bundle if item.name == name]
        if len(matches) != 1 or not matches[0].isfile() or matches[0].size > 1024 * 1024:
            raise ValueError("The exact reviewed license member is unavailable")
        with bundle.extractfile(matches[0]) as license_stream:
            if hashlib.file_digest(license_stream, "sha256").hexdigest() != source["license_sha256"]:
                raise ValueError("Archive license SHA-256 differs from the source lock")
    return archive


class RecipeProbe(ReadOnlyProbe):
    """UCRT64 probes resolve the same installed tools as the native recipe."""

    def _candidates(self, name, executable):
        if name == "git":
            return super()._candidates(name, executable)
        roots = (MSYS / "ucrt64/bin",) if name.startswith("msys2-") else (MSYS / "ucrt64/bin", MSYS / "usr/bin")
        return [folder / (executable + ".exe") for folder in roots]

    def _lookup(self, name, executable):
        if name == "git":
            return super()._lookup(name, executable)
        for path in self._candidates(name, executable):
            try:
                return ordinary_path(path)
            except FileNotFoundError:
                continue
        return None

    def output(self, program, *args):
        executable = ordinary_path(program)
        return self._execute((str(executable), *args))

    def installed_tool(self, name, executable, args=("--version",), minimum=None):
        path = None
        try:
            if isinstance(executable, Path):
                path = ordinary_path(executable)
            else:
                path = self._lookup(name, executable)
                if path is None:
                    return _record("MISSING", "The reviewed tool was not found in the recipe's tool roots")
            result = self.output(path, *args)
        except FileNotFoundError:
            return _record("MISSING", "The reviewed executable is absent", path=path)
        except (OSError, ValueError, subprocess.TimeoutExpired):
            return _record("BLOCKED", "Tool probe denied or did not finish", path=path)
        content = (result.stdout or "") + "\n" + (result.stderr or "")
        version = _version_tuple(content) if len(content) <= self.max_output_chars and result.returncode == 0 else None
        if version is None:
            return _record("UNKNOWN", "The probe did not establish an installed version", path=path)
        status = "INCOMPATIBLE" if minimum and version < minimum else "AVAILABLE"
        reason = "Installed version observed in the reviewed tool root" if status == "AVAILABLE" else "Installed version is below the recipe minimum"
        return _record(status, reason, version=".".join(map(str, version)), path=path)

    def dotnet(self, executable, name, switch, version):
        try:
            result = self.output(executable, switch)
        except FileNotFoundError:
            return _record("MISSING", "No global .NET executable in Program Files", path=executable)
        except (OSError, ValueError, subprocess.TimeoutExpired):
            return _record("BLOCKED", ".NET probe denied or timed out", path=executable)
        if result.returncode or len(result.stdout) > self.max_output_chars:
            return _record("UNKNOWN", "Could not obtain a .NET inventory", path=executable)
        pattern = r"^([0-9.]+) " if name == "dotnet-sdk" else r"^Microsoft\.NETCore\.App ([0-9.]+) "
        versions = re.findall(pattern, result.stdout, re.MULTILINE)
        if version in versions:
            return _record("AVAILABLE", "Pinned .NET version is installed", version=version, path=executable)
        if versions:
            return _record("INCOMPATIBLE", "Pinned version is absent from installed inventory",
                           version=", ".join(versions)[:110], path=executable)
        return _record("MISSING", "No installed version of this component was reported", path=executable)

    def autotools(self):
        path = MSYS / "usr/bin/autoconf"
        try:
            ordinary_path(path)
        except FileNotFoundError:
            return _record("MISSING", "Autoconf is not needed for release tarballs", path=path)
        except (OSError, ValueError):
            return _record("BLOCKED", "Autoconf path is not a trusted local file", path=path)
        return self.installed_tool("autoconf", MSYS / "usr/bin/bash.exe", ("--noprofile", "--norc", "/usr/bin/autoconf", "--version"))


def _file_observation(path, check):
    try:
        check(ordinary_path(path))
    except FileNotFoundError:
        return _record("MISSING", "Required file is absent", path=path)
    except (ValueError, tarfile.TarError) as error:
        return _record("INCOMPATIBLE", str(error), path=path)
    except OSError:
        return _record("BLOCKED", "File could not be read from a trusted local path", path=path)
    return _record("AVAILABLE", "File contents verified", path=path)


def _require_pe(path):
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError("Missing PE executable header")
        stream.seek(struct.unpack_from("<I", header, 60)[0])
        if stream.read(6) != b"PE\x00\x00\x64\x86":
            raise ValueError("Expected a Windows x64 PE image")


def scan(profile, cache, output, probe=None):
    if profile not in PROFILES:
        raise ValueError("Invalid Windows build profile")
    cache, output = Path(cache), Path(output)
    probe = probe or RecipeProbe()
    host = probe.host()
    if host["os"] == "windows":
        host["osVersion"] = platform.version()
    rows = []

    def add(name, requirement, observation, *, optional=False, phase="build", method="manual", url=None):
        check = dependency_check(name, profile, observation["status"], requirement, observation["reason"],
                                 optional=optional, detected_version=observation.get("version"),
                                 architecture=observation.get("architecture"))
        require_valid("DependencyCheck", check)
        rows.append({**check, "name": name, "path": observation.get("path"), "phase": phase,
                     "installationMethod": method, "sourceUrl": url})

    status = "AVAILABLE" if host["os"] == "windows" and host["architecture"] == "x86_64" else "INCOMPATIBLE"
    add("windows-host", "Native Windows x64", _record(status, f"Observed {host['os']} {host['architecture']}", version=host["osVersion"]))
    resources = probe.resources(output if output.is_dir() else output.parent)
    if status != "AVAILABLE":
        return {"profile": profile, "status": "BLOCKED", "host": host, "resources": resources, "dependencies": rows}

    msys_output = _file_observation(MSYS / "usr/bin/msys-2.0.dll", _require_pe)
    add("msys2", "MSYS2 x64 at C:/msys64", msys_output, method="official installer", url=BOOTSTRAP_URL)
    policy = json.loads((ROOT / "config/dependencies/manifest-v1.json").read_text(encoding="utf-8"))
    specs = {row["id"]: row for row in policy["tools"] if "windows" in row["oses"]}
    for name in (*TOOLS, "git"):
        observed = probe.tool(name)
        status, reason = _classified_tool(specs[name], observed)
        add(name, specs[name]["requirement"], {**observed, "status": status, "reason": reason}, optional=name == "git",
            method="manual" if name == "git" else "MSYS2 signed package", url=BOOTSTRAP_URL if name != "git" else None)
    for name in EXTRA:
        add(name, "Version observed in the MSYS2 recipe environment", probe.installed_tool(name, name),
            method="MSYS2 signed package", url=BOOTSTRAP_URL)
    add("msys2-python", "MSYS2 Python >=3.12", probe.installed_tool("msys2-python", MSYS / "usr/bin/python3.exe", minimum=(3, 12, 0)),
        method="MSYS2 signed package", url=BOOTSTRAP_URL)
    python = probe.tool("python")
    status, reason = _classified_tool({"minimumVersion": [3, 12]}, python)
    add("host-python", "Windows Python >=3.12 to run developer tooling", {**python, "status": status, "reason": reason})
    add("autotools", "Release archives supply configure and do not require regeneration", probe.autotools(), optional=True)
    add("ucrt-headers", "MinGW-w64 UCRT headers", _file_observation(MSYS / "ucrt64/include/_mingw.h", lambda file: None))
    add("ucrt-import-library", "UCRT import library", _file_observation(MSYS / "ucrt64/lib/libucrt.a", lambda file: None))
    add("runtime-winpthread", "x64 libwinpthread-1.dll for package dependency closure", _file_observation(MSYS / "ucrt64/bin/libwinpthread-1.dll", _require_pe), phase="runtime")
    dotnet = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "dotnet/dotnet.exe"
    for name, version, switch in (("dotnet-sdk", "11.0.100", "--list-sdks"), ("dotnet-runtime", "11.0.0", "--list-runtimes")):
        observation = probe.dotnet(dotnet, name, switch, version)
        add(name, f"Pinned .NET {version} for desktop developer workflows", observation, optional=True, phase="desktop", url=DOTNET_URL)

    for source in source_manifest()["sources"]:
        check = _file_observation(cache / source["archive"], lambda _: inspect_source(cache, source))
        add("source-" + source["name"], f"Pinned {source['version']} archive and license SHA-256", check, url=source["url"], method="verified local archive")
    for patch in source_manifest()["tor_source_patches"]:
        def verify(file):
            if sha256(file) != patch["sha256"]:
                raise ValueError("The Windows patch differs from its reviewed hash")
        add("windows-source-patch", "Exact reviewed Windows patch", _file_observation(ROOT / "windows-native" / patch["file"], verify))

    valid_output = output.is_absolute() and not output.drive.startswith("\\") and not any(ch.isspace() for ch in str(output))
    add("output-location", "Local build workspace path without whitespace", _record("AVAILABLE" if valid_output else "INCOMPATIBLE",
        "Path shape accepted" if valid_output else "Select a local workspace with no whitespace", path=output))
    free = resources["diskFreeBytes"]
    free_threshold = 1
    for source in source_manifest()["sources"]:
        try:
            free_threshold += ordinary_path(cache / source["archive"]).stat().st_size
        except (OSError, ValueError):
            continue
    disk_status = "UNKNOWN" if free is None else "INCOMPATIBLE" if free < free_threshold else "AVAILABLE"
    add("workspace-disk", "Free bytes for input staging; peak build demand is not measured", _record(disk_status,
        f"Observed {free} bytes; archive staging requires at least {free_threshold}" if free is not None else "Disk probe was unavailable", path=output))
    status = "AVAILABLE" if all(row["optional"] or row["status"] == "AVAILABLE" for row in rows) else "BLOCKED"
    return {"profile": profile, "status": status, "host": host, "resources": resources, "dependencies": rows}
