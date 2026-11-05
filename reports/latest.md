# Optimization validation

Run: `20261105T093316Z-b835cc6d`

**NON_COMPARABLE** — Restore SMP/-O2 kernel execution; offset footprint with XZ and removal of unused debug, initrd and applets

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `bc282a6b21bbaf0fd577fd4740fbd5116323cebc`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| elf_hardening | offline | PASS | PIE, RELRO, NOW and non-executable stack observed |
| security_config | offline | PASS | explicit assertions passed |
| tor_config | offline | PASS | explicit assertions passed |
| tor_startup | offline | PASS | explicit assertions passed |
| socks5 | online | PASS | explicit assertions passed |
| public_bootstrap | online | PASS | explicit assertions passed |
| onion_v3 | online | PASS | explicit assertions passed |
| two_node | online | PASS | explicit assertions passed |
| bidirectional_integrity | online | PASS | explicit assertions passed |
| reconnection | online | PASS | explicit assertions passed |
| production_service | online | PASS | native daemon bootstrapped as uid 905; no fixture libraries or Python in deployed root |

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | 14.960927134999999 | 22.529744904 | -7.5688177690000025 | 33.594777931356916 |
| complete_image_bytes | 27141120 | 275092480 | -247951360 | 90.13381972491578 |
| cpu_percent | 36.19762894517375 | 49.052049862876515 | -12.854420917702768 | 26.20567530538867 |
| idle_pss_bytes | 48838568.85106383 | 124722677.10638298 | -75884108.25531915 | 60.84227023975225 |
| idle_rss_bytes | 57737128.85106383 | 134523185.0212766 | -76786056.17021276 | 57.080165146304 |
| image_host_allocated_bytes | 10641408 | 79142912 | -68501504 | 86.55418693717006 |
| installed_rootfs_bytes | 12209160 | 63362683 | -51153523 | 80.73130836331536 |
| kernel_bytes | 5207040 | 6657024 | -1449984 | 21.781264420858328 |
| library_bytes | 9003952 | 18515107 | -9511155 | 51.36970042895242 |
| onion_connection_seconds | 3.4593593990000002 | 3.961948402999994 | -0.5025890039999936 | 12.685400032454547 |
| peak_process_rss_bytes | 58830848 | 136028160 | -77197312 | 56.75097862089732 |
| peak_system_used_bytes | 97091584 | 171220992 | -74129408 | 43.29457920673652 |
| persistent_storage_bytes | 16605141 | 16651263 | -46122 | 0.2769879978473705 |
| production_peak_rss_bytes | 27308032 | None | None | None |
| production_pss_bytes | 26294272 | None | None | None |
| production_rss_bytes | 26755072 | None | None | None |
| production_state_bytes | 8298312 | None | None | None |
| production_state_free_bytes | 4887552 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 21934080 | 268435456 | -246501376 | 91.82891845703125 |
| startup_seconds | 0.39424389100000035 | 6.65769389 | -6.263449999 | 94.07837161765332 |
| throughput_bytes_per_second | 183892.1303753186 | 261955.94246885675 | -78063.81209353814 | 29.80035931149718 |
| tor_executable_bytes | 2428664 | 3397272 | -968608 | 28.511346751157987 |

## Decision

- test environment, architecture or validation policy differs; comparison refused
- no measured improvement in a policy-controlled metric

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
