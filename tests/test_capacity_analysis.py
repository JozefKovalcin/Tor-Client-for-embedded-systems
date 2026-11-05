import sys
from pathlib import Path
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from analyze_capacity import paired_ratio_ci,distribution,cpu_phase


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


if __name__=='__main__':unittest.main()
