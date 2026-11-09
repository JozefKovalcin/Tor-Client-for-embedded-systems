#!/usr/bin/env python3
"""Repackage the two-PC demo with versioned launchers and verified ARM images."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCES = ROOT / 'scripts/two_pc_demo'
LAUNCHERS = ('Spustit-Tor-Test.cmd', 'Start-Tor-Test.ps1', 'run-wsl.sh', 'run.py', 'README.txt')


def package(base, output):
    artifacts = json.loads((ROOT / 'reports/arm-2026-11-05/build-artifacts.json').read_text())
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        raise ValueError('output exists; preserve it and choose a fresh output path')
    with zipfile.ZipFile(base) as original:
        images = {}
        for item in artifacts:
            name = Path(item['path']).name
            data = original.read(name)
            if len(data) != item['bytes'] or hashlib.sha256(data).hexdigest() != item['sha256']:
                raise ValueError('ARM image SHA-256 or size mismatch: ' + name)
            images[name] = data
        with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
            for name in LAUNCHERS:
                data = (SOURCES / name).read_bytes().replace(b'\r\n', b'\n')
                if name.endswith('.cmd'):
                    data = data.replace(b'\n', b'\r\n')
                archive.writestr(name, data)
            for name, data in images.items():
                archive.writestr(name, data)
    return {name: hashlib.sha256(data).hexdigest() for name, data in images.items()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-zip', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(package(args.base_zip, args.output), indent=2))


if __name__ == '__main__':
    main()
