#!/usr/bin/env python3
"""Native endpoint laboratory: host controller, one guest Tor, no guest Python.

The observer and echo backend live on a read-only diagnostic disk, not in the
production image. SAFECOOKIE credentials and onion private keys stay in guest.
Private-network diagnostics never satisfy the mandatory public bootstrap test.
"""
import base64
import argparse
import contextlib
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import socket
import struct
import subprocess
import sys
import threading
import time

from common import ROOT,read_json,write_json,sha256,git_metadata,utcnow
sys.path.insert(0,str(ROOT/'tests'))
from run_all import Control,Blocked,port,socks_handshake
from echo_server import SERVER_PAYLOAD,read_frame,receive,send_frame


class Agent:
    def __init__(self,address):self.address=address
    def open(self,command,timeout=30):
        s=socket.create_connection(('127.0.0.1',self.address),timeout=timeout)
        s.settimeout(timeout);s.sendall(command.encode()+b'\n');return s
    def execute(self,command,timeout=60,check=True):
        with self.open('EXEC '+command,timeout) as s:
            data=bytearray()
            while True:
                block=s.recv(65536)
                if not block:break
                data.extend(block)
                if len(data)>8*1024**2:raise ValueError('diagnostic output exceeds bound')
        text=data.decode('utf-8',errors='replace');m=re.search(r'\n__CAPACITY_EXIT=(\d+)\s*$',text)
        if not m:raise RuntimeError('missing native command completion')
        if check and int(m[1]):raise RuntimeError('native command exited '+m[1])
        return text[:m.start()],int(m[1])
    def sample(self):
        with self.open('SAMPLE') as s:return json.loads(s.makefile('rb').readline(65536))
    def control(self):
        # The agent verifies SAFECOOKIE locally before forwarding any commands.
        c=Control.__new__(Control);c.sock=self.open('CONTROL');c.file=c.sock.makefile('rb');return c
    def socks(self,hostname,target=80,timeout=180):
        if not re.fullmatch(r'[a-z2-7]{56}\.onion',hostname):raise ValueError('v3 address required')
        s=self.open('SOCKS',timeout)
        try:
            socks_handshake(s);host=hostname.encode()
            s.sendall(b'\x05\x01\x00\x03'+bytes([len(host)])+host+struct.pack('!H',target))
            h=receive(s,4)
            if h[:3]!=b'\x05\x00\x00':raise ConnectionError('candidate SOCKS rejected connection')
            size={1:4,4:16}.get(h[3])
            if h[3]==3:size=receive(s,1)[0]
            if size is None:raise AssertionError('invalid SOCKS reply')
            receive(s,size+2);return s
        except BaseException:s.close();raise


class NativeVM:
    def __init__(self,profile,images,fixture,memory,run):
        self.profile=read_json(ROOT/'config/profiles'/(profile+'.json'))
        self.images=ROOT/images;self.fixture=Path(fixture);self.memory=memory;self.run=Path(run)
        self.process=None;self.agent=Agent(port());self.phase='boot';self.samples=[];self.errors=[]
        self.done=threading.Event();self.reader=None
    def start(self):
        p=self.profile;e=p['environment'];self.run.mkdir(parents=True,exist_ok=False)
        self.image_hashes={k:sha256(self.images/name) for k,name in p['artifacts'].items()}
        net=f'user,id=net0,restrict=off,hostfwd=tcp:127.0.0.1:{self.agent.address}-:19090'
        cmd=[p['qemu'],'-machine',e['machine'],'-cpu',e['cpu'],'-accel',e['accelerator'],'-m',str(self.memory),'-smp',str(e['cpus']),'-nographic','-monitor','none','-no-reboot','-kernel',str(self.images/p['artifacts']['kernel']),'-drive',f'file={self.images/p["artifacts"]["rootfs"]},if=virtio,format=raw,snapshot=on','-append',p.get('kernel_root','root=/dev/vda1 ro')+' console=ttyS0,115200 init=/bin/sh tor_validation_external=1','-netdev',net,'-device','virtio-net-pci,netdev=net0','-device','virtio-rng-pci','-drive',f'file={self.fixture},if=virtio,format=raw,readonly=on']
        write_json(self.run/'qemu-command.json',cmd)
        self.log=(self.run/'qemu.log').open('wb');begin=time.monotonic()
        self.process=subprocess.Popen(cmd,stdin=subprocess.PIPE,stdout=self.log,stderr=subprocess.STDOUT)
        stage=0
        script='\n'.join(['#!/bin/sh','set -eu','mkdir -p /run/lock/subsys','hostname -F /etc/hostname','/etc/init.d/rcS','umask 077','mkdir -p /run/probe /run/tor /run/capacity /var/lib/tor','chown tor:tor /run/tor /var/lib/tor','chmod 0700 /run/tor /var/lib/tor','mount -t ext4 -o ro,noload /dev/vdb /run/probe','exec /run/probe/native-agent'])+'\n'
        payload=base64.b64encode(gzip.compress(script.encode(),mtime=0)).decode()
        while time.monotonic()-begin<120:
            if self.process.poll() is not None:raise Blocked('guest exited during native boot')
            text=(self.run/'qemu.log').read_bytes()[-8192:]
            if stage==0 and any(x in text for x in (b"can't access tty",b'/ # ',b'~ # ')):
                self.process.stdin.write(b"mount -t proc proc /proc && mkdir -p /dev/pts /dev/shm && mount -a && stty -echo && printf '\\nCAPACITY_BOOTSTRAP_READY\\n'\n");self.process.stdin.flush();stage=1
            elif stage==1 and re.search(rb'(?:^|\n)CAPACITY_BOOTSTRAP_READY\r?\n',text):
                self.process.stdin.write(('printf %s '+payload+' | base64 -d | gunzip > /tmp/native-boot.sh && sh /tmp/native-boot.sh\n').encode());self.process.stdin.flush();stage=2
            elif stage==2 and b'CAPACITY_AGENT_READY' in text:
                try:self.agent.sample();break
                except (OSError,ValueError):pass
            time.sleep(.2)
        else:raise Blocked('native boot deadline; serial preserved')
        self.boot_seconds=time.monotonic()-begin
        self.reader=threading.Thread(target=self._monitor,daemon=True);self.reader.start()
        return self
    def _monitor(self):
        try:
            with self.agent.open('MONITOR') as s,s.makefile('rb') as file,(self.run/'samples.jsonl').open('w') as out:
                while not self.done.is_set():
                    line=file.readline(65536)
                    if not line:break
                    row=json.loads(line);row['host_time']=time.monotonic();row['phase']=self.phase
                    self.samples.append(row);out.write(json.dumps(row,separators=(',',':'))+'\n');out.flush()
        except (OSError,ValueError) as exc:self.errors.append(type(exc).__name__+': '+str(exc))
    def close(self):
        self.done.set()
        if self.reader:self.reader.join(timeout=2)
        if self.process and self.process.poll() is None:
            with contextlib.suppress(Exception):
                text,_=self.agent.execute('tail -n 120 /run/tor/native.log 2>/dev/null || true',timeout=3,check=False)
                (self.run/'guest-notice.log').write_text(text)
            with contextlib.suppress(Exception):self.agent.execute('poweroff -f',timeout=3,check=False)
            try:self.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self.process.terminate()
                try:self.process.wait(timeout=5)
                except subprocess.TimeoutExpired:self.process.kill();self.process.wait(timeout=5)
        if hasattr(self,'log'):self.log.close()
        for key,name in self.profile['artifacts'].items():
            if sha256(self.images/name)!=self.image_hashes[key]:raise AssertionError('preserved image changed')
    def __enter__(self):
        try:return self.start()
        except BaseException:
            if self.process:self.close()
            raise
    def __exit__(self,*args):self.close()


def wait_until(function,timeout,detail):
    deadline=time.monotonic()+timeout;last=None
    while time.monotonic()<deadline:
        try:
            value=function()
            if value:return value
        except (OSError,ConnectionError,AssertionError,RuntimeError) as exc:last=type(exc).__name__
        time.sleep(1)
    raise Blocked(detail+(' ('+last+')' if last else ''))


def tor_config(private_lines=None):
    text='''User tor
DataDirectory /var/lib/tor
PidFile /run/tor/tor.pid
SocksPort 127.0.0.1:9050
ControlPort 127.0.0.1:9051
CookieAuthentication 1
CookieAuthFileGroupReadable 0
ClientOnly 1
SafeLogging 1
AvoidDiskWrites 1
RunAsDaemon 1
Log notice file /run/tor/native.log
GeoIPFile /dev/null
GeoIPv6File /dev/null
DisableNetwork 0
HiddenServiceDir /var/lib/tor/capacity-onion
HiddenServiceVersion 3
HiddenServicePort 80 127.0.0.1:18080
'''
    if private_lines:text+='TestingTorNetwork 1\nPathsNeededToBuildCircuits 0.67\n'+private_lines
    return text


def install_config(vm,private_lines=None):
    data=base64.b64encode(tor_config(private_lines).encode()).decode()
    vm.agent.execute('printf %s '+shlex.quote(data)+' | base64 -d > /run/capacity/torrc')


def start_tor(vm):
    vm.agent.execute('/usr/bin/tor --defaults-torrc /etc/tor/defaults-torrc -f /run/capacity/torrc')


def bootstrapped(vm):
    with contextlib.closing(vm.agent.control()) as c:return 'PROGRESS=100' in c.command('GETINFO status/bootstrap-phase')


def identity(vm):
    value,_=vm.agent.execute('cat /var/lib/tor/capacity-onion/hostname')
    value=value.strip()
    if not re.fullmatch(r'[a-z2-7]{56}\.onion',value):raise AssertionError('not a v3 service')
    return value


def payloads():
    # Exact required sizes/content from the original unchanged regression suite.
    import random
    rng=random.Random(734291)
    return [b'',b'hello\x00\xff',bytes(range(256)),rng.randbytes(4096),rng.randbytes(1024**2)]


def exchange(sock,data):
    if read_frame(sock)!=SERVER_PAYLOAD:raise AssertionError('server push differs')
    begin=time.monotonic();total=0
    for block in data:
        send_frame(sock,block);received=read_frame(sock)
        if hashlib.sha256(received).digest()!=hashlib.sha256(block).digest():raise AssertionError('bidirectional payload integrity failed')
        total+=2*len(block)
    return total/max(1e-6,time.monotonic()-begin)


def summarize_samples(samples):
    if not samples:return {'samples':0}
    mem=[x['memory'] for x in samples]
    tor=[x['tor'] for x in samples if x.get('tor')]
    def extrema(values,method=max):
        values=[v for v in values if isinstance(v,(int,float))];return method(values) if values else None
    used=[m['MemTotal']-m['MemAvailable'] for m in mem if m.get('MemAvailable') is not None]
    available=extrema([m.get('MemAvailable') for m in mem],min)
    totals=[m.get('MemTotal') for m in mem]
    cpu={}
    for row in samples:
        p=row.get('tor')
        if p:cpu[p['pid']]=max(cpu.get(p['pid'],0),p['cpu_ticks'])
    elapsed=samples[-1]['time']-samples[0]['time']
    return {'samples':len(samples),'linux_memtotal_bytes':extrema(totals),'min_available_bytes':available,'min_available_fraction':available/extrema(totals) if available is not None else None,'peak_system_used_bytes':extrema(used),'peak_process_hwm_bytes':extrema([p.get('hwm_bytes') for p in tor]),'peak_rss_bytes':extrema([p.get('rss_bytes') for p in tor]),'peak_pss_bytes':extrema([p.get('pss_bytes') for p in tor]),'tor_cpu_seconds':sum(cpu.values())/samples[-1]['hz'],'tor_cpu_percent':100*sum(cpu.values())/samples[-1]['hz']/elapsed if elapsed>0 else None,'vmstat_start':samples[0]['vmstat'],'vmstat_end':samples[-1]['vmstat'],'state_available_min_bytes':extrema([x.get('state_available_bytes') for x in samples],min)}


def run_case(profile,images,fixture,memory,run,peer,origin,local_trials=3):
    """Additional native workload, not a replacement for frozen regression tests."""
    import copy
    result=copy.deepcopy(origin);result.update(run_id=Path(run).name,timestamp=utcnow(),tests=[],metrics={k:None for k in origin['metrics']})
    static=('tor_executable_bytes','library_bytes','kernel_bytes','rootfs_bytes','complete_image_bytes','installed_rootfs_bytes')
    result['metrics'].update({k:origin['metrics'][k] for k in static if k in origin['metrics']})
    result['environment']={**read_json(ROOT/'config/profiles'/(profile+'.json'))['environment'],'memory_mib':memory,'measurement_protocol':'native-one-endpoint-v1','test_mode':'online','diagnostic_fixture_sha256':sha256(fixture),'qemu_version':subprocess.check_output(['qemu-system-x86_64','--version'],text=True).splitlines()[0],'peer_tor_sha256':peer.data['tor_sha256']}
    result['metadata']['native_measurement_git']=git_metadata();result['metadata']['harness']='host Python; one guest Tor plus test-only C observer/backend; no guest Python'
    result['build']['detail']='Preserved complete built image; hashes verified; no rebuild claimed'
    rows=result['tests'];phase=[];vm=NativeVM(profile,images,fixture,memory,run)
    def test(name,function,mode='online'):
        vm.phase=name;begin=time.monotonic()
        try:function();status='PASS';detail='executed native endpoint assertions'
        except (Blocked,TimeoutError,ConnectionError,OSError) as exc:status='BLOCKED';detail=type(exc).__name__+': '+str(exc)
        except Exception as exc:status='FAIL';detail=type(exc).__name__+': '+str(exc)
        rows.append({'id':name,'mode':mode,'status':status,'detail':detail,'seconds':time.monotonic()-begin})
        phase.append({'phase':name,'host_start':begin,'host_end':time.monotonic()})
        print(name,status,round(time.monotonic()-begin,2),flush=True);return status=='PASS'
    def security():
        text=tor_config()
        for name,value in [('CookieAuthentication','1'),('CookieAuthFileGroupReadable','0'),('ClientOnly','1'),('SafeLogging','1'),('User','tor'),('RunAsDaemon','1')]:
            if re.findall(r'^'+name+r'\s+(.+)$',text,re.M)!=[value]:raise AssertionError('unsafe production directive '+name)
        for name in ('SocksPort','ControlPort'):
            values=re.findall(r'^'+name+r'\s+(.+)$',text,re.M)
            if len(values)!=1 or not re.fullmatch(r'127\.0\.0\.1:[0-9]+',values[0]):raise AssertionError('nonloopback listener')
        if 'TestingTorNetwork' in text:raise AssertionError('private-network settings in public test')
    def verify():
        text,_=vm.agent.execute('/usr/bin/tor --defaults-torrc /etc/tor/defaults-torrc -f /run/capacity/torrc --verify-config')
        if 'Configuration was valid' not in text:raise AssertionError('configuration not verified')
    started=[None];address=[None]
    def startup():
        started[0]=time.monotonic();start_tor(vm)
        def ready():
            with contextlib.closing(vm.agent.control()) as c:return '9050' in c.command('GETCONF SocksPort')
        wait_until(ready,45,'production Tor startup')
        pid=vm.agent.sample()['tor']['pid'];text,_=vm.agent.execute(f'cat /proc/{int(pid)}/status; cat /proc/{int(pid)}/maps; cat /proc/mounts')
        for field in ('Uid','Gid'):
            values=re.search(r'^'+field+r':\s+(.+)$',text,re.M)[1].split()
            if any(int(x)!=905 for x in values):raise AssertionError('native privilege drop failed')
        if 'libpython' in text or '/mnt/validation/' in text or '/run/probe/' in text:raise AssertionError('Tor used diagnostic libraries')
        mounts=[x.split() for x in text.splitlines() if len(x.split())>=4]
        root=next(x for x in mounts if x[1]=='/' and x[2]=='squashfs');state=next(x for x in mounts if x[1]=='/var/lib/tor' and x[2]=='ext4')
        if 'ro' not in root[3].split(',') or 'rw' not in state[3].split(','):raise AssertionError('root/state mount protection failed')
        with contextlib.closing(vm.agent.control()) as c:
            answer=c.command('GETCONF CookieAuthentication SafeLogging ClientOnly')
            for name in ('CookieAuthentication','SafeLogging','ClientOnly'):
                if name+'=1' not in answer:raise AssertionError('effective security setting mismatch')
    def socks():
        with vm.agent.open('SOCKS') as s:socks_handshake(s)
    def bootstrap():
        wait_until(lambda:bootstrapped(vm),600,'public bootstrap timeout');result['metrics']['bootstrap_seconds']=time.monotonic()-started[0]
    def onion():
        address[0]=wait_until(lambda:identity(vm),30,'v3 identity missing')
        begin=time.monotonic();deadline=begin+180;last=None
        while time.monotonic()<deadline:
            try:
                with peer.connect(address[0],min(30,max(1,deadline-time.monotonic()))) as s:
                    if read_frame(s)!=SERVER_PAYLOAD:raise AssertionError('v3 server identity fixture differs')
                result['metrics']['onion_connection_seconds']=time.monotonic()-begin;return
            except (OSError,ConnectionError) as exc:last=type(exc).__name__+': '+str(exc);time.sleep(1)
        raise Blocked('v3 onion reachability deadline; '+str(last))
    def two_node():
        with peer.connect(address[0]) as s:exchange(s,[b'embedded Tor two-node test'])
        with vm.agent.socks(peer.hostname) as s:exchange(s,[b'embedded Tor reverse two-node test'])
    def integrity():
        with peer.connect(address[0]) as s:server_rate=exchange(s,payloads())
        with vm.agent.socks(peer.hostname) as s:client_rate=exchange(s,payloads())
        result['metrics'].update(server_role_throughput_bytes_per_second=server_rate,client_role_throughput_bytes_per_second=client_rate,throughput_bytes_per_second=min(server_rate,client_rate))
    def reconnect():
        for _ in range(3):
            with peer.connect(address[0]) as s:exchange(s,[b'repeated connection'])
        pid=int(vm.agent.sample()['tor']['pid']);vm.agent.execute('kill -KILL '+str(pid))
        wait_until(lambda:not vm.agent.sample().get('tor'),10,'old Tor process did not stop');start_tor(vm)
        wait_until(lambda:vm.agent.sample().get('tor'),45,'restarted Tor missing')
        with contextlib.closing(vm.agent.control()) as events:
            events.command('SETEVENTS HS_DESC');wait_until(lambda:bootstrapped(vm),600,'restarted public bootstrap')
            if identity(vm)!=address[0]:raise AssertionError('onion identity changed after crash')
            deadline=time.monotonic()+180
            while time.monotonic()<deadline:
                events.sock.settimeout(max(.1,deadline-time.monotonic()))
                line=events.file.readline(65536).decode('ascii')
                if not line:raise ConnectionError('control closed during descriptor publication')
                if ' HS_DESC UPLOADED ' in line and address[0].removesuffix('.onion') in line:break
            else:raise Blocked('restarted descriptor publication timed out')
        def recovered():
            with contextlib.closing(peer.control()) as c:c.command('SIGNAL NEWNYM')
            try:
                with peer.connect(address[0]) as s:exchange(s,[b'reconnected payload'])
                return True
            except (OSError,ConnectionError):return False
        wait_until(recovered,180,'same-identity reconnection timeout')
        with vm.agent.socks(peer.hostname) as s:exchange(s,[b'restarted client payload'])
    try:
        with vm:
            result['metrics']['linux_boot_seconds']=vm.boot_seconds;install_config(vm)
            clock,_=vm.agent.execute('date -u +%s');result['metadata']['guest_clock_offset_seconds']=int(clock.strip())-time.time()
            for name,function in [('security_config',security),('tor_config',verify),('tor_startup',startup),('socks5',socks),('public_bootstrap',bootstrap)]:
                if not test(name,function,'offline' if name in ('security_config','tor_config','tor_startup') else 'online'):raise Blocked('native prerequisite '+name)
            vm.phase='idle';time.sleep(10)
            idle=[x['tor'] for x in vm.samples if x['phase']=='idle' and x.get('tor')]
            if idle:
                result['metrics']['idle_rss_bytes']=sum(x['rss_bytes'] for x in idle)/len(idle)
                pss=[x['pss_bytes'] for x in idle if x.get('pss_bytes') is not None]
                result['metrics']['idle_pss_bytes']=sum(pss)/len(pss) if len(pss)==len(idle) else None
            if test('onion_v3',onion):
                for name,function in [('two_node',two_node),('bidirectional_integrity',integrity),('reconnection',reconnect)]:test(name,function)
            vm.phase='local_tcp';data=b'\x9d'*1024**2;rates=[]
            for _ in range(local_trials):
                with vm.agent.open('ECHO',180) as s:rates.append(exchange(s,[data]*16))
            result['local_tcp_trials_bytes_per_second']=rates
            vm.phase='crypto';text,_=vm.agent.execute('/run/probe/crypto-capacity',180);result['crypto_capacity']=json.loads(text)
            text,_=vm.agent.execute('/run/probe/crypto-probe',180);result['crypto_bulk']=json.loads(text)
            vm.phase='state';text,_=vm.agent.execute('du -sk /var/lib/tor');result['metrics']['persistent_storage_allocated_bytes']=1024*int(text.split()[0])
            text,_=vm.agent.execute('sha256sum /var/lib/tor/cached-microdesc-consensus 2>/dev/null || true');result['metadata']['public_consensus_sha256']=text.split()[0] if re.match(r'^[a-f0-9]{64}\s',text) else None
    except Exception as exc:result['error']=type(exc).__name__+': '+str(exc)
    required=read_json(ROOT/'config/policy.json')['required_tests']
    for name in required:
        if not any(x['id']==name for x in rows):rows.append({'id':name,'mode':'online','status':'BLOCKED','detail':'prerequisite or observer failure'})
    result['native_summary']=summarize_samples(vm.samples);result['phases']={name:summarize_samples([x for x in vm.samples if x['phase']==name]) for name in {x['phase'] for x in vm.samples}}
    result['metrics'].update(peak_process_rss_bytes=result['native_summary'].get('peak_process_hwm_bytes'),peak_system_used_bytes=result['native_summary'].get('peak_system_used_bytes'),cpu_percent=result['native_summary'].get('tor_cpu_percent'),persistent_storage_bytes=result['metrics'].get('persistent_storage_allocated_bytes'))
    result['observer_errors']=vm.errors;result['phase_intervals']=phase
    result['qualification']={'functional_pass':all(next(x['status'] for x in rows if x['id']==name)=='PASS' for name in required),'memory_margin_pass':bool(vm.samples) and result['native_summary'].get('min_available_fraction',0)>=.2 and result['native_summary'].get('min_available_bytes',0)>=16*1024**2,'performance':'UNASSESSED: controlled-local comparison uses the same image at 256 MiB; public variability is separate'}
    write_json(Path(run)/'results.json',result);return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture',type=Path,required=True)
    parser.add_argument('--peer-session',type=Path,required=True)
    parser.add_argument('--matrix',type=Path,required=True,help='image -> [profile, preserved image directory, source build run]')
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--memory-mib',nargs='+',type=int,choices=(96,112,128,160,192,256),default=[256,192,160,128,112,96])
    parser.add_argument('--rounds',type=int,choices=range(1,13),default=1)
    args=parser.parse_args()
    from capacity_peer import Peer
    state=read_json(args.peer_session)
    peer=Peer({**state['public'],'tor_sha256':state['provenance']['tor_sha256']})
    images=read_json(args.matrix)['images'];args.output.mkdir(parents=True,exist_ok=False)
    plan={'created_before_measurements':utcnow(),'protocol':'native-one-endpoint-v1','images':images,
          'ram_order':args.memory_mib,'repeats':args.rounds,'git':git_metadata(),'fixture_sha256':sha256(args.fixture),
          'local_tcp_trials':3,'small_buffer_crypto_trials':5,
          'margin':{'minimum_available_fraction':.2,'minimum_available_bytes':16*1024**2},
          'performance_limits':'Existing limits unchanged; intentional RAM scaling must be reported separately from equivalent-environment optimization comparisons.',
          'results':[]}
    write_json(args.output/'plan.json',plan)
    failed=False
    for repeat in range(args.rounds):
        for index,memory in enumerate(args.memory_mib):
            order=list(images) if (index+repeat)%2==0 else list(reversed(images))
            for name in order:
                profile,source,rid=images[name]
                run=args.output/(f'{repeat+1:02d}-{index+1:02d}-{name}-{memory}')
                print('START NATIVE',name,memory,str(run),flush=True)
                result=run_case(profile,source,args.fixture,memory,run,peer,read_json(ROOT/'reports/benchmarks'/rid/'results.json'))
                plan['results'].append({'image':name,'memory_mib':memory,'directory':str(run),'qualification':result['qualification']})
                failed=failed or not result['qualification']['functional_pass'] or not result['qualification']['memory_margin_pass']
                write_json(args.output/'plan.json',plan)
                print('DONE NATIVE',name,memory,result['qualification'],flush=True)
    # Performance still requires explicit matched analysis, even if assertions passed.
    return 1 if failed else 0


if __name__=='__main__':raise SystemExit(main())
