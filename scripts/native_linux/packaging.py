"""Read-only W6 ELF/runtime inventory and packaging design; creates no package.

Never invokes ldd or the inspected executable. A binary and license inventory
is not clean-host runtime qualification or permission to distribute state.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct

from scripts.contracts import require_valid
from . import executor
from . import preflight as native


KIND = "native-linux-package-plan-v1"
MAX_ELF_BYTES = 256 * 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024
SYSTEM_SONAMES = frozenset(("libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0",
                           "librt.so.1", "ld-linux-x86-64.so.2", "libgcc_s.so.1"))
LOADER = "/lib64/ld-linux-x86-64.so.2"
SONAME = re.compile(r"^[A-Za-z0-9_+.-]+\.so(?:\.[0-9]+)*$")


def stable_bytes(path, maximum, *, allow_empty=False):
    """Bounded immutable read, refusing links and changed file identity/state."""
    path = Path(path)
    before = path.stat(follow_symlinks=False)
    if (not stat.S_ISREG(before.st_mode) or before.st_size < (0 if allow_empty else 1)
            or before.st_size > maximum):
        raise ValueError("input must be a bounded nonempty regular file")
    fd = os.open(path, os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0))
    with os.fdopen(fd, "rb", buffering=0) as source:
        opened = os.fstat(source.fileno())
        if executor._same_file(opened) != executor._same_file(before):
            raise ValueError("input identity changed before read")
        value = source.read(maximum + 1)
        if (len(value) != opened.st_size or len(value) > maximum or
                executor._same_file(os.fstat(source.fileno())) != executor._same_file(opened) or
                executor._same_file(path.stat(follow_symlinks=False)) != executor._same_file(before)):
            raise ValueError("input changed during read")
    return value


def scoped_file(root, relative):
    pure = PurePosixPath(relative)
    if (not relative or pure.is_absolute() or "\\" in relative or ":" in relative or
            any(part in ("", ".", "..") for part in relative.split("/"))):
        raise ValueError("unsafe package input reference")
    root = Path(root).absolute()
    path = root.joinpath(*pure.parts)
    for component in (root, *path.parents):
        if component == root or root in component.parents:
            if stat.S_ISLNK(component.stat(follow_symlinks=False).st_mode):
                raise ValueError("symlink component in package input")
    if path.resolve(strict=True) != path.absolute():
        raise ValueError("package input resolves outside the reviewed path")
    return path


def inspect_elf(data):
    """Decode bounded ELF64 LE x86_64 metadata without executing any code."""
    if len(data) < 64 or len(data) > MAX_ELF_BYTES or data[:7] != b"\x7fELF\x02\x01\x01":
        raise ValueError("expected bounded ELF64 little-endian version-1 input")
    elf = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
    _, kind, machine, version, entry, phoff, _, _, ehsize, phsize, phcount, _, _, _ = elf
    if machine != 62 or version != 1 or ehsize != 64 or phsize != 56 or not 0 < phcount <= 256:
        raise ValueError("unsupported ELF ABI or program header table")
    def read(offset, length):
        if offset < 0 or length < 0 or offset + length > len(data):
            raise ValueError("ELF range outside file")
        return data[offset:offset + length]
    read(phoff, phsize * phcount)
    segments = []
    for index in range(phcount):
        segment = struct.unpack_from("<IIQQQQQQ", data, phoff + index * phsize)
        ptype, flags, offset, address, _, file_size, memory_size, _ = segment
        read(offset, file_size)
        if file_size > memory_size:
            raise ValueError("ELF segment file size exceeds memory size")
        segments.append({"type": ptype, "flags": flags, "offset": offset,
                         "address": address, "size": file_size})
    def one(ptype):
        matches = [item for item in segments if item["type"] == ptype]
        if len(matches) > 1:
            raise ValueError("duplicate critical ELF segment")
        return matches[0] if matches else None
    interp, dynamic, stack, relro = [one(value) for value in (3, 2, 0x6474E551, 0x6474E552)]
    interpreter = None
    if interp:
        raw = read(interp["offset"], interp["size"])
        if len(raw) > 256 or not raw.endswith(b"\0") or b"\0" in raw[:-1]:
            raise ValueError("invalid ELF interpreter string")
        interpreter = raw[:-1].decode("ascii")
    tags = []
    if dynamic:
        if dynamic["size"] % 16 or dynamic["size"] > 1024 * 1024:
            raise ValueError("invalid dynamic segment size")
        for offset in range(dynamic["offset"], dynamic["offset"] + dynamic["size"], 16):
            tag, value = struct.unpack_from("<qQ", data, offset)
            if tag == 0:
                break
            tags.append((tag, value))
        else:
            raise ValueError("unterminated ELF dynamic table")
    def unique_tag(tag):
        values = [value for current, value in tags if current == tag]
        if len(values) > 1:
            raise ValueError("duplicate ELF dynamic property")
        return values[0] if values else None
    needed, string_address, string_size = [], unique_tag(5), unique_tag(10)
    needed_offsets = [value for tag, value in tags if tag == 1]
    if needed_offsets:
        if string_address is None or string_size is None or not 0 < string_size <= 1024 * 1024:
            raise ValueError("missing bounded dynamic string table")
        mappings = [item for item in segments if item["type"] == 1 and
                    item["address"] <= string_address and
                    string_address + string_size <= item["address"] + item["size"]]
        if len(mappings) != 1:
            raise ValueError("ambiguous ELF dynamic string mapping")
        segment = mappings[0]
        strings = read(segment["offset"] + string_address - segment["address"], string_size)
        for offset in needed_offsets:
            if offset >= len(strings) or strings.find(b"\0", offset) == -1:
                raise ValueError("unterminated ELF dependency name")
            name = strings[offset:strings.index(b"\0", offset)].decode("ascii")
            if not SONAME.fullmatch(name) or name in needed:
                raise ValueError("unsafe or duplicate ELF dependency")
            needed.append(name)
    flags, flags1 = unique_tag(30) or 0, unique_tag(0x6FFFFFFB) or 0
    bind_now = any(tag == 24 for tag, _ in tags) or bool(flags & 8) or bool(flags1 & 1)
    checks = {"elf64X86_64": True, "pie": kind == 3 and entry != 0 and interpreter == LOADER,
              "nonExecutableStack": stack is not None and not bool(stack["flags"] & 1),
              "relro": relro is not None and relro["size"] > 0, "bindNow": bind_now,
              "noTextRelocations": not any(tag == 22 for tag, _ in tags) and not bool(flags & 4),
              "noEmbeddedSearchPaths": not any(tag in (15, 29) for tag, _ in tags),
              "onlyDeclaredSystemDependencies": all(name in SYSTEM_SONAMES for name in needed)}
    return {"interpreter": interpreter, "needed": sorted(needed), "checks": checks,
            "metadataStatus": "PASS" if all(checks.values()) else "FAIL",
            "runtimeQualification": "NOT RUN", "abiSymbolVersionCheck": "NOT RUN"}


def package_plan(build_root, expected_manifest_sha256, *, root=native.ROOT):
    """Inspect verified build outputs; propose an exact allowlist, never tar them."""
    if not native.SHA256.fullmatch(expected_manifest_sha256 or ""):
        raise ValueError("verified expected build-manifest SHA-256 required")
    build_root = Path(build_root).absolute()
    raw = stable_bytes(scoped_file(build_root, "build-manifest.json"), MAX_MANIFEST_BYTES)
    if hashlib.sha256(raw).hexdigest() != expected_manifest_sha256:
        raise ValueError("build manifest differs from verified reference")
    manifest = json.loads(raw)
    if (manifest.get("schemaVersion") != 1 or manifest.get("kind") != executor.ADAPTER_ID or
            manifest.get("status") != "PASS" or not manifest.get("finishedAtUtc")):
        raise ValueError("incomplete or unsuccessful source build cannot be packaged")
    inputs = manifest["inputs"]
    digest = native.canonical_hash(inputs)
    if digest != manifest["executionInputSha256"]:
        raise ValueError("build input manifest hash mismatch")
    request = manifest["buildRequest"]
    require_valid("BuildRequest", request)
    policy = native._load_json(Path(root) / "config/native-linux/v1.json")
    pins, _, epoch = native.load_source_pins(root, policy)
    if (inputs["sources"] != pins or inputs["sourceDateEpoch"] != epoch or
            request["inputSha256"] != digest or request["targetId"] != policy["targetId"] or
            request["mode"] != "build" or request["adapterId"] != executor.ADAPTER_ID):
        raise ValueError("build source pins, epoch, target or request disagree with selected policy")
    expected_names = [name + "-" + action for name in executor.SOURCE_ORDER
                      for action in ("configure", "make", "install")]
    commands = manifest["commands"]
    if [command["name"] for command in commands] != expected_names:
        raise ValueError("incomplete build command outcomes")
    for index, command in enumerate(commands):
        if (type(command["returnCode"]) is not int or type(command["rawExitCode"]) is not int or
                command["returnCode"] != 0 or command["rawExitCode"] != 0 or
                command["terminationSignal"] is not None or command["reason"] is not None):
            raise ValueError("unsuccessful build command")
        log = scoped_file(build_root, f"logs/{index:02d}-{command['name']}.log")
        if hashlib.sha256(stable_bytes(log, executor.MAX_LOG_BYTES, allow_empty=True)).hexdigest() != command["logSha256"]:
            raise ValueError("build log digest mismatch")
    outputs = manifest["outputs"]
    if len(outputs) != 1 or outputs[0]["pathRef"] != "prefix/bin/tor":
        raise ValueError("only the explicitly expected Tor build output is a runtime candidate")
    binary = stable_bytes(scoped_file(build_root, "prefix/bin/tor"), MAX_ELF_BYTES)
    binary_sha = hashlib.sha256(binary).hexdigest()
    if binary_sha != outputs[0]["sha256"] or len(binary) != outputs[0]["bytes"]:
        raise ValueError("runtime candidate digest/size mismatch")
    elf = inspect_elf(binary)
    if elf["metadataStatus"] != "PASS":
        raise ValueError("ELF metadata hardening or dependency inventory failed")
    files = [{"sourceRef": "prefix/bin/tor", "destinationRef": "bin/tor",
              "sha256": binary_sha, "bytes": len(binary)}]
    for pin in pins:
        relative = "sources/" + pin["directory"] + "/" + pin["license_file"]
        content = stable_bytes(scoped_file(build_root, relative), 1024 * 1024)
        if hashlib.sha256(content).hexdigest() != pin["license_sha256"]:
            raise ValueError("pinned license digest mismatch")
        files.append({"sourceRef": relative, "destinationRef": "licenses/" + pin["name"] + "-LICENSE.txt",
                      "sha256": pin["license_sha256"], "bytes": len(content)})
    return {"schemaVersion": 1, "kind": KIND, "targetId": policy["targetId"],
            "buildManifestSha256": expected_manifest_sha256, "inputSha256": digest,
            "files": files, "elfInventory": elf,
            "distributionTarget": {"architecture": "x86_64", "distro": "Ubuntu 24.04",
                                   "libc": "glibc 2.39", "loader": LOADER,
                                   "systemDependencies": elf["needed"]},
            "packagingExecutionStatus": "NOT RUN", "packageArtifact": None,
            "readyForDistribution": False, "containsPrivateState": False,
            "plannedChecks": ["Verify GLIBC symbol version requirements on the selected ABI",
                              "Verify full dependency closure on a supported clean host",
                              "Verify stack protector/fortify symbols and final link map",
                              "Audit transitive/bundled license notices before distribution",
                              "Copy only the exact hashed allowlist into a fresh private package tree",
                              "Create deterministic archive with SOURCE_DATE_EPOCH and normalized ownership",
                              "Reinspect final archive allowlist and digests; reject any private state",
                              "Emit W1 BuildArtifact only after a real package exists",
                              "Execute authorized offline runtime smoke; online qualification remains separate"],
            "excluded": ["home", "tmp", "logs", "snapshots", "DataDirectory", "hidden_service",
                         "control_auth_cookie", "credentials", "private identities", "host configuration"],
            "limitations": ["Read-only inspection/design, no package written or executable launched",
                            "Metadata PASS does not prove runtime, symbol ABI or complete ELF hardening",
                            "The expected manifest digest must come from a trusted producer/reference",
                            "Private prefix source libraries are statically linked; no universal ABI portability",
                            "Future packaging must snapshot/reverify the exact consumed files"]}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build-root", required=True)
    parser.add_argument("--expected-build-manifest-sha256", required=True)
    args = parser.parse_args(argv)
    try:
        result = package_plan(args.build_root, args.expected_build_manifest_sha256)
        code = 0
    except (OSError, ValueError, KeyError, TypeError, UnicodeError, struct.error) as exc:
        result = {"schemaVersion": 1, "kind": KIND, "planningStatus": "BLOCKED",
                  "packagingExecutionStatus": "NOT RUN", "packageArtifact": None,
                  "readyForDistribution": False, "reason": type(exc).__name__ + ": " + str(exc)}
        code = 1
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
