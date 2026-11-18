import hashlib
import io
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from scripts.windows_developer import scan as developer


class WindowsDeveloperScanTests(unittest.TestCase):
    def test_verified_archive_and_license_are_both_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            cache = Path(temporary)
            archive = cache / "fixture.tar"
            data = b"fixture license"
            with tarfile.open(archive, "w") as bundle:
                member = tarfile.TarInfo("fixture/LICENSE")
                member.size = len(data)
                bundle.addfile(member, io.BytesIO(data))
            source = {"archive": archive.name, "directory": "fixture", "license_file": "LICENSE",
                      "sha256": developer.sha256(archive), "license_sha256": hashlib.sha256(data).hexdigest()}
            self.assertEqual(developer.inspect_source(cache, source), archive)
            with self.assertRaisesRegex(ValueError, "license SHA-256"):
                developer.inspect_source(cache, {**source, "license_sha256": "0" * 64})
            with self.assertRaisesRegex(ValueError, "Archive SHA-256"):
                developer.inspect_source(cache, {**source, "sha256": "0" * 64})

    def test_windows_recipe_lookup_ignores_arbitrary_path(self):
        probe = developer.RecipeProbe()
        with tempfile.TemporaryDirectory() as temporary, patch.object(developer, "MSYS", Path(temporary) / "missing"), \
                patch.dict("os.environ", {"PATH": temporary}):
            (Path(temporary) / "gcc.exe").touch()
            self.assertIsNone(probe._lookup("msys2-gcc", "gcc"))
            self.assertEqual(probe.tool("msys2-gcc")["status"], "MISSING")

    def test_compiler_triplet_and_side_by_side_sdk(self):
        probe = developer.RecipeProbe()
        status, _ = developer._classified_tool({"target": "x86_64-w64-mingw32"},
            developer._record("AVAILABLE", "Observed", version="aarch64-w64-mingw32"))
        self.assertEqual(status, "INCOMPATIBLE")
        with patch.object(probe, "output", return_value=subprocess.CompletedProcess([], 0, "10.0.200 [older]\n11.0.100 [pinned]\n")):
            observed = probe.dotnet(Path("dotnet"), "dotnet-sdk", "--list-sdks", "11.0.100")
            self.assertEqual((observed["status"], observed["version"]), ("AVAILABLE", "11.0.100"))
