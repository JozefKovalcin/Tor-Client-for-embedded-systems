"""Ensure upstream filesystem-skeleton links cannot escape extraction writes."""
import io
from pathlib import Path
import sys
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from bootstrap import extract_upstream
TEST_TMP = ROOT / "build/test-tmp"
TEST_TMP.mkdir(parents=True, exist_ok=True)


def archive(entries):
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w") as tar:
        for name, kind, value in entries:
            info = tarfile.TarInfo(name)
            info.mode = 0o644
            if kind == "link":
                info.type = tarfile.SYMTYPE
                info.linkname = value
                tar.addfile(info)
            else:
                info.size = len(value)
                tar.addfile(info, io.BytesIO(value))
    buf.seek(0)
    return tarfile.open(fileobj=buf)


class ExtractionTests(unittest.TestCase):
    def test_path_traversal_refused(self):
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp, archive([("../escape", "file", b"bad")]) as tar:
            with self.assertRaises(ValueError):
                extract_upstream(tar, tmp, "upstream")

    def test_write_through_symlink_refused(self):
        entries = [("upstream/root", "link", "/tmp"), ("upstream/root/escape", "file", b"bad")]
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp, archive(entries) as tar:
            with self.assertRaises(ValueError):
                extract_upstream(tar, tmp, "upstream")

    @unittest.skipIf(sys.platform == "win32", "Windows symlink creation may require developer mode")
    def test_inert_absolute_target_symlink_preserved(self):
        entries = [("upstream/config", "file", b"valid"), ("upstream/skeleton/link", "link", "/etc/init.d/rcS")]
        with tempfile.TemporaryDirectory(dir=TEST_TMP) as tmp, archive(entries) as tar:
            extract_upstream(tar, tmp, "upstream")
            self.assertEqual((Path(tmp) / "upstream/config").read_bytes(), b"valid")
            self.assertEqual(str((Path(tmp) / "upstream/skeleton/link").readlink()), "/etc/init.d/rcS")


if __name__ == "__main__":
    unittest.main()
