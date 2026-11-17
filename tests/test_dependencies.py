"""Read-only dependency probe behavior; no native compiler/build required."""

import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.dont_write_bytecode = True

from scripts.dependencies import checker
from scripts.dependencies.__main__ import main

from scripts.contracts import validate_contract

TEST_TMP = ROOT / "build/test-tmp"
TEST_TMP.mkdir(parents=True, exist_ok=True)


def observation(status="AVAILABLE", version="99.0.0", reason="Synthetic observation"):
    return checker._record(status, reason, version=version)


class FakeProbe:
    def __init__(self, overrides=None):
        self.overrides = overrides or {}
        self.calls = []

    def host(self):
        return {"os": "linux", "architecture": "x86_64", "osVersion": "synthetic",
                "distroId": "ubuntu", "distroVersion": "24.04", "libcName": "glibc",
                "libcVersion": "2.39", "logicalCpus": 2, "isWsl": False,
                "probeStatus": "AVAILABLE", "reason": "Synthetic host"}

    def resources(self, path=None):
        return {"logicalCpus": 2, "ramTotalBytes": 1024, "ramAvailableBytes": 512,
                "diskFreeBytes": 4096, "diskTotalBytes": 8192,
                "probeStatuses": {"memory": "AVAILABLE", "disk": "AVAILABLE"},
                "reasons": {"memory": "Synthetic, deliberately below any Yocto budget", "disk": "Synthetic"}}

    def tool(self, name):
        self.calls.append(name)
        if name in self.overrides:
            return self.overrides[name]
        if name == "gcc-target":
            return observation(version="x86_64-linux-gnu")
        return observation()


class ProbeBehaviorTests(unittest.TestCase):
    def setUp(self):
        self.probe = checker.ReadOnlyProbe()

    def run_tool(self, name="gcc", output="gcc (synthetic) 13.2.0", code=0, stderr=""):
        completed = subprocess.CompletedProcess(["synthetic"], code, output, stderr)
        with patch.object(self.probe, "_lookup", return_value=Path("/usr/bin/gcc")), \
             patch.object(self.probe, "_execute", return_value=completed):
            return self.probe.tool(name)

    def test_missing_is_distinct_from_blocked(self):
        with patch.object(self.probe, "_lookup", return_value=None), \
             patch.object(self.probe, "_execute") as execute:
            self.assertEqual(self.probe.tool("gcc")["status"], "MISSING")
            execute.assert_not_called()
        with patch.object(self.probe, "_lookup", side_effect=PermissionError("denied")):
            self.assertEqual(self.probe.tool("gcc")["status"], "BLOCKED")

    def test_denied_and_timeout_are_blocked_without_fabricated_exit(self):
        for error in (PermissionError("denied"), subprocess.TimeoutExpired(["gcc"], 3)):
            with self.subTest(error=type(error).__name__), \
                 patch.object(self.probe, "_lookup", return_value=Path("/usr/bin/gcc")), \
                 patch.object(self.probe, "_execute", side_effect=error):
                result = self.probe.tool("gcc")
                self.assertEqual(result["status"], "BLOCKED")
                self.assertIsNone(result["version"])
                self.assertIsNone(result["rawExitCode"])

    def test_unparseable_and_nonzero_are_unknown(self):
        self.assertEqual(self.run_tool(output="version unavailable")["status"], "UNKNOWN")
        result = self.run_tool(code=2, stderr="synthetic probe failure")
        self.assertEqual(result["status"], "UNKNOWN")
        self.assertEqual(result["rawExitCode"], 2)
        with patch.object(checker.sys, "platform", "win32"):
            result = self.run_tool(code=0xC0000142, output="")
        self.assertEqual(result["status"], "BLOCKED")
        self.assertEqual(result["rawExitCode"], 0xC0000142)

    def test_observed_version_and_target(self):
        result = self.run_tool()
        self.assertEqual(result["version"], "13.2.0")
        self.assertEqual(result["status"], "AVAILABLE")
        result = self.run_tool("gcc-target", "x86_64-linux-gnu\n")
        self.assertEqual(result["version"], "x86_64-linux-gnu")
        self.assertEqual(result["architecture"], "x86_64")

    def test_version_suffixed_gcc_banners_retain_family_and_version_checks(self):
        for banner in ("gcc-13 (Ubuntu 13.3.0-6ubuntu2) 13.3.0",
                       "x86_64-linux-gnu-gcc-13 (Ubuntu 13.3.0-6ubuntu2) 13.3.0",
                       "/usr/bin/x86_64-linux-gnu-gcc-13 (GCC) 13.3.0",
                       "x86_64-w64-mingw32-gcc-13.3.0.exe (GCC) 13.3.0"):
            with self.subTest(banner=banner):
                observed = self.run_tool(output=banner)
                self.assertEqual(observed["status"], "AVAILABLE")
                self.assertEqual(observed["version"], "13.3.0")
        old = self.run_tool(output="x86_64-linux-gnu-gcc-11 (GCC) 11.4.0")
        self.assertEqual(checker._classified_tool({"minimumVersion": [13]}, old)[0], "INCOMPATIBLE")
        for banner in ("gcc-preview 13.3.0", "gcc-13-wrapper 13.3.0"):
            with self.subTest(banner=banner):
                self.assertEqual(self.run_tool(output=banner)["status"], "UNKNOWN")

    def test_target_cannot_include_shell_or_extra_lines(self):
        for text in ("x86_64-linux-gnu; curl example.invalid", "x86_64-linux-gnu\nother"):
            self.assertEqual(self.run_tool("gcc-target", text)["status"], "UNKNOWN")

    def test_wrong_version_and_architecture_are_incompatible(self):
        status, _ = checker._classified_tool({"minimumVersion": [3, 12]}, observation(version="3.11.9"))
        self.assertEqual(status, "INCOMPATIBLE")
        status, _ = checker._classified_tool({"target": "x86_64-linux-gnu"}, observation(version="aarch64-linux-gnu"))
        self.assertEqual(status, "INCOMPATIBLE")

    def test_unknown_version_cannot_become_available(self):
        status, _ = checker._classified_tool({"minimumVersion": [3, 12]}, observation(version=None))
        self.assertEqual(status, "UNKNOWN")

    def test_compiler_and_make_family_are_not_inferred_from_version_digits(self):
        result = self.run_tool(output="Ubuntu clang version 18.1.0")
        self.assertEqual(result["status"], "INCOMPATIBLE")
        self.assertEqual(self.run_tool(output="unidentified compiler 99.0.0")["status"], "UNKNOWN")
        self.assertEqual(self.run_tool("make", "BSD make 4.1.0")["status"], "INCOMPATIBLE")
        self.assertEqual(self.run_tool("make", "GNU Make 4.3\n")["status"], "AVAILABLE")

    def test_unallowlisted_probe_rejected_without_lookup(self):
        with patch.object(self.probe, "_lookup") as lookup:
            for command in ("COMPILE.cmd", "pacman", "curl", "sudo", "wsl", "gcc -c source.c"):
                with self.subTest(command=command), self.assertRaises(ValueError):
                    self.probe.tool(command)
            self.assertRaises(ValueError, self.probe.library, "arbitrary-package")
            lookup.assert_not_called()

    def test_fixed_library_query_is_metadata_only(self):
        with patch.object(self.probe, "_lookup", return_value=Path("/usr/bin/pkg-config")), \
             patch.object(self.probe, "_execute", return_value=subprocess.CompletedProcess([], 0, "3.6.5", "")) as execute:
            self.assertEqual(self.probe.library("openssl")["version"], "3.6.5")
            self.assertEqual(execute.call_args.args[0][1:], ("--modversion", "openssl"))
        result = self.run_tool("pkg-openssl", output="", code=1, stderr="Package openssl was not found")
        self.assertEqual(result["status"], "MISSING")

    def test_diagnostic_output_is_not_exported(self):
        secret = "synthetic-sensitive-string"
        result = self.run_tool(output="gcc 13.2.0\n" + secret, stderr=secret)
        self.assertNotIn(secret, json.dumps(result))
        result = self.run_tool(output="gcc 13.2.0" + "x" * self.probe.max_output_chars)
        self.assertEqual(result["status"], "UNKNOWN")

    def test_subprocess_is_bounded_direct_argv(self):
        with patch.dict(os.environ, {"PERL5OPT": "-MUntrusted", "MAKEFLAGS": "--eval=untrusted",
                                     "LD_PRELOAD": "/untrusted.so", "PKG_CONFIG_PATH": "/untrusted"}), \
             patch.object(checker.subprocess, "run") as run:
            self.probe._execute(("/usr/bin/gcc", "--version"))
            self.assertEqual(run.call_args.args[0], ["/usr/bin/gcc", "--version"])
            self.assertIs(run.call_args.kwargs["shell"], False)
            self.assertEqual(run.call_args.kwargs["timeout"], 3)
            self.assertEqual(run.call_args.kwargs["stdin"], subprocess.DEVNULL)
            for name in ("PERL5OPT", "MAKEFLAGS", "LD_PRELOAD", "PKG_CONFIG_PATH"):
                self.assertNotIn(name, run.call_args.kwargs["env"])
            self.assertEqual(run.call_args.kwargs["cwd"], str(Path("/usr/bin/gcc").parent))

    def test_relative_path_and_project_tool_never_execute(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as temp:
            candidate = Path(temp) / ("gcc.exe" if sys.platform == "win32" else "gcc")
            candidate.write_bytes(b"synthetic-untrusted-tool")
            with patch.dict(os.environ, {"PATH": "." + os.pathsep + temp}), \
                 patch.object(self.probe, "_candidates", return_value=[candidate]), \
                 patch.object(self.probe, "_execute") as execute:
                self.assertEqual(self.probe.tool("gcc")["status"], "BLOCKED")
                execute.assert_not_called()

    @unittest.skipUnless(sys.platform == "win32", "Windows WSL launcher path check")
    def test_windows_wsl_bash_launcher_is_not_run(self):
        launcher = Path(os.environ["SystemRoot"]) / "System32/bash.exe"
        if not launcher.exists():
            self.skipTest("Windows legacy WSL launcher absent")
        with patch.dict(os.environ, {"PATH": str(launcher.parent)}), \
             patch.object(self.probe, "_candidates", return_value=[launcher]), \
             patch.object(self.probe, "_execute") as execute:
            self.assertEqual(self.probe.tool("bash")["status"], "BLOCKED")
            execute.assert_not_called()

    def test_denied_stat_is_blocked_not_missing(self):
        with patch.object(self.probe, "_candidates", return_value=[Path("/synthetic/gcc")]), \
             patch.object(checker.Path, "stat", side_effect=PermissionError("denied")), \
             patch.object(self.probe, "_execute") as execute:
            self.assertEqual(self.probe.tool("gcc")["status"], "BLOCKED")
            execute.assert_not_called()

    def test_disk_failure_keeps_nulls(self):
        with patch.object(checker.shutil, "disk_usage", side_effect=PermissionError("denied")):
            result = self.probe.resources()
        self.assertEqual(result["probeStatuses"]["disk"], "BLOCKED")
        self.assertIsNone(result["diskFreeBytes"])
        self.assertIsNone(result["diskTotalBytes"])

    def test_dependency_status_shape_and_no_false_certainty(self):
        for status in checker.STATUSES:
            result = checker.dependency_check("test", "host", status, "Synthetic requirement", "Synthetic reason")
            self.assertEqual(result["status"], status)
            self.assertIsNone(result["detectedVersion"])
            self.assertIsNone(result["expectedSha256"])
            self.assertEqual(result["certainty"], "verified" if status == "AVAILABLE" else
                             "BLOCKED" if status == "BLOCKED" else "UNVERIFIED")
        self.assertRaises(ValueError, checker.dependency_check, "x", "host", "PASS", "requirement", "reason")

    def test_cli_report_failure_is_machine_readable_blocked(self):
        output = io.StringIO()
        with patch("scripts.dependencies.__main__.collect_report", side_effect=ImportError("W1 missing")), \
             contextlib.redirect_stderr(output):
            self.assertEqual(main([]), 2)
        self.assertEqual(json.loads(output.getvalue())["status"], "BLOCKED")


class SharedContractIntegrationTests(unittest.TestCase):
    def test_every_emitted_check_validates_and_no_hardware_or_yocto_gate(self):
        probe = FakeProbe()
        report = checker.collect_report(probe=probe)
        self.assertTrue(report["summary"]["readyForScope"])
        for check in report["checks"]:
            self.assertEqual(validate_contract("DependencyCheck", check), [])
        self.assertFalse(any("yocto" in check["id"] or "pico" in check["id"] for check in report["checks"]))
        self.assertFalse(any("compile" in call.lower() or "download" in call for call in probe.calls))

    def test_all_non_available_required_statuses_prevent_ready(self):
        for status in ("MISSING", "INCOMPATIBLE", "UNKNOWN", "BLOCKED"):
            with self.subTest(status=status):
                report = checker.collect_report(probe=FakeProbe({"git": observation(status, None)}))
                self.assertFalse(report["summary"]["readyForScope"])
                self.assertIn("git", report["summary"]["requiredNotAvailable"])
                self.assertEqual(report["summary"]["counts"][status], 1)

    def test_wrong_compiler_target_prevents_ready(self):
        report = checker.collect_report(probe=FakeProbe({"gcc-target": observation(version="aarch64-linux-gnu")}))
        row = next(check for check in report["checks"] if check["id"] == "gcc-target")
        self.assertEqual(row["status"], "INCOMPATIBLE")
        self.assertFalse(report["summary"]["readyForScope"])

    def test_optional_unavailable_tool_does_not_block_required_scope(self):
        manifest = json.loads(checker.MANIFEST.read_text(encoding="utf-8"))
        next(item for item in manifest["tools"] if item["id"] == "git")["optional"] = True
        with patch.object(checker, "MANIFEST") as policy:
            policy.read_text.return_value = json.dumps(manifest)
            report = checker.collect_report(probe=FakeProbe({"git": observation("MISSING", None)}))
        self.assertTrue(report["summary"]["readyForScope"])
        self.assertEqual(report["summary"]["counts"]["MISSING"], 1)

    def test_cli_emits_machine_readable_report_and_explicit_nonzero_readiness(self):
        report = checker.collect_report(probe=FakeProbe({"git": observation("BLOCKED", None)}))
        output = io.StringIO()
        with patch("scripts.dependencies.__main__.collect_report", return_value=report), contextlib.redirect_stdout(output):
            self.assertEqual(main([]), 1)
        self.assertEqual(json.loads(output.getvalue()), report)

    def test_strict_read_only_cli_launch_does_not_create_files(self):
        # -B is essential: CPython may cache __init__.py before executing it.
        before = set(p.relative_to(ROOT) for p in (ROOT / "scripts/dependencies").rglob("*"))
        result = subprocess.run([sys.executable, "-B", "-m", "scripts.dependencies"],
                                cwd=ROOT, capture_output=True, text=True, timeout=60)
        self.assertIn(result.returncode, (0, 1), result.stderr)
        report = json.loads(result.stdout)
        self.assertEqual(report["scope"], "read-only-discovery")
        after = set(p.relative_to(ROOT) for p in (ROOT / "scripts/dependencies").rglob("*"))
        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
