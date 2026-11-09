"""Launcher discovery and compatibility gates without booting or updating a host."""
from pathlib import Path
import shutil
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]


class LauncherTests(unittest.TestCase):
    @unittest.skipUnless(sys.platform == 'linux' and shutil.which('bash'), 'Linux shell required')
    def test_compatibility_and_upgrade_opt_in(self):
        harness = r'''
python3() { if [[ "$1" == -c ]]; then return "$PYTHON_OLD"; fi; printf 'UNEXPECTED_TEST_RUN\n'; return 99; }
qemu-system-arm() { if [[ "$QEMU_OLD" == 1 ]]; then printf 'virt-6.2  QEMU ARM\n'; else printf 'virt-8.2  QEMU ARM\n'; fi; }
curl() { return 0; }
do-release-upgrade() { return 0; }
sudo() { printf 'MOCK_UPGRADE:%s\n' "$*"; return 0; }
export -f python3 qemu-system-arm curl do-release-upgrade sudo
export PYTHON_OLD="$1" QEMU_OLD="$2"
bash "$3" --check
'''
        cases = [(0, 0, 'n\n', 0, False), (1, 0, 'n\n', 1, False),
                 (0, 1, 'n\n', 1, False), (1, 1, '', 1, False),
                 (1, 1, 'a\n', 2, True)]
        for python_old, qemu_old, answer, code, upgrade in cases:
            with self.subTest(python_old=python_old, qemu_old=qemu_old, answer=answer):
                result = subprocess.run(['bash', '-c', harness, 'launcher-test', str(python_old),
                                         str(qemu_old), str(ROOT / 'scripts/two_pc_demo/run-wsl.sh')],
                                        input=answer, capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, code, result.stdout + result.stderr)
                self.assertEqual('MOCK_UPGRADE:do-release-upgrade' in result.stdout, upgrade)
                self.assertNotIn('UNEXPECTED_TEST_RUN', result.stdout)

    @unittest.skipUnless(sys.platform == 'win32' and shutil.which('powershell'), 'Windows PowerShell required')
    def test_windows_discovery_and_arguments(self):
        result = subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                                 str(ROOT / 'tests/test_two_pc_launcher.ps1')],
                                capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
