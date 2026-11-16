"""Bundle the native source builder and single-entry two-PC test."""
import hashlib
from pathlib import Path
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = (
    'COMPILE.cmd', 'build-windows.sh', 'source-manifest.json',
    'scripts/compile.sh', 'scripts/prepare.py', 'scripts/package.py', 'scripts/package.sh',
    'scripts/start-tor.cmd', 'config/defaults-torrc', 'config/torrc', 'config/torrc-onion-service',
    'patches/0001-queue-connected-before-reading.patch',
    'two-pc-test/TEST.cmd', 'two-pc-test/test.ps1',
    'two-pc-test/test-server.ps1', 'two-pc-test/test-peer.ps1',
)


def main():
    output = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent / 'build/Tor-Windows-Compile-Test-8081.zip'
    output.parent.mkdir(parents=True, exist_ok=True)
    entries = {name: (ROOT / name).read_bytes() for name in FILES}
    entries['README.txt'] = (ROOT / 'BUNDLE_README.txt').read_text(encoding='utf-8').encode('utf-8-sig')
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(entries.items()):
            if name.endswith(('.cmd', '.txt')):
                data = data.replace(b'\r\n', b'\n').replace(b'\n', b'\r\n')
            info = zipfile.ZipInfo('Tor-Windows-8081/' + name, (2026, 11, 12, 0, 0, 0))
            info.create_system = 3
            info.external_attr = (0o100755 if name.endswith('.sh') else 0o100644) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(entries)
        assert [n for n in archive.namelist() if n.endswith(('.md','.txt'))] == ['Tor-Windows-8081/README.txt']
    with output.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    output.with_suffix('.zip.sha256').write_text(digest + '  ' + output.name + '\n')
    print(str(output))
    print(f'{len(entries)} files; {output.stat().st_size:,} bytes; SHA-256 {digest}')


if __name__ == '__main__':
    main()
