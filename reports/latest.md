# Optimization validation

Run: `20261104T173131Z-b63cb205`

**REJECTED** — Iteration 3 diagnostic repeat: unchanged image after public HSDir timeout; original incomplete result retained

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `31cbdc0b645d8d6821045d969979d92b0ec012de`; dirty: `True`.

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
| bootstrap_seconds | 18.422064179 | 21.527869747 | -3.105805568000001 | 14.426906166286182 |
| complete_image_bytes | 37975040 | 275092480 | -237117440 | 86.1955368609131 |
| cpu_percent | 63.649398417817196 | 54.37785271538515 | 9.271545702432043 | -17.050224014838378 |
| idle_pss_bytes | 41077804.52173913 | 118120091.82608695 | -77042287.30434781 | 65.22369405010312 |
| idle_rss_bytes | 49351724.52173913 | 127745691.82608695 | -78393967.30434781 | 61.367210262615664 |
| image_host_allocated_bytes | 20951040 | 79142912 | -58191872 | 73.52758513611427 |
| installed_rootfs_bytes | 11949958 | 63362683 | -51412725 | 81.14038510648294 |
| kernel_bytes | 4420608 | 6657024 | -2236416 | 33.594831564374715 |
| library_bytes | 8479760 | 18515107 | -10035347 | 54.20085879060812 |
| onion_connection_seconds | 7.588422586999997 | 1.924364752999999 | 5.664057833999998 | -294.333900325808 |
| peak_process_rss_bytes | 60948480 | 131522560 | -70574080 | 53.65929616941763 |
| peak_system_used_bytes | 85770240 | 161996800 | -76226560 | 47.0543615676359 |
| persistent_storage_bytes | 11525748 | 11506100 | 19648 | -0.1707615960229791 |
| production_state_bytes | 5745357 | None | None | None |
| rootfs_bytes | 33554432 | 268435456 | -234881024 | 87.5 |
| startup_seconds | 0.4900201150000001 | 7.139516616 | -6.649496501 | 93.13650851513054 |
| throughput_bytes_per_second | 182794.08313502706 | 288197.7190368614 | -105403.63590183432 | 36.5733761717777 |
| tor_executable_bytes | 2592728 | 3397272 | -804544 | 23.68206019417933 |

## Decision

- onion_connection_seconds exceeds regression limit 20%
- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
