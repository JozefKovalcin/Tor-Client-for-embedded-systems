# Optimization validation

Run: `20261105T105238Z-64e9527c`

**INCOMPLETE** — Original upstream full Tor O2/module/link settings with Buildroot-selected XZ kernel compression

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `0f33bfbf1a5079645273146bb0f53beeb537540d`; dirty: `False`.

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
| reconnection | online | BLOCKED | reconnection after service crash timed out |
| production_service | online | PASS | native daemon bootstrapped as uid 905; no fixture libraries or Python in deployed root |

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | 15.935308701 | 22.529744904 | -6.594436203000001 | 29.269910649672752 |
| complete_image_bytes | 26203136 | 275092480 | -248889344 | 90.47479015057046 |
| cpu_percent | 9.730613681346089 | 49.052049862876515 | -39.32143618153043 | 80.16267677182154 |
| idle_pss_bytes | 49977032.347826086 | 124722677.10638298 | -74745644.7585569 | 59.92947432871581 |
| idle_rss_bytes | 59737399.652173914 | 134523185.0212766 | -74785785.36910269 | 55.59323127628471 |
| image_host_allocated_bytes | 9703424 | 79142912 | -69439488 | 87.73936445502535 |
| installed_rootfs_bytes | 13177768 | 63362683 | -50184915 | 79.20263572172283 |
| kernel_bytes | 3941376 | 6657024 | -2715648 | 40.793724042455004 |
| library_bytes | 9003952 | 18515107 | -9511155 | 51.36970042895242 |
| onion_connection_seconds | 2.6624040999999963 | 3.961948402999994 | -1.2995443029999976 | 32.80063672752478 |
| peak_process_rss_bytes | 60760064 | 136028160 | -75268096 | 55.332731105088826 |
| peak_system_used_bytes | 96169984 | 171220992 | -75051008 | 43.8328309650256 |
| persistent_storage_bytes | 16649802 | 16651263 | -1461 | 0.00877410920721149 |
| production_peak_rss_bytes | 27856896 | None | None | None |
| production_pss_bytes | 26902528 | None | None | None |
| production_rss_bytes | 27369472 | None | None | None |
| production_state_bytes | 8320183 | None | None | None |
| production_state_free_bytes | 4866048 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 22261760 | 268435456 | -246173696 | 91.70684814453125 |
| startup_seconds | 0.4630577450000004 | 6.65769389 | -6.194636145 | 93.04477266977499 |
| throughput_bytes_per_second | 161076.04782617363 | 261955.94246885675 | -100879.89464268312 | 38.51025240806532 |
| tor_executable_bytes | 3397272 | 3397272 | 0 | 0.0 |

## Decision

- mandatory test not passed: reconnection
- functional regressions: reconnection
- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
