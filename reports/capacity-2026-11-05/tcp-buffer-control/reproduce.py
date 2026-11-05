from pathlib import Path
from statistics import median
import sys

root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / 'scripts'))
from capacity_native import NativeVM, exchange
from common import sha256, utcnow, write_json

fixture = root / 'build/capacity-fixture-v2.ext4'
images = root / 'build/iterations/12-capacity32'
output = Path(sys.argv[1]).resolve()
output.mkdir()
report = {
    'started': utcnow(),
    'fixture_sha256': sha256(fixture),
    'profile': 'qemu-x86_64-capacity32',
    'image_sha256': {name: sha256(images / name) for name in ('bzImage', 'disk.img')},
    'scope': 'Offline ECHO path, with Tor idle and unstarted. Guest sysctls only. No production change or online qualification.',
    'orders': [[256, 128, 112], [112, 128, 256], [128, 256, 112]],
    'rows': [],
}
settings = {}
payload = [b'\x9d' * 1024**2] * 16


def read_buffers(agent):
    raw, _ = agent.execute('cat /proc/sys/net/ipv4/tcp_rmem /proc/sys/net/ipv4/tcp_wmem /proc/sys/net/ipv4/tcp_mem')
    return dict(zip(('tcp_rmem', 'tcp_wmem', 'tcp_mem'), [list(map(int, line.split())) for line in raw.splitlines()]))


def set_buffers(agent, defaults, ceilings):
    for name in ('tcp_rmem', 'tcp_wmem'):
        values = [*defaults[name][:2], ceilings[name][2]]
        agent.execute('printf "%s\\n" "' + ' '.join(map(str, values)) + f'" > /proc/sys/net/ipv4/{name}')
    observed = read_buffers(agent)
    for name in ('tcp_rmem', 'tcp_wmem'):
        assert observed[name] == [*defaults[name][:2], ceilings[name][2]]
    assert observed['tcp_mem'] == defaults['tcp_mem']
    return observed


for mem in (128, 256):
    with NativeVM(report['profile'], images, fixture, mem, output / f'defaults-{mem}') as vm:
        settings[mem] = read_buffers(vm.agent)
report['control_defaults'] = settings
write_json(output / 'results.json', report)
print('TCP DEFAULTS', settings, flush=True)
for block, order in enumerate(report['orders'], 1):
    for mem in order:
        with NativeVM(report['profile'], images, fixture, mem, output / f'{block}-{mem}') as vm:
            defaults = read_buffers(vm.agent)
            treatments = ('default', 'crossed', 'crossed', 'default')
            if block % 2 == 0:
                treatments = ('crossed', 'default', 'default', 'crossed')
            for treatment in treatments:
                vm.phase = treatment
                ceilings = defaults if treatment == 'default' else settings[128 if mem == 256 else 256]
                buffers = set_buffers(vm.agent, defaults, ceilings)
                with vm.agent.open('ECHO', 30) as connection:
                    rate = exchange(connection, payload)
                report['rows'].append({'block': block, 'ram_mib': mem, 'treatment': treatment, 'tcp_buffers': buffers, 'bytes_per_second': rate})
                write_json(output / 'results.json', report)
        print('TCP BLOCK', block, mem, flush=True)
for mem in (112, 128, 256):
    print(mem, {name: round(median(row['bytes_per_second'] for row in report['rows'] if row['ram_mib'] == mem and row['treatment'] == name) / 2**20, 2) for name in ('default', 'crossed')})
print('TCP CONTROL COMPLETE', output, flush=True)
