# Optimization validation

Run: `20261104T165728Z-643dbbe1`

**REJECTED** — Iteration 2: -Os, upstream client modules, minimal kernel, 48 MiB rootfs

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `c9f138fde715c6c5564780a938e2db771f314187`; dirty: `False`.

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

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | 15.795952429 | 20.921977645000002 | -5.126025216000002 | 24.500672464990593 |
| complete_image_bytes | 54752256 | 275092480 | -220340224 | 80.09678199854827 |
| cpu_percent | 39.218179745203436 | 16.88222863518813 | 22.335951110015305 | -132.3045173281199 |
| idle_pss_bytes | 40844135.4893617 | 117296302.29787233 | -76452166.80851063 | 65.17866745224535 |
| idle_rss_bytes | 50226023.4893617 | 128241838.29787233 | -78015814.80851063 | 60.83491615840709 |
| image_host_allocated_bytes | 41590784 | 79142912 | -37552128 | 47.44850429562157 |
| installed_rootfs_bytes | 32604200 | 63362683 | -30758483 | 48.5435299512175 |
| kernel_bytes | 4420608 | 6657024 | -2236416 | 33.594831564374715 |
| library_bytes | 16350112 | 18515107 | -2164995 | 11.693127131266376 |
| onion_connection_seconds | 3.8131253959999967 | 2.2833709969999987 | 1.529754398999998 | -66.99543792970402 |
| peak_process_rss_bytes | 53694464 | 130019328 | -76324864 | 58.702706108433354 |
| peak_system_used_bytes | 84164608 | 160100352 | -75935744 | 47.43009184639394 |
| persistent_storage_bytes | 11504389 | 11499552 | 4837 | -0.04206250817423148 |
| rootfs_bytes | 50331648 | 268435456 | -218103808 | 81.25 |
| startup_seconds | 0.36945826800000026 | 6.326056145000001 | -5.956597877 | 94.15973776502105 |
| throughput_bytes_per_second | 190787.4639294566 | 243826.73790498156 | -53039.273975524964 | 21.75285386305508 |
| tor_executable_bytes | 2592728 | 3397272 | -804544 | 23.68206019417933 |

## Decision

- onion_connection_seconds exceeds regression limit 20%
- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
