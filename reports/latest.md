# Optimization validation

Run: `20261105T101727Z-3c2822d0`

**ACCEPTED** — Reduce SquashFS buffer RAM and unused archive codec storage while keeping qualified performance flags

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `6247a1393d289ece4918826b4e9ea1835909b760`; dirty: `False`.

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
| bootstrap_seconds | 15.689931228 | 16.241655212 | -0.5517239840000006 | 3.3969689468125406 |
| complete_image_bytes | 27137024 | 27141120 | -4096 | 0.015091492171288436 |
| cpu_percent | 42.75315887617588 | 36.44993240938906 | 6.303226466786818 | -17.29283444477165 |
| idle_pss_bytes | 48945463.652173914 | 49077248.0 | -131784.34782608598 | 0.2685243227698627 |
| idle_rss_bytes | 57838191.30434783 | 57931776.0 | -93584.69565217197 | 0.1615429426022982 |
| image_host_allocated_bytes | 10637312 | 10641408 | -4096 | 0.03849114703618168 |
| installed_rootfs_bytes | 12209160 | 12209160 | 0 | 0.0 |
| kernel_bytes | 5207040 | 5207040 | 0 | 0.0 |
| library_bytes | 9003952 | 9003952 | 0 | 0.0 |
| onion_connection_seconds | 2.2031843460000005 | 3.2507847729999995 | -1.047600426999999 | 32.22607770594473 |
| peak_process_rss_bytes | 60518400 | 60489728 | 28672 | -0.04739978331527627 |
| peak_system_used_bytes | 89636864 | 89772032 | -135168 | 0.15056805219692476 |
| persistent_storage_bytes | 16648867 | 16605192 | 43675 | -0.26302014454274303 |
| production_peak_rss_bytes | 27574272 | 27578368 | -4096 | 0.014852220406950839 |
| production_pss_bytes | 26602496 | 26575872 | 26624 | -0.10018109659769583 |
| production_rss_bytes | 27070464 | 27074560 | -4096 | 0.015128593040847202 |
| production_state_bytes | 8320183 | 8298312 | 21871 | -0.2635596251382209 |
| production_state_free_bytes | 4866048 | 4887552 | -21504 | 0.43997485857950974 |
| production_state_volume_bytes | 14410752 | 14410752 | 0 | 0.0 |
| rootfs_bytes | 21929984 | 21934080 | -4096 | 0.018674136321195144 |
| startup_seconds | 0.4270389169999991 | 0.4394667390000002 | -0.012427822000001143 | 2.8279323318712266 |
| throughput_bytes_per_second | 275884.4981868447 | 233216.21054444314 | 42668.287642401556 | -18.29559255027447 |
| tor_executable_bytes | 2428664 | 2428664 | 0 | 0.0 |

## Decision

- all mandatory tests passed under equivalent conditions; measured thresholds satisfied

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
