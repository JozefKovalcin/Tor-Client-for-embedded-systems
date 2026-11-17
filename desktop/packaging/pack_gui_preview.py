#!/usr/bin/env python3
"""Assemble a local Windows x64 GUI preview from a locked offline publish."""

import argparse
import base64
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import struct
import subprocess
import xml.etree.ElementTree as ET
import zipfile


DESKTOP = Path(__file__).resolve().parents[1]
PROJECT = DESKTOP / 'Tor.Desktop'
PROJECT_ASSEMBLIES = {'Tor.Desktop.dll', 'Tor.Application.dll', 'Tor.Contracts.dll',
                      'Tor.Core.dll', 'Tor.Control.dll', 'Tor.Adapters.Windows.dll'}
MIT_BODY = """Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""


def member_path(value):
    path = PurePosixPath(value)
    if not path.parts or path.is_absolute() or '\\' in value or ':' in value or '..' in path.parts:
        raise ValueError(f'Invalid archive or build path: {value}')
    return path


def read_file(root, name):
    parts = member_path(name).parts
    path = root.joinpath(*parts)
    if path.resolve(strict=True) != root.joinpath(*parts) or not path.is_file():
        raise ValueError(f'Expected an ordinary build output: {name}')
    return path.read_bytes()


def x64_pe(data, name):
    if len(data) < 64 or data[:2] != b'MZ':
        raise ValueError(f'Expected an x64 PE: {name}')
    offset = struct.unpack_from('<I', data, 0x3c)[0]
    if offset + 6 > len(data) or data[offset:offset + 6] != b'PE\x00\x00\x64\x86':
        raise ValueError(f'Expected an x64 PE: {name}')


def notices(archive, package_id):
    nuspec_path = next(name for name in archive.namelist() if name.lower() == package_id.lower()+'.nuspec')
    metadata = ET.fromstring(archive.read(nuspec_path)).find('{*}metadata')
    license_element = metadata.find('{*}license')
    if license_element is None or not license_element.text:
        raise ValueError(f'Missing license declaration: {package_id}')
    names = {name for name in archive.namelist() if '/' not in name and
             any(word in name.lower() for word in ('license','notice','copying'))}
    result = {}
    if license_element.get('type') == 'file':
        names.add(license_element.text.strip())
    elif license_element.text.strip() == 'MIT':
        copyright_text = metadata.findtext('{*}copyright')
        if not copyright_text:
            raise ValueError(f'Missing MIT copyright notice: {package_id}')
        result['MIT.txt'] = (copyright_text+'\n\n'+MIT_BODY).encode()
    else:
        raise ValueError(f'Unreviewed license expression: {package_id}')
    for name in sorted(names):
        result[str(member_path(name))] = archive.read(name)
    return result


def build_preview(publish, cache, destination):
    publish = publish.resolve(strict=True)
    cache = cache.resolve(strict=True)
    dependency_name = 'Tor.Desktop.deps.json'
    runtime_name = 'Tor.Desktop.runtimeconfig.json'
    artifacts = {name: read_file(publish,name) for name in ('Tor.Desktop.exe',dependency_name,runtime_name)}
    x64_pe(artifacts['Tor.Desktop.exe'],'Tor.Desktop.exe')
    runtime = json.loads(artifacts[runtime_name])['runtimeOptions']
    if runtime.get('framework') != {'name':'Microsoft.NETCore.App','version':'11.0.0'}:
        raise ValueError('Preview requires a framework-dependent .NET 11.0.0 publish')
    lock_bytes = (PROJECT/'packages.lock.json').read_bytes()
    locked = json.loads(lock_bytes)['dependencies']['net11.0']
    inventory_bytes = (PROJECT/'resolved-package-inventory.json').read_bytes()
    inventory = {item['id']:item for item in json.loads(inventory_bytes)['packages']}
    deps = json.loads(artifacts[dependency_name])
    target_name = deps['runtimeTarget']['name']
    if target_name != '.NETCoreApp,Version=v11.0':
        raise ValueError('Expected the reviewed portable lock target')
    included = []
    projects = set()
    for package, entry in deps['targets'][target_name].items():
        package_id, version = package.split('/')
        library = deps['libraries'][package]
        managed = [(name,member_path(name).name) for name in entry.get('runtime',{})]
        native = [(name,name) for name, row in entry.get('runtimeTargets',{}).items()
                  if row.get('rid') in ('win','win-x64') and row.get('assetType')=='native' and name.lower().endswith('.dll')]
        if library['type'] == 'project':
            for _, name in managed:
                if name not in PROJECT_ASSEMBLIES:
                    raise ValueError(f'Unexpected project output: {name}')
                projects.add(name)
                artifacts[name] = read_file(publish,name)
            continue
        if not managed and not native:
            continue
        pin = locked[package_id]
        reviewed = inventory[package_id]
        if (library['type'] != 'package' or pin['resolved'] != version or reviewed['version'] != version or
                pin['contentHash'] != reviewed['nugetContentHash'] or library['sha512'] != 'sha512-'+pin['contentHash']):
            raise ValueError(f'Dependency lock mismatch: {package}')
        slug = package_id.lower()
        archive_path = f'{slug}/{version}/{slug}.{version}.nupkg'
        archive_bytes = read_file(cache,archive_path)
        sha512 = base64.b64encode(hashlib.sha512(archive_bytes).digest()).decode()
        if sha512 != reviewed['signedArchiveSha512Base64']:
            raise ValueError(f'Cached package archive changed: {package}')
        with zipfile.ZipFile(io.BytesIO(archive_bytes)) as archive:
            for source, name in (*managed,*native):
                payload = read_file(publish,name)
                if payload != archive.read(str(member_path(source))) or name in artifacts:
                    raise ValueError(f'Build output differs from pinned package or collides: {name}')
                if source.startswith('runtimes/'):
                    x64_pe(payload,name)
                artifacts[name] = payload
            for name, payload in notices(archive,package_id).items():
                artifacts[f'licenses/{package_id}/{name}'] = payload
        included.append({'id':package_id,'version':version,'archiveSha512Base64':sha512})
    if projects != PROJECT_ASSEMBLIES:
        raise ValueError(f'Application assembly inventory differs: {sorted(projects)}')
    artifacts['README.md'] = (DESKTOP/'packaging'/'WINDOWS-OFFLINE.md').read_bytes()
    artifacts['provenance/packages.lock.json'] = lock_bytes
    artifacts['provenance/resolved-package-inventory.json'] = inventory_bytes
    source_head = subprocess.check_output(['git','-C',str(DESKTOP),'rev-parse','HEAD'],text=True).strip()
    subprocess.run(['git','-C',str(DESKTOP),'diff','--quiet','HEAD','--','desktop'],check=True)
    manifest = {
        'schemaVersion':1,'artifactKind':'windows-x64-gui-preview','sourceCommit':source_head,
        'selfContained':False,'requiresInstalledRuntime':'Microsoft.NETCore.App 11.0.0 x64',
        'bundledTorStatus':'BLOCKED','cleanHostQualification':'NOT RUN','packages':included,
        'files':{name:{'bytes':len(contents),'sha256':hashlib.sha256(contents).hexdigest()}
                 for name, contents in sorted(artifacts.items())},
    }
    artifacts['manifest.json'] = (json.dumps(manifest,indent=2)+'\n').encode()
    with zipfile.ZipFile(destination,'x',compression=zipfile.ZIP_DEFLATED) as archive:
        for name, contents in sorted(artifacts.items()):
            archive.writestr(name,contents)
    with zipfile.ZipFile(destination) as archive:
        if sorted(archive.namelist()) != sorted(artifacts) or archive.testzip() is not None:
            raise ValueError('ZIP contents do not match the reviewed allowlist')
        for name, contents in artifacts.items():
            if archive.read(name) != contents:
                raise ValueError(f'ZIP entry does not match the manifest: {name}')
    print(f'{destination.name} SHA-256 {hashlib.sha256(destination.read_bytes()).hexdigest()}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish',type=Path,required=True)
    parser.add_argument('--cache',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    args = parser.parse_args()
    build_preview(args.publish,args.cache,args.out)
