"""Verify every source and license before extracting into a NEW build directory."""
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
import tarfile


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def verify(path, expected):
    if digest(path) != expected:
        raise ValueError('SHA-256 mismatch: ' + str(path))


def extract(archive, destination, top):
    with tarfile.open(archive) as tar:
        members = tar.getmembers()
        for entry in members:
            path = PurePosixPath(entry.name)
            if ('\\' in entry.name or path.is_absolute() or '..' in path.parts
                    or not path.parts or path.parts[0] != top
                    or not (entry.isfile() or entry.isdir())):
                raise ValueError('unsafe archive member: ' + entry.name)
        # Links and special files were rejected, including on older Python.
        tar.extractall(destination, members=members, filter='data')


def apply_patches(manifest, data, destination):
    tor = next(s for s in data['sources'] if s['name'] == 'tor')
    source = destination / tor['directory']
    for entry in data['tor_source_patches']:
        patch = manifest.parent / entry['file']
        verify(patch, entry['sha256'])
        target = source / entry['source_file']
        verify(target, entry['before_sha256'])
        subprocess.run(['patch', '--batch', '--forward', '--fuzz=0', '-p1',
                        '-i', str(patch.resolve())], cwd=source, check=True)
        verify(target, entry['after_sha256'])
        print('VERIFIED PATCH', entry['file'], entry['sha256'], flush=True)


def main():
    manifest, cache, destination = map(Path, sys.argv[1:])
    data = json.loads(manifest.read_text(encoding='utf-8'))
    cache.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        raise ValueError('source destination already exists; use a fresh build: ' + str(destination))
    for source in data['sources']:
        archive = cache / source['archive']
        if not archive.exists():
            if os.environ.get('TOR_WINDOWS_OFFLINE') == '1':
                raise ValueError('Verified local archive is required: ' + source['archive'])
            temporary = archive.with_suffix(archive.suffix + '.partial')
            subprocess.run(['curl', '--fail', '--location', '--retry', '3',
                            '--proto', '=https', '--tlsv1.2', '-o', str(temporary), source['url']], check=True)
            verify(temporary, source['sha256'])
            temporary.replace(archive)
        verify(archive, source['sha256'])
    destination.mkdir(parents=True)
    for source in data['sources']:
        extract(cache / source['archive'], destination, source['directory'])
        verify(destination / source['directory'] / source['license_file'], source['license_sha256'])
        print('VERIFIED', source['name'], source['version'], source['sha256'], flush=True)
    apply_patches(manifest, data, destination)


if __name__ == '__main__':
    main()
