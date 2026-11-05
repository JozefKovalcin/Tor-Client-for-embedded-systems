import sys
from pathlib import Path
import unittest
import tempfile
import json

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from analyze_capacity import paired_ratio_ci,distribution,cpu_phase,native


class CapacityAnalysisTests(unittest.TestCase):
    def test_missing_pair_is_not_dropped(self):
        self.assertIsNone(paired_ratio_ci([1,2,3],[1,None,3]))
        self.assertIsNone(paired_ratio_ci([1,2,3],[1,2]))

    def test_paired_constant_ratio_is_retained(self):
        d=paired_ratio_ci([10,20,30],[5,10,15],draws=100)
        self.assertEqual(d['ratio_of_medians'],.5)
        self.assertEqual(d['percentile_95_low'],.5)
        self.assertEqual(d['percentile_95_high'],.5)

    def test_empty_metrics_remain_unavailable(self):
        self.assertEqual(distribution([None])['median'],None)
        self.assertEqual(distribution([None])['n'],0)

    def test_phase_cpu_uses_differences_not_lifetime_counters(self):
        def sample(t,ticks,busy,idle):
            return {'time':t,'hz':100,'memory':{'MemTotal':100,'MemAvailable':80},'vmstat':{'oom_kill':0},
                    'tor':{'pid':42,'cpu_ticks':ticks},'system_cpu':{'busy':busy,'idle':idle,'iowait':0}}
        d=cpu_phase([sample(10,1000,1000,1000),sample(12,1050,1050,1150)])
        self.assertEqual(d['tor_cpu_seconds'],.5)
        self.assertEqual(d['tor_cpu_percent'],25)
        self.assertEqual(d['system_busy_percent'],25)

    def test_missing_crypto_blocks_an_otherwise_complete_capacity_group(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);plan={'images':{'unit':[]},'ram_order':[128,256],'repeats':1,'results':[]}
            for memory in (128,256):
                run=root/str(memory);run.mkdir()
                d={'qualification':{'functional_pass':True,'memory_margin_pass':True},'tests':[],
                   'metrics':{'bootstrap_seconds':1,'onion_connection_seconds':1,'throughput_bytes_per_second':100},
                   'local_tcp_trials_bytes_per_second':[100,100,100]}
                (run/'results.json').write_text(json.dumps(d));(run/'guest-notice.log').write_text('')
                rows=[{'time':t,'hz':100,'memory':{'MemTotal':64*2**20,'MemAvailable':48*2**20},'tor':None,'vmstat':{},'phase':'local_tcp','state_available_bytes':8*2**20} for t in (1,2)]
                (run/'samples.jsonl').write_text('\n'.join(json.dumps(x) for x in rows))
                plan['results'].append({'directory':str(run),'image':'unit','memory_mib':memory})
            (root/'plan.json').write_text(json.dumps(plan));native(root,root)
            result=json.loads((root/'native-analysis.json').read_text())
            self.assertTrue(result['complete'])
            self.assertTrue(result['groups']['unit-128']['all_functional_assertions_pass'])
            self.assertIsNone(result['groups']['unit-128']['local_median_gates']['aes_cell_bytes_per_second']['pass'])
            self.assertNotEqual(result['groups']['unit-128']['capacity_screen'],'PASS')


if __name__=='__main__':unittest.main()
