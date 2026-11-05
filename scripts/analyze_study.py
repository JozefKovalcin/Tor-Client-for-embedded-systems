#!/usr/bin/env python3
"""Summarize every planned repeat; unchanged gates operate on documented medians."""
import argparse
import copy
import csv
import json
from pathlib import Path
import statistics
from common import ROOT,read_json,write_json,sha256,utcnow,seal_baseline,resolve_result
from compare import compare

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('series',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--seal-reference',action='store_true')
    parser.add_argument('--reference',type=Path,help='previously frozen matched repeated reference')
    args=parser.parse_args()
    plan=read_json(args.series/'plan.json')
    expected=len(plan['orders'])
    groups={name:[] for name in plan['images']}
    for round_index,order in enumerate(plan['orders'],1):
        for name in order:
            p=args.series/(f'{round_index:02d}-'+name)/'results.json'
            if p.is_file():groups[name].append(read_json(p))
    output=args.output;output.mkdir(parents=True,exist_ok=True)
    policy=read_json(ROOT/'config/policy.json')
    aggregates={};summary={};raw=[]
    metrics=('complete_image_bytes','idle_rss_bytes','peak_system_used_bytes','cpu_percent','bootstrap_seconds','onion_connection_seconds','throughput_bytes_per_second')
    for name,runs in groups.items():
        if not runs:continue
        summary[name]={'completed':len(runs),'planned':expected,'metrics':{},'functional_attempts':[],'crypto':{},'phases':{}}
        for d in runs:
            row={'image':name,'run_id':d['run_id'],**d['metrics']}
            summary[name]['functional_attempts'].append({'run':d['run_id'],'tests':{t['id']:t['status'] for t in d['tests']}})
            raw.append(row)
            write_json(output/(d['run_id']+'.json'),d)
        for key in sorted(set().union(*(d['metrics'] for d in runs))):
            values=[d['metrics'][key] for d in runs if isinstance(d['metrics'].get(key),(int,float))]
            if values:summary[name]['metrics'][key]={'n':len(values),'median':statistics.median(values),'min':min(values),'max':max(values),'stdev':statistics.stdev(values) if len(values)>1 else None}
        for key in ('aes128ctr_bytes_per_second','sha256_bytes_per_second','x25519_ops_per_second'):
            values=[statistics.median(t[key] for t in d['diagnostics']['crypto']['trials']) for d in runs]
            summary[name]['crypto'][key]={'median':statistics.median(values),'min':min(values),'max':max(values)}
        for phase in ('startup','bootstrap','onion','integrity','reconnect'):
            values=[stage['tor_cpu_seconds'] for d in runs for stage in d['diagnostics']['stages'] if stage['stage']==phase and stage['tor_cpu_seconds'] is not None]
            if values:summary[name]['phases'][phase]={'median_tor_cpu_seconds':statistics.median(values),'min':min(values),'max':max(values)}
        a=copy.deepcopy(runs[0]);a['run_id']='median-'+name;a['timestamp']=utcnow()
        a['metadata']['aggregation']={'method':'component-wise median','expected_repeats':expected,'source_runs':[d['run_id'] for d in runs],'series':str(args.series)}
        a['build']['detail']='Aggregate of repeated measurements of the same hash-verified previously built image; no rebuild claimed'
        a['metrics']={k:v['median'] if v['n']==expected else None for k,v in summary[name]['metrics'].items()}
        a['tests']=[]
        for test in sorted(set().union(*({t['id'] for t in d['tests']} for d in runs))):
            rows=[next((t for t in d['tests'] if t['id']==test),{}) for d in runs]
            statuses=[t.get('status','BLOCKED') for t in rows]
            status='PASS' if len(runs)==expected and all(s=='PASS' for s in statuses) else 'FAIL' if 'FAIL' in statuses else 'BLOCKED'
            a['tests'].append({'id':test,'status':status,'mode':rows[0].get('mode','online'),'detail':'All planned repeats required; statuses: '+', '.join(statuses)})
        if len(runs)!=expected:a['build']['status']='BLOCKED'
        # Comparisons must reject any change in the apparatus within a group.
        if any(d['environment']!=a['environment'] for d in runs):a['build']['status']='BLOCKED'
        aggregates[name]=a;write_json(output/('median-'+name+'.json'),a)
    comparisons={}
    reference=resolve_result(args.reference) if args.reference else aggregates.get('baseline')
    if reference:
        for name,a in aggregates.items():
            if name!='baseline':comparisons[name]=compare(reference,a,policy,sha256(ROOT/'config/policy.json'))
    write_json(output/'summary.json',summary);write_json(output/'comparisons.json',comparisons)
    if raw:
        keys=list(dict.fromkeys(k for row in raw for k in row))
        with (output/'measurements.csv').open('w',newline='') as f:
            writer=csv.DictWriter(f,fieldnames=keys);writer.writeheader();writer.writerows(raw)
    def val(name,key,scale=1):
        x=summary[name]['metrics'].get(key)
        return f"{x['median']/scale:.2f} [{x['min']/scale:.2f}, {x['max']/scale:.2f}]" if x else 'unavailable'
    lines=['# Repeated embedded Tor performance study','',
        'Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. '
        'Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. '
        'Three repeats are descriptive evidence, not a tight statistical confidence bound.','',
        '| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |',
        '|---|---:|---:|---:|---:|---:|---:|---:|---|']
    for name in plan['images']:
        if name not in summary:continue
        lines.append('| '+ ' | '.join([name,f"{summary[name]['completed']}/{expected}",val(name,'complete_image_bytes',1048576),val(name,'idle_rss_bytes',1048576),val(name,'cpu_percent'),val(name,'bootstrap_seconds'),val(name,'onion_connection_seconds'),val(name,'throughput_bytes_per_second',1024),comparisons.get(name,{}).get('status','REFERENCE')])+' |')
    lines+=['','CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. '
        'All images use identical QEMU CPU/machine/512 MiB RAM/NAT/RNG, baseline functional suite, and read-only diagnostic disk. '
        'Public relays/circuits are not held identical. No host builds run during the series.','',
        '| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |',
        '|---|---:|---:|---:|---:|---:|---:|']
    for name,s in summary.items():
        cpu=[s['phases'].get(k,{}).get('median_tor_cpu_seconds') for k in ('startup','bootstrap','integrity')]
        crypto=[s['crypto'][k]['median']/scale for k,scale in (('aes128ctr_bytes_per_second',1048576),('sha256_bytes_per_second',1048576),('x25519_ops_per_second',1))]
        lines.append('| '+name+' | '+' | '.join(f'{x:.2f}' if x is not None else 'unavailable' for x in cpu+crypto)+' |')
    for name,c in comparisons.items():lines+=['',f'**{name}: {c["status"]}.** '+'; '.join(c['reasons'])+'.']
    (output/'README.md').write_text('\n'.join(lines)+'\n')
    if args.seal_reference:
        seal_baseline(aggregates['baseline'],directory=ROOT/'reports/references'/args.series.name/'baseline',policy=policy)
    print(json.dumps({name:c['status'] for name,c in comparisons.items()}))

if __name__=='__main__':main()
