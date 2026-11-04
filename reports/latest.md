# Optimization validation

Run: `20261104T163515Z-f2a9da00`

**REJECTED** — Iteration 1: remove unused GeoIP/helper files; 64 MiB filesystem

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `0e80161297bba3f386c311f45f17477348534fd2`; dirty: `False`.

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
| bootstrap_seconds | 14.919582403 | 20.921977645000002 | -6.002395242000002 | 28.689425750507258 |
| complete_image_bytes | 73765888 | 275092480 | -201326592 | 73.18505834837796 |
| cpu_percent | 15.068089977173448 | 16.88222863518813 | -1.8141386580146825 | 10.745848176901356 |
| idle_pss_bytes | 41949227.57446808 | 117296302.29787233 | -75347074.72340426 | 64.23653026338495 |
| idle_rss_bytes | 52925984.680851065 | 128241838.29787233 | -75315853.61702126 | 58.72954927711047 |
| image_host_allocated_bytes | 47161344 | 79142912 | -31981568 | 40.40989545595694 |
| installed_rootfs_bytes | 35838513 | 63362683 | -27524170 | 43.43908543140447 |
| kernel_bytes | 6657024 | 6657024 | 0 | 0.0 |
| library_bytes | 18510879 | 18515107 | -4228 | 0.02283540678430862 |
| onion_connection_seconds | 3.1969938860000013 | 2.2833709969999987 | 0.9136228890000027 | -40.01202127032199 |
| peak_process_rss_bytes | 56459264 | 130019328 | -73560064 | 56.57625303216457 |
| peak_system_used_bytes | 85295104 | 160100352 | -74805248 | 46.7239747230537 |
| persistent_storage_bytes | 11499597 | 11499552 | 45 | -0.0003913195922762904 |
| rootfs_bytes | 67108864 | 268435456 | -201326592 | 75.0 |
| startup_seconds | 0.5325857270000003 | 6.326056145000001 | -5.793470418 | 91.58107808731754 |
| throughput_bytes_per_second | 337277.99418018386 | 243826.73790498156 | 93451.2562752023 | -38.326910772033514 |
| tor_executable_bytes | 3397272 | 3397272 | 0 | 0.0 |

## Decision

- onion_connection_seconds exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
