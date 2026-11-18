import importlib.util
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

from scripts.windows_developer import build as adapter


class WindowsBuildAdapterTests(unittest.TestCase):
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
