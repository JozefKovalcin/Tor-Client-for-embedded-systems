"""Contract boundaries only: synthetic fixtures, no Tor/build/network activity."""
import copy
import hashlib
import io
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import contracts

FIXTURES = ROOT / "tests/fixtures/contracts"


def fixture(name):
    return json.loads((FIXTURES / "valid" / (name + ".json")).read_text(encoding="utf-8"))


class ContractTests(unittest.TestCase):
    def test_all_eight_valid_and_invalid_contract_fixtures(self):
        for name in contracts.CONTRACT_NAMES:
            with self.subTest(contract=name):
                valid = fixture(name)
                before = copy.deepcopy(valid)
                self.assertEqual(contracts.validate_contract(name, valid), [])
                contracts.require_valid(name, valid)
                self.assertEqual(valid, before)
                invalid = contracts.read_json_file(FIXTURES / "invalid" / (name + ".json"))
                self.assertTrue(contracts.validate_contract(name, invalid))
                with self.assertRaises(ValueError):
                    contracts.require_valid(name, invalid)

    def test_unknown_contract_and_version_fail_closed(self):
        for name in ("../../policy", "targetdescriptor", "FutureContract"):
            self.assertTrue(contracts.validate_contract(name, {}))
        for name in contracts.CONTRACT_NAMES:
            for version in (True, 0, 2, "1", None):
                with self.subTest(contract=name, version=version):
                    value = fixture(name)
                    value["schemaVersion"] = version
                    self.assertTrue(contracts.validate_contract(name, value))

    def test_required_and_unknown_members_are_rejected(self):
        for name in contracts.CONTRACT_NAMES:
            value = fixture(name)
            value.pop("schemaVersion")
            self.assertTrue(contracts.validate_contract(name, value))
            value = fixture(name)
            value["privateKey"] = "never-export-this"
            self.assertTrue(contracts.validate_contract(name, value))

    def test_dependency_states_are_distinct_and_never_test_pass(self):
        for state in ("AVAILABLE", "MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED"):
            value = fixture("DependencyCheck")
            value["status"] = state
            value["certainty"] = {"AVAILABLE": "verified", "BLOCKED": "BLOCKED"}.get(state, "UNVERIFIED")
            self.assertEqual(contracts.validate_contract("DependencyCheck", value), [], state)
        for state in ("PASS", "detected", "missing", "FAILED"):
            value["status"] = state
            self.assertTrue(contracts.validate_contract("DependencyCheck", value))

    def test_unknown_or_blocked_probe_cannot_be_available(self):
        for certainty in ("UNVERIFIED", "BLOCKED", "ASSUMPTION"):
            value = fixture("DependencyCheck")
            value.update(status="AVAILABLE", certainty=certainty)
            self.assertTrue(contracts.validate_contract("DependencyCheck", value))
        value = fixture("DependencyCheck")
        value.update(status="AVAILABLE", certainty="verified", expectedSha256="0" * 64)
        self.assertTrue(contracts.validate_contract("DependencyCheck", value))
        value["observedSha256"] = "1" * 64
        self.assertTrue(contracts.validate_contract("DependencyCheck", value))
        value["observedSha256"] = "0" * 64
        self.assertEqual(contracts.validate_contract("DependencyCheck", value), [])

    def test_interrupted_crashes_and_new_retry_identity(self):
        value = fixture("TaskProgress")
        for reason in ("app-crash", "runner-crash", "runner-lost"):
            value["reasonCode"] = reason
            self.assertEqual(contracts.validate_contract("TaskProgress", value), [])
            self.assertIsNone(value["rawExitCode"])
        value["reasonCode"] = "cancelled"
        self.assertTrue(contracts.validate_contract("TaskProgress", value))
        value["reasonCode"] = "runner-lost"
        value["retryOfTaskId"] = value["taskId"]
        self.assertTrue(contracts.validate_contract("TaskProgress", value))
        value["taskId"] = "synthetic-retry-task"
        value["state"] = "queued"
        value["reasonCode"] = None
        self.assertEqual(contracts.validate_contract("TaskProgress", value), [])

    def test_indeterminate_and_bounded_progress(self):
        base = fixture("TaskProgress")
        for change in ({"completed": 1}, {"completed": 2, "total": 1, "unit": "steps"},
                       {"completed": 0, "total": 0, "unit": "steps"},
                       {"completed": 1, "total": 2, "unit": "%"},
                       {"completed": True, "total": 100, "unit": "%"}):
            value = {**base, **change}
            self.assertTrue(contracts.validate_contract("TaskProgress", value), change)
        self.assertEqual(contracts.validate_contract("TaskProgress", {**base, "completed": 50, "total": 100, "unit": "%"}), [])

    def test_exit_code_and_signal_are_not_conflated(self):
        value = fixture("TaskProgress")
        value.update(state="failed", rawExitCode=4294967295)
        self.assertEqual(contracts.validate_contract("TaskProgress", value), [])
        value["terminationSignal"] = 15
        self.assertTrue(contracts.validate_contract("TaskProgress", value))
        value.update(rawExitCode=None)
        self.assertEqual(contracts.validate_contract("TaskProgress", value), [])
        value["state"] = "succeeded"
        self.assertTrue(contracts.validate_contract("TaskProgress", value))

    def test_refs_reject_traversal_absolute_urls_and_private_state(self):
        for bad in ("../x", "x/../y", "/tmp/x", "C:/tmp/x", "file://secret", "https://host/a",
                    "x\\y", "x//y", "x/%2e%2e/y", "x/control_auth_cookie", "x/PRIVATE_KEY",
                    "x/private.pem", "x/private.PFX", "x/DataDirectory/state", "x/.env", "name\n"):
            value = fixture("TaskProgress")
            value["logRef"] = bad
            self.assertTrue(contracts.validate_contract("TaskProgress", value), bad)
        value["logRef"] = "reports/task-123/sanitized-log.txt"
        self.assertEqual(contracts.validate_contract("TaskProgress", value), [])

    def test_nonfinite_bool_and_non_json_values_fail(self):
        for bad in (float("nan"), float("inf"), float("-inf"), True, "123"):
            value = fixture("TaskProgress")
            value.update(completed=bad, total=100, unit="%")
            self.assertTrue(contracts.validate_contract("TaskProgress", value), repr(bad))
        for bad in ({1: "non-json-key"}, ("tuple",), object()):
            self.assertTrue(contracts.validate_contract("TaskProgress", bad))
        cyclic = {}; cyclic["self"] = cyclic
        self.assertTrue(contracts.validate_contract("TaskProgress", cyclic))

    def test_metric_nulls_methods_and_units(self):
        base = fixture("TestResult")
        for change in ({"unavailableReason": None}, {"value": float("nan")},
                       {"unit": "s"}, {"value": 0, "method": None}):
            value = copy.deepcopy(base)
            value["metrics"]["idle_rss_bytes"].update(change)
            self.assertTrue(contracts.validate_contract("TestResult", value), change)
        base.update(status="PASS", startedAtUtc="2000-01-01T00:00:00Z", durationSeconds=0, rawExitCode=0)
        base["metrics"]["idle_rss_bytes"].update(value=0, method="synthetic observer", unavailableReason=None)
        self.assertEqual(contracts.validate_contract("TestResult", base), [])
        base["rawExitCode"] = 1
        self.assertTrue(contracts.validate_contract("TestResult", base))

    def test_not_run_and_pass_need_truthful_execution_metadata(self):
        value = fixture("TestResult")
        value["durationSeconds"] = 0
        self.assertTrue(contracts.validate_contract("TestResult", value))
        value = fixture("TestResult")
        value["status"] = "PASS"
        self.assertTrue(contracts.validate_contract("TestResult", value))

    def test_signature_private_state_and_pairing_fail_closed(self):
        artifact = fixture("BuildArtifact")
        artifact["signature"]["status"] = "verified"
        self.assertTrue(contracts.validate_contract("BuildArtifact", artifact))
        artifact = fixture("BuildArtifact")
        artifact["containsPrivateState"] = True
        self.assertTrue(contracts.validate_contract("BuildArtifact", artifact))
        device = fixture("DeviceInfo")
        device["pairing"] = "paired"
        self.assertTrue(contracts.validate_contract("DeviceInfo", device))
        device["peerFingerprint"] = "0" * 64
        self.assertEqual(contracts.validate_contract("DeviceInfo", device), [])

    def test_simulation_does_not_become_physical_proof(self):
        target = fixture("TargetDescriptor")
        target.update(kind="buildroot-qemu", abi="musl", abiVersion=None, executionScope="physical")
        self.assertTrue(contracts.validate_contract("TargetDescriptor", target))
        target.update(kind="pico-yocto", architecture="armv7", executionScope="physical", certainty="verified")
        self.assertTrue(contracts.validate_contract("TargetDescriptor", target))
        target.update(certainty="BLOCKED")
        self.assertEqual(contracts.validate_contract("TargetDescriptor", target), [])

    def test_native_v1_abi_and_profile_exclusivity(self):
        for field, bad in (("abi", "musl"), ("abiVersion", "2.40"), ("architecture", "armv7")):
            value = fixture("TargetDescriptor")
            value["id"] = "linux-x86_64-glibc239-v1"; value[field] = bad
            self.assertTrue(contracts.validate_contract("TargetDescriptor", value))
        value = fixture("BuildRequest"); value["imageProfile"] = "baseline"
        self.assertTrue(contracts.validate_contract("BuildRequest", value))
        value.update(adapterId="yocto-v1", existingProfileId="qemu-arm-capacity32")
        self.assertTrue(contracts.validate_contract("BuildRequest", value))

    def test_nested_evidence_preserves_policy_protocol_scope_and_targets(self):
        for field, bad in (("targetId", "other-target"), ("policySha256", "1" * 64),
                           ("protocolId", "other-protocol"), ("scope", "physical")):
            value = fixture("EvidenceManifest"); value["tests"][0][field] = bad
            self.assertTrue(contracts.validate_contract("EvidenceManifest", value), field)
        value = fixture("EvidenceManifest")
        value["tests"].append(copy.deepcopy(value["tests"][0]))
        self.assertTrue(contracts.validate_contract("EvidenceManifest", value))

    def test_describing_another_explicit_abi_does_not_select_supported_v1_policy(self):
        for abi, version in (("glibc", "2.40"), ("musl", "1.2.6")):
            value = fixture("TargetDescriptor")
            value.update(id="synthetic-future-target", abi=abi, abiVersion=version)
            self.assertEqual(contracts.validate_contract("TargetDescriptor", value), [])
            value["id"] = "linux-x86_64-glibc239-v1"
            self.assertTrue(contracts.validate_contract("TargetDescriptor", value))

    def test_accepted_requires_all_mandatory_online_tests_metrics_and_reference(self):
        value = fixture("EvidenceManifest")
        value["comparison"] = {"referenceId": "synthetic-reference", "status": "ACCEPTED", "reasons": []}
        value["toolchainManifestRef"] = "synthetic-toolchain"
        value["artifacts"] = [fixture("BuildArtifact")]
        policy = contracts.read_json_file(ROOT / "config/policy.json")
        sample = fixture("TestResult")
        sample.update(status="PASS", startedAtUtc="2000-01-01T00:00:00Z", durationSeconds=1, rawExitCode=0)
        online = {"socks5", "public_bootstrap", "onion_v3", "two_node", "bidirectional_integrity", "reconnection"}
        value["tests"] = [{**sample, "testId": name, "mode": "online" if name in online else "offline"} for name in policy["required_tests"]]
        for name in policy["required_metrics"]:
            unit = "B/s" if name.endswith("_bytes_per_second") else "B" if name.endswith("_bytes") else "s" if name.endswith("_seconds") else "%"
            value["metrics"][name] = {"value": 1, "unit": unit, "method": "synthetic only", "unavailableReason": None}
        self.assertEqual(contracts.validate_contract("EvidenceManifest", value), [])
        for bad in ("SKIPPED", "BLOCKED", "FAIL", "NOT RUN"):
            changed = copy.deepcopy(value); changed["tests"][-1]["status"] = bad
            self.assertTrue(contracts.validate_contract("EvidenceManifest", changed), bad)
        changed = copy.deepcopy(value); changed["tests"][-1]["mode"] = "offline"
        self.assertTrue(contracts.validate_contract("EvidenceManifest", changed))
        changed = copy.deepcopy(value); changed["metrics"].pop(policy["required_metrics"][0])
        self.assertTrue(contracts.validate_contract("EvidenceManifest", changed))
        changed = copy.deepcopy(value); changed["comparison"]["referenceId"] = None
        self.assertTrue(contracts.validate_contract("EvidenceManifest", changed))

    def test_unsupported_schema_keyword_format_and_remote_ref_fail_closed(self):
        original = contracts.load_schema("TaskProgress")
        for addition in ({"unevaluatedProperties": False}, {"format": "email"},
                         {"$ref": "https://example.invalid/schema"},
                         {"$ref": "../TargetDescriptor.schema.json"}):
            schema = {**original, **addition}
            with patch.object(contracts, "read_json_file", return_value=schema):
                self.assertTrue(contracts.validate_contract("TaskProgress", fixture("TaskProgress")))

    def test_utc_dates_and_real_calendar_are_validated(self):
        for bad in ("2000-01-01", "2000-02-30T00:00:00Z", "2000-01-01T00:00:00+02:00", "not-a-date"):
            value = fixture("TaskProgress"); value["timestampUtc"] = bad
            self.assertTrue(contracts.validate_contract("TaskProgress", value))

    def test_reader_rejects_duplicate_nonfinite_and_oversized_json(self):
        for data in (b'{"schemaVersion":1,"schemaVersion":2}', b'{"value":NaN}',
                     b'{"value":Infinity}', b' ' * (contracts.MAX_JSON_BYTES + 1)):
            with patch.object(Path, "open", return_value=io.BytesIO(data)):
                with self.assertRaises(ValueError):
                    contracts.read_json_file("synthetic.json")

    def test_legacy_reports_remain_readable_and_byte_identical(self):
        path = ROOT / "reports/arm-2026-11-05/offline-128.json"
        before = hashlib.sha256(path.read_bytes()).hexdigest()
        legacy = contracts.read_json_file(path)
        statuses = [test["status"] for test in legacy["tests"]]
        self.assertIn("SKIPPED", statuses)
        self.assertTrue(contracts.validate_contract("EvidenceManifest", legacy))
        self.assertEqual(statuses, [test["status"] for test in contracts.read_json_file(path)["tests"]])
        self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), before)

    def test_cli_reports_validity_and_nonzero_invalid_without_echoing_data(self):
        for area, code in (("valid", 0), ("invalid", 1)):
            result = subprocess.run([sys.executable, "-B", str(ROOT / "scripts/contracts.py"),
                                     "TaskProgress", str(FIXTURES / area / "TaskProgress.json")],
                                    capture_output=True, text=True, timeout=10, check=False)
            self.assertEqual(result.returncode, code, result.stderr)
            report = json.loads(result.stdout)
            self.assertEqual(report["valid"], code == 0)
            self.assertNotIn("Synthetic runner loss", result.stdout)


if __name__ == "__main__":
    unittest.main()
