import importlib.util
import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

from scripts.windows_developer import build as adapter


class WindowsBuildAdapterTests(unittest.TestCase):
    def test_native_build_has_explicit_per_run_scratch_paths(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory)
            with patch.object(adapter, "msys_path", side_effect=lambda path: "/fixture/" + path.name), \
                    patch.object(adapter, "native_environment", return_value={"SystemRoot": str(run / "Windows")}):
                env = adapter.build_environment(run, run / "archives", "static")
            scratch = run / "tmp"
            self.assertTrue(scratch.is_dir())
            self.assertEqual(env["TMP"], str(scratch))
            self.assertEqual(env["TEMP"], str(scratch))
            self.assertEqual(env["TMPDIR"], "/fixture/tmp")
            self.assertEqual(env["COMSPEC"], str(run / "Windows/System32/cmd.exe"))
            self.assertEqual(env["TOR_WINDOWS_OFFLINE"], "1")

    def test_disk_and_pinned_snapshot_are_checked_before_invoking_recipe(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            archive = base / "source"
            archive.write_bytes(b"unverified")
            source = {"archive": archive.name, "sha256": "0" * 64}
            with patch.object(shutil, "disk_usage", return_value=shutil._ntuple_diskusage(100, 100, 0)):
                with self.assertRaisesRegex(ValueError, "Insufficient disk"):
                    adapter.stage_archives(base, base / "no-space", [source])
            with self.assertRaisesRegex(ValueError, "Archive SHA-256"):
                adapter.stage_archives(base, base / "bad-pin", [source])

    def test_offline_prepare_never_calls_download_for_missing_pin(self):
        spec = importlib.util.spec_from_file_location("prepare", adapter.NATIVE / "scripts/prepare.py")
        prepare = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(prepare)
        with tempfile.TemporaryDirectory() as directory, patch.dict("os.environ", {"TOR_WINDOWS_OFFLINE": "1"}):
            base = Path(directory)
            with patch.object(sys, "argv", ["prepare", str(adapter.NATIVE / "source-manifest.json"),
                                           str(base / "cache"), str(base / "src")]), patch.object(prepare.subprocess, "run") as spawn:
                with self.assertRaisesRegex(ValueError, "Verified local archive"):
                    prepare.main()
                spawn.assert_not_called()

    def test_patch_or_compiler_failure_preserves_exit_and_log(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            for label, code in [("patch failed", 1), ("compiler failed", 77)]:
                log = base / (str(code) + ".log")
                command = [sys.executable, "-I", "-c", f"print({label!r}); raise SystemExit({code})"]
                self.assertEqual(adapter.monitor(command, None, base, log), code)
                self.assertIn(label, log.read_text())

    def test_artifact_path_cannot_escape_package(self):
        for name in ["../tor.exe", "C:/tor.exe", "bin/../tor.exe", "/tor.exe", r"bin\..\tor.exe"]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                adapter.resolve_record_path(Path("package"), name)

    def test_offline_smoke_rejects_wrong_version_and_online_result(self):
        offline = [
            {"id": name, "status": "PASS", "detail": "Tor version 0.4.9.14." if name == "--version" else "verified"}
            for name in ("--version", "--help", "shipped_configs", "compiled_modules", "invalid_config_rejected",
                         "startup_and_writable_state", "clean_shutdown", "restart")
        ]
        offline.extend({"id": name, "status": "SKIPPED", "detail": "offline"}
                       for name in ("public_bootstrap", "socks_remote_dns"))
        self.assertTrue(adapter.smoke_passed({"tests": offline}, "0.4.9.14"))
        self.assertFalse(adapter.smoke_passed({"tests": offline}, "0.4.9.13"))
        self.assertFalse(adapter.smoke_passed({"tests": offline[:-1]}, "0.4.9.14"))
        offline[-1]["status"] = "PASS"
        self.assertFalse(adapter.smoke_passed({"tests": offline}, "0.4.9.14"))

    def test_activation_refuses_unbuilt_absent_or_unverified_smoke(self):
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            run = workspace / "builds/run-fixture"
            run.mkdir(parents=True)
            for state, smoke in [("INTERRUPTED", "NOT RUN"), ("FAILED", "FAIL"), ("BUILT", "NOT RUN")]:
                record = {"schemaVersion": 1, "id": run.name, "profile": "optimized-static",
                          "package": "pipeline/dist", "compilerExit": 0, "state": state, "smokeState": smoke}
                adapter.write_record(run / "build-record.json", record)
                with self.assertRaisesRegex(ValueError, "successful offline smoke"):
                    adapter.candidate(workspace, run.name)
                with self.assertRaisesRegex(ValueError, "successful offline smoke"):
                    adapter.export_runtime(workspace, run.name)
                self.assertFalse((workspace / "exports").exists())

    def test_export_rechecks_inventory_and_never_overwrites_a_zip(self):
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            run = workspace / "builds/run-fixture"
            package = run / "pipeline/dist"
            (package / "licenses").mkdir(parents=True)
            native = workspace / "native"
            native.mkdir()
            (native / "build-windows.sh").write_bytes(b"reviewed recipe")
            license_path = package / "licenses/tor-LICENSE.txt"
            license_path.write_bytes(b"fixture license text")
            manifest = {"sources": [{"name": "tor", "version": "0.4.9.14", "license_sha256": adapter.sha256(license_path)}]}
            (native / "source-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            shutil.copyfile(native / "source-manifest.json", package / "source-manifest.json")

            header = bytearray(64)
            header[:2] = b"MZ"
            struct.pack_into("<I", header, 60, 64)
            image = package / "tor.exe"
            image.write_bytes(header + b"PE\x00\x00\x64\x86")
            files = {path.relative_to(package).as_posix(): {"bytes": path.stat().st_size, "sha256": adapter.sha256(path)}
                     for path in (image, license_path, package / "source-manifest.json")}
            pe = {"machine": "AMD64", "format": "PE32+", "dynamic_base": True, "nx_compat": True, "high_entropy_va": True}
            report_path = package / "package-report.json"
            adapter.write_record(report_path, {"status": "PASS", "files": files, "pe": {"tor.exe": pe}})
            smoke = run / "smoke.json"
            smoke.write_text("{}", encoding="utf-8")
            adapter.write_record(run / "build-record.json", {
                "schemaVersion": 1, "id": run.name, "profile": "optimized-static", "version": "0.4.9.14",
                "package": "pipeline/dist", "compilerExit": 0, "state": "BUILT", "smokeState": "PASS",
                "sourceManifestSha256": adapter.sha256(native / "source-manifest.json"),
                "recipeSha256": adapter.sha256(native / "build-windows.sh"),
                "packageReportSha256": adapter.sha256(report_path), "smokePackageSha256": adapter.sha256(report_path),
                "artifacts": files, "smokeEvidence": smoke.name, "smokeEvidenceSha256": adapter.sha256(smoke),
            })
            with patch.object(adapter, "NATIVE", native), patch.object(adapter, "source_manifest", return_value=manifest), \
                    patch.object(adapter, "smoke_passed", return_value=True):
                exported = adapter.export_runtime(workspace, run.name)
                archive_path = Path(exported["path"])
                self.assertEqual(exported["sha256"], adapter.sha256(archive_path))
                with zipfile.ZipFile(archive_path) as archive:
                    self.assertIsNone(archive.testzip())
                    self.assertCountEqual(archive.namelist(), [*files, report_path.name])
                    for name, expected in files.items():
                        self.assertEqual(archive.read(name), (package / name).read_bytes())
                with self.assertRaisesRegex(ValueError, "already exists"):
                    adapter.export_runtime(workspace, run.name)
                image.write_bytes(b"unexpected runtime")
                with self.assertRaisesRegex(ValueError, "size or SHA-256 changed"):
                    adapter.export_runtime(workspace, run.name)
                self.assertEqual(exported["sha256"], adapter.sha256(archive_path))
                self.assertEqual(list(archive_path.parent.iterdir()), [archive_path])
