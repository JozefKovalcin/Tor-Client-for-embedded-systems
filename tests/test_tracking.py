"""Regression tests of decision gates and preservation guarantees; synthetic data only."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from common import read_json, resolve_result, seal_baseline, sha256, validate_result, write_json, publish
from compare import compare
from benchmark import collect, file_total
from validate import check_resolved_config

POLICY = read_json(ROOT / "config/policy.json")
TEST_TMP = ROOT / "build/test-tmp"
TEST_TMP.mkdir(parents=True, exist_ok=True)


def fixture():
    return {"schema_version": 1, "run_id": "synthetic-test-only", "timestamp": "2000-01-01T00:00:00Z",
            "change": "Synthetic fixture; never a hardware benchmark", "build": {"status": "PASS"},
            "tests": [{"id": name, "status": "PASS", "mode": "online" if name in
                       ("socks5", "public_bootstrap", "onion_v3", "two_node", "bidirectional_integrity", "reconnection") else "offline"}
                      for name in POLICY["required_tests"]],
            "metrics": {name: 100 for name in POLICY["required_metrics"]},
            "environment": {"hardware": "synthetic"},
            "metadata": {**{name: "synthetic" for name in
                             ("architecture", "buildroot_version", "tor_version", "kernel_version", "compiler",
                              "compiler_flags", "configuration_id", "source_provenance_sha256", "input_sha256", "policy_sha256")},
                         "git": {"commit": "1234567890", "dirty": False}}}


class ComparisonTests(unittest.TestCase):
    def setUp(self):
        self.base = fixture()
        self.candidate = copy.deepcopy(self.base)
        self.candidate["metrics"]["tor_executable_bytes"] = 90

    def comparison(self):
        return compare(self.base, self.candidate, POLICY)

    def test_accepts_valid_improvement(self):
        r = self.comparison()
        self.assertEqual(r["status"], "ACCEPTED")
        diff = next(d for d in r["differences"] if d["metric"] == "tor_executable_bytes")
        self.assertEqual(diff["delta"], -10)
        self.assertEqual(diff["reduction_percent"], 10)

    def test_online_skipped_never_accepted(self):
        for status in ("SKIPPED", "BLOCKED", "FAIL"):
            with self.subTest(status=status):
                self.candidate["tests"][-1]["status"] = status
                self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_removed_required_test(self):
        self.candidate["tests"].pop()
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_candidate_cannot_opt_out_of_online_tests(self):
        for row in self.candidate["tests"]:
            row["mandatory"] = False
        self.candidate["tests"].pop()
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_optional_test_regression_rejected(self):
        self.base["tests"].append({"id": "optional", "status": "PASS"})
        self.assertIn("optional", self.comparison()["functional_regressions"])
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_failed_build_rejected(self):
        self.candidate["build"]["status"] = "FAIL"
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_different_hardware_is_noncomparable(self):
        self.candidate["environment"]["hardware"] = "other"
        self.assertEqual(self.comparison()["status"], "NON_COMPARABLE")

    def test_different_policy_is_noncomparable(self):
        self.candidate["metadata"]["policy_sha256"] = "changed"
        self.assertEqual(self.comparison()["status"], "NON_COMPARABLE")

    def test_changed_current_policy_cannot_accept_old_runs(self):
        result = compare(self.base, self.candidate, POLICY, policy_sha256="new-policy")
        self.assertEqual(result["status"], "NON_COMPARABLE")

    def test_architecture_is_noncomparable(self):
        self.candidate["metadata"]["architecture"] = "arm"
        self.assertEqual(self.comparison()["status"], "NON_COMPARABLE")

    def test_bad_measurements_rejected(self):
        for value in (None, float("nan"), float("inf"), -1, True, "100"):
            with self.subTest(value=value):
                self.candidate["metrics"]["idle_rss_bytes"] = value
                self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_regression_rejected_despite_smaller_binary(self):
        self.candidate["metrics"]["peak_process_rss_bytes"] = 150
        self.assertEqual(self.comparison()["status"], "REJECTED")

    def test_throughput_direction(self):
        self.candidate["metrics"]["throughput_bytes_per_second"] = 70
        self.assertEqual(self.comparison()["status"], "REJECTED")

    def test_zero_baseline_safe(self):
        self.base["metrics"]["kernel_bytes"] = 0
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_no_improvement(self):
        self.candidate = copy.deepcopy(self.base)
        self.assertEqual(self.comparison()["status"], "REJECTED")

    def test_invalid_baseline(self):
        self.base["tests"].pop()
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")

    def test_duplicate_test(self):
        self.candidate["tests"].append(self.candidate["tests"][0])
        self.assertNotEqual(self.comparison()["status"], "ACCEPTED")


class PreservationTests(unittest.TestCase):
    def test_baseline_tampering_detected(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            path = Path(tmp) / "baseline"
            seal_baseline(fixture(), path, POLICY)
            result_path = path / "results.json"
            result_path.chmod(0o644)
            result_path.write_text(result_path.read_text() + " ")
            with self.assertRaises(ValueError):
                resolve_result(path)
            for child in path.iterdir():
                child.chmod(0o644)

    def test_baseline_is_exclusive_and_unchanged(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            path = Path(tmp) / "baseline"
            seal_baseline(fixture(), path, POLICY)
            before = sha256(path / "results.json")
            with self.assertRaises(FileExistsError):
                seal_baseline(fixture(), path, POLICY)
            self.assertEqual(sha256(path / "results.json"), before)
            for child in path.iterdir():
                child.chmod(0o644)  # Allow Windows TemporaryDirectory cleanup.

    def test_dirty_baseline_refused(self):
        result = fixture()
        result["metadata"]["git"]["dirty"] = True
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp, self.assertRaises(ValueError):
            seal_baseline(result, Path(tmp) / "baseline", POLICY)

    def test_blocked_baseline_refused(self):
        result = fixture()
        result["tests"][-1]["status"] = "BLOCKED"
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp, self.assertRaises(ValueError):
            seal_baseline(result, Path(tmp) / "baseline", POLICY)

    def test_reports_keep_nulls_and_csv(self):
        result = fixture()
        result["metrics"]["idle_rss_bytes"] = None
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            publish(result, {"status": "INCOMPLETE", "reasons": ["fixture"], "differences": []}, tmp)
            reports = Path(tmp)
            self.assertIn("INCOMPLETE", (reports / "latest.md").read_text())
            self.assertTrue((reports / "optimization-history.csv").is_file())
            saved = read_json(reports / "benchmarks/synthetic-test-only/results.json")
            self.assertIsNone(saved["metrics"]["idle_rss_bytes"])

    def test_kconfig_dropped_option_rejected(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            a, b = Path(tmp) / "requested", Path(tmp) / "resolved"
            a.write_text("BR2_PACKAGE_TOR=y\n")
            b.write_text("# BR2_PACKAGE_TOR is not set\n")
            with self.assertRaises(ValueError):
                check_resolved_config(a, b)

    def test_empty_artifact_rejected(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            with self.assertRaises(ValueError):
                collect(Path(tmp), {"artifacts": {"kernel": "bzImage", "rootfs": "rootfs.ext2"}}, Path(tmp))

    def test_hardlinks_not_double_counted(self):
        import os
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            a, b = Path(tmp) / "a", Path(tmp) / "b"
            a.write_bytes(b"123")
            os.link(a, b)
            self.assertEqual(file_total([a, b]), 3)


if __name__ == "__main__":
    unittest.main()
