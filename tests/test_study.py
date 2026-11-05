import sys
from pathlib import Path
import unittest

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from performance_study import measured_metrics

class RuntimeProvenanceTests(unittest.TestCase):
    def test_blocked_run_cannot_inherit_successful_historical_measurements(self):
        old={'complete_image_bytes':1234,'onion_connection_seconds':2.0,
             'throughput_bytes_per_second':900000,'production_state_bytes':500}
        actual=measured_metrics(old,{'bootstrap_seconds':18.0})
        self.assertEqual(actual['complete_image_bytes'],1234)
        self.assertEqual(actual['bootstrap_seconds'],18.0)
        for name in ('onion_connection_seconds','throughput_bytes_per_second','production_state_bytes'):
            self.assertIsNone(actual[name])
