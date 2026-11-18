import hashlib
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from scripts.windows_developer import install


class WindowsInstallerPlanTests(unittest.TestCase):
    def test_transaction_denied_before_any_process_or_log(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(install.subprocess, "Popen") as spawn:
            stage = Path(folder)
            with self.assertRaisesRegex(ValueError, "cancelled before"):
                install.execute_transaction(["pacman"], stage, {}, lambda: False)
            self.assertEqual(list(stage.iterdir()), [])
            spawn.assert_not_called()

    def test_missing_only_reviewed_local_archive_and_current_approval(self):
        with tempfile.TemporaryDirectory() as folder:
            cache = Path(folder)
            archive = cache / "reviewed-1-x86_64.pkg.tar.zst"
            archive.write_bytes(b"test archive")
            (cache / (archive.name + ".sig")).write_bytes(b"test signature")
            fixture = (("make", "reviewed", "1", "msys/x86_64", "x86_64", hashlib.sha256(b"test archive").hexdigest(), 1),)
            scan = {"profile": "optimized-static", "dependencies": [{"id": "make", "status": "MISSING"},
                    {"id": "pacman", "status": "AVAILABLE"}, {"id": "gcc", "status": "MISSING"}]}
            with patch.object(install, "PACKAGES", fixture):
                report = install.installation_plan(scan, cache)
                self.assertEqual([row["package"] for row in report["packages"]], ["reviewed"])
                self.assertEqual(report["manualDependencies"], ["gcc"])
                install.require_approval(report, report["approvalSha256"])
                with self.assertRaisesRegex(ValueError, "must be approved"):
                    install.require_approval(report, None)
                with self.assertRaisesRegex(ValueError, "must be approved"):
                    install.require_approval({**report, "packages": []}, report["approvalSha256"])
                archive.write_bytes(b"modified")
                damaged = install.installation_plan(scan, cache)
                self.assertEqual(damaged["status"], "BLOCKED")
                self.assertEqual(damaged["blockedArchives"], ["reviewed"])

    def test_workspace_lock_is_exclusive_and_reusable(self):
        with tempfile.TemporaryDirectory() as folder:
            workspace = Path(folder)
            with install.operation_lock(workspace, "install.lock"):
                with self.assertRaisesRegex(ValueError, "Another developer operation"):
                    with install.operation_lock(workspace, "install.lock"):
                        self.fail("a second install was admitted")
            with install.operation_lock(workspace, "install.lock"):
                self.assertTrue((workspace / "install.lock").is_file())
