import io
import json
from pathlib import Path
import struct
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import prepare
import package


class PipelineTests(unittest.TestCase):
    def test_source_pins_match_embedded(self):
        for source in json.loads((ROOT / 'source-manifest.json').read_text())['sources']:
            name = {'openssl': 'libopenssl', 'zlib': 'libzlib'}.get(source['name'], source['name'])
            upstream = (ROOT.parent / 'config/upstream' / (name + '.hash')).read_text()
            self.assertIn(source['sha256'] + '  ' + source['archive'], upstream)
            self.assertIn(source['license_sha256'], upstream)

    def test_bad_hash_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'archive'
            path.write_bytes(b'tampered')
            with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
                prepare.verify(path, '0' * 64)

    def test_reviewed_patch_hash(self):
        manifest = json.loads((ROOT / 'source-manifest.json').read_text())
        self.assertEqual(len(manifest['tor_source_patches']), 1)
        for entry in manifest['tor_source_patches']:
            prepare.verify(ROOT / entry['file'], entry['sha256'])
            self.assertNotEqual(entry['before_sha256'], entry['after_sha256'])

    def test_traversal_and_links_rejected(self):
        for name, kind in [('src/../../escape', tarfile.REGTYPE), ('/src/escape', tarfile.REGTYPE),
                           ('src/link', tarfile.SYMTYPE), ('src/hard', tarfile.LNKTYPE),
                           ('src/..\\escape', tarfile.REGTYPE)]:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as tmp:
                archive = Path(tmp) / 'bad.tar'
                with tarfile.open(archive, 'w') as tar:
                    entry = tarfile.TarInfo(name); entry.type = kind; entry.linkname = '/escape'
                    tar.addfile(entry, io.BytesIO())
                with self.assertRaisesRegex(ValueError, 'unsafe archive'):
                    prepare.extract(archive, Path(tmp) / 'out', 'src')

    def test_pe_security_and_architecture(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'tor.exe'
            data = bytearray(512); data[:2] = b'MZ'
            struct.pack_into('<I', data, 0x3c, 128)
            data[128:132] = b'PE\0\0'
            struct.pack_into('<H', data, 132, 0x8664)
            struct.pack_into('<H', data, 152, 0x20b)
            struct.pack_into('<H', data, 222, 0x160)
            path.write_bytes(data)
            self.assertEqual(package.pe_info(path)['format'], 'PE32+')
            struct.pack_into('<H', data, 222, 0x60); path.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'ASLR / DEP'):
                package.pe_info(path)

    def test_unresolved_and_msys_dependencies_fail_closed(self):
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            for name in ('msys-2.0.dll', 'cygwin1.dll', 'unknown.dll'):
                with self.subTest(name=name), patch.object(package, 'pe_info', return_value={}), \
                     patch.object(package, 'imports', return_value=[name]):
                    with self.assertRaises(ValueError):
                        package.resolve(stage, [stage])


if __name__ == '__main__':
    unittest.main()
