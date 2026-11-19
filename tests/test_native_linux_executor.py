"""Offline W6 engine tests: synthetic archives and tiny Python processes only."""
from contextlib import nullcontext
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tarfile
import tempfile
import threading
import time
import unittest
from unittest.mock import Mock, patch

from scripts.contracts import validate_contract
from scripts.native_linux import executor as build


class SourceSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for name in ("cache", "snapshots", "sources"):
            (self.root / name).mkdir()

    def archive(self, members=None, name="synthetic"):
        license_bytes = b"Synthetic license, not upstream sources"
        members = members if members is not None else [(name + "/LICENSE", license_bytes), (name + "/configure", b"synthetic data")]
        path = self.root / "cache" / (name + ".tar")
        with tarfile.open(path, "w") as archive:
            for item in members:
                if isinstance(item, tarfile.TarInfo):
                    archive.addfile(item)
                else:
                    filename, content = item
                    member = tarfile.TarInfo(filename)
                    member.size = len(content)
                    member.mtime = 1_600_000_000
                    archive.addfile(member, io.BytesIO(content))
        return {"name": name, "version": "synthetic", "archive": path.name,
                "directory": name, "sha256": build.native.hash_file(path),
                "license_file": "LICENSE", "license_sha256": hashlib.sha256(license_bytes).hexdigest(),
                "license": "Synthetic test fixture"}

    def extract(self, pin, **kwargs):
        snapshot = build.snapshot_source(self.root / "cache", self.root / "snapshots", pin)
        return build.extract_snapshot(snapshot, self.root / "sources", pin, byte_budget=kwargs.get("byte_budget", 1024 * 1024))

    def test_snapshot_and_extraction_verify_archive_and_license_without_cache_mutation(self):
        pin = self.archive()
        before = (self.root / "cache" / pin["archive"]).read_bytes()
        directory, count = self.extract(pin)
        self.assertEqual((directory / "configure").read_bytes(), b"synthetic data")
        self.assertEqual((directory / "configure").stat().st_mtime, 1_600_000_000)
        self.assertGreater(count, 0)
        self.assertEqual((self.root / "cache" / pin["archive"]).read_bytes(), before)
        self.assertEqual(build.native.hash_file(self.root / "snapshots" / pin["archive"]), pin["sha256"])

    def test_tampered_archive_and_tampered_snapshot_fail_closed(self):
        pin = self.archive()
        pin["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            build.snapshot_source(self.root / "cache", self.root / "snapshots", pin)
        pin = self.archive(name="other")
        snapshot = build.snapshot_source(self.root / "cache", self.root / "snapshots", pin)
        snapshot.chmod(0o600)
        snapshot.write_bytes(b"changed bytes")
        with self.assertRaisesRegex(ValueError, "changed"):
            build.extract_snapshot(snapshot, self.root / "sources", pin, byte_budget=1024)

    def test_growing_archive_is_rejected_without_exceeding_admitted_size(self):
        cache, snapshots = self.root / "cache", self.root / "snapshots"
        name, content = "growing.tar", b"original"
        archive = cache / name
        archive.write_bytes(content)
        pin = {"archive": name, "sha256": hashlib.sha256(content).hexdigest()}
        open_stream = os.fdopen

        class GrowingStream(io.FileIO):
            def read(self, size=-1):
                if self.tell() == 0:
                    with archive.open("ab") as writer:
                        writer.write(b"x" * 100_000)
                return super().read(size)

        def wrap(descriptor, mode, **kwargs):
            return GrowingStream(descriptor, mode) if mode == "rb" else open_stream(descriptor, mode, **kwargs)

        with patch.object(build, "MAX_FILE_BYTES", 16), patch.object(build.os, "fdopen", side_effect=wrap):
            with self.assertRaises(ValueError):
                build.snapshot_source(cache, snapshots, pin)
        # Even failed partial evidence must respect the admitted eight bytes.
        self.assertLessEqual((snapshots / name).stat().st_size, len(content))

    def test_snapshot_copy_checks_cancellation_between_chunks(self):
        cache, snapshots = self.root / "cache", self.root / "snapshots"
        name, content = "interrupt.tar", b"x" * (1024 * 1024 + 1)
        (cache / name).write_bytes(content)
        pin = {"archive": name, "sha256": hashlib.sha256(content).hexdigest()}
        checkpoint = Mock(side_effect=[None, InterruptedError("cancelled")])
        with self.assertRaises(InterruptedError):
            build.snapshot_source(cache, snapshots, pin, checkpoint=checkpoint)
        self.assertEqual((snapshots / name).stat().st_size, 1024 * 1024)

    def test_path_traversal_absolute_backslash_and_wrong_root_are_refused(self):
        for index, path in enumerate(("../escape", "/absolute", "synthetic/../escape", "synthetic\\evil", "other/file", "synthetic/C:bad")):
            with self.subTest(path=path):
                pin = self.archive([(path, b"unsafe")], name="unsafe" + str(index))
                with self.assertRaises(ValueError):
                    self.extract(pin)
                self.assertFalse((self.root / "escape").exists())

    def test_symlinks_hardlinks_and_devices_are_refused(self):
        for index, kind in enumerate((tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.CHRTYPE, tarfile.FIFOTYPE)):
            with self.subTest(kind=kind):
                name = "fixture" + str(index)
                member = tarfile.TarInfo(name + "/unsafe")
                member.type, member.linkname = kind, "/outside"
                pin = self.archive([member], name=name)
                with self.assertRaisesRegex(ValueError, "links and special"):
                    self.extract(pin)

    def test_duplicate_members_license_mismatch_and_expansion_budget_are_refused(self):
        pin = self.archive([("duplicate/file", b"one"), ("duplicate/file", b"two")], name="duplicate")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            self.extract(pin)
        pin = self.archive(name="license")
        pin["license_sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "license"):
            self.extract(pin)
        pin = self.archive(name="large")
        with self.assertRaisesRegex(ValueError, "budget"):
            self.extract(pin, byte_budget=1)

    @unittest.skipUnless(os.name == "posix", "POSIX archive symlink creation")
    def test_cache_archive_symlink_is_not_followed(self):
        pin = self.archive()
        path = self.root / "cache" / pin["archive"]
        original = path.with_suffix(".original")
        path.rename(original)
        path.symlink_to(original)
        with self.assertRaisesRegex(ValueError, "regular"):
            build.snapshot_source(self.root / "cache", self.root / "snapshots", pin)


class RecipeTests(unittest.TestCase):
    def plan(self):
        tools = {name: {"path": sys.executable, "sha256": build.native.hash_file(sys.executable)}
                 for name in ("gcc", "ar", "ranlib", "pkg-config", "perl", "make", "bash", "gperf")}
        return {"executionInputSha256": "b" * 64,
                "inputs": {"tools": tools, "sourceDateEpoch": 1234567890, "maxJobs": 1,
                           "sources": [{"name": name, "directory": name} for name in build.SOURCE_ORDER]}}

    def test_fixed_private_prefix_recipe_keeps_linux_hardening_and_no_windows_patch(self):
        commands = build.build_commands(self.plan(), "/tmp/synthetic-native-build")
        self.assertEqual(len(commands), 15)
        self.assertEqual(commands[0].name, "zlib-configure")
        self.assertEqual(commands[-1].name, "tor-install")
        tor = next(item for item in commands if item.name == "tor-configure")
        for value in ("--enable-seccomp", "--enable-gcc-hardening", "--enable-linker-hardening", "--enable-static-openssl"):
            self.assertIn(value, tor.argv)
        self.assertIn("-fPIE", tor.environment["CFLAGS"])
        self.assertIn("-fstack-protector-strong", tor.environment["CFLAGS"])
        self.assertIn("FORTIFY_SOURCE=3", tor.environment["CPPFLAGS"])
        self.assertIn("-z,relro,-z,now,-z,noexecstack", tor.environment["LDFLAGS"])
        self.assertEqual(Path(tor.environment["PKG_CONFIG_LIBDIR"]).as_posix(), "/tmp/synthetic-native-build/prefix/lib/pkgconfig")
        self.assertEqual(tor.environment["PKG_CONFIG_PATH"], "")
        for forbidden in ("LD_PRELOAD", "BASH_ENV", "PYTHONPATH", "HTTP_PROXY"):
            self.assertNotIn(forbidden, tor.environment)
        self.assertTrue(all("mingw" not in str(item.argv) and "COMPILE" not in str(item.argv) for item in commands))
        self.assertTrue(all("-j1" in item.argv for item in commands if item.name.endswith("-make")))

    def test_missing_or_changed_approval_never_reaches_engine(self):
        with patch.object(build.sys, "platform", "linux"), patch.object(build, "_run_verified") as engine:
            with self.assertRaises(build.BuildBlocked):
                build.execute_build("/tmp/source", "/tmp/output", "/tmp/cache")
            with tempfile.TemporaryDirectory() as tmp, \
                    patch.object(build, "workspace_reservation", return_value=nullcontext()) as reservation, \
                    patch.object(build, "execution_plan", return_value={"readyForExecution": True, "executionInputSha256": "c" * 64}) as fresh:
                with self.assertRaisesRegex(build.BuildBlocked, "digest differs"):
                    build.execute_build("/tmp/source", str(Path(tmp) / "new"), "/tmp/cache", approved_input_sha256="b" * 64)
                fresh.assert_called_once()
                reservation.assert_not_called()
            engine.assert_not_called()

    def test_unqualified_w3_report_is_not_promoted_and_creates_no_output(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "never-created"
            with patch.object(build.native, "preflight", return_value={"readyForBuildPlan": False, "buildRequest": None}):
                result = build.execution_plan("source", str(output), "cache")
            self.assertEqual(result["executionStatus"], "NOT RUN")
            self.assertFalse(result["readyForExecution"])
            self.assertIsNone(result["executionInputSha256"])
            self.assertFalse(output.exists())

    def test_deterministic_execution_hash_binds_recipe_tool_bytes_and_source_pins(self):
        from test_native_linux import NativePlanTests
        case = NativePlanTests()
        case.setUp()
        w3 = case.plan()
        for item in w3["toolchainManifest"].values():
            item["path"] = sys.executable
        with patch.object(build.native, "preflight", return_value=w3):
            first = build.execution_plan("source", "output", "cache")
            second = build.execution_plan("source", "output", "cache")
        self.assertEqual(first["executionInputSha256"], second["executionInputSha256"])
        self.assertEqual(first["inputs"]["patches"], [])
        self.assertEqual(first["buildRequest"]["mode"], "build")
        self.assertEqual(first["buildRequest"]["inputSha256"], first["executionInputSha256"])
        self.assertEqual(validate_contract("BuildRequest", first["buildRequest"]), [])
        changed = copy.deepcopy(first["inputs"])
        changed["flags"]["CFLAGS"] += " changed"
        self.assertNotEqual(first["executionInputSha256"], build.native.canonical_hash(changed))

    def test_changed_tool_bytes_are_blocked(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "tool"
            path.write_bytes(b"synthetic executable")
            tools = {"gcc": {"path": str(path), "sha256": build.native.hash_file(path)}}
            path.write_bytes(b"replaced")
            with self.assertRaisesRegex(build.BuildBlocked, "tool bytes"):
                build._verify_tools(tools)

    @unittest.skipUnless(sys.platform == "linux", "Actual Linux preflight observation")
    def test_real_linux_preflight_records_observation_without_building(self):
        root = Path(__file__).resolve().parents[1]
        policy = build.native._load_json(root / "config/native-linux/v1.json")
        with tempfile.TemporaryDirectory(prefix="tor-w6-preflight-") as temporary:
            folder = Path(temporary)
            cache, output = folder / "explicit-empty-cache", folder / "never-created-output"
            cache.mkdir()
            before = set(folder.rglob("*"))
            report = build.native.preflight(str(root), str(output), str(cache), root=root)
            self.assertEqual(report["executionStatus"], "NOT RUN")
            self.assertFalse(report["readyForBuildPlan"])
            self.assertIsNone(report["buildRequest"])
            self.assertFalse(output.exists())
            self.assertEqual(before, set(folder.rglob("*")))
            self.assertTrue(all(item["status"] == "MISSING" for item in report["sourceObservations"]))
            host = report["environment"]["host"]
            selected = all(host.get(name) == wanted for name, wanted in (
                ("os", "linux"), ("architecture", "x86_64"), ("distroId", "ubuntu"),
                ("distroVersion", "24.04"), ("libcName", "glibc"), ("libcVersion", "2.39")))
            observation = {"schemaVersion": 1, "kind": "actual-w6-preflight-observation",
                           "scope": "host-native" if not host.get("isWsl") else "wsl-host",
                           "selectedHostFactsMatch": selected, "host": host,
                           "resources": report["environment"]["resources"],
                           "sourceCheckout": str(root), "output": str(output), "archiveCache": str(cache),
                           "cacheScope": "Explicit newly-created empty unit-test cache only; no global cache discovery",
                           "tools": {name: {key: row.get(key) for key in ("status", "version", "path", "architecture")}
                                     for name, row in report["toolchainManifest"].items()},
                           "archives": report["sourceObservations"],
                           "requiredNotAvailable": report["requiredNotAvailable"],
                           "executionStatus": "NOT RUN", "buildRequest": None,
                           "limitation": "Environment observation only; missing archives deliberately scoped to empty cache, no build/runtime qualification"}
        evidence = root / "build/task04-checks/native-ci-preflight.json"
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps(observation, sort_keys=True, indent=2) + "\n", encoding="utf-8")
        print("W6_NATIVE_PREFLIGHT_OBSERVATION=" + json.dumps(observation, sort_keys=True))


class ProcessTests(unittest.TestCase):
    def command(self, code, cwd):
        return build.Command("synthetic-process", (sys.executable, "-B", "-c", code), cwd, dict(os.environ))

    def test_original_nonzero_exit_and_bounded_log_are_retained(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "process.log"
            result = build.ProcessRunner(timeout=5).run(self.command("print('synthetic log'); raise SystemExit(23)", tmp), path, threading.Event())
            self.assertEqual(result["returnCode"], 23)
            self.assertEqual(result["rawExitCode"], 23)
            self.assertIsNone(result["terminationSignal"])
            self.assertIn(b"synthetic log", path.read_bytes())
            self.assertEqual(result["logSha256"], build.native.hash_file(path))

    def test_log_open_failure_never_starts_a_process(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "existing.log"
            path.write_bytes(b"preserve existing evidence")
            with patch.object(build.subprocess, "Popen") as process:
                with self.assertRaises(FileExistsError):
                    build.ProcessRunner().run(self.command("raise SystemExit(0)", tmp), path, threading.Event())
            process.assert_not_called()
            self.assertEqual(path.read_bytes(), b"preserve existing evidence")

    def test_thread_start_failure_tears_down_started_process_and_retains_observed_exit(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(build.threading.Thread, "start", side_effect=RuntimeError("synthetic thread failure")):
                started = time.monotonic()
                result = build.ProcessRunner(grace_seconds=0.1).run(
                    self.command("import time; time.sleep(20)", tmp), Path(tmp) / "thread.log", threading.Event())
            self.assertLess(time.monotonic() - started, 5)
            self.assertEqual(result["reason"], "runner-error")
            self.assertEqual(result["errorType"], "RuntimeError")
            self.assertIsNotNone(result["returnCode"])

    def test_output_budget_and_timeout_stop_the_process(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "overflow.log"
            result = build.ProcessRunner(timeout=5, max_log_bytes=64).run(
                self.command("print('x'*10000, flush=True)", tmp), path, threading.Event())
            self.assertEqual(result["reason"], "log-budget")
            self.assertLessEqual(path.stat().st_size, 64)
            path = Path(tmp) / "timeout.log"
            result = build.ProcessRunner(timeout=0.1, grace_seconds=0.1).run(
                self.command("import time; time.sleep(20)", tmp), path, threading.Event())
            self.assertEqual(result["reason"], "timeout")

    @unittest.skipUnless(os.name == "posix", "POSIX process-group cancellation")
    def test_cancellation_stops_parent_and_descendant_with_original_signal(self):
        with tempfile.TemporaryDirectory() as tmp:
            cancellation = threading.Event()
            timer = threading.Timer(0.2, cancellation.set)
            timer.start()
            try:
                result = build.ProcessRunner(timeout=5, grace_seconds=0.1).run(self.command(
                    "import subprocess,sys,time; subprocess.Popen([sys.executable,'-c','import time; time.sleep(20)']); time.sleep(20)", tmp),
                    Path(tmp) / "cancel.log", cancellation)
            finally:
                timer.join()
            self.assertEqual(result["reason"], "cancelled")
            self.assertIsNone(result["rawExitCode"])
            self.assertIn(result["terminationSignal"], (signal.SIGTERM, signal.SIGKILL))


class EngineTests(unittest.TestCase):
    setUp = SourceSafetyTests.setUp
    archive = SourceSafetyTests.archive
    plan = RecipeTests.plan

    def synthetic_plan(self):
        plan = self.plan()
        plan["inputs"]["sources"] = [self.archive(name=name) for name in build.SOURCE_ORDER]
        plan["preflight"] = {"buildRequest": {"diskBudgetBytes": 1024 * 1024}}
        plan["buildRequest"] = json.loads((Path(__file__).resolve().parent / "fixtures/contracts/valid/BuildRequest.json").read_text())
        plan["buildRequest"].update(mode="build", inputSha256=plan["executionInputSha256"])
        return plan

    @unittest.skipUnless(sys.platform == "linux", "Linux workspace permission enforcement")
    def test_shared_workspace_parent_is_refused_before_lock_or_execution(self):
        parent = self.root / "shared"
        parent.mkdir()
        output = parent / "new"
        approval = {"readyForExecution": True, "executionInputSha256": "b" * 64}
        for mode in (0o777, 0o770, 0o1777):
            with self.subTest(mode=oct(mode)):
                parent.chmod(mode)
                with patch.object(build, "execution_plan", return_value=approval), \
                        patch.object(build, "_run_verified") as engine:
                    with self.assertRaisesRegex(build.BuildBlocked, "workspace.*private"):
                        build.execute_build(str(self.root / "sources"), str(output), str(self.root / "cache"),
                                            approved_input_sha256=approval["executionInputSha256"])
                    engine.assert_not_called()
                self.assertEqual(list(parent.iterdir()), [])

    @unittest.skipUnless(sys.platform == "linux", "Linux workspace permission enforcement")
    def test_workspace_rejects_wrong_owner_symlink_and_unsafe_ancestor(self):
        parent = self.root / "private"
        parent.mkdir(mode=0o700)
        link = self.root / "link"
        link.symlink_to(parent, target_is_directory=True)
        shared = self.root / "shared"
        shared.mkdir()
        shared.chmod(0o777)
        nested = shared / "private"
        nested.mkdir(mode=0o700)
        approval = {"readyForExecution": True, "executionInputSha256": "b" * 64}
        with patch.object(build, "execution_plan", return_value=approval), \
                patch.object(build, "_run_verified") as engine:
            def execute(destination):
                return build.execute_build(str(self.root / "sources"), str(destination), str(self.root / "cache"),
                                           approved_input_sha256=approval["executionInputSha256"])

            with self.assertRaisesRegex(build.BuildBlocked, "symlink"):
                execute(link / "run")
            with self.assertRaisesRegex(build.BuildBlocked, "ancestor"):
                execute(nested / "run")
            with patch.object(build.os, "geteuid", return_value=os.geteuid() + 1):
                with self.assertRaises(build.BuildBlocked):
                    execute(parent / "run")
            engine.assert_not_called()
        self.assertEqual(list(parent.iterdir()), [])
        self.assertEqual(list(nested.iterdir()), [])

    @unittest.skipUnless(sys.platform == "linux", "Linux workspace identity enforcement")
    def test_workspace_replacement_cannot_receive_final_evidence(self):
        plan = self.synthetic_plan()
        plan["readyForExecution"] = True
        output = self.root / "run"
        preserved = self.root / "original"
        runner = Mock()

        def replace_run(command, log_path, cancellation):
            self.assertEqual(Path(command.cwd).parent, output / "sources")
            Path(log_path).write_bytes(b"synthetic retained log")
            log_sha = build.native.hash_file(log_path)
            output.rename(preserved)
            output.mkdir(mode=0o700)
            return {"returnCode": 23, "rawExitCode": 23, "terminationSignal": None,
                    "reason": None, "logSha256": log_sha}

        runner.run.side_effect = replace_run
        with patch.object(build, "execution_plan", return_value=plan), \
                patch.object(build, "ProcessRunner", return_value=runner):
            result = build.execute_build(str(self.root / "sources"), str(output), str(self.root / "cache"),
                                         approved_input_sha256=plan["executionInputSha256"])
        runner.run.assert_called_once()
        self.assertEqual(result["status"], "BLOCKED")
        self.assertEqual(list(output.iterdir()), [])
        manifest = json.loads((preserved / "build-manifest.json").read_text())
        self.assertEqual(manifest["status"], "BLOCKED")
        self.assertEqual(manifest["commands"][0]["rawExitCode"], 23)
        saved_log = preserved / "logs/00-zlib-configure.log"
        self.assertEqual(manifest["commands"][0]["logSha256"], build.native.hash_file(saved_log))

    def test_synthetic_engine_success_emits_valid_w1_progress_and_output_digest(self):
        plan, output = self.synthetic_plan(), self.root / "run"
        class FakeRunner:
            def run(self, command, log_path, cancellation):
                Path(log_path).write_bytes(b"Synthetic runner, no compiler invoked")
                if command.name == "tor-install":
                    binary = output / "prefix/bin/tor"
                    binary.parent.mkdir()
                    binary.write_bytes(b"synthetic binary; not an ELF or qualified artifact")
                return {"returnCode": 0, "rawExitCode": 0, "terminationSignal": None, "reason": None,
                        "logSha256": build.native.hash_file(log_path)}
        result = build._run_verified(plan, output, self.root / "cache", threading.Event(), runner=FakeRunner())
        self.assertEqual(result["status"], "PASS")
        self.assertEqual(len(result["commands"]), 15)
        self.assertEqual(result["outputs"][0]["sha256"], build.native.hash_file(output / "prefix/bin/tor"))
        events = [json.loads(line) for line in (output / "events.jsonl").read_text().splitlines()]
        self.assertEqual(events[-1]["state"], "succeeded")
        self.assertEqual([event["sequence"] for event in events], list(range(1, len(events) + 1)))
        self.assertTrue(all(validate_contract("TaskProgress", event) == [] for event in events))
        self.assertTrue(all(event["completed"] is None for event in events))
        with self.assertRaises(FileExistsError):
            build._run_verified(plan, output, self.root / "cache", threading.Event(), runner=FakeRunner())

    def test_synthetic_failure_stops_dag_preserves_exit_and_failed_output(self):
        output = self.root / "failed"
        class FakeRunner:
            def run(self, command, log_path, cancellation):
                Path(log_path).write_bytes(b"synthetic failure")
                return {"returnCode": 37, "rawExitCode": 37, "terminationSignal": None, "reason": None,
                        "logSha256": build.native.hash_file(log_path)}
        result = build._run_verified(self.synthetic_plan(), output, self.root / "cache", threading.Event(), runner=FakeRunner())
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(len(result["commands"]), 1)
        self.assertEqual(result["commands"][0]["rawExitCode"], 37)
        self.assertEqual(result["outputs"], [])
        self.assertTrue((output / "build-manifest.json").exists())

    def test_pre_cancelled_engine_launches_no_process_and_retains_cancelled_manifest(self):
        cancel = threading.Event()
        cancel.set()
        with patch.object(build, "ProcessRunner") as runner:
            result = build._run_verified(self.synthetic_plan(), self.root / "cancelled", self.root / "cache", cancel)
        runner.return_value.run.assert_not_called()
        self.assertEqual(result["status"], "CANCELLED")
        self.assertEqual(result["commands"], [])

    def test_build_deadline_already_applies_during_source_preparation(self):
        output = self.root / "expired"
        with patch.object(build, "BUILD_TIMEOUT", 0), patch.object(build, "ProcessRunner") as runner:
            result = build._run_verified(self.synthetic_plan(), output, self.root / "cache", threading.Event())
        runner.return_value.run.assert_not_called()
        self.assertEqual(result["status"], "FAIL")
        self.assertEqual(result["errorType"], "TimeoutError")
        self.assertEqual(list((output / "snapshots").iterdir()), [])


if __name__ == "__main__":
    unittest.main()
