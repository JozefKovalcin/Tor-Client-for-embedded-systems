"""Exercise failure/report paths with explicit fake build dependencies."""
import contextlib
import io
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
import validate
from common import read_json
from run_all import run
from types import SimpleNamespace

TEST_TMP = ROOT / "build/test-tmp"
TEST_TMP.mkdir(parents=True, exist_ok=True)


class FailurePipelineTests(unittest.TestCase):
    def test_failed_build_keeps_logs_and_reports_nonzero(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            root = Path(tmp)
            for name in ("config", "br2-external"):
                shutil.copytree(ROOT / name, root / name)
            (root / "scripts").mkdir()
            (root / "tests").mkdir()
            source = root / "upstream"
            source.mkdir()
            calls = []
            def fake_run(command, path, env=None):
                calls.append(command)
                path.write_text("synthetic command output\n")
                if "all" in command:
                    raise subprocess.CalledProcessError(2, command)
                if command[-1] == "tor_qemu_x86_64_defconfig":
                    output = Path(next(c[2:] for c in command if c.startswith("O=")))
                    shutil.copy2(root / "br2-external/configs/tor_qemu_x86_64_defconfig", output / ".config")
            with patch.object(validate, "ROOT", root), patch.object(validate, "source_tree", return_value=source), \
                 patch.object(validate, "run_logged", side_effect=fake_run), \
                 patch.object(validate, "check_host"), \
                 patch.object(validate, "git_metadata", return_value={"commit": "synthetic", "dirty": True}), \
                 patch.object(sys, "argv", ["validate.py", "--change", "synthetic failure fixture"]), \
                 contextlib.redirect_stdout(io.StringIO()):
                code = validate.main()
            self.assertEqual(code, 1)
            runs = list((root / "reports/benchmarks").iterdir())
            self.assertEqual(len(runs), 1)
            self.assertIn("synthetic command output", (runs[0] / "build.log").read_text())
            result = read_json(runs[0] / "results.json")
            self.assertEqual(result["build"]["status"], "FAIL")
            self.assertTrue(all(t["status"] == "BLOCKED" for t in result["tests"] if t["id"] != "repository_checks"))
            self.assertTrue((root / "reports/optimization-history.csv").is_file())
            self.assertFalse((root / "reports/baseline").exists())

    def test_unavailable_tor_is_blocked_without_false_metrics(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp:
            args = SimpleNamespace(work_dir=Path(tmp) / "state", tor="no-such-test-tor-executable",
                                   mode="online", template=ROOT / "br2-external/board/qemu/torrc.in")
            result = run(args)
            self.assertTrue(all(t["status"] == "BLOCKED" for t in result["tests"]))
            self.assertIsNone(result["metrics"]["persistent_storage_bytes"])


if __name__ == "__main__":
    unittest.main()
