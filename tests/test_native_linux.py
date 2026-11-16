"""Synthetic native Linux plan behavior; never build, fetch, or qualify Tor."""
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from scripts.contracts import validate_contract
from scripts.native_linux import preflight as native

TEST_TMP = ROOT / "build/test-tmp"
TEST_TMP.mkdir(parents=True, exist_ok=True)


def synthetic_environment(policy):
    host = {"os": "linux", "architecture": "x86_64", "distroId": "ubuntu", "distroVersion": "24.04",
            "libcName": "glibc", "libcVersion": "2.39", "probeStatus": "AVAILABLE", "isWsl": False}
    resources = {"ramAvailableBytes": policy["ramBudgetBytes"], "diskFreeBytes": policy["diskBudgetBytes"],
                 "probeStatuses": {"memory": "AVAILABLE", "disk": "AVAILABLE"}}
    observations = {name: {"status": "AVAILABLE", "version": "13.3.0", "architecture": "x86_64",
                           "path": "/usr/bin/" + name, "reason": "synthetic observed probe"}
                    for name in policy["requiredTools"]}
    observations["python3"]["version"] = "3.12.3"
    observations["gcc-target"]["version"] = "x86_64-linux-gnu"
    return {"host": host, "resources": resources}, observations


class NativePlanTests(unittest.TestCase):
    def setUp(self):
        self.policy = native._load_json(ROOT / "config/native-linux/v1.json")
        self.pins, self.inputs, self.epoch = native.load_source_pins(ROOT, self.policy)
        self.environment, self.tools = synthetic_environment(self.policy)

    def plan(self, environment=None, tools=None, statuses=None):
        environment = environment or self.environment
        tools = tools or self.tools
        checks = native.evaluate_environment(environment, tools, self.policy)
        for name in ("native-paths", "native-libc-headers", "source-provenance"):
            checks.append(native._check(name, "AVAILABLE", "synthetic observation only", self.policy))
        source_observations = []
        for pin in self.pins:
            checks.append(native._check("source-" + pin["name"], "AVAILABLE", "synthetic observation only", self.policy))
            source_observations.append({"name": pin["name"], "status": "AVAILABLE",
                                        "observedSha256": pin["sha256"], "observedLicenseSha256": pin["license_sha256"]})
        for check in checks:
            if statuses and check["id"] in statuses:
                check["status"] = statuses[check["id"]]
                check["certainty"] = "BLOCKED" if check["status"] == "BLOCKED" else "UNVERIFIED"
        return native.create_plan(policy=self.policy, checks=checks, source_pins=self.pins,
                                  source_observations=source_observations, source_inputs=self.inputs,
                                  tool_observations=tools, provenance={"commit": "b" * 40, "dirty": False},
                                  paths={"source": "/home/test/tor", "output": "/home/test/output/new", "cache": "/home/test/cache"},
                                  adapter_sha256="0" * 64, source_date_epoch=self.epoch)

    def test_ready_synthetic_plan_validates_request_and_remains_not_run(self):
        result = self.plan()
        self.assertTrue(result["readyForBuildPlan"])
        self.assertEqual(validate_contract("BuildRequest", result["buildRequest"]), [])
        self.assertTrue(all(row["status"] == "NOT RUN" and not row["executionSupported"] for row in result["stages"]))
        self.assertEqual(result["inputMetadata"]["patches"], [])
        self.assertEqual(result["inputMetadata"]["sourceDateEpoch"], self.epoch)
        seen = set()
        for row in result["stages"]:
            self.assertTrue(set(row["dependsOn"]) <= seen)
            seen.add(row["id"])
        self.assertIn("source-libseccomp", seen)

    def test_wrong_abi_distro_architecture_and_toolchain_are_not_ready(self):
        for field, bad in (("libcName", "musl"), ("libcVersion", "2.40"), ("architecture", "arm64"),
                           ("distroId", "debian"), ("distroVersion", "22.04")):
            env = copy.deepcopy(self.environment)
            env["host"][field] = bad
            with self.subTest(field=field):
                result = self.plan(environment=env)
                self.assertFalse(result["readyForBuildPlan"])
                self.assertIsNone(result["buildRequest"])
        tools = copy.deepcopy(self.tools)
        tools["gcc-target"]["version"] = "x86_64-buildroot-linux-musl"
        self.assertFalse(self.plan(tools=tools)["readyForBuildPlan"])

    def test_no_missing_unknown_incompatible_or_blocked_probe_can_pass(self):
        for status in ("MISSING", "UNKNOWN", "INCOMPATIBLE", "BLOCKED"):
            with self.subTest(status=status):
                result = self.plan(statuses={"native-tool-gcc": status})
                self.assertFalse(result["readyForBuildPlan"])
                self.assertIn("native-tool-gcc", result["requiredNotAvailable"])
                self.assertIsNone(result["buildRequest"])

    def test_native_budget_is_independent_of_yocto_and_unknown_is_not_zero(self):
        env = copy.deepcopy(self.environment)
        env["resources"]["ramAvailableBytes"] = 3 * 1024 ** 3
        self.assertTrue(self.plan(environment=env)["readyForBuildPlan"])
        for value in (None, True, 0, -1, float("nan")):
            env["resources"]["ramAvailableBytes"] = value
            self.assertFalse(self.plan(environment=env)["readyForBuildPlan"])

    def test_reproducible_hash_ignores_volatile_resources_but_tracks_toolchain(self):
        baseline = self.plan()["inputSha256"]
        env = copy.deepcopy(self.environment)
        env["resources"]["ramAvailableBytes"] *= 2
        env["resources"]["diskFreeBytes"] *= 2
        self.assertEqual(baseline, self.plan(environment=env)["inputSha256"])
        tools = copy.deepcopy(self.tools)
        tools["gcc"]["version"] = "13.4.0"
        self.assertNotEqual(baseline, self.plan(tools=tools)["inputSha256"])

    def test_source_pins_preserve_existing_licenses_and_exclude_windows_patch(self):
        self.assertEqual({row["name"] for row in self.pins}, {"tor", "openssl", "libevent", "zlib", "libseccomp"})
        self.assertTrue(all(native.SHA256.fullmatch(row["license_sha256"]) for row in self.pins))
        self.assertNotIn("tor_source_patches", self.plan()["inputMetadata"])

    def test_unsafe_linux_paths_and_state_cache_rejected(self):
        for source, output, cache in (("relative", "/home/test/new", "/home/test/cache"),
                                       ("/home/test/tor", "/mnt/c/build", "/home/test/cache"),
                                       ("/home/test/tor", "/home/test/tor/out", "/home/test/cache"),
                                       ("/home/test/tor", "/home/test/../new", "/home/test/cache"),
                                       ("/home/test/tor", "/home/test/new out", "/home/test/cache"),
                                       ("/home/test/tor", "/home/test/new", "/home/test/runtime")):
            with self.subTest(output=output, cache=cache):
                self.assertTrue(native.validate_path_syntax(source, output, cache))
        self.assertEqual(native.validate_path_syntax("/home/test/tor", "/home/test/new", "/home/test/cache"), [])

    def test_cgroup_limit_is_checked_without_a_compile_or_system_change(self):
        env = copy.deepcopy(self.environment)
        env["resources"]["ramAvailableBytes"] = 8 * 1024 ** 3
        values = {"/proc/self/cgroup": "0::/unit\n", "/sys/fs/cgroup/unit/memory.max": "1073741824\n",
                  "/sys/fs/cgroup/unit/memory.current": "536870912\n", "/sys/fs/cgroup/memory.max": "max\n",
                  "/proc/self/mountinfo": "36 25 0:32 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n",
                  "/sys/fs/cgroup/cgroup.controllers": "cpu memory pids\n"}
        def read(path, **kwargs):
            if path.as_posix() not in values:
                raise FileNotFoundError(path)
            return values[path.as_posix()]
        with patch.object(Path, "read_text", read):
            native.apply_linux_memory_limits(env)
        self.assertEqual(env["resources"]["ramAvailableBytes"], 536870912)
        self.assertFalse(self.plan(environment=env)["readyForBuildPlan"])
        with patch.object(Path, "read_text", side_effect=PermissionError("denied")):
            native.apply_linux_memory_limits(env)
        self.assertIsNone(env["resources"]["ramAvailableBytes"])
        self.assertEqual(env["resources"]["probeStatuses"]["memory"], "BLOCKED")

    def test_verified_cgroup_root_can_lack_memory_max_but_other_mapping_is_unknown(self):
        env = copy.deepcopy(self.environment)
        values = {"/proc/self/cgroup": "0::/unit\n", "/sys/fs/cgroup/unit/memory.max": "max\n",
                  "/proc/self/mountinfo": "36 25 0:32 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n",
                  "/sys/fs/cgroup/cgroup.controllers": "cpu memory pids\n"}
        def read(path, **kwargs):
            if path.as_posix() not in values:
                raise FileNotFoundError(path)
            return values[path.as_posix()]
        with patch.object(Path, "read_text", read):
            native.apply_linux_memory_limits(env)
        self.assertEqual(env["resources"]["cgroupMemoryProbe"]["status"], "AVAILABLE")
        self.assertEqual(env["resources"]["ramAvailableBytes"], self.policy["ramBudgetBytes"])
        values["/proc/self/mountinfo"] = "36 25 0:32 /delegate /sys/fs/cgroup rw - cgroup2 cgroup rw\n"
        with patch.object(Path, "read_text", read):
            native.apply_linux_memory_limits(env)
        self.assertIsNone(env["resources"]["ramAvailableBytes"])
        self.assertEqual(env["resources"]["cgroupMemoryProbe"]["status"], "UNKNOWN")

    def test_preflight_consumes_w2_api_and_skips_unsafe_source_reads(self):
        env, tools = self.environment, self.tools
        calls = []
        class Probe:
            def host(self):
                return dict(env["host"], reason="synthetic Linux host fact fixture", osVersion="synthetic")
            def resources(self, path):
                return copy.deepcopy(env["resources"])
            def tool(self, name):
                calls.append(name)
                return dict(tools.get("python3" if name == "python" else name,
                                      {"status": "MISSING", "version": None, "architecture": None,
                                       "path": None, "reason": "synthetic absent optional probe"}), rawExitCode=0, command=[])
            def library(self, name):
                return {"status": "MISSING", "version": None, "reason": "synthetic optional system library absent"}
        # W2 collect_report is real; only its platform probe object is synthetic.
        with patch.object(native, "inspect_paths", return_value=[native._check("native-paths", "INCOMPATIBLE", "synthetic unsafe path", self.policy)]), \
             patch.object(native, "inspect_sources", side_effect=AssertionError("unsafe archive read")), \
             patch.object(native, "_git_provenance", side_effect=AssertionError("unsafe Git source probe")), \
             patch.object(subprocess, "run", side_effect=AssertionError("unexpected process")):
            report = native.preflight(str(ROOT), "/home/test/never-created", "/home/test/cache", probe=Probe())
        self.assertFalse(report["readyForBuildPlan"])
        self.assertEqual(report["environment"]["scope"], "read-only-discovery")
        self.assertTrue(set(self.policy["requiredTools"]) <= set(calls))
        self.assertEqual(report["sourceObservations"], [])
        for check in report["checks"]:
            self.assertEqual(validate_contract("DependencyCheck", check), [])

    def test_git_provenance_discards_environment_redirection_and_uses_no_hooks_or_diff(self):
        toxic = {"GIT_DIR": "/other/repo/.git", "GIT_WORK_TREE": "/other/repo", "GIT_INDEX_FILE": "/other/index",
                 "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.fsmonitor",
                 "GIT_CONFIG_VALUE_0": "untrusted-command", "GIT_CONFIG_GLOBAL": "/other/config",
                 "LD_PRELOAD": "/other/plugin.so", "BASH_ENV": "/other/startup"}
        with patch.dict(os.environ, toxic), patch.object(subprocess, "run", side_effect=[
                SimpleNamespace(stdout="b" * 40 + "\n"), SimpleNamespace(stdout="")]) as run:
            result = native._git_provenance("/home/test/tor", "/usr/bin/git")
        self.assertEqual(result, {"commit": "b" * 40, "dirty": False})
        self.assertEqual(len(run.call_args_list), 2)
        for call in run.call_args_list:
            command, env = call.args[0], call.kwargs["env"]
            self.assertIn("core.fsmonitor=false", command)
            self.assertNotIn("diff", command)
            self.assertEqual(command[command.index("-C") + 1], "/home/test/tor")
            self.assertFalse(call.kwargs["shell"])
            self.assertEqual(env["GIT_OPTIONAL_LOCKS"], "0")
            self.assertEqual(env["GIT_CONFIG_GLOBAL"], os.devnull)
            for name in toxic:
                if name != "GIT_CONFIG_GLOBAL":
                    self.assertNotIn(name, env)
        self.assertIn("rev-parse", run.call_args_list[0].args[0])
        self.assertIn("--ignore-submodules=all", run.call_args_list[1].args[0])

    @unittest.skipUnless(sys.platform == "win32", "Native Windows blocked CLI smoke")
    def test_real_windows_cli_is_blocked_and_creates_no_outputs(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            folder = Path(tmp)
            output, cache = folder / "not-created", folder / "empty-cache"
            cache.mkdir()
            before = set(folder.rglob("*"))
            process = subprocess.run([sys.executable, "-B", "-m", "scripts.native_linux",
                                      "--source-dir", str(ROOT), "--output-dir", str(output),
                                      "--archive-cache", str(cache)], cwd=ROOT, text=True,
                                     capture_output=True, timeout=50)
            self.assertEqual(process.returncode, 1, process.stderr + process.stdout)
            report = json.loads(process.stdout)
            self.assertEqual(report["executionStatus"], "NOT RUN")
            self.assertFalse(report["readyForBuildPlan"])
            self.assertIsNone(report["buildRequest"])
            self.assertFalse(output.exists())
            self.assertEqual(before, set(folder.rglob("*")))


class CachedArchiveTests(unittest.TestCase):
    def setUp(self):
        self.policy = native._load_json(ROOT / "config/native-linux/v1.json")

    def archive(self, folder, members):
        path = folder / "synthetic-1.tar.gz"
        with tarfile.open(path, "w:gz") as stream:
            for member, content in members:
                stream.addfile(member, io.BytesIO(content) if member.isfile() else None)
        return path

    def pin(self, path, license_bytes=b"synthetic license"):
        return {"name": "synthetic", "version": "1", "archive": path.name, "directory": "synthetic-1",
                "sha256": native.hash_file(path), "license_file": "LICENSE",
                "license_sha256": hashlib.sha256(license_bytes).hexdigest(), "license": "synthetic fixture"}

    def member(self, name="synthetic-1/LICENSE", content=b"synthetic license"):
        member = tarfile.TarInfo(name)
        member.size = len(content)
        return member, content

    def test_hash_and_bounded_regular_license_verified_without_extraction(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            folder = Path(tmp)
            path = self.archive(folder, [self.member()])
            before = list(folder.iterdir())
            checks, observations = native.inspect_sources(folder, [self.pin(path)], self.policy)
            self.assertEqual(checks[0]["status"], "AVAILABLE")
            self.assertEqual(observations[0]["observedLicenseSha256"], self.pin(path)["license_sha256"])
            self.assertEqual(before, list(folder.iterdir()))

    def test_missing_mismatched_and_denied_archives_remain_nonavailable(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            folder = Path(tmp)
            path = self.archive(folder, [self.member()])
            pin = self.pin(path)
            pin["sha256"] = "0" * 64
            checks, _ = native.inspect_sources(folder, [pin], self.policy)
            self.assertEqual(checks[0]["status"], "INCOMPATIBLE")
            path.unlink()
            checks, _ = native.inspect_sources(folder, [pin], self.policy)
            self.assertEqual(checks[0]["status"], "MISSING")
            with patch.object(Path, "stat", side_effect=PermissionError("denied")):
                checks, _ = native.inspect_sources(folder, [pin], self.policy)
            self.assertEqual(checks[0]["status"], "BLOCKED")

    def test_duplicate_symlink_and_oversized_license_members_rejected(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            folder = Path(tmp)
            duplicate = [self.member(), self.member()]
            link = self.member()[0]
            link.type, link.linkname = tarfile.SYMTYPE, "/secret"
            large = self.member(content=b"x" * (1024 * 1024 + 1))
            for members in (duplicate, [(link, b"")], [large]):
                with self.subTest(kind=members[0][0].type):
                    path = self.archive(folder, members)
                    checks, _ = native.inspect_sources(folder, [self.pin(path)], self.policy)
                    self.assertEqual(checks[0]["status"], "INCOMPATIBLE")


if __name__ == "__main__":
    unittest.main()
