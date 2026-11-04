from pathlib import Path
import copy
import json
import shutil
import subprocess
import sys
import time
import uuid

root=Path('/home/jozef/embedded-tor')
sys.path.insert(0,str(root/'scripts'))
from common import read_json,resolve_result,sha256,utcnow,write_json,git_metadata,seal_baseline,publish,validate_result
from validate import emulation

baseline=resolve_result('baseline')
reference=root/'reports/references/external-fixture/baseline'
if reference.exists():
    raise SystemExit('Reference already exists; refusing replacement')
profile=read_json(root/'config/profiles/qemu-x86_64.json')
profile['validation_fixture']='baseline'
profile['environment']['measurement_protocol']='tor-two-node-v2-external-fixture'
out=root/'build/fixture-reference'
(out/'images').mkdir(parents=True,exist_ok=True)
for item in baseline['metadata']['baseline_artifacts']:
    source=root/item['path']
    if sha256(source)!=item['sha256']:
        raise RuntimeError('Immutable baseline artifact mismatch')
    link=out/'images'/Path(item['path']).name
    if not link.exists():link.symlink_to(source)
run_id=time.strftime('%Y%m%dT%H%M%SZ',time.gmtime())+'-fixture-reference-'+uuid.uuid4().hex[:6]
run=root/'reports/benchmarks'/run_id
run.mkdir()
write_json(run/'measurement-profile.json',profile)
shutil.copyfile(__file__,run/'measurement-driver.py')
commit=git_metadata()
target,environment=emulation(out,profile,'online',run)
r=copy.deepcopy(baseline)
r.update(run_id=run_id,timestamp=utcnow(),change='Reference: immutable original image under external validation fixture',environment=environment)
r['metadata']['validation_git']=commit
r['metadata']['configuration_id']='baseline-external-fixture-reference'
r['metadata']['image_origin_run']=baseline['run_id']
r['build'].update(status='PASS',detail='Previously validated immutable baseline image; artifact hashes rechecked; no image rebuild claimed')
r['tests']=[t for t in baseline['tests'] if t['id'] not in {x['id'] for x in target['tests']}]+target['tests']
r['metrics'].update({k:v for k,v in target['metrics'].items() if not isinstance(v,dict)})
policy=read_json(root/'config/policy.json')
problems=validate_result(r,policy)
if problems:
    publish(r,{'status':'INCOMPLETE','reasons':problems,'differences':[]})
    raise SystemExit('Reference did not pass all mandatory checks')
seal_baseline(r,directory=reference,policy=policy)
publish(r,{'status':'REFERENCE','reasons':['Original baseline remeasured with the exact same external test disk used for production candidates; original baseline remains immutable'],'differences':[]})
print('Reference sealed:',reference,flush=True)
print(json.dumps(r['metrics'],indent=2),flush=True)
