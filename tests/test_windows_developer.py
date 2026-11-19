import hashlib
import io
from pathlib import Path
import subprocess
import struct
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from scripts.windows_developer import scan as developer


class WindowsDeveloperScanTests(unittest.TestCase):
    def test_wrong_msys2_architecture_is_not_available(self):
        with tempfile.TemporaryDirectory() as temporary:
            image = Path(temporary) / "msys-2.0.dll"
            header = bytearray(64)
            header[:2] = b"MZ"
            struct.pack_into("<I", header, 60, 64)
            image.write_bytes(header + b"PE\x00\x00\x4c\x01")
            observed = developer._file_observation(image, developer._require_pe)
            self.assertEqual(observed["status"], "INCOMPATIBLE")

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

    def test_recipe_launcher_and_python_match_actual_precedence(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(developer, "MSYS", Path(temporary)):
            ucrt = Path(temporary) / "ucrt64/bin"
            ucrt.mkdir(parents=True)
            msys = Path(temporary) / "usr/bin"
            msys.mkdir(parents=True)
            (ucrt / "bash.exe").touch()
            (ucrt / "python3.exe").touch()
            (msys / "python3.exe").touch()
            probe = developer.RecipeProbe()
            self.assertIsNone(probe._lookup("bash", "bash"))
            self.assertEqual(probe._lookup("msys2-python", "python3"), ucrt / "python3.exe")
