#!/usr/bin/env python3
"""Offline upstream Tor cell-operation controls; never a functional qualification."""
import argparse
from pathlib import Path
import shlex
import shutil
import subprocess
import time
from common import ROOT,read_json,write_json,sha256,git_metadata,utcnow
from capacity_native import NativeVM


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--matrix',type=Path,required=True)
    p.add_argument('--base-fixture-directory',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args();args.output.mkdir(parents=True,exist_ok=False)
    images=read_json(args.matrix)['images'];fixture=ROOT/'build'/('cell-fixture-'+args.output.name)
    fixture.mkdir();manifest={'created':utcnow(),'git':git_metadata(),'purpose':'test-only unchanged upstream built-in benchmarks; no production installation','binaries':{}}
    for name in ('native-agent','crypto-capacity','crypto-probe'):shutil.copy2(args.base_fixture_directory/name,fixture/name)
    for name,(_,_,rid) in images.items():
        origin=read_json(ROOT/'reports/benchmarks'/rid/'results.json');out=ROOT/origin['build']['output_directory']
        source=next((out/'build').glob('tor-*/src/test/bench'));target=fixture/('tor-bench-'+name)
        shutil.copy2(source,target)
        manifest['binaries'][name]={'source_run':rid,'sha256':sha256(target),'source_path':str(source),'compiler_flags':origin['metadata']['compiler_flags']}
    write_json(fixture/'source-manifest.json',manifest)
    disk=Path(str(fixture)+'.ext4')
    with disk.open('xb') as f:f.truncate(64*1024**2)
    subprocess.run(['mke2fs','-q','-F','-t','ext4','-d',str(fixture),str(disk)],check=True)
    plan={'created_before_measurements':utcnow(),'git':git_metadata(),'images':images,'memory_mib':128,
          'fixture_sha256':sha256(disk),'fixture_manifest':manifest,'operations':['cell_ops_tor1','cell_ops_cgo','polyval'],
          'orders':[list(images),list(reversed(images))],'rows':[],
          'scope':'Offline cell-operation microbenchmarks only; no private/public Tor network throughput or functional validation claimed. All binary/library combinations and attempts retained.'}
    write_json(args.output/'plan.json',plan)
    for root,(profile,source,rid) in images.items():
        run=args.output/root
        with NativeVM(profile,source,disk,128,run) as vm:
            for repeat,order in enumerate(plan['orders'],1):
                for binary in order:
                    vm.phase='cell-'+str(repeat)+'-'+binary;begin=time.monotonic()
                    row={'root_image':root,'benchmark_binary':binary,'repeat':repeat,'started':utcnow()}
                    print('START CELL',root,binary,repeat,flush=True)
                    try:
                        text,code=vm.agent.execute(shlex.join(['/run/probe/tor-bench-'+binary,*plan['operations']]),timeout=240,check=False)
                        row.update(status='PASS' if code==0 else 'FAIL',returncode=code,output=text)
                    except (OSError,RuntimeError) as exc:row.update(status='BLOCKED',detail=type(exc).__name__+': '+str(exc))
                    row['seconds']=time.monotonic()-begin;plan['rows'].append(row)
                    write_json(args.output/'plan.json',plan);print('DONE CELL',root,binary,repeat,row['status'],round(row['seconds'],2),flush=True)
                    if row['status']=='BLOCKED':break
    print('CELL CONTROLS COMPLETE',args.output,flush=True)
    return 0 if len(plan['rows'])==len(images)**2*2 and all(row['status']=='PASS' for row in plan['rows']) else 1


if __name__=='__main__':raise SystemExit(main())
