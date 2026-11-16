"""Bounded, allowlisted, read-only host/tool probes for W2 and W3.

Only installed system tools are probed. Reports distinguish presence from
compatibility and execution permission; they never establish a successful build.
"""

from __future__ import annotations

import ctypes
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import stat
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "config/dependencies/manifest-v1.json"
STATUSES = ("AVAILABLE", "MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED")

# No input can add a command, argument or package query to this allowlist.
COMMANDS = {
    "gcc": ("gcc", "--version"),
    "gcc-target": ("gcc", "-dumpmachine"),
    "make": ("make", "--version"),
    "ld": ("ld", "--version"),
    "ar": ("ar", "--version"),
    "ranlib": ("ranlib", "--version"),
    "strip": ("strip", "--version"),
    "pkg-config": ("pkg-config", "--version"),
    "perl": ("perl", "-e", "print $^V"),
    "tar": ("tar", "--version"),
    "patch": ("patch", "--version"),
    "git": ("git", "--version"),
    "bash": ("bash", "--version"),
    "msys2-gcc": ("gcc", "--version"),
    "msys2-gcc-target": ("gcc", "-dumpmachine"),
    "pkg-openssl": ("pkg-config", "--modversion", "openssl"),
    "pkg-libevent": ("pkg-config", "--modversion", "libevent"),
    "pkg-zlib": ("pkg-config", "--modversion", "zlib"),
    "pkg-libseccomp": ("pkg-config", "--modversion", "libseccomp"),
}
LIBRARIES = {name: "pkg-" + name for name in
             ("openssl", "libevent", "zlib", "libseccomp")}


def normalize_arch(value):
    value = value.lower()
    return {"amd64": "x86_64", "x86_64": "x86_64", "armv7l": "armv7",
            "armv7": "armv7", "aarch64": "arm64", "arm64": "arm64"}.get(value, "unknown")


def dependency_check(check_id, target_id, status, requirement, reason, *,
                     detected_version=None, architecture=None, optional=False,
                     owner="os-manual", approval="none", required_for=("developer",),
                     certainty=None, expected_sha256=None, observed_sha256=None,
                     remediation_plan_ref=None, evidence_refs=()):
    """Create the complete shared W1 DependencyCheck shape (no path leakage)."""
    if status not in STATUSES:
        raise ValueError("unsupported dependency status")
    return {
        "schemaVersion": 1, "id": check_id, "targetId": target_id, "status": status,
        "requirement": requirement, "detectedVersion": detected_version,
        "architecture": architecture, "requiredFor": list(required_for),
        "optional": optional, "owner": owner, "approval": approval,
        "expectedSha256": expected_sha256, "observedSha256": observed_sha256,
        "reason": reason, "remediationPlanRef": remediation_plan_ref,
        "evidenceRefs": list(evidence_refs),
        "certainty": certainty or ("verified" if status == "AVAILABLE" else
                                   "BLOCKED" if status == "BLOCKED" else "UNVERIFIED"),
    }


def _record(status, reason, *, version=None, architecture=None, code=None,
            command=(), path=None):
    return {"status": status, "version": version, "architecture": architecture,
            "rawExitCode": code, "command": list(command),
            "path": str(path) if path is not None else None, "reason": reason}


def _is_within(path, root):
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def _version_tuple(value):
    match = re.search(r"(?<!\d)(\d+)\.(\d+)(?:\.(\d+))?", value or "")
    return tuple(int(x or 0) for x in match.groups()) if match else None


class ReadOnlyProbe:
    """No arbitrary command execution, installers, network or WSL startup.

    Executable lookup ignores relative PATH entries and blocks files outside
    standard OS/tool installation roots. A discovered untrusted file is BLOCKED,
    never executed. Symlinks resolve before this check. Caller-controlled source
    or build directories cannot become trusted merely by adding them to PATH.
    """

    timeout_seconds = 3
    max_output_chars = 65536

    def host(self):
        os_name = {"Windows": "windows", "Linux": "linux"}.get(platform.system(), "unknown")
        facts = {
            "os": os_name, "architecture": normalize_arch(platform.machine()),
            "osVersion": platform.release(), "distroId": None, "distroVersion": None,
            "libcName": None, "libcVersion": None, "logicalCpus": os.cpu_count(),
            "isWsl": False, "probeStatus": "AVAILABLE" if os_name != "unknown" else "UNKNOWN",
            "reason": "Host facts read without launching external commands",
            "factStatuses": {"distribution": "UNKNOWN", "libc": "UNKNOWN", "wsl": "UNKNOWN"},
        }
        if os_name == "linux":
            try:
                fields = {}
                for line in Path("/etc/os-release").read_text(encoding="utf-8").splitlines():
                    if "=" in line and not line.lstrip().startswith("#"):
                        key, value = line.split("=", 1)
                        tokens = shlex.split(value)
                        fields[key] = tokens[0] if len(tokens) == 1 else value
                facts["distroId"] = fields.get("ID")
                facts["distroVersion"] = fields.get("VERSION_ID")
                if facts["distroId"] and facts["distroVersion"]:
                    facts["factStatuses"]["distribution"] = "AVAILABLE"
            except PermissionError:
                facts["factStatuses"]["distribution"] = "BLOCKED"
                facts["reason"] += "; Linux distribution probe denied"
            except (OSError, ValueError):
                facts["reason"] += "; Linux distribution facts unavailable"
            try:
                libc = os.confstr("CS_GNU_LIBC_VERSION")
                if libc and libc.startswith("glibc "):
                    facts["libcName"], facts["libcVersion"] = libc.split(" ", 1)
                    facts["factStatuses"]["libc"] = "AVAILABLE"
            except PermissionError:
                facts["factStatuses"]["libc"] = "BLOCKED"
            except (OSError, ValueError, AttributeError):
                pass
            try:
                release = Path("/proc/sys/kernel/osrelease").read_text(encoding="ascii").lower()
                facts["isWsl"] = "microsoft" in release or "wsl" in release
                facts["factStatuses"]["wsl"] = "AVAILABLE"
            except PermissionError:
                facts["isWsl"] = None
                facts["factStatuses"]["wsl"] = "BLOCKED"
            except OSError:
                facts["isWsl"] = None
        return facts

    def resources(self, path=None):
        result = {"logicalCpus": os.cpu_count(), "ramTotalBytes": None,
                  "ramAvailableBytes": None, "diskFreeBytes": None, "diskTotalBytes": None,
                  "probeStatuses": {"memory": "UNKNOWN", "disk": "UNKNOWN"},
                  "reasons": {"memory": "Memory probe is unsupported on this OS", "disk": None}}
        try:
            if sys.platform == "win32":
                class MemoryStatus(ctypes.Structure):
                    _fields_ = [("length", ctypes.c_ulong), ("load", ctypes.c_ulong),
                                ("totalPhysical", ctypes.c_ulonglong), ("availPhysical", ctypes.c_ulonglong),
                                ("totalPageFile", ctypes.c_ulonglong), ("availPageFile", ctypes.c_ulonglong),
                                ("totalVirtual", ctypes.c_ulonglong), ("availVirtual", ctypes.c_ulonglong),
                                ("availExtendedVirtual", ctypes.c_ulonglong)]
                memory = MemoryStatus()
                memory.length = ctypes.sizeof(memory)
                if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(memory)):
                    raise OSError("GlobalMemoryStatusEx unavailable")
                result["ramTotalBytes"] = memory.totalPhysical
                result["ramAvailableBytes"] = memory.availPhysical
            elif sys.platform.startswith("linux"):
                meminfo = {}
                for line in Path("/proc/meminfo").read_text(encoding="ascii").splitlines():
                    key, value = line.split(":", 1)
                    fields = value.split()
                    if fields and fields[0].isdigit():
                        meminfo[key] = int(fields[0]) * 1024
                result["ramTotalBytes"] = meminfo.get("MemTotal")
                result["ramAvailableBytes"] = meminfo.get("MemAvailable")
            if result["ramTotalBytes"] is not None and result["ramAvailableBytes"] is not None:
                result["probeStatuses"]["memory"] = "AVAILABLE"
                result["reasons"]["memory"] = "Physical RAM snapshot; not a reservation or a cgroup/WSL allocation guarantee"
        except (OSError, ValueError, AttributeError):
            result["probeStatuses"]["memory"] = "BLOCKED"
            result["reasons"]["memory"] = "Memory snapshot could not be read"
        try:
            usage = shutil.disk_usage(Path(path) if path is not None else Path.cwd())
            result["diskFreeBytes"], result["diskTotalBytes"] = usage.free, usage.total
            result["probeStatuses"]["disk"] = "AVAILABLE"
            result["reasons"]["disk"] = "Existing volume free-space snapshot; no directory created or reserved"
        except OSError:
            result["probeStatuses"]["disk"] = "BLOCKED"
            result["reasons"]["disk"] = "Disk usage probe denied or path unavailable"
        return result

    def _trusted_roots(self):
        if sys.platform == "win32":
            roots = [Path(sys.executable).resolve().parent,
                     Path("C:/msys64/usr/bin"), Path("C:/msys64/ucrt64/bin")]
            for name in ("SystemRoot", "ProgramFiles", "ProgramFiles(x86)"):
                if os.environ.get(name):
                    roots.append(Path(os.environ[name]))
            return [p.resolve() for p in roots]
        return [Path(p).resolve() for p in ("/usr/bin", "/bin", "/usr/local/bin")]

    def _candidates(self, name, executable):
        if name.startswith("msys2-"):
            if sys.platform != "win32":
                return []
            candidates = [Path("C:/msys64/ucrt64/bin/gcc.exe")]
        else:
            candidates = []
            if sys.platform == "win32":
                candidates.extend(Path(prefix) / (executable + ".exe") for prefix in
                                  ("C:/msys64/ucrt64/bin", "C:/msys64/usr/bin"))
            for part in os.environ.get("PATH", "").split(os.pathsep):
                if part and Path(part).is_absolute():
                    candidates.append(Path(part) / (executable + (".exe" if sys.platform == "win32" else "")))
        return candidates

    def _lookup(self, name, executable):
        denied = None
        for candidate in self._candidates(name, executable):
            try:
                metadata = candidate.stat()
            except FileNotFoundError:
                continue
            except PermissionError:
                denied = candidate
                continue
            resolved = candidate.resolve()
            trusted = any(_is_within(resolved, root) for root in self._trusted_roots())
            in_project = _is_within(resolved, ROOT.resolve())
            wsl_launcher = (sys.platform == "win32" and executable == "bash" and
                            not _is_within(resolved, Path("C:/msys64").resolve()))
            is_program = (stat.S_ISREG(metadata.st_mode) and (sys.platform == "win32" and resolved.suffix.lower() == ".exe"
                                                or sys.platform != "win32" and os.access(resolved, os.X_OK)))
            if trusted and not in_project and not wsl_launcher and is_program:
                return resolved
            denied = resolved
        if denied is not None:
            raise PermissionError("Discovered executable is outside approved system tool roots or is a blocked launcher")
        return None

    def _execute(self, command):
        # Do not inherit startup/plugin/compiler/package overrides (PERL5OPT,
        # MAKEFLAGS, LD_PRELOAD, BASH_ENV, PKG_CONFIG_PATH, GIT_CONFIG_* ...).
        # Probes run from their installed tool directory, never a source tree.
        environment = {"PATH": os.pathsep.join(str(p) for p in self._trusted_roots()),
                       "LANG": "C", "LC_ALL": "C", "TZ": "UTC"}
        if sys.platform == "win32":
            for name in ("SystemRoot", "WINDIR", "SystemDrive"):
                if os.environ.get(name):
                    environment[name] = os.environ[name]
        return subprocess.run(list(command), stdin=subprocess.DEVNULL, capture_output=True,
                              text=True, encoding="utf-8", errors="replace", shell=False,
                              timeout=self.timeout_seconds, check=False, env=environment,
                              cwd=str(Path(command[0]).parent))

    def tool(self, name):
        if name in ("python", "python3"):
            return _record("AVAILABLE", "Running Python interpreter observed directly",
                           version=platform.python_version(), architecture=normalize_arch(platform.machine()),
                           path=Path(sys.executable).resolve())
        if name not in COMMANDS:
            raise ValueError("Tool probe is not allowlisted")
        spec = COMMANDS[name]
        try:
            executable = self._lookup(name, spec[0])
        except (OSError, ValueError):
            return _record("BLOCKED", "Executable lookup denied or executable is outside approved system tool roots")
        if executable is None:
            return _record("MISSING", "Allowlisted installed tool was not found")
        command = (str(executable), *spec[1:])
        try:
            process = self._execute(command)
        except (OSError, subprocess.TimeoutExpired):
            return _record("BLOCKED", "Probe denied, unavailable or exceeded its deadline", command=command, path=executable)
        output = (process.stdout or "") + "\n" + (process.stderr or "")
        if process.returncode != 0:
            if sys.platform == "win32" and (process.returncode & 0xFFFFFFFF) in (
                    0xC0000022, 0xC0000135, 0xC0000142):
                return _record("BLOCKED", "Windows denied process access or could not initialize the installed tool",
                               code=process.returncode, command=command, path=executable)
            if name.startswith("pkg-") and "not found" in output.lower():
                return _record("MISSING", "Requested pkg-config library metadata was not found",
                               code=process.returncode, command=command, path=executable)
            return _record("UNKNOWN", "Installed tool returned a nonzero probe exit code",
                           code=process.returncode, command=command, path=executable)
        if len(output) > self.max_output_chars:
            return _record("UNKNOWN", "Probe output exceeded the accepted diagnostic limit",
                           code=0, command=command, path=executable)
        if name.endswith("-target"):
            target = process.stdout.strip()
            if not re.fullmatch(r"[A-Za-z0-9_+.]+(?:-[A-Za-z0-9_+.]+){1,4}", target):
                return _record("UNKNOWN", "Compiler target output could not be interpreted",
                               code=0, command=command, path=executable)
            architecture = normalize_arch(target.split("-", 1)[0])
            return _record("AVAILABLE", "Compiler target observed", version=target,
                           architecture=None if architecture == "unknown" else architecture,
                           code=0, command=command, path=executable)
        parsed = _version_tuple(output)
        if parsed is None:
            return _record("UNKNOWN", "Installed tool emitted no recognized version",
                           code=0, command=command, path=executable)
        if name in ("gcc", "msys2-gcc"):
            if "clang" in output.lower():
                return _record("INCOMPATIBLE", "Compiler banner identifies Clang rather than GNU GCC",
                               version=".".join(str(x) for x in parsed), code=0, command=command, path=executable)
            if not re.search(r"(?im)^(?:[^\s]+[/-])?[^\s]*gcc(?:\.exe)?(?:[\s(]|$)", output):
                return _record("UNKNOWN", "Compiler version does not establish GNU GCC family",
                               version=".".join(str(x) for x in parsed), code=0, command=command, path=executable)
        if name == "make" and not re.search(r"(?m)^GNU Make\s", output):
            return _record("INCOMPATIBLE", "Tool banner does not identify required GNU Make",
                           version=".".join(str(x) for x in parsed), code=0, command=command, path=executable)
        return _record("AVAILABLE", "Installed allowlisted tool version observed",
                       version=".".join(str(x) for x in parsed), code=0, command=command, path=executable)

    def library(self, name):
        if name not in LIBRARIES:
            raise ValueError("Library probe is not allowlisted")
        return self.tool(LIBRARIES[name])


def _classified_tool(spec, observed):
    status = observed["status"]
    if status not in STATUSES:
        raise ValueError("Probe returned an unsupported status")
    reason = observed["reason"]
    if status == "AVAILABLE":
        if spec.get("target") is not None and observed.get("version") != spec["target"]:
            status, reason = "INCOMPATIBLE", "Observed compiler target differs from required target"
        elif spec.get("minimumVersion") is not None:
            actual = _version_tuple(observed.get("version"))
            minimum = tuple(spec["minimumVersion"])
            if actual is None:
                status, reason = "UNKNOWN", "Version observation is not sufficient for compatibility"
            elif actual < minimum + (0,) * (len(actual) - len(minimum)):
                status, reason = "INCOMPATIBLE", "Observed version is below the planning policy minimum"
    return status, reason


def collect_report(profile="developer", target_id="host", probe=None, resource_path=None):
    """Collect facts and W1 DependencyCheck records; stdout consumers own storage.

    Inject a probe object implementing host/resources/tool for deterministic
    offline tests. No install/download/compile operation exists in this module.
    """
    if profile != "developer":
        raise ValueError("Only the developer diagnostic profile is implemented")
    # W1 owns shared validation. Missing contracts block before any tool runs.
    from scripts.contracts import require_valid
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1:
        raise ValueError("Unsupported dependency policy version")
    probe = probe or ReadOnlyProbe()
    host = probe.host()
    resources = probe.resources(resource_path)
    checks, observations = [], {}
    host_status = host["probeStatus"]
    if host_status == "AVAILABLE" and host["os"] not in ("windows", "linux"):
        host_status = "INCOMPATIBLE"
    checks.append(dependency_check("host-os", target_id, host_status,
                                   "Native Windows or Linux diagnostics host",
                                   host["reason"], detected_version=host.get("osVersion"),
                                   architecture=None if host["architecture"] == "unknown" else host["architecture"]))
    arch_status = "AVAILABLE" if host["architecture"] == "x86_64" else (
        "UNKNOWN" if host["architecture"] == "unknown" else "INCOMPATIBLE")
    checks.append(dependency_check("host-architecture", target_id, arch_status,
                                   "x86_64 host for the current native desktop planning scope",
                                   "Detected host machine architecture" if arch_status == "AVAILABLE" else
                                   "Host machine architecture is unknown or outside the selected scope",
                                   architecture=None if host["architecture"] == "unknown" else host["architecture"]))
    for spec in manifest["tools"]:
        if host["os"] not in spec["oses"]:
            continue
        observed = probe.tool(spec["id"])
        observations[spec["id"]] = observed
        status, reason = _classified_tool(spec, observed)
        checks.append(dependency_check(spec["id"], target_id, status, spec["requirement"], reason,
                                       detected_version=observed.get("version"),
                                       architecture=observed.get("architecture"),
                                       optional=spec["optional"], owner=spec["owner"],
                                       approval=spec["approval"],
                                       remediation_plan_ref="dependency-policy-v1" if status != "AVAILABLE" else None))
    counts = {status: sum(check["status"] == status for check in checks) for status in STATUSES}
    unavailable = [check["id"] for check in checks if not check["optional"] and check["status"] != "AVAILABLE"]
    for check in checks:
        require_valid("DependencyCheck", check)
    return {
        "schemaVersion": 1, "kind": "dependency-environment-report",
        "scope": "read-only-discovery", "profile": profile,
        "host": host, "resources": resources, "checks": checks, "observations": observations,
        "summary": {"counts": counts, "readyForScope": not unavailable,
                    "requiredNotAvailable": unavailable},
        "limitations": ["Readiness concerns only listed developer checks; no build or runtime qualification",
                        "RAM/disk are observations, not reservations; cgroup/WSL limits require separate review",
                        "Tool output is captured in memory before size checking, with a three-second execution deadline",
                        "No physical board, Yocto build capacity, installation, download or public-network test required"],
    }
