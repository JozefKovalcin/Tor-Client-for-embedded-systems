import base64
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from desktop.packaging import pack_windows_release as release


class WindowsReleasePackagingTests(unittest.TestCase):
    def test_publish_record_binds_commit_and_file_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            publish = Path(directory).resolve(strict=True)
            artifacts = {name: name.encode() for name in release.PUBLISH_FILES}
            original = {"sourceCommit": "a" * 40, "sha256": release.publish_hashes(artifacts)}
            (publish / release.PUBLISH_RECORD).write_text(json.dumps(original), encoding="utf-8")
            release.verify_publish(publish, artifacts, "a" * 40)
            with self.assertRaisesRegex(ValueError, "build-time record"):
                release.verify_publish(publish, artifacts, "b" * 40)
            artifacts["Tor.Desktop.dll"] = b"changed"
            with self.assertRaisesRegex(ValueError, "build-time record"):
                release.verify_publish(publish, artifacts, "a" * 40)

    def test_reviewed_archive_bytes_are_checked_before_use(self):
        with tempfile.TemporaryDirectory() as directory:
            feed = Path(directory).resolve()
            archive = feed / "sample.1.2.3.nupkg"
            original = b"pinned archive fixture"
            archive.write_bytes(original)
            reviewed = {"packages": [{"id": "Sample", "version": "1.2.3",
                        "signedArchiveSha512Base64": base64.b64encode(hashlib.sha512(original).digest()).decode()}]}
            self.assertEqual(release.locked_archives(feed, reviewed, {"packages": []}), {"Sample/1.2.3": original})
            archive.write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "archive changed"):
                release.locked_archives(feed, reviewed, {"packages": []})

    def test_zip_publication_uses_exact_allowlist_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            target = root / "release.zip"
            (root / "private-state").write_bytes(b"fixture")
            entries = {"manifest.json": b"inventory", "tor-runtime/tor.exe": b"sample"}
            release.write_zip(target, entries)
            with zipfile.ZipFile(target) as archive:
                self.assertIsNone(archive.testzip())
                self.assertEqual({name: archive.read(name) for name in archive.namelist()}, entries)
            original = target.read_bytes()
            with self.assertRaises(FileExistsError):
                release.write_zip(target, {"different": b"replacement"})
            self.assertEqual(target.read_bytes(), original)
