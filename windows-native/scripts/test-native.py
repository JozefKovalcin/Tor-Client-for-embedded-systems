"""Run the frozen functional assertions with native Windows resident-memory sampling.

All arguments pass through to tests/run_all.py. Only its Linux /proc observer
is replaced. No assertion, network operation, timeout or payload is changed.
Python is a build/test dependency, never a deployed Tor dependency.
"""
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import sys

if sys.platform != 'win32':
    raise SystemExit('This observer requires native Windows Python, not MSYS Python.')
if hasattr(ctypes.WinDLL('ntdll'), 'wine_get_version'):
    raise SystemExit('Wine detected: run this qualification observer on native Windows.')

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tests'))
import run_all


class ProcessMemoryCounters(ctypes.Structure):
    _fields_ = [('cb', wintypes.DWORD), ('PageFaultCount', wintypes.DWORD)] + [
        (name, ctypes.c_size_t) for name in (
            'PeakWorkingSetSize', 'WorkingSetSize', 'QuotaPeakPagedPoolUsage',
            'QuotaPagedPoolUsage', 'QuotaPeakNonPagedPoolUsage',
            'QuotaNonPagedPoolUsage', 'PagefileUsage', 'PeakPagefileUsage')]


kernel = ctypes.WinDLL('kernel32', use_last_error=True)
psapi = ctypes.WinDLL('psapi', use_last_error=True)
kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel.OpenProcess.restype = wintypes.HANDLE
kernel.CloseHandle.argtypes = [wintypes.HANDLE]
kernel.CloseHandle.restype = wintypes.BOOL
psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(ProcessMemoryCounters), wintypes.DWORD]
psapi.GetProcessMemoryInfo.restype = wintypes.BOOL


def windows_memory(pid):
    handle = kernel.OpenProcess(0x0400 | 0x0010, False, pid)
    if not handle:
        return {}
    try:
        counters = ProcessMemoryCounters()
        counters.cb = ctypes.sizeof(counters)
        if not psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
            return {}
        return {'VmRSS': counters.WorkingSetSize,
                'VmHWM': counters.PeakWorkingSetSize, 'Pss': None}
    finally:
        kernel.CloseHandle(handle)


run_all.proc_memory = windows_memory
if __name__ == '__main__':
    status = run_all.main()
    # Preserve the workload's output and add a precise observer definition.
    output = Path('reports/test_results.json')
    idle_seconds = 10.0
    for index, arg in enumerate(sys.argv):
        if arg == '--output':
            output = Path(sys.argv[index + 1])
        elif arg.startswith('--output='):
            output = Path(arg.split('=', 1)[1])
        elif arg == '--idle-seconds':
            idle_seconds = float(sys.argv[index + 1])
        elif arg.startswith('--idle-seconds='):
            idle_seconds = float(arg.split('=', 1)[1])
    result = json.loads(output.read_text(encoding='utf-8'))
    result['memory_observer'] = {
        'platform': 'native Windows', 'api': 'GetProcessMemoryInfo',
        'resident_memory': 'Windows WorkingSetSize and PeakWorkingSetSize; combined values sum two Tor processes',
        'idle_interval_seconds': idle_seconds, 'sampling_interval_seconds': 0.2,
        'pss': None, 'cross_platform_comparability': False,
        'assertions_and_timeouts': 'unchanged tests/run_all.py'}
    output.write_text(json.dumps(result, indent=2, allow_nan=False) + '\n', encoding='utf-8')
    raise SystemExit(status)
