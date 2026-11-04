# Optimization validation

Run: `20261104T151222Z-e2c5aa16`

**INCOMPLETE** — Initial integration: Linux target build unavailable on Windows host

Build: BLOCKED. Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner

Commit: `None`; dirty: `True`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| security_config | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| tor_config | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| tor_startup | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| socks5 | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| public_bootstrap | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| onion_v3 | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| two_node | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| bidirectional_integrity | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| reconnection | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | None | None | None | None |
| complete_image_bytes | None | None | None | None |
| cpu_percent | None | None | None | None |
| idle_rss_bytes | None | None | None | None |
| kernel_bytes | None | None | None | None |
| library_bytes | None | None | None | None |
| onion_connection_seconds | None | None | None | None |
| peak_process_rss_bytes | None | None | None | None |
| peak_system_used_bytes | None | None | None | None |
| persistent_storage_bytes | None | None | None | None |
| rootfs_bytes | None | None | None | None |
| throughput_bytes_per_second | None | None | None | None |
| tor_executable_bytes | None | None | None | None |

## Decision

- no usable immutable baseline: [Errno 2] No such file or directory: 'C:\\Users\\jozef\\Downloads\\Tor Klient\\reports\\baseline'
- complete build did not pass
- mandatory test not passed: security_config
- mandatory test not passed: tor_config
- mandatory test not passed: tor_startup
- mandatory test not passed: socks5
- mandatory test not passed: public_bootstrap
- mandatory test not passed: onion_v3
- mandatory test not passed: two_node
- mandatory test not passed: bidirectional_integrity
- mandatory test not passed: reconnection
- missing/invalid measurement: tor_executable_bytes
- missing/invalid measurement: library_bytes
- missing/invalid measurement: kernel_bytes
- missing/invalid measurement: rootfs_bytes
- missing/invalid measurement: complete_image_bytes
- missing/invalid measurement: persistent_storage_bytes
- missing/invalid measurement: idle_rss_bytes
- missing/invalid measurement: peak_process_rss_bytes
- missing/invalid measurement: peak_system_used_bytes
- missing/invalid measurement: bootstrap_seconds
- missing/invalid measurement: onion_connection_seconds
- missing/invalid measurement: cpu_percent
- missing/invalid measurement: throughput_bytes_per_second
- missing provenance: compiler
- missing provenance: compiler_flags
- missing provenance: source_provenance_sha256
- missing Git commit
- missing measurement environment

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
