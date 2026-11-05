# Optimization validation

Run: `20261105T083655Z-9a24cdbe`

**REJECTED** — Restore -O2 userland, retain small kernel, remove diagnostic exports and garbage-collect unreachable Tor sections

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `f7b48b660ad35299f270fd3f411fa0ce4ba4f737`; dirty: `False`.

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
| bootstrap_seconds | 14.861876812000002 | 22.553607893 | -7.691731080999997 | 34.10421568687151 |
| complete_image_bytes | 27431936 | 275092480 | -247660544 | 90.02810400342459 |
| cpu_percent | 13.67034243890223 | 20.26267122673946 | -6.59232878783723 | 32.53435203122538 |
| idle_pss_bytes | 49147102.60869565 | 123724451.40425532 | -74577348.79555967 | 60.276968658270164 |
| idle_rss_bytes | 58184926.60869565 | 133537443.40425532 | -75352516.79555967 | 56.42800616411867 |
| image_host_allocated_bytes | 10932224 | 79142912 | -68210688 | 86.18673015215816 |
| installed_rootfs_bytes | 12270600 | 63362683 | -51092083 | 80.63434277238544 |
| kernel_bytes | 4445184 | 6657024 | -2211840 | 33.22565759113982 |
| library_bytes | 9003952 | 18515107 | -9511155 | 51.36970042895242 |
| onion_connection_seconds | 4.078738665000003 | 3.0411382779999983 | 1.0376003870000048 | -34.11881644797756 |
| peak_process_rss_bytes | 59224064 | 134811648 | -75587584 | 56.06903047428068 |
| peak_system_used_bytes | 96440320 | 171053056 | -74612736 | 43.61964512344053 |
| persistent_storage_bytes | 16651600 | 16321542 | 330058 | -2.0222231453376156 |
| production_peak_rss_bytes | 27332608 | None | None | None |
| production_pss_bytes | 26396672 | None | None | None |
| production_rss_bytes | 26861568 | None | None | None |
| production_state_bytes | 8321409 | None | None | None |
| production_state_free_bytes | 4865024 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 22986752 | 268435456 | -245448704 | 91.436767578125 |
| startup_seconds | 0.3740472609999994 | 6.915354525000001 | -6.541307264000001 | 94.59106167807066 |
| throughput_bytes_per_second | 159745.50862134952 | 335069.59929016826 | -175324.09066881874 | 52.32467852656162 |
| tor_executable_bytes | 2428664 | 3397272 | -968608 | 28.511346751157987 |

## Decision

- onion_connection_seconds exceeds regression limit 20%
- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
