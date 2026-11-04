# Optimization validation

Run: `20261104T162447Z-cd6efe9f`

**BASELINE** — Linux baseline with descriptor-aware authenticated restart recovery

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `da7c5281145589821ceeb4f6b7aabd4d98833587`; dirty: `False`.

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
| bootstrap_seconds | 20.921977645000002 | None | None | None |
| complete_image_bytes | 275092480 | None | None | None |
| cpu_percent | 16.88222863518813 | None | None | None |
| idle_pss_bytes | 117296302.29787233 | None | None | None |
| idle_rss_bytes | 128241838.29787233 | None | None | None |
| image_host_allocated_bytes | 79142912 | None | None | None |
| installed_rootfs_bytes | 63362683 | None | None | None |
| kernel_bytes | 6657024 | None | None | None |
| library_bytes | 18515107 | None | None | None |
| onion_connection_seconds | 2.2833709969999987 | None | None | None |
| peak_process_rss_bytes | 130019328 | None | None | None |
| peak_system_used_bytes | 160100352 | None | None | None |
| persistent_storage_bytes | 11499552 | None | None | None |
| rootfs_bytes | 268435456 | None | None | None |
| startup_seconds | 6.326056145000001 | None | None | None |
| throughput_bytes_per_second | 243826.73790498156 | None | None | None |
| tor_executable_bytes | 3397272 | None | None | None |

## Decision

- first fully validated build sealed; no optimization claim

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
