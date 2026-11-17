"""Synthetic ELF and manifest fixtures; never compile, package or run Tor."""
import copy
import hashlib
import json
import os
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

from scripts.native_linux import packaging as package


def synthetic_elf(*, kind=3, machine=62, executable_stack=False, relro=True,
                  now=True, needed="libc.so.6", search_path=False, textrel=False,
                  flag_textrel=False, overlapping_load=False):
    """Small structural ELF fixture, deliberately not executable program code."""
    data = bytearray(1024)
    ident = b"\x7fELF\x02\x01\x01" + bytes(9)
    struct.pack_into("<16sHHIQQQIHHHHHH", data, 0, ident, kind, machine, 1, 0x400200,
                     64, 0, 0, 64, 56, 6 if overlapping_load else 5, 0, 0, 0)
    def segment(index, tag, flags, offset, address, size):
        struct.pack_into("<IIQQQQQQ", data, 64 + index * 56, tag, flags, offset,
                         address, address, size, size, 8)
    segment(0, 1, 5, 0, 0x400000, 1024)
    loader = package.LOADER.encode() + b"\0"
    data[416:416 + len(loader)] = loader
    segment(1, 3, 4, 416, 0x400000 + 416, len(loader))
    strings = b"\0" + needed.encode() + b"\0"
    data[800:800 + len(strings)] = strings
    tags = [(5, 0x400000 + 800), (10, len(strings)), (1, 1), (30, (8 if now else 0) | (4 if flag_textrel else 0))]
    if flag_textrel:
        tags.append((0x6FFFFFFB, 1))  # DF_1_NOW preserves binding independently.
    if search_path:
        tags.append((29, 1))
    if textrel:
        tags.append((22, 0))
    tags.append((0, 0))
    for index, (tag, value) in enumerate(tags):
        struct.pack_into("<qQ", data, 512 + index * 16, tag, value)
    segment(2, 2, 6, 512, 0x400000 + 512, len(tags) * 16)
    segment(3, 0x6474E551, 7 if executable_stack else 6, 0, 0, 0)
    segment(4, 0x6474E552 if relro else 4, 4, 512, 0x400000 + 512, 16)
    if overlapping_load:
        segment(5, 1, 6, 512, 0x400000 + 512, 8)
    return bytes(data)


def synthetic_loader_tag(tag, name, string_table):
    data = bytearray(synthetic_elf())
    offset = 800 + len(b"\0libc.so.6\0")
    name = name.encode("ascii") + b"\0"
    data[offset:offset + len(name)] = name
    struct.pack_into("<qQ", data, 512 + 16, 10, offset - 800 + len(name))
    struct.pack_into("<qQ", data, 512 + 4 * 16, tag, offset - 800)
    # Extend PT_DYNAMIC for the new tag and the following DT_NULL.
    struct.pack_into("<QQ", data, 64 + 2 * 56 + 32, 6 * 16, 6 * 16)
    if string_table != "valid":
        # Remove DT_NEEDED so the old inventory would skip string validation.
        struct.pack_into("<qQ", data, 512 + 2 * 16, 21, 0)  # DT_DEBUG
        if string_table == "missing":
            struct.pack_into("<qQ", data, 512, 21, 0)
        else:
            struct.pack_into("<qQ", data, 512, 5, 0)
    return bytes(data)


class ElfTests(unittest.TestCase):
    def test_structural_hardening_inventory_never_claims_runtime_qualification(self):
        result = package.inspect_elf(synthetic_elf())
        self.assertEqual(result["metadataStatus"], "PASS")
        self.assertEqual(result["needed"], ["libc.so.6"])
        self.assertTrue(all(result["checks"].values()))
        self.assertEqual(result["runtimeQualification"], "NOT RUN")
        self.assertEqual(result["abiSymbolVersionCheck"], "NOT RUN")

    def test_missing_hardening_and_unreviewed_runtime_dependency_fail(self):
        for arguments, check in (({"kind": 2}, "pie"), ({"executable_stack": True}, "nonExecutableStack"),
                                 ({"relro": False}, "relro"), ({"now": False}, "bindNow"),
                                 ({"needed": "libssl.so.3"}, "onlyDeclaredSystemDependencies"),
                                 ({"search_path": True}, "noEmbeddedSearchPaths"),
                                 ({"textrel": True}, "noTextRelocations")):
            with self.subTest(arguments=arguments):
                result = package.inspect_elf(synthetic_elf(**arguments))
                self.assertFalse(result["checks"][check])
                self.assertEqual(result["metadataStatus"], "FAIL")

    def test_bad_architecture_malformed_offsets_and_dependency_paths_are_refused(self):
        fixtures = [b"not an ELF", synthetic_elf(machine=183), synthetic_elf(needed="../libc.so.6")]
        invalid = bytearray(synthetic_elf())
        struct.pack_into("<Q", invalid, 32, 0xFFFFFFFFFFFFFFFF)
        fixtures.append(bytes(invalid))
        invalid = bytearray(synthetic_elf())
        struct.pack_into("<qQ", invalid, 512 + 3 * 16, 5, 123)  # Duplicate DT_STRTAB
        fixtures.append(bytes(invalid))
        for fixture in fixtures:
            with self.subTest(prefix=fixture[:16]):
                with self.assertRaises(ValueError):
                    package.inspect_elf(fixture)

    def test_flag_only_text_relocations_are_rejected_with_independent_immediate_binding(self):
        result = package.inspect_elf(synthetic_elf(now=False, flag_textrel=True))
        self.assertTrue(result["checks"]["bindNow"])
        self.assertFalse(result["checks"]["noTextRelocations"])
        self.assertEqual(result["metadataStatus"], "FAIL")

    def test_truncated_string_and_dynamic_tables_are_refused(self):
        invalid = bytearray(synthetic_elf())
        struct.pack_into("<qQ", invalid, 512 + 2 * 16, 1, 100000)
        with self.assertRaises(ValueError):
            package.inspect_elf(bytes(invalid))
        invalid = bytearray(synthetic_elf())
        struct.pack_into("<qQ", invalid, 512 + 4 * 16, 99, 99)
        with self.assertRaisesRegex(ValueError, "unterminated"):
            package.inspect_elf(bytes(invalid))

    def test_relro_requires_one_load_to_map_the_whole_range(self):
        relro_address = 64 + 4 * 56 + 16
        relro_file_offset = 64 + 4 * 56 + 8
        relro_memory_size = 64 + 4 * 56 + 40
        for field, value in ((relro_address, 0x700000),
                             (relro_memory_size, 513),
                             (relro_file_offset, 520)):
            with self.subTest(field=field, value=value):
                invalid = bytearray(synthetic_elf())
                struct.pack_into("<Q", invalid, field, value)
                result = package.inspect_elf(bytes(invalid))
                self.assertFalse(result["checks"]["relro"])
                self.assertEqual(result["metadataStatus"], "FAIL")
        result = package.inspect_elf(synthetic_elf(overlapping_load=True))
        self.assertFalse(result["checks"]["relro"])
        self.assertEqual(result["metadataStatus"], "FAIL")

    def test_relro_rejects_overflowing_addresses_and_malformed_alignment(self):
        relro_address = 64 + 4 * 56 + 16
        load_alignment = 64 + 48
        for field, value, reason in ((relro_address, (1 << 64) - 8, "virtual range"),
                                     (load_alignment, 3, "alignment"),
                                     (load_alignment, 1 << 23, "alignment")):
            with self.subTest(field=field, value=value):
                invalid = bytearray(synthetic_elf())
                struct.pack_into("<Q", invalid, field, value)
                with self.assertRaisesRegex(ValueError, reason):
                    package.inspect_elf(bytes(invalid))


class PackagePlanTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.root = Path(__file__).resolve().parents[1]
        self.policy = package.native._load_json(self.root / "config/native-linux/v1.json")
        self.pins = []
        for name in package.executor.SOURCE_ORDER:
            content = ("Synthetic " + name + " license").encode()
            pin = {"name": name, "directory": name, "license_file": "LICENSE",
                   "license_sha256": hashlib.sha256(content).hexdigest()}
            path = self.folder / "sources" / name / "LICENSE"
            path.parent.mkdir(parents=True)
            path.write_bytes(content)
            self.pins.append(pin)
        binary = self.folder / "prefix/bin/tor"
        binary.parent.mkdir(parents=True)
        binary.write_bytes(synthetic_elf())
        inputs = {"sources": self.pins, "sourceDateEpoch": 1234}
        digest = package.native.canonical_hash(inputs)
        request = json.loads((self.root / "tests/fixtures/contracts/valid/BuildRequest.json").read_text())
        request.update(mode="build", adapterId=package.executor.ADAPTER_ID,
                       targetId=self.policy["targetId"], inputSha256=digest)
        commands = []
        for index, name in enumerate(name + "-" + action for name in package.executor.SOURCE_ORDER
                                     for action in ("configure", "make", "install")):
            path = self.folder / "logs" / f"{index:02d}-{name}.log"
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(b"")  # A legitimately silent successful command.
            commands.append({"name": name, "returnCode": 0, "rawExitCode": 0,
                             "terminationSignal": None, "reason": None,
                             "logSha256": package.native.hash_file(path)})
        self.manifest = {"schemaVersion": 1, "kind": package.executor.ADAPTER_ID,
                         "status": "PASS", "finishedAtUtc": "2000-01-01T00:00:00Z",
                         "inputs": inputs, "executionInputSha256": digest, "buildRequest": request,
                         "commands": commands, "outputs": [{"pathRef": "prefix/bin/tor",
                            "bytes": binary.stat().st_size, "sha256": package.native.hash_file(binary)}]}
        self.write_manifest()
        self.source_pins = patch.object(package.native, "load_source_pins", return_value=(self.pins, {}, 1234))
        self.source_pins.start()
        self.addCleanup(self.source_pins.stop)

    def write_manifest(self):
        path = self.folder / "build-manifest.json"
        path.write_text(json.dumps(self.manifest), encoding="utf-8")
        self.expected = package.native.hash_file(path)

    def plan(self):
        return package.package_plan(self.folder, self.expected)

    def test_only_exact_binary_and_pinned_licenses_are_planned_without_writes(self):
        private = self.folder / "prefix" / "DataDirectory"
        private.mkdir()
        (private / "private_key").write_bytes(b"synthetic secret marker, must never be included")
        before = {str(p.relative_to(self.folder)): package.native.hash_file(p)
                  for p in self.folder.rglob("*") if p.is_file()}
        result = self.plan()
        self.assertEqual(len(result["files"]), 6)
        self.assertEqual(result["files"][0]["destinationRef"], "bin/tor")
        self.assertTrue(all(item["destinationRef"].startswith("licenses/") for item in result["files"][1:]))
        self.assertFalse(any("private" in item["sourceRef"].lower() for item in result["files"]))
        self.assertEqual(result["packagingExecutionStatus"], "NOT RUN")
        self.assertIsNone(result["packageArtifact"])
        self.assertFalse(result["readyForDistribution"])
        after = {str(p.relative_to(self.folder)): package.native.hash_file(p)
                 for p in self.folder.rglob("*") if p.is_file()}
        self.assertEqual(before, after)

    def test_untrusted_manifest_reference_and_modified_binary_or_license_are_refused(self):
        with self.assertRaisesRegex(ValueError, "verified reference"):
            package.package_plan(self.folder, "0" * 64)
        path = self.folder / "prefix/bin/tor"
        path.write_bytes(b"modified")
        with self.assertRaisesRegex(ValueError, "digest/size"):
            self.plan()
        path.write_bytes(synthetic_elf())
        (self.folder / "sources/tor/LICENSE").write_bytes(b"different license")
        with self.assertRaisesRegex(ValueError, "license digest"):
            self.plan()

    def test_failed_incomplete_or_mismatched_input_build_is_refused(self):
        original = copy.deepcopy(self.manifest)
        for mutate in (lambda d: d.update(status="RUNNING"), lambda d: d["commands"].pop(),
                       lambda d: d["commands"][0].update(returnCode=7, rawExitCode=7),
                       lambda d: d["inputs"].update(sourceDateEpoch=5678),
                       lambda d: d["buildRequest"].update(mode="plan")):
            with self.subTest(mutation=mutate):
                self.manifest = copy.deepcopy(original)
                mutate(self.manifest)
                self.write_manifest()
                with self.assertRaises(ValueError):
                    self.plan()

    def test_changed_log_and_unsafe_output_reference_are_refused(self):
        path = self.folder / "logs/00-zlib-configure.log"
        path.write_bytes(b"modified")
        with self.assertRaisesRegex(ValueError, "log digest"):
            self.plan()
        path.write_bytes(b"")
        self.manifest["outputs"][0]["pathRef"] = "../outside"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "expected Tor"):
            self.plan()

    @unittest.skipUnless(os.name == "posix", "POSIX package input symlinks")
    def test_symlinked_binary_is_refused(self):
        path = self.folder / "prefix/bin/tor"
        original = path.with_suffix(".original")
        path.rename(original)
        path.symlink_to(original)
        with self.assertRaises(ValueError):
            self.plan()

    def test_invalid_elf_metadata_never_creates_a_distribution_ready_plan(self):
        path = self.folder / "prefix/bin/tor"
        for binary in (synthetic_elf(executable_stack=True), synthetic_elf(now=False, flag_textrel=True)):
            with self.subTest(binary=binary[512:608]):
                path.write_bytes(binary)
                self.manifest["outputs"][0]["sha256"] = package.native.hash_file(path)
                self.write_manifest()
                with self.assertRaisesRegex(ValueError, "ELF metadata"):
                    self.plan()

    def test_unsupported_loader_tags_block_packaging(self):
        path = self.folder / "prefix/bin/tor"
        tags = {0x6FFFFEFC: "DT_AUDIT", 0x6FFFFEFB: "DT_DEPAUDIT",
                0x7FFFFFFD: "DT_AUXILIARY", 0x7FFFFFFF: "DT_FILTER"}
        for tag, tag_name in tags.items():
            for name, table in (("libexample.so", "valid"), ("/fixture/libexample.so", "valid"),
                                ("../libexample.so", "valid"), ("libexample.so", "missing"),
                                ("libexample.so", "invalid")):
                with self.subTest(tag=tag_name, name=name, table=table):
                    path.write_bytes(synthetic_loader_tag(tag, name, table))
                    self.manifest["outputs"][0]["sha256"] = package.native.hash_file(path)
                    self.write_manifest()
                    with self.assertRaisesRegex(ValueError, tag_name):
                        self.plan()


if __name__ == "__main__":
    unittest.main()
