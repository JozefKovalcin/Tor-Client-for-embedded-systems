#!/usr/bin/env python3
"""Measurement-only wrapper around the unchanged immutable functional suite."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

sys.path.insert(0, '/mnt/validation/opt/tor-validation/tests')
import run_all as suite

nodes=[]
samplers=[]
stages=[]
starts={}
original_init=suite.Node.__init__
def node_init(self,*a,**kw):
    original_init(self,*a,**kw)
    nodes.append(self)
suite.Node.__init__=node_init
original_sampler_init=suite.Sampler.__init__
def sampler_init(self,*a,**kw):
    original_sampler_init(self,*a,**kw)
    samplers.append(self)
suite.Sampler.__init__=sampler_init

def counters():
    processes={}
    for node in nodes:
        if not node.process or node.process.poll() is not None:
            continue
        pid=node.process.pid
        try:
            fields=Path(f'/proc/{pid}/stat').read_text().rsplit(')',1)[1].split()
            try:
                io={k:int(v) for k,v in (line.split(':') for line in Path(f'/proc/{pid}/io').read_text().splitlines())}
            except OSError:
                io={'io_accounting_available':False}
            processes[str(pid)]={'cpu_ticks':int(fields[11])+int(fields[12]),'minor_faults':int(fields[7]),'major_faults':int(fields[9]),**io}
        except OSError:
            pass
    vm={k:int(v) for k,v in (line.split() for line in Path('/proc/vmstat').read_text().splitlines())}
    mem={line.split(':')[0]:line.split(':')[1].strip() for line in Path('/proc/meminfo').read_text().splitlines()}
    return {'time':time.monotonic(),'processes':processes,'vmstat':{k:v for k,v in vm.items() if k in ('pgmajfault','pswpin','pswpout','pgscan_direct','pgscan_kswapd','allocstall_normal','allocstall_dma32','oom_kill')},
            'memory':{k:mem.get(k) for k in ('MemAvailable','SwapTotal','SwapFree','Dirty','Writeback')},
            'pressure':{name:(Path('/proc/pressure')/name).read_text() if (Path('/proc/pressure')/name).exists() else None for name in ('cpu','memory','io')}}

names={'startup','bootstrap','onion','integrity','reconnect'}
def profile(frame,event,arg):
    name=frame.f_code.co_name
    if name not in names or Path(frame.f_code.co_filename).name!='run_all.py' or name=='bootstrap' and 'self' in frame.f_locals:
        return
    identity=id(frame)
    if event=='call':starts[identity]=counters()
    elif event=='return' and identity in starts:
        a=starts.pop(identity);b=counters();cpu=0
        for pid,now in b['processes'].items():
            cpu+=now['cpu_ticks']-a['processes'].get(pid,{}).get('cpu_ticks',0)
        # Reconnect replaces a PID; recover its final sampled ticks too.
        old=set(a['processes'])-set(b['processes'])
        for sampler in samplers:
            for pid in old:
                values=[p['cpu_ticks'] for sample in sampler.samples for p in sample['processes'].values() if str(p['pid'])==pid and 'cpu_ticks' in p]
                if values:cpu+=max(values)-a['processes'][pid]['cpu_ticks']
        elapsed=b['time']-a['time'];seconds=cpu/os.sysconf('SC_CLK_TCK') if b['processes'] else None
        stages.append({'stage':name,'elapsed_seconds':elapsed,'tor_cpu_seconds':seconds,'tor_cpu_percent':100*seconds/elapsed if seconds is not None and elapsed else None,'before':a,'after':b})

parser=argparse.ArgumentParser()
parser.add_argument('--mode',default='online')
parser.add_argument('--tor',required=True)
parser.add_argument('--template',type=Path,required=True)
parser.add_argument('--work-dir',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--bootstrap-timeout',type=float,default=600)
parser.add_argument('--connection-timeout',type=float,default=180)
parser.add_argument('--idle-seconds',type=float,default=10)
args=parser.parse_args()
clean={k:v for k,v in os.environ.items() if k not in ('LD_LIBRARY_PATH','PYTHONHOME','PYTHONPATH')}
crypto=subprocess.run(['/run/probe/crypto-probe'],env=clean,capture_output=True,text=True,timeout=120)
if crypto.returncode:raise RuntimeError('candidate-library crypto probe failed: '+str(crypto.returncode))
io=None
upstream=[]
if args.mode=='offline' and Path('/run/probe/io-probe').is_file():
    probe=subprocess.run(['/run/probe/io-probe'],env=clean,capture_output=True,text=True,timeout=60)
    if probe.returncode:raise RuntimeError('I/O probe failed: '+str(probe.returncode)+' '+probe.stderr)
    io=json.loads(probe.stdout)
    for name in ('tor-bench-baseline','tor-bench-os','tor-bench-o2'):
        binary=Path('/run/probe')/name
        if not binary.is_file():continue
        print('UPSTREAM_BENCH_START',name,flush=True)
        try:
            p=subprocess.run([str(binary),'cell_aes','polyval'],env=clean,capture_output=True,text=True,timeout=240)
            upstream.append({'name':name,'returncode':p.returncode,'output':p.stdout,'stderr':p.stderr})
        except subprocess.TimeoutExpired:
            upstream.append({'name':name,'status':'BLOCKED','detail':'240-second diagnostic deadline exceeded'})
        print('UPSTREAM_BENCH_END',name,flush=True)
sys.setprofile(profile)
try:result=suite.run(args)
finally:sys.setprofile(None)
result['diagnostics']={'crypto':json.loads(crypto.stdout),'stages':stages,'guest_cpuinfo':Path('/proc/cpuinfo').read_text(),
                       'functional_driver_sha256':__import__('hashlib').sha256(Path(suite.__file__).read_bytes()).hexdigest()}
result['diagnostics']['io_probe']=io
result['diagnostics']['upstream_benchmarks']=upstream
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(json.dumps(result,separators=(',',':'),allow_nan=False)+'\n')
print('PROFILE',json.dumps([{k:v for k,v in row.items() if k not in ('before','after')} for row in stages]),flush=True)
