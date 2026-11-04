# Optimization validation

Run: `20261104T161505Z-bed18230`

**INCOMPLETE** — Baseline retry: guest JSON framing fixed; all network tests required

Build: PASS. baseline refused: mandatory test not passed: reconnection

Commit: `6e0cf4709995b4feaa5778d16ad3321c66ea59c4`; dirty: `False`.

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

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | 19.70004763 | None | None | None |
| complete_image_bytes | 275092480 | None | None | None |
| cpu_percent | 12.831817768044667 | None | None | None |
| idle_pss_bytes | 116757961.53191489 | None | None | None |
| idle_rss_bytes | 127778816.0 | None | None | None |
| image_host_allocated_bytes | 79142912 | None | None | None |
| installed_rootfs_bytes | 63360624 | None | None | None |
| kernel_bytes | 6657024 | None | None | None |
| library_bytes | 18515107 | None | None | None |
| onion_connection_seconds | 3.8382847190000007 | None | None | None |
| peak_process_rss_bytes | 131448832 | None | None | None |
| peak_system_used_bytes | 161128448 | None | None | None |
| persistent_storage_bytes | 11499831 | None | None | None |
| rootfs_bytes | 268435456 | None | None | None |
| startup_seconds | 6.706675341 | None | None | None |
| throughput_bytes_per_second | 209773.5293207537 | None | None | None |
| tor_executable_bytes | 3397272 | None | None | None |

## Decision

- no usable immutable baseline: [Errno 2] No such file or directory: '/home/jozef/embedded-tor/reports/baseline'
- mandatory test not passed: reconnection

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
