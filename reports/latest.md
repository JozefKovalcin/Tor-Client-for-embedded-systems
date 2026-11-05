# Optimization validation

Run: `20261105T080754Z-8e9862ab`

**REJECTED** — Ablation: only Tor CFLAGS -Os to -O2; kernel, libraries and gzip filesystem unchanged

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `ccc95611560be699a267014ade1bc5a1301b5a47`; dirty: `False`.

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
| bootstrap_seconds | 18.135261557999996 | 22.553607893 | -4.4183463350000025 | 19.59041921789964 |
| complete_image_bytes | 27333632 | 275092480 | -247758848 | 90.06383889519627 |
| cpu_percent | 16.115412383016203 | 20.26267122673946 | -4.147258843723257 | 20.46748327165454 |
| idle_pss_bytes | 48054850.782608695 | 123724451.40425532 | -75669600.62164661 | 61.15977865555852 |
| idle_rss_bytes | 56692290.782608695 | 133537443.40425532 | -76845152.62164661 | 57.54577192931182 |
| image_host_allocated_bytes | 10833920 | 79142912 | -68308992 | 86.31094089638754 |
| installed_rootfs_bytes | 11980608 | 63362683 | -51382075 | 81.09201278613786 |
| kernel_bytes | 4445184 | 6657024 | -2211840 | 33.22565759113982 |
| library_bytes | 8111688 | 18515107 | -10403419 | 56.18881381565875 |
| onion_connection_seconds | 4.035402197000003 | 3.0411382779999983 | 0.9942639190000051 | -32.69380830831151 |
| peak_process_rss_bytes | 58757120 | 134811648 | -76054528 | 56.4153981709355 |
| peak_system_used_bytes | 94593024 | 171053056 | -76460032 | 44.69960010536146 |
| persistent_storage_bytes | 16475965 | 16321542 | 154423 | -0.946129967376857 |
| production_peak_rss_bytes | 26505216 | None | None | None |
| production_pss_bytes | 25513984 | None | None | None |
| production_rss_bytes | 25997312 | None | None | None |
| production_state_bytes | 8156610 | None | None | None |
| production_state_free_bytes | 5029888 | None | None | None |
| production_state_volume_bytes | 14410752 | None | None | None |
| rootfs_bytes | 22888448 | 268435456 | -245547008 | 91.473388671875 |
| startup_seconds | 0.5104580639999998 | 6.915354525000001 | -6.404896461000001 | 92.61848308492904 |
| throughput_bytes_per_second | 274440.20972307265 | 335069.59929016826 | -60629.38956709561 | 18.094565933625905 |
| tor_executable_bytes | 3030936 | 3397272 | -366336 | 10.783240199783826 |

## Decision

- onion_connection_seconds exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
