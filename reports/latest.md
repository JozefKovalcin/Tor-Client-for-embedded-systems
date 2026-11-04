# Optimization validation

Run: `20261104T173443Z-2cd3ee20`

**REJECTED** — Iteration 4: gzip SquashFS read-only production root, 16 MiB ext4 state, omit unused split libevent libraries

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `f33d5a3f345ebac146cf262e0c0c3c8043f3e4b9`; dirty: `False`.

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
| bootstrap_seconds | 83.43889079899999 | 21.527869747 | 61.911021051999995 | -287.5854498359159 |
| complete_image_bytes | 27145216 | 275092480 | -247947264 | 90.13233077109196 |
| cpu_percent | 15.488579442814718 | 54.37785271538515 | -38.889273272570435 | 71.51675053466661 |
| idle_pss_bytes | 41904955.91489362 | 118120091.82608695 | -76215135.91119334 | 64.52343097007409 |
| idle_rss_bytes | 50084667.91489362 | 127745691.82608695 | -77661023.91119334 | 60.793458316325136 |
| image_host_allocated_bytes | 10645504 | 79142912 | -68497408 | 86.54901148949384 |
| installed_rootfs_bytes | 11542400 | 63362683 | -51820283 | 81.78359966228072 |
| kernel_bytes | 4445184 | 6657024 | -2211840 | 33.22565759113982 |
| library_bytes | 8111688 | 18515107 | -10403419 | 56.18881381565875 |
| onion_connection_seconds | 161.34814355999998 | 1.924364752999999 | 159.423778807 | -8284.488611551704 |
| peak_process_rss_bytes | 56115200 | 131522560 | -75407360 | 57.33416381189661 |
| peak_system_used_bytes | 88244224 | 161996800 | -73752576 | 45.527180783817954 |
| persistent_storage_bytes | 11538685 | 11506100 | 32585 | -0.2831976082252023 |
| production_peak_rss_bytes | 23064576 | None | None | None |
| production_pss_bytes | 22173696 | None | None | None |
| production_rss_bytes | 22675456 | None | None | None |
| production_state_bytes | 5640895 | None | None | None |
| production_state_free_bytes | 7545856 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 22700032 | 268435456 | -245735424 | 91.5435791015625 |
| startup_seconds | 0.3852869339999998 | 7.139516616 | -6.754229682 | 94.60345910342791 |
| throughput_bytes_per_second | 163495.94819980508 | 288197.7190368614 | -124701.7708370563 | 43.26952040210511 |
| tor_executable_bytes | 2592728 | 3397272 | -804544 | 23.68206019417933 |

## Decision

- bootstrap_seconds exceeds regression limit 20%
- onion_connection_seconds exceeds regression limit 20%
- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
