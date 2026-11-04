# Optimization validation

Run: `20261104T155711Z-ad98aa0a`

**INCOMPLETE** — Complete Linux baseline: corrected compiler metadata and QEMU tests

Build: PASS. target produced no complete test result; see QEMU log

Commit: `95cc6f54e7c513a81e657528a45e705031ed2198`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| elf_hardening | offline | PASS | PIE, RELRO, NOW and non-executable stack observed |
| security_config | offline | BLOCKED | target produced no complete test result; see QEMU log |
| tor_config | offline | BLOCKED | target produced no complete test result; see QEMU log |
| tor_startup | offline | BLOCKED | target produced no complete test result; see QEMU log |
| socks5 | online | BLOCKED | target produced no complete test result; see QEMU log |
| public_bootstrap | online | BLOCKED | target produced no complete test result; see QEMU log |
| onion_v3 | online | BLOCKED | target produced no complete test result; see QEMU log |
| two_node | online | BLOCKED | target produced no complete test result; see QEMU log |
| bidirectional_integrity | online | BLOCKED | target produced no complete test result; see QEMU log |
| reconnection | online | BLOCKED | target produced no complete test result; see QEMU log |

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | None | None | None | None |
| complete_image_bytes | 275092480 | None | None | None |
| cpu_percent | None | None | None | None |
| idle_pss_bytes | None | None | None | None |
| idle_rss_bytes | None | None | None | None |
| image_host_allocated_bytes | 79142912 | None | None | None |
| installed_rootfs_bytes | 63360624 | None | None | None |
| kernel_bytes | 6657024 | None | None | None |
| library_bytes | 18515107 | None | None | None |
| onion_connection_seconds | None | None | None | None |
| peak_process_rss_bytes | None | None | None | None |
| peak_system_used_bytes | None | None | None | None |
| persistent_storage_bytes | None | None | None | None |
| rootfs_bytes | 268435456 | None | None | None |
| throughput_bytes_per_second | None | None | None | None |
| tor_executable_bytes | 3397272 | None | None | None |

## Decision

- no usable immutable baseline: [Errno 2] No such file or directory: '/home/jozef/embedded-tor/reports/baseline'
- mandatory test not passed: security_config
- mandatory test not passed: tor_config
- mandatory test not passed: tor_startup
- mandatory test not passed: socks5
- mandatory test not passed: public_bootstrap
- mandatory test not passed: onion_v3
- mandatory test not passed: two_node
- mandatory test not passed: bidirectional_integrity
- mandatory test not passed: reconnection
- missing/invalid measurement: persistent_storage_bytes
- missing/invalid measurement: idle_rss_bytes
- missing/invalid measurement: peak_process_rss_bytes
- missing/invalid measurement: peak_system_used_bytes
- missing/invalid measurement: bootstrap_seconds
- missing/invalid measurement: onion_connection_seconds
- missing/invalid measurement: cpu_percent
- missing/invalid measurement: throughput_bytes_per_second
- missing measurement environment

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
