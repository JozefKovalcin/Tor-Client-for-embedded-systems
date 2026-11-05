# Optimization validation

Run: `20261105T103950Z-d842416e`

**ACCEPTED** — Restore upstream full Tor O2/module/link configuration; offset storage with XZ kernel compression

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `0c2496e8dd22758782760ec4984b4b2ceb264bc1`; dirty: `False`.

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
| bootstrap_seconds | 14.100062197000002 | 22.529744904 | -8.429682707 | 37.41579295690724 |
| complete_image_bytes | 27468800 | 275092480 | -247623680 | 90.01470341901022 |
| cpu_percent | 38.18305957110851 | 49.052049862876515 | -10.868990291768007 | 22.158075599596618 |
| idle_pss_bytes | 49827164.595744684 | 124722677.10638298 | -74895512.5106383 | 60.04963511708116 |
| idle_rss_bytes | 59678284.25531915 | 134523185.0212766 | -74844900.76595744 | 55.63717566910101 |
| image_host_allocated_bytes | 10969088 | 79142912 | -68173824 | 86.14015112307214 |
| installed_rootfs_bytes | 13177768 | 63362683 | -50184915 | 79.20263572172283 |
| kernel_bytes | 5207040 | 6657024 | -1449984 | 21.781264420858328 |
| library_bytes | 9003952 | 18515107 | -9511155 | 51.36970042895242 |
| onion_connection_seconds | 3.814559156999998 | 3.961948402999994 | -0.14738924599999592 | 3.7201202794158688 |
| peak_process_rss_bytes | 61288448 | 136028160 | -74739712 | 54.94429388738332 |
| peak_system_used_bytes | 95571968 | 171220992 | -75649024 | 44.18209655040429 |
| persistent_storage_bytes | 16648852 | 16651263 | -2411 | 0.0144793821345564 |
| production_peak_rss_bytes | 27881472 | None | None | None |
| production_pss_bytes | 26975232 | None | None | None |
| production_rss_bytes | 27439104 | None | None | None |
| production_state_bytes | 8320183 | None | None | None |
| production_state_free_bytes | 4866048 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 22261760 | 268435456 | -246173696 | 91.70684814453125 |
| startup_seconds | 0.4022768859999992 | 6.65769389 | -6.255417004000001 | 93.95771429797594 |
| throughput_bytes_per_second | 271386.4183737183 | 261955.94246885675 | 9430.475904861523 | -3.6000236589336727 |
| tor_executable_bytes | 3397272 | 3397272 | 0 | 0.0 |

## Decision

- all mandatory tests passed under equivalent conditions; measured thresholds satisfied

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
