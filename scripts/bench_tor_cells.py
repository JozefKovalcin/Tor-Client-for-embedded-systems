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
    p.add_argument('--control-image',default='ram')
    p.add_argument('--openssl-images',nargs='+',default=['ram','slim'])
    args=p.parse_args();args.output.mkdir(parents=True,exist_ok=False)
    images=read_json(args.matrix)['images'];fixture=ROOT/'build'/('cell-fixture-'+args.output.name)
    fixture.mkdir();manifest={'created':utcnow(),'git':git_metadata(),'purpose':'test-only unchanged upstream built-in benchmarks; no production installation','binaries':{}}
    for name in ('native-agent','crypto-capacity','crypto-probe'):shutil.copy2(args.base_fixture_directory/name,fixture/name)
    for name,(_,_,rid) in images.items():
        origin=read_json(ROOT/'reports/benchmarks'/rid/'results.json');out=ROOT/origin['build']['output_directory']
        source=next((out/'build').glob('tor-*/src/test/bench'));target=fixture/('tor-bench-'+name)
        shutil.copy2(source,target)
        manifest['binaries'][name]={'source_run':rid,'sha256':sha256(target),'source_path':str(source),'compiler_flags':origin['metadata']['compiler_flags']}
    manifest['openssl_builds']={}
    for name in args.openssl_images:
        origin=read_json(ROOT/'reports/benchmarks'/images[name][2]/'results.json');out=ROOT/origin['build']['output_directory']
        libraries=fixture/('openssl-'+name);libraries.mkdir()
        manifest['openssl_builds'][name]={}
        for library in ('libcrypto.so.3','libssl.so.3'):
            shutil.copy2(out/'target/usr/lib'/library,libraries/library)
            manifest['openssl_builds'][name][library]=sha256(libraries/library)
    write_json(fixture/'source-manifest.json',manifest)
    disk=Path(str(fixture)+'.ext4')
    with disk.open('xb') as f:f.truncate(64*1024**2)
    subprocess.run(['mke2fs','-q','-F','-t','ext4','-d',str(fixture),str(disk)],check=True)
    plan={'created_before_measurements':utcnow(),'git':git_metadata(),'images':images,'control_image':args.control_image,'memory_mib':128,
          'fixture_sha256':sha256(disk),'fixture_manifest':manifest,'operations':['cell_ops_tor1','cell_ops_cgo','polyval'],
          'orders':[list(images),list(reversed(images))],'library_orders':[args.openssl_images,list(reversed(args.openssl_images))],'rows':[],
          'scope':'Offline cell-operation controls in one fixed 128 MiB VM. Only the benchmark process selects the pinned O2/Os OpenSSL 3.6.5 build through LD_LIBRARY_PATH; production images and Tor services are not modified. Same version/configure options/API/ABI, different optimization flags. No private/public Tor network throughput or functional qualification claimed; every attempt retained.'}
    write_json(args.output/'plan.json',plan)
    profile,source,rid=images[args.control_image]
    with NativeVM(profile,source,disk,128,args.output/'control-vm') as vm:
        plan['verified_image_hashes']=vm.image_hashes
        blocked=False
        for repeat,order in enumerate(plan['orders'],1):
            for library in plan['library_orders'][repeat-1]:
                for binary in order:
                    vm.phase='cell-'+str(repeat)+'-'+library+'-'+binary;begin=time.monotonic()
                    row={'root_image':args.control_image,'openssl_build':library,'benchmark_binary':binary,'repeat':repeat,'started':utcnow()}
                    print('START CELL',library,binary,repeat,flush=True)
                    try:
                        command='LD_LIBRARY_PATH='+shlex.quote('/run/probe/openssl-'+library)+' '+shlex.join(['/run/probe/tor-bench-'+binary,*plan['operations']])
                        text,code=vm.agent.execute(command,timeout=240,check=False)
                        row.update(status='PASS' if code==0 else 'FAIL',returncode=code,output=text)
                    except (OSError,RuntimeError) as exc:row.update(status='BLOCKED',detail=type(exc).__name__+': '+str(exc))
                    row['seconds']=time.monotonic()-begin;plan['rows'].append(row)
                    write_json(args.output/'plan.json',plan);print('DONE CELL',library,binary,repeat,row['status'],round(row['seconds'],2),flush=True)
                    if row['status']=='BLOCKED':blocked=True;break
                if blocked:break
            if blocked:break
    print('CELL CONTROLS COMPLETE',args.output,flush=True)
    return 0 if len(plan['rows'])==len(images)*len(args.openssl_images)*2 and all(row['status']=='PASS' for row in plan['rows']) else 1


if __name__=='__main__':raise SystemExit(main())
