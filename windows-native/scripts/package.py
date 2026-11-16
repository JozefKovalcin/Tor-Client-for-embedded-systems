"""Stage a new distribution, verify PE hardening and resolve every DLL recursively."""
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import time
from prepare import digest

# These are Windows components, never copied from the build environment.
SYSTEM_DLLS = set('advapi32.dll bcrypt.dll crypt32.dll gdi32.dll iphlpapi.dll '
                 'kernel32.dll msvcrt.dll ncrypt.dll normaliz.dll ole32.dll '
                 'oleaut32.dll psapi.dll rpcrt4.dll secur32.dll shell32.dll '
                 'shlwapi.dll ucrtbase.dll user32.dll userenv.dll version.dll '
                 'winmm.dll ws2_32.dll wldap32.dll kernelbase.dll ntdll.dll '
                 'comdlg32.dll comctl32.dll dnsapi.dll netapi32.dll wintrust.dll'.split())


def pe_info(path):
    data = Path(path).read_bytes()
    if data[:2] != b'MZ' or len(data) < 64:
        raise ValueError('not a PE executable: ' + str(path))
    offset = struct.unpack_from('<I', data, 0x3c)[0]
    if data[offset:offset+4] != b'PE\0\0':
        raise ValueError('missing PE signature: ' + str(path))
    machine, = struct.unpack_from('<H', data, offset + 4)
    magic, = struct.unpack_from('<H', data, offset + 24)
    characteristics, = struct.unpack_from('<H', data, offset + 24 + 70)
    flags = {'dynamic_base': bool(characteristics & 0x40),
             'nx_compat': bool(characteristics & 0x100),
             'high_entropy_va': bool(characteristics & 0x20)}
    if machine != 0x8664 or magic != 0x20b or not all(flags.values()):
        raise ValueError('x86-64 PE32+ / ASLR / DEP / high-entropy VA required: ' + str(path))
    return {'machine': 'AMD64', 'format': 'PE32+', **flags}


def imports(path):
    result = subprocess.check_output(['objdump', '-p', str(path)], text=True)
    return sorted(set(re.findall(r'DLL Name:\s*(\S+)', result)), key=str.lower)


def system_dll(name):
    name = name.lower()
    return name in SYSTEM_DLLS or name.startswith(('api-ms-win-', 'ext-ms-win-'))


def resolve(stage, search):
    pending = [stage / 'tor.exe']
    done = {}
    while pending:
        path = pending.pop()
        key = path.name.lower()
        if key in done:
            continue
        info = pe_info(path)
        dependencies = imports(path)
        done[key] = {**info, 'imports': dependencies}
        for name in dependencies:
            lower = name.lower()
            if 'msys-' in lower or 'cygwin' in lower:
                raise ValueError('POSIX runtime dependency forbidden: ' + name)
            if system_dll(name):
                continue
            target = stage / name
            if not target.exists():
                found = next((p for directory in search for p in directory.glob('*')
                              if p.name.lower() == lower), None)
                if found is None:
                    raise ValueError('unresolved DLL (not a Windows system component): ' + name)
                shutil.copy2(found, target)
                subprocess.run(['strip', '--strip-unneeded', str(target)], check=True)
            pending.append(target)
    return done


def main():
    root, run, output, toolbin = map(Path, sys.argv[1:])
    stage = run / ('package-' + str(time.time_ns()))
    stage.mkdir()  # Never reuse a half-built package.
    shutil.copy2(run / 'src/tor-0.4.9.14/src/app/tor.exe', stage / 'tor.exe')
    subprocess.run(['strip', '--strip-unneeded', str(stage / 'tor.exe')], check=True)
    dependencies = resolve(stage, [run / 'prefix/bin', toolbin])
    shutil.copytree(root / 'config', stage / 'config')
    shutil.copy2(root / 'scripts/start-tor.cmd', stage / 'start-tor.cmd')
    # Runtime launchers must work through cmd.exe regardless of Git EOL settings.
    launcher = stage / 'start-tor.cmd'
    launcher.write_bytes(launcher.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
    if (root / 'README.txt').is_file():
        shutil.copy2(root / 'README.txt', stage / 'README.txt')
    else:
        shutil.copy2(root / 'RUNTIME.md', stage / 'README.md')
    if (root / 'two-pc-test').is_dir():
        for name in ('TEST.cmd', 'test.ps1', 'test-server.ps1', 'test-peer.ps1'):
            target = stage / name
            shutil.copy2(root / 'two-pc-test' / name, target)
    for target in stage.glob('*.cmd'):
        target.write_bytes(target.read_bytes().replace(b'\r\n', b'\n').replace(b'\n', b'\r\n'))
    shutil.copy2(root / 'source-manifest.json', stage / 'source-manifest.json')
    licenses = stage / 'licenses'
    licenses.mkdir()
    manifest = json.loads((root / 'source-manifest.json').read_text(encoding='utf-8'))
    for source in manifest['sources']:
        shutil.copy2(run / 'src' / source['directory'] / source['license_file'],
                     licenses / (source['name'] + '-LICENSE.txt'))
    # libssp/libgcc DLLs, if imported, are covered by GCC's runtime exception.
    for name in ('gcc', 'libgcc', 'libwinpthread', 'crt'):
        runtime_licenses = toolbin.parent / 'share/licenses' / name
        if not runtime_licenses.exists():
            raise ValueError('Missing runtime licensing records: ' + str(runtime_licenses))
        shutil.copytree(runtime_licenses, licenses / name)
    files = {str(p.relative_to(stage)): {'bytes': p.stat().st_size, 'sha256': digest(p)}
             for p in stage.rglob('*') if p.is_file()}
    report = {'status': 'PASS', 'scope': 'build/package static inspection; native runtime is separate',
              'pe': dependencies, 'files': files, 'tor_exe_bytes': files['tor.exe']['bytes'],
              'dll_bytes': sum(v['bytes'] for k, v in files.items() if k.lower().endswith('.dll')),
              'distribution_bytes_before_report': sum(v['bytes'] for v in files.values()),
              'metrics': {'idle_rss_bytes': None, 'peak_rss_bytes': None, 'bootstrap_seconds': None,
                          'connection_latency_seconds': None, 'throughput_bytes_per_second': None}}
    (stage / 'package-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    # Preserve earlier distributions and any user state; no recursive deletion.
    if output.exists():
        backup = output.with_name(output.name + '.previous-' + str(time.time_ns()))
        output.rename(backup)
        print('Preserved previous distribution:', backup)
    shutil.copytree(stage, output)
    (run / 'package-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('status', 'tor_exe_bytes', 'dll_bytes', 'distribution_bytes_before_report')}, indent=2))


if __name__ == '__main__':
    main()
