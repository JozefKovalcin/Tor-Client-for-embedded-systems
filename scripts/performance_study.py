#!/usr/bin/env python3
"""Fixed-order repeated measurements of hash-verified existing images; no builds."""
import argparse
import copy
import json
from pathlib import Path
import subprocess
import time
import uuid
from common import ROOT,read_json,write_json,sha256,resolve_result,git_metadata,utcnow
from validate import emulation
from compare import compare

IMAGES={
 'baseline':('qemu-x86_64','build/baseline','20261104T162447Z-cd6efe9f'),
 'small':('qemu-x86_64-small','build/iterations/01-data','20261104T163515Z-f2a9da00'),
 'tiny':('qemu-x86_64-tiny','build/iterations/02-tiny','20261104T165728Z-643dbbe1'),
 'production':('qemu-x86_64-production','build/iterations/03-production','20261104T173131Z-b63cb205'),
 'compressed':('qemu-x86_64-compressed','build/iterations/04-compressed','20261104T173443Z-2cd3ee20'),
}
# Predetermined interleaving; never stop or drop samples when a gate passes.
ORDERS=[['baseline','compressed','small','production','tiny'],
        ['tiny','production','small','compressed','baseline'],
        ['compressed','baseline','tiny','small','production']]

def measured_metrics(previous, fresh):
    static={'tor_executable_bytes','library_bytes','kernel_bytes','rootfs_bytes','complete_image_bytes','installed_rootfs_bytes','image_host_allocated_bytes'}
    result={k:v if k in static else None for k,v in previous.items()}
    result.update({k:v for k,v in fresh.items() if not isinstance(v,dict)})
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rounds',type=int,default=3,choices=range(1,13))
    parser.add_argument('--series',type=Path)
    parser.add_argument('--fixture',type=Path,default=ROOT/'build/performance-probe-v2.ext4')
    parser.add_argument('--matrix',type=Path,help='explicit additional experiment image mapping and fixed orders')
    parser.add_argument('--mode',choices=('online','offline'),default='online')
    parser.add_argument('--memory-mib',type=int,choices=(96,112,128,160,192,256,512),help='explicit capacity experiment; optimization comparisons require the same RAM')
    args=parser.parse_args()
    matrix=read_json(args.matrix) if args.matrix else {'images':IMAGES,'orders':ORDERS}
    if len(matrix['orders']) < args.rounds:
        parser.error('matrix must declare every requested round before measurements')
    images=matrix['images']
    orders=matrix['orders'][:args.rounds]
    directory=args.series or ROOT/'reports/benchmarks'/('study-'+time.strftime('%Y%m%dT%H%M%SZ',time.gmtime()))
    directory.mkdir(parents=True,exist_ok=False)
    fixture=args.fixture.resolve()
    plan={'created':utcnow(),'git':git_metadata(),'orders':orders,'mode':args.mode,'memory_mib_override':args.memory_mib,'diagnostic_sha256':sha256(fixture),
          'aggregation':'all attempts retained; medians and ranges descriptive; existing 20% limits unchanged; no automatic acceptance from selected samples',
          'images':{},'results':[]}
    for name,(profile_name,source,rid) in images.items():
        original=read_json(ROOT/'reports/benchmarks'/rid/'results.json')
        artifacts=read_json(ROOT/'reports/benchmarks'/rid/'artifacts.json')
        plan['images'][name]={'source_run':rid,'profile':profile_name,'artifacts':artifacts}
        out=ROOT/'build/study-images'/directory.name/name/'images';out.mkdir(parents=True,exist_ok=True)
        for item in artifacts:
            src=ROOT/source/Path(item['path']).name
            if sha256(src)!=item['sha256']:raise ValueError('immutable artifact digest mismatch: '+str(src))
            link=out/src.name
            if not link.exists():link.symlink_to(src)
            if link.resolve()!=src.resolve():raise ValueError('unexpected image link')
    write_json(directory/'plan.json',plan)
    policy=read_json(ROOT/'config/policy.json')
    reference=resolve_result(ROOT/'reports/references/external-fixture/baseline')
    for round_index,order in enumerate(plan['orders'],1):
        for name in order:
            run=directory/(f'{round_index:02d}-'+name);run.mkdir()
            profile_name,source,rid=images[name]
            profile=read_json(ROOT/'config/profiles'/(profile_name+'.json'))
            profile['validation_fixture']='baseline'
            profile['diagnostic_fixture']={'path':str(fixture),'sha256':plan['diagnostic_sha256']}
            profile['environment']['measurement_protocol']='tor-two-node-v3-stage-profile'
            if args.memory_mib is not None:
                profile['environment']['memory_mib']=args.memory_mib
            if args.mode=='offline':
                # Bounded capacity diagnostic, never a full online qualification.
                profile['timeout_seconds']=180
            write_json(run/'measurement-profile.json',profile)
            before=sha256(ROOT/source/Path(profile['artifacts']['rootfs']).name)
            print('START',run,flush=True)
            try:
                target,environment=emulation(ROOT/'build/study-images'/directory.name/name,profile,args.mode,run)
                original=read_json(ROOT/'reports/benchmarks'/rid/'results.json')
                result=copy.deepcopy(original)
                result.update(run_id=run.name,timestamp=utcnow(),environment=environment,tests=target['tests'])
                result['metrics']=measured_metrics(original['metrics'],target['metrics'])
                result['metadata']['repeated_measurement_git']=git_metadata()
                result['metadata']['image_origin_run']=rid
                result['diagnostics']=target.get('diagnostics',{})
                result['build']['detail']='Previously built immutable image; hashes verified before/after measurement; no rebuild claimed'
                write_json(run/'results.json',result)
                # Deliberately report NON_COMPARABLE against the old apparatus.
                write_json(run/'frozen-reference-comparison.json',compare(reference,result,policy,sha256(ROOT/'config/policy.json')))
                plan['results'].append({'round':round_index,'image':name,'directory':str(run.relative_to(ROOT)),'functional_status':{t['id']:t['status'] for t in target['tests']}})
                print('DONE',name,json.dumps(result['metrics']),flush=True)
            except Exception as exc:
                write_json(run/'failure.json',{'error':type(exc).__name__+': '+str(exc)})
                plan['results'].append({'round':round_index,'image':name,'error':str(exc)})
                print('FAILED',name,type(exc).__name__,str(exc),flush=True)
            if sha256(ROOT/source/Path(profile['artifacts']['rootfs']).name)!=before:raise ValueError('original image changed')
            write_json(directory/'plan.json',plan)
    print('SERIES COMPLETE',directory,flush=True)

if __name__=='__main__':main()
