#!/usr/bin/env python3
"""Describe RAM scaling and public variability without replacing regression gates."""
import argparse
import csv
import json
from pathlib import Path
import random
import statistics as stats
from common import ROOT,read_json,write_json,sha256
from capacity_native import summarize_samples


def distribution(values):
    values=[v for v in values if isinstance(v,(int,float))]
    if not values:return {'n':0,'median':None}
    q=stats.quantiles(values,n=4,method='inclusive') if len(values)>1 else [values[0]]*3
    mean=stats.mean(values)
    return {'n':len(values),'median':stats.median(values),'min':min(values),'max':max(values),
            'q1':q[0],'q3':q[2],'cv':stats.stdev(values)/mean if len(values)>1 and mean else None}


def paired_ratio_ci(control,candidate,draws=20000):
    if len(control)!=len(candidate) or len(control)<3 or any(v is None or v<=0 for v in control+candidate):return None
    rng=random.Random(734291);ratios=[];n=len(control)
    for _ in range(draws):
        ids=[rng.randrange(n) for _ in range(n)]
        ratios.append(stats.median(candidate[i] for i in ids)/stats.median(control[i] for i in ids))
    ratios.sort()
    return {'n_pairs':n,'ratio_of_medians':stats.median(candidate)/stats.median(control),
            'percentile_95_low':ratios[int(draws*.025)],'percentile_95_high':ratios[int(draws*.975)],
            'draws':draws,'seed':734291,'assumption':'paired temporal rounds are exchangeable; public circuits are uncontrolled; this is not a reliability confidence bound'}


def cpu_phase(samples):
    result=summarize_samples(samples)
    if len(samples)<2:return result
    first,last=samples[0],samples[-1];dt=last['time']-first['time'];delta={}
    for key in first.get('system_cpu',{}):delta[key]=last['system_cpu'][key]-first['system_cpu'][key]
    total=sum(delta.values())
    result['system_busy_percent']=100*delta.get('busy',0)/total if total else None
    result['system_iowait_percent']=100*delta.get('iowait',0)/total if total else None
    result['vmstat_delta']={k:last['vmstat'][k]-v if v is not None and last['vmstat'].get(k) is not None else None for k,v in first['vmstat'].items()}
    result['elapsed_seconds']=dt
    return result


def native(series,output):
    plan=read_json(series/'plan.json');cases=[];references={}
    for item in plan['results']:
        path=Path(item['directory']);d=read_json(path/'results.json')
        samples=[json.loads(s) for s in (path/'samples.jsonl').read_text().splitlines()]
        row={'image':item['image'],'memory_mib':item['memory_mib'],'run':path.name,'qualification':d['qualification'],
             'tests':d['tests'],'summary':cpu_phase(samples),'metrics':d['metrics'],
             'phases':{phase:cpu_phase([s for s in samples if s['phase']==phase]) for phase in {s['phase'] for s in samples}},
             'local':{'tcp':distribution(d.get('local_tcp_trials_bytes_per_second',[]))}}
        for group in ('crypto_capacity','crypto_bulk'):
            trials=d.get(group,{}).get('trials',[])
            for key in sorted({k for t in trials for k in t if k.endswith('_bytes_per_second') or k.endswith('_ops_per_second')}):
                row['local'][key]=distribution([t.get(key) for t in trials])
        if item['memory_mib']==256:references[item['image']]=row
        cases.append(row)
    for row in cases:
        reference=references.get(row['image']);scaling={}
        if reference:
            for key,value in row['local'].items():
                a=reference['local'].get(key,{}).get('median');b=value.get('median')
                scaling[key]={'reference_256_median':a,'candidate_median':b,'ratio':b/a if a and b else None,
                              'within_unchanged_20_percent_limit':b>=a*.8 if a and b else None}
        row['local_scaling']=scaling
        row['capacity_screen']='PASS' if row['qualification']['functional_pass'] and row['qualification']['memory_margin_pass'] and scaling and all(x['within_unchanged_20_percent_limit'] for x in scaling.values()) else 'BLOCKED_OR_REJECTED'
    write_json(output/'native-analysis.json',{'plan':plan,'cases':cases,'note':'CPU counters recomputed from raw samples using per-PID differences. Original per-phase summaries accumulated earlier process CPU; they are not used. RAM scaling intentionally varies RAM and is not an equivalent-environment optimization acceptance.'})
    fields=['image','memory_mib','run','capacity_screen','functional_pass','memory_margin_pass','linux_memtotal_bytes','peak_system_used_bytes','peak_process_hwm_bytes','min_available_bytes','min_available_fraction','state_available_min_bytes','tor_cpu_seconds','tor_cpu_percent','tcp_bytes_per_second']
    with (output/'native-measurements.csv').open('w',newline='') as f:
        writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader()
        for row in cases:writer.writerow({**{k:row.get(k) for k in fields[:4]},**{k:row['summary'].get(k) for k in fields[6:-1]},'functional_pass':row['qualification']['functional_pass'],'memory_margin_pass':row['qualification']['memory_margin_pass'],'tcp_bytes_per_second':row['local']['tcp']['median']})
    return cases


def public(series,output):
    plan=read_json(series/'plan.json');keys=('bootstrap_seconds','onion_connection_seconds','throughput_bytes_per_second');groups={name:[] for name in plan['images']}
    for index,order in enumerate(plan['orders'],1):
        for name in order:
            path=series/(f'{index:02d}-'+name)/'results.json'
            groups[name].append(read_json(path) if path.exists() else None)
    summaries={};comparisons={}
    control=groups['baseline'];required=read_json(ROOT/'config/policy.json')['required_tests']
    for name,runs in groups.items():
        complete=all(d and all(next((t['status'] for t in d['tests'] if t['id']==k),'BLOCKED')=='PASS' for k in required) for d in runs)
        summaries[name]={'planned':len(runs),'completed':sum(d is not None for d in runs),'all_mandatory_assertions_pass':complete,
                         'metrics':{key:distribution([d['metrics'].get(key) for d in runs if d]) for key in keys},
                         'functional_attempts':[{'run':d['run_id'],'tests':{t['id']:t['status'] for t in d['tests']}} if d else {'run':None,'status':'BLOCKED'} for d in runs]}
        if name=='baseline':continue
        comparisons[name]={}
        for key in keys:
            a=[d['metrics'].get(key) if d else None for d in control];b=[d['metrics'].get(key) if d else None for d in runs]
            ci=paired_ratio_ci(a,b)
            gate=None if len([v for v in a+b if v is not None])!=len(a+b) else stats.median(b)>=.8*stats.median(a) if key=='throughput_bytes_per_second' else stats.median(b)<=1.2*stats.median(a)
            comparisons[name][key]={'median_runtime_gate':gate,'paired_bootstrap':ci}
    result={'series':str(series),'policy_sha256':sha256(ROOT/'config/policy.json'),'summaries':summaries,'runtime_comparisons':comparisons,
            'note':'Runtime gates shown separately; this does not waive artifact, RAM or mandatory-test gates. All planned attempts retained. CI overlap with the rejection boundary means attribution remains inconclusive.'}
    write_json(output/'public-variability.json',result);return result


def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--native',type=Path);p.add_argument('--public',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
    args.output.mkdir(parents=True,exist_ok=True)
    if args.native:native(args.native,args.output)
    if args.public:public(args.public,args.output)


if __name__=='__main__':main()
