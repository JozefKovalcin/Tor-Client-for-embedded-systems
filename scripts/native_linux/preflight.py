"""Inspect cached source inputs and report a native Linux plan without mutation.

Only W2 allowlisted version/target/pkg-config probes are executed. This module
does not fetch/extract sources, create outputs, compile, install, or run Tor.
"""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import tarfile

from scripts.contracts import require_valid
from scripts.dependencies.checker import ReadOnlyProbe, collect_report, dependency_check


ROOT = Path(__file__).resolve().parents[2]
POLICY_PATH = ROOT / "config/native-linux/v1.json"
ADAPTER_ID = "native-linux-preflight-v1"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
SOURCE_MAP = {"openssl": "libopenssl", "zlib": "libzlib"}


def canonical_hash(value):
    """Hash reproducible JSON only; NaN and Infinity are never valid inputs."""
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                     ensure_ascii=False, allow_nan=False).encode()).hexdigest()


def hash_file(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


class ArchiveChangedError(ValueError):
    """The cache no longer identifies the bytes inspected by this probe."""


def _file_state(metadata):
    state = (metadata.st_dev, metadata.st_ino, metadata.st_mode, metadata.st_size,
             metadata.st_mtime_ns)
    # Windows path stat and descriptor fstat can disagree on ctime semantics.
    # Identity plus full content digests remain mandatory on every platform.
    return state + ((metadata.st_ctime_ns,) if os.name == "posix" else ())


def _hash_stream(stream):
    """Hash the already-open archive, without resolving its pathname again."""
    stream.seek(0)
    digest = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
    return digest.hexdigest()


def _assert_archive_unchanged(path, source, initial, digest):
    # A second complete digest detects in-place writes even if size and mtime
    # are preserved. Identity checks also catch atomic pathname replacement.
    if (_hash_stream(source) != digest or
            _file_state(os.fstat(source.fileno())) != _file_state(initial) or
            _file_state(path.stat(follow_symlinks=False)) != _file_state(initial)):
        raise ArchiveChangedError("archive changed during verification")


def _load_json(path):
    def reject(value):
        raise ValueError("non-finite JSON constant: " + value)
    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=reject)


def _check(check_id, status, reason, policy, **kwargs):
    result = dependency_check(check_id, policy["targetId"], status, check_id, reason, **kwargs)
    require_valid("DependencyCheck", result)
    return result


def load_source_pins(root=ROOT, policy=None):
    """Reuse existing archive/license pins; never import Windows-only patches."""
    root = Path(root)
    policy = policy or _load_json(root / "config/native-linux/v1.json")
    manifest_path = root / policy["sourceManifest"]
    manifest = _load_json(manifest_path)
    if manifest.get("schema_version") != 1:
        raise ValueError("unsupported existing source manifest version")
    epoch = manifest.get("source_date_epoch")
    if not isinstance(epoch, int) or isinstance(epoch, bool) or epoch < 0:
        raise ValueError("existing source manifest lacks a valid source_date_epoch")
    by_name = {item["name"]: item for item in manifest["sources"]}
    if len(by_name) != len(manifest["sources"]):
        raise ValueError("duplicate existing source names")
    embedded_lock_path = root / "config/sources.lock.json"
    embedded_lock = _load_json(embedded_lock_path)
    if (embedded_lock.get("tor_version") != by_name["tor"]["version"]
            or embedded_lock.get("source_date_epoch") != epoch):
        raise ValueError("native source manifest disagrees with existing Tor version/epoch lock")
    pins = []
    inputs = {policy["sourceManifest"]: hash_file(manifest_path),
              "config/sources.lock.json": hash_file(embedded_lock_path)}
    for name in policy["sourceNames"]:
        source = by_name[name]
        hash_name = SOURCE_MAP.get(name, name)
        rel = f"config/upstream/{hash_name}.hash"
        text = (root / rel).read_text(encoding="utf-8")
        entries = _hash_entries(text)
        if entries.get(source["archive"]) != source["sha256"]:
            raise ValueError(f"{name}: archive pin disagrees with upstream hash")
        if entries.get(source["license_file"]) != source["license_sha256"]:
            raise ValueError(f"{name}: license pin disagrees with upstream hash")
        for field in ("archive", "directory", "license_file"):
            value = source[field]
            if not value or "/" in value or "\\" in value or value in (".", ".."):
                raise ValueError(f"{name}: unsafe source manifest path")
        if not SHA256.fullmatch(source["sha256"]) or not SHA256.fullmatch(source["license_sha256"]):
            raise ValueError(f"{name}: invalid source digest")
        pins.append({key: source[key] for key in (
            "name", "version", "archive", "directory", "sha256", "license_file", "license_sha256", "license")})
        inputs[rel] = hash_file(root / rel)
    rel = policy["linuxAdditionalHashFile"]
    entries = _hash_entries((root / rel).read_text(encoding="utf-8"))
    archives = [(name, digest) for name, digest in entries.items()
                if re.fullmatch(r"libseccomp-[0-9.]+\.tar\.gz", name)]
    if len(archives) != 1 or "LICENSE" not in entries:
        raise ValueError("ambiguous libseccomp archive/license pin")
    archive, digest = archives[0]
    version = archive.removeprefix("libseccomp-").removesuffix(".tar.gz")
    pins.append({"name": "libseccomp", "version": version, "archive": archive,
                 "directory": "libseccomp-" + version, "sha256": digest,
                 "license_file": "LICENSE", "license_sha256": entries["LICENSE"],
                 "license": "Existing libseccomp LICENSE; no license reclassification"})
    inputs[rel] = hash_file(root / rel)
    return pins, inputs, epoch


def _hash_entries(text):
    entries = {}
    for line in text.splitlines():
        parts = line.split()
        if len(parts) == 3 and parts[0] == "sha256":
            if not SHA256.fullmatch(parts[1]) or parts[2] in entries:
                raise ValueError("invalid or duplicate upstream hash entry")
            entries[parts[2]] = parts[1]
    return entries


def inspect_sources(cache, pins, policy):
    """Read explicit cache archives and license members, without extraction."""
    cache = Path(cache)
    checks, observations = [], []
    for pin in pins:
        path = cache / pin["archive"]
        row = {"name": pin["name"], "archive": pin["archive"],
               "expectedSha256": pin["sha256"], "observedSha256": None,
               "expectedLicenseSha256": pin["license_sha256"], "observedLicenseSha256": None}
        status, reason = "UNKNOWN", "source observation not completed"
        try:
            initial = path.stat(follow_symlinks=False)
            mode = initial.st_mode
            if stat.S_ISLNK(mode) or path.parent.resolve() != cache.resolve():
                status, reason = "INCOMPATIBLE", "archive symlink or cache escape refused"
            elif not stat.S_ISREG(mode):
                status, reason = "INCOMPATIBLE", "archive is not a regular file"
            else:
                flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
                descriptor = os.open(path, flags)
                # No Python read buffer may replay pre-mutation bytes during
                # the final digest after tarfile seeks through the archive.
                with os.fdopen(descriptor, "rb", buffering=0) as source:
                    opened = os.fstat(source.fileno())
                    if not stat.S_ISREG(opened.st_mode) or _file_state(opened) != _file_state(initial):
                        raise ArchiveChangedError("archive identity changed before verification")
                    row["observedSha256"] = _hash_stream(source)
                    if row["observedSha256"] != pin["sha256"]:
                        status, reason = "INCOMPATIBLE", "archive SHA-256 mismatch; original retained"
                    else:
                        source.seek(0)
                        with tarfile.open(fileobj=source, mode="r:*") as archive:
                            member_name = pin["directory"] + "/" + pin["license_file"]
                            members = [member for member in archive if member.name == member_name]
                            if len(members) != 1:
                                raise ValueError("license member missing or duplicated")
                            member = members[0]
                            if not member.isfile() or member.size <= 0 or member.size > 1024 * 1024:
                                raise ValueError("license member must be a regular file <=1 MiB")
                            stream = archive.extractfile(member)
                            if stream is None:
                                raise ValueError("license member unreadable")
                            with stream:
                                row["observedLicenseSha256"] = hashlib.sha256(stream.read(1024 * 1024 + 1)).hexdigest()
                        _assert_archive_unchanged(path, source, opened, row["observedSha256"])
                        if row["observedLicenseSha256"] != pin["license_sha256"]:
                            status, reason = "INCOMPATIBLE", "license SHA-256 mismatch"
                        else:
                            status, reason = "AVAILABLE", "stable archive and license match existing SHA-256 pins"
        except ArchiveChangedError:
            status, reason = "INCOMPATIBLE", "archive changed during verification; cache retained"
        except FileNotFoundError:
            status, reason = "MISSING", "pinned archive absent or disappeared; no download attempted"
        except (PermissionError, OSError) as exc:
            status, reason = "BLOCKED", "archive read blocked: " + type(exc).__name__
        except (ValueError, KeyError, tarfile.TarError):
            status, reason = "INCOMPATIBLE", "invalid archive or missing/unsafe license member"
        row["status"] = status
        observations.append(row)
        checks.append(_check("source-" + pin["name"], status, reason, policy,
                             owner="app-local", expected_sha256=pin["sha256"],
                             observed_sha256=row["observedSha256"]))
    return checks, observations


def validate_path_syntax(source_dir, output_dir, archive_cache):
    """Portable lexical validation; filesystem containment is checked separately."""
    errors = []
    paths = {"source": source_dir, "output": output_dir, "cache": archive_cache}
    for label, value in paths.items():
        if not isinstance(value, str) or not value.startswith("/") or value.startswith("//"):
            errors.append(label + ": absolute native Linux path required")
            continue
        if (any(char.isspace() for char in value) or "\\" in value or ":" in value
                or "\x00" in value or any(ord(char) < 32 for char in value)
                or ".." in value.split("/")):
            errors.append(label + ": whitespace, control, traversal or Windows path refused")
        pure = PurePosixPath(value)
        if pure == PurePosixPath("/") or re.match(r"^/mnt/[a-z](?:/|$)", str(pure), re.I):
            errors.append(label + ": root or Windows-mounted build path refused")
        if label == "cache" and any(part in ("runtime", "DataDirectory", "hidden_service", "onion-service", ".git")
                                     for part in pure.parts):
            errors.append(label + ": private runtime or Git state is not a source cache")
    if not errors:
        source, output, cache = [PurePosixPath(paths[name]) for name in ("source", "output", "cache")]
        if output == source or output in source.parents or source in output.parents:
            errors.append("output: must be outside source tree")
        if output == cache or output in cache.parents or cache in output.parents:
            errors.append("output: must be outside source cache")
    return errors


def inspect_paths(source_dir, output_dir, archive_cache, policy):
    errors = validate_path_syntax(source_dir, output_dir, archive_cache)
    if errors:
        return [_check("native-paths", "INCOMPATIBLE", "; ".join(errors), policy)]
    if os.name != "posix":
        return [_check("native-paths", "BLOCKED", "native Linux filesystem inspection requires Linux", policy)]
    try:
        for value in (source_dir, output_dir, archive_cache):
            path = Path(value)
            for parent in (path, *path.parents):
                try:
                    mode = parent.stat(follow_symlinks=False).st_mode
                except FileNotFoundError:
                    continue
                if stat.S_ISLNK(mode):
                    raise ValueError("symlink components refused in native build paths")
            if str(path.resolve()) != str(PurePosixPath(value)):
                raise ValueError("path resolution differs from reviewed lexical path")
        if not stat.S_ISDIR(Path(source_dir).stat().st_mode):
            return [_check("native-paths", "MISSING", "source checkout directory absent", policy)]
        if not stat.S_ISDIR(Path(archive_cache).stat().st_mode):
            return [_check("native-paths", "MISSING", "explicit source archive cache absent", policy)]
        output = Path(output_dir)
        try:
            output.stat(follow_symlinks=False)
        except FileNotFoundError:
            pass
        else:
            raise ValueError("output already exists; preflight never overwrites or reuses it")
        parent = output.parent
        while True:
            try:
                parent_mode = parent.stat().st_mode
                break
            except FileNotFoundError:
                parent = parent.parent
        if not stat.S_ISDIR(parent_mode) or not os.access(parent, os.W_OK | os.X_OK):
            return [_check("native-paths", "BLOCKED", "output ancestor lacks write/search access", policy)]
    except FileNotFoundError:
        return [_check("native-paths", "MISSING", "source or cache path absent", policy)]
    except (PermissionError, OSError):
        return [_check("native-paths", "BLOCKED", "filesystem inspection denied", policy)]
    except ValueError as exc:
        return [_check("native-paths", "INCOMPATIBLE", str(exc), policy)]
    return [_check("native-paths", "AVAILABLE", "native paths have no symlinks; output does not exist", policy)]


def _version_tuple(value):
    match = re.match(r"^(\d+)\.(\d+)", value or "")
    return tuple(map(int, match.groups())) if match else None


def _w2_tool_snapshot(environment, policy):
    """Use one classified W2 observation; python3 names the running interpreter."""
    decisions = {}
    for check in environment.get("checks", []):
        require_valid("DependencyCheck", check)
        if check["id"] in decisions:
            raise ValueError("duplicate W2 dependency check IDs")
        decisions[check["id"]] = check
    snapshot = {}
    for name in policy["requiredTools"]:
        dependency_id = "python" if name == "python3" else name
        decision = decisions.get(dependency_id)
        raw = environment.get("observations", {}).get(dependency_id, {})
        snapshot[name] = {
            **raw,
            "status": decision["status"] if decision else "UNKNOWN",
            "version": decision["detectedVersion"] if decision else None,
            "architecture": decision["architecture"] if decision else None,
            "reason": decision["reason"] if decision else "W2 classification not available",
        }
    return snapshot


def evaluate_environment(environment, observations, policy):
    """Assess v1 requirements without using another profile's readiness gate."""
    checks = []
    host, resources = environment["host"], environment["resources"]
    expected = {"os": policy["os"], "architecture": policy["architecture"],
                "distroId": policy["distroId"], "distroVersion": policy["distroVersion"],
                "libcName": policy["abi"], "libcVersion": policy["abiVersion"]}
    for key, wanted in expected.items():
        actual = host.get(key)
        status = "UNKNOWN" if actual is None or actual == "unknown" else ("AVAILABLE" if actual == wanted else "INCOMPATIBLE")
        if host.get("probeStatus") == "BLOCKED" and status == "UNKNOWN":
            status = "BLOCKED"
        fact = "libc" if key.startswith("libc") else "distribution" if key.startswith("distro") else None
        if fact and host.get("factStatuses", {}).get(fact) == "BLOCKED" and status == "UNKNOWN":
            status = "BLOCKED"
        checks.append(_check("native-" + key.lower(), status,
                             f"requires {key}={wanted}; observed {actual!r}", policy,
                             detected_version=str(actual) if actual is not None else None,
                             architecture=host.get("architecture") if host.get("architecture") != "unknown" else None))
    # WSL may plan this ABI, but it is explicitly not native runtime qualification.
    # Raw executable availability never overrides W2's compatibility decision.
    classified = _w2_tool_snapshot(environment, policy)
    for name in policy["requiredTools"]:
        item = classified[name]
        status = item.get("status", "UNKNOWN")
        version = item.get("version")
        reason = item.get("reason", "tool not observed")
        if status == "AVAILABLE":
            if name == "gcc-target" and version != policy["compilerTarget"]:
                status, reason = "INCOMPATIBLE", "compiler target does not match selected native GNU ABI"
            elif name == "gcc":
                parsed = _version_tuple(version)
                if parsed is None:
                    status, reason = "UNKNOWN", "cannot parse GCC version"
                elif parsed[0] < policy["minimumGccMajor"]:
                    status, reason = "INCOMPATIBLE", "GCC below selected v1 planning policy"
            elif name == "python3":
                parsed = _version_tuple(version)
                if parsed is None:
                    status, reason = "UNKNOWN", "cannot parse Python version"
                elif parsed < tuple(policy["minimumPython"]):
                    status, reason = "INCOMPATIBLE", "Python below v1 tooling requirement"
        checks.append(_check("native-tool-" + name, status, reason, policy,
                             detected_version=version, architecture=item.get("architecture"), owner="package-manager"))
    for key, budget in (("ramAvailableBytes", policy["ramBudgetBytes"]),
                        ("diskFreeBytes", policy["diskBudgetBytes"])):
        value = resources.get(key)
        valid = isinstance(value, int) and not isinstance(value, bool) and value >= 0
        if not valid:
            probe_key = "memory" if key.startswith("ram") else "disk"
            status = "BLOCKED" if resources.get("probeStatuses", {}).get(probe_key) == "BLOCKED" else "UNKNOWN"
            reason = "resource observation unavailable; no guessed budget pass"
        else:
            status = "AVAILABLE" if value >= budget else "INCOMPATIBLE"
            reason = f"observed {value} B; native-source reservation {budget} B (not Yocto)"
        checks.append(_check("native-" + key.lower(), status, reason, policy))
    return checks


def apply_linux_memory_limits(environment):
    """Bound physical available memory by current/ancestor cgroup-v2 limits.

    No files are written and a denied/unsupported limit probe cannot yield an
    available RAM gate. Resource snapshots are not a reservation held by a job.
    """
    if environment["host"].get("os") != "linux":
        return
    resources = environment["resources"]
    resources["physicalRamAvailableBytes"] = resources.get("ramAvailableBytes")
    try:
        entries = Path("/proc/self/cgroup").read_text(encoding="ascii").splitlines()
        v2 = [line.split(":", 2)[2] for line in entries if line.startswith("0::")]
        if len(v2) != 1 or ".." in v2[0].split("/"):
            raise ValueError("current cgroup v2 location unknown")
        base = Path("/sys/fs/cgroup")
        mounts = Path("/proc/self/mountinfo").read_text(encoding="ascii").splitlines()
        mapping = []
        for line in mounts:
            fields = line.split()
            if "-" in fields:
                split = fields.index("-")
                if len(fields) > split + 1 and fields[split + 1] == "cgroup2" and fields[4] == base.as_posix():
                    mapping.append(fields[3])
        if mapping != ["/"]:
            raise ValueError("cgroup mount/root namespace mapping is unsupported")
        controllers = (base / "cgroup.controllers").read_text(encoding="ascii").split()
        if "memory" not in controllers:
            raise ValueError("cgroup v2 memory controller root could not be established")
        current = base / v2[0].lstrip("/")
        available = []
        while True:
            try:
                limit = (current / "memory.max").read_text(encoding="ascii").strip()
            except FileNotFoundError:
                if current != base:
                    raise
                # Verified cgroup-v2 root has no resource-controller limit files.
                limit = "max"
            if limit != "max":
                usage = (current / "memory.current").read_text(encoding="ascii").strip()
                if not limit.isdecimal() or not usage.isdecimal():
                    raise ValueError("unparseable cgroup memory limit")
                available.append(max(0, int(limit) - int(usage)))
            if current == base:
                break
            if base not in current.parents:
                raise ValueError("cgroup namespace path outside expected root")
            current = current.parent
        physical = resources.get("ramAvailableBytes")
        if available and isinstance(physical, int) and not isinstance(physical, bool):
            resources["ramAvailableBytes"] = min(physical, *available)
        resources["cgroupMemoryProbe"] = {"status": "AVAILABLE", "availableBytes": min(available) if available else None,
                                            "reason": "read current and ancestor cgroup v2 limits; no reservation held"}
    except PermissionError:
        resources["ramAvailableBytes"] = None
        resources["probeStatuses"]["memory"] = "BLOCKED"
        resources["cgroupMemoryProbe"] = {"status": "BLOCKED", "availableBytes": None, "reason": "cgroup read denied"}
    except (OSError, ValueError, IndexError):
        resources["ramAvailableBytes"] = None
        resources["probeStatuses"]["memory"] = "UNKNOWN"
        resources["cgroupMemoryProbe"] = {"status": "UNKNOWN", "availableBytes": None,
                                            "reason": "cgroup v2 memory limits could not be established"}


def _git_provenance(root, executable):
    """Read Git metadata only, disabling optional locks and external diff hooks."""
    # A supplied GIT_DIR/WORK_TREE/INDEX_FILE or injected config must never
    # redirect source provenance away from the reviewed checkout.
    roots = [str(Path(executable).parent)]
    if sys.platform == "win32":
        system_root = os.environ.get("SystemRoot", "C:/Windows")
        roots.append(str(Path(system_root) / "System32"))
    else:
        roots.extend(("/usr/bin", "/bin"))
    env = {"PATH": os.pathsep.join(roots), "LANG": "C", "LC_ALL": "C", "TZ": "UTC",
           "GIT_OPTIONAL_LOCKS": "0", "GIT_PAGER": "cat", "GIT_CONFIG_NOSYSTEM": "1",
           "GIT_CONFIG_GLOBAL": os.devnull}
    if sys.platform == "win32":
        for name in ("SystemRoot", "WINDIR", "SystemDrive"):
            if os.environ.get(name):
                env[name] = os.environ[name]
    common = [executable, "-c", "core.fsmonitor=false", "-C", str(root)]
    def run(args):
        return subprocess.run(common + args, check=True, capture_output=True, text=True,
                              stdin=subprocess.DEVNULL, shell=False,
                              timeout=5, env=env).stdout.strip()
    commit = run(["rev-parse", "HEAD"])
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("source commit is not a SHA-1 commit identifier")
    dirty = bool(run(["status", "--porcelain", "--untracked-files=normal", "--ignore-submodules=all"]))
    return {"commit": commit, "dirty": dirty}


def _nearest_existing(path):
    path = Path(path)
    while True:
        try:
            path.stat()
            return path
        except FileNotFoundError:
            if path == path.parent:
                return path
            path = path.parent
        except OSError:
            return path


def create_plan(*, policy, checks, source_pins, source_observations, source_inputs,
                tool_observations, provenance, paths, adapter_sha256, source_date_epoch):
    """Pure plan assembly. The returned stages are descriptive, never executable."""
    for check in checks:
        require_valid("DependencyCheck", check)
    expected_ids = {"native-paths", "native-libc-headers", "source-provenance",
                    "native-ramavailablebytes", "native-diskfreebytes"}
    expected_ids.update("native-" + name.lower() for name in (
        "os", "architecture", "distroId", "distroVersion", "libcName", "libcVersion"))
    expected_ids.update("native-tool-" + name for name in policy["requiredTools"])
    expected_ids.update("source-" + pin["name"] for pin in source_pins)
    observed_ids = [check["id"] for check in checks]
    if len(set(observed_ids)) != len(observed_ids):
        raise ValueError("duplicate preflight check IDs")
    required = [item["id"] for item in checks if not item["optional"] and item["status"] != "AVAILABLE"]
    required.extend(sorted(expected_ids - set(observed_ids)))
    for check in checks:
        if check["id"] in expected_ids and check["optional"]:
            required.append(check["id"] + ":required-check-marked-optional")
    observed_sources = {row["name"]: row for row in source_observations}
    for pin in source_pins:
        row = observed_sources.get(pin["name"], {})
        if (row.get("status") != "AVAILABLE" or row.get("observedSha256") != pin["sha256"]
                or row.get("observedLicenseSha256") != pin["license_sha256"]):
            required.append("source-evidence-" + pin["name"])
    if provenance is None or provenance.get("dirty") is not False:
        required.append("source-provenance")
    tool_manifest = {name: {key: tool_observations.get(name, {}).get(key)
                            for key in ("status", "version", "architecture", "path")}
                     for name in policy["requiredTools"]}
    source_lock_sha = canonical_hash({"inputs": source_inputs, "sources": source_pins,
                                     "patches": [], "trust": policy["sourceTrust"]})
    config_sha = canonical_hash(policy)
    tool_sha = canonical_hash(tool_manifest)
    reproducible = {"source": provenance, "sourceLockSha256": source_lock_sha,
                    "configSha256": config_sha, "toolchainManifestSha256": tool_sha,
                    "adapterSha256": adapter_sha256,
                    "targetId": policy["targetId"], "abi": policy["abi"], "abiVersion": policy["abiVersion"],
                    "sourceDateEpoch": source_date_epoch, "patches": []}
    input_sha = canonical_hash(reproducible)
    request = None
    if not required:
        request = {"schemaVersion": 1, "requestId": "native-plan-" + input_sha[:20],
                   "targetId": policy["targetId"], "imageProfile": None, "existingProfileId": None,
                   "sourceCommit": provenance["commit"], "dirtyInputManifestRef": None,
                   "sourceLockSha256": source_lock_sha, "configSha256": config_sha,
                   "toolchainManifestSha256": tool_sha, "inputSha256": input_sha,
                   "adapterId": ADAPTER_ID, "outputScopeRef": "native-linux/" + input_sha[:20],
                   "mode": "plan", "maxJobs": policy["maxJobs"], "ramBudgetBytes": policy["ramBudgetBytes"],
                   "diskBudgetBytes": policy["diskBudgetBytes"], "cancellationId": "native-plan-" + input_sha[:20],
                   "consentReceiptRefs": []}
        require_valid("BuildRequest", request)
    stages = [
        {"id": "verify-inputs", "dependsOn": [], "purpose": "Verify source/archive/license/config/toolchain hashes"},
        {"id": "prepare-private-prefix", "dependsOn": ["verify-inputs"],
         "purpose": "Future isolated output/prefix for selected native ABI; no directory created by preflight"}]
    for pin in source_pins:
        if pin["name"] != "tor":
            stages.append({"id": "source-" + pin["name"], "dependsOn": ["prepare-private-prefix"],
                           "purpose": "Future source build into private prefix; never substitute installed system library",
                           "source": {key: pin[key] for key in ("name", "version", "archive", "sha256", "license_sha256")}})
    libraries = [stage["id"] for stage in stages if stage["id"].startswith("source-")]
    tor = next(pin for pin in source_pins if pin["name"] == "tor")
    stages.extend([
        {"id": "configure-tor", "dependsOn": libraries,
         "purpose": "Future upstream Linux configure against private prefix with preserved hardening/seccomp and client/onion features",
         "source": {key: tor[key] for key in ("name", "version", "archive", "sha256", "license_sha256")}},
        {"id": "compile-tor", "dependsOn": ["configure-tor"], "purpose": "Future native source compilation; executor outside W3"},
        {"id": "inspect-elf-and-licenses", "dependsOn": ["compile-tor"], "purpose": "Future ELF ABI/hardening/dependency closure and license manifest"},
        {"id": "offline-runtime", "dependsOn": ["inspect-elf-and-licenses"], "purpose": "Future matching-host offline controls/configuration checks"},
        {"id": "public-qualification", "dependsOn": ["offline-runtime"], "purpose": "Future separately authorized unchanged nine-assertion gate"}])
    for stage in stages:
        stage.update(status="NOT RUN", executionSupported=False)
    return {"schemaVersion": 1, "kind": "native-linux-build-plan", "adapterId": ADAPTER_ID,
            "targetId": policy["targetId"], "scope": "read-only-preflight", "readyForBuildPlan": not required,
            "executionStatus": "NOT RUN", "buildRequest": request, "requiredNotAvailable": required,
            "checks": checks, "paths": paths, "sourceObservations": source_observations,
            "inputMetadata": reproducible, "inputSha256": input_sha, "toolchainManifest": tool_manifest,
            "stages": stages,
            "limitations": ["Planning only; no source extraction, configure, compilation or tests executed",
                            "Installed system libraries are optional diagnostics; production dependencies must be private source-built",
                            "Existing archive/license SHA-256 trust only; detached signatures not independently verified",
                            "No native Linux runtime qualification, physical board or Yocto evidence",
                            "No Windows-only Tor patch is applied; Linux source patch set is empty"]}


def preflight(source_dir, output_dir, archive_cache, *, root=ROOT, probe=None):
    root = Path(root)
    policy = _load_json(root / "config/native-linux/v1.json")
    if policy.get("schemaVersion") != 1:
        raise ValueError("unsupported native policy schemaVersion")
    probe = probe or ReadOnlyProbe()
    environment = collect_report(profile="developer", target_id=policy["targetId"], probe=probe,
                                 resource_path=_nearest_existing(output_dir))
    apply_linux_memory_limits(environment)
    observations = _w2_tool_snapshot(environment, policy)
    checks = evaluate_environment(environment, observations, policy)
    path_checks = inspect_paths(source_dir, output_dir, archive_cache, policy)
    checks.extend(path_checks)
    pins, source_inputs, source_epoch = load_source_pins(root, policy)
    if any(check["status"] != "AVAILABLE" for check in path_checks):
        source_checks = [_check("source-" + pin["name"], "BLOCKED", "unsafe native path; archive inspection not attempted", policy,
                                owner="app-local", expected_sha256=pin["sha256"]) for pin in pins]
        source_observations = []
    else:
        source_checks, source_observations = inspect_sources(archive_cache, pins, policy)
    checks.extend(source_checks)
    # Query installed package metadata only, never use it as pinned runtime closure.
    for name in policy["systemLibraryObservations"]:
        item = probe.library(name)
        checks.append(_check("system-library-" + name, item["status"], item["reason"], policy,
                             detected_version=item.get("version"), optional=True, owner="package-manager"))
    if environment["host"].get("os") == "linux":
        try:
            headers = (Path("/usr/include/stdio.h"), Path("/usr/include/x86_64-linux-gnu/sys/types.h"))
            status = "AVAILABLE" if all(stat.S_ISREG(path.stat().st_mode) for path in headers) else "MISSING"
            checks.append(_check("native-libc-headers", status, "selected ABI system libc header presence (no compile/link proof)", policy))
        except FileNotFoundError:
            checks.append(_check("native-libc-headers", "MISSING", "selected ABI libc headers absent", policy))
        except (PermissionError, OSError):
            checks.append(_check("native-libc-headers", "BLOCKED", "libc header inspection denied", policy))
    else:
        checks.append(_check("native-libc-headers", "BLOCKED", "Linux libc headers not probed on this host", policy))
    try:
        if any(check["status"] != "AVAILABLE" for check in path_checks):
            raise ValueError("source paths not approved for Git inspection")
        if Path(source_dir).resolve() != root.resolve():
            raise ValueError("source checkout differs from policy/source input root")
        git = observations.get("git", {})
        if git.get("status") != "AVAILABLE" or not git.get("path"):
            raise ValueError("trusted Git probe unavailable")
        provenance = _git_provenance(source_dir, git["path"])
    except (OSError, ValueError, subprocess.SubprocessError):
        provenance = None
    checks.append(_check("source-provenance", "AVAILABLE" if provenance and provenance["dirty"] is False else "BLOCKED",
                         "clean committed source required for reproducible request; no working files modified", policy))
    plan = create_plan(policy=policy, checks=checks, source_pins=pins, source_observations=source_observations,
                       source_inputs=source_inputs, tool_observations=observations, provenance=provenance,
                       paths={"source": source_dir, "output": output_dir, "cache": archive_cache},
                       adapter_sha256=hash_file(Path(__file__)), source_date_epoch=source_epoch)
    plan["environment"] = environment
    return plan
