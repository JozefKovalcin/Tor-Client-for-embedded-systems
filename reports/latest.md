# Optimization validation

Run: `20261105T130046Z-719a45c7`

**REJECTED** — Selective Tor/OpenSSL O2 with Os userland and XZ kernel at 128 MiB

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `b9d2bdecd79e1c94e7fc953c199c166809131a4f`; dirty: `False`.

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
| bootstrap_seconds | 16.690900327 | 16.003011715 | 0.6878886120000018 | -4.298494709937803 |
| complete_image_bytes | 25842688 | 27137024 | -1294336 | 4.769631334666617 |
| cpu_percent | 38.794474243748006 | 44.5449750122731 | -5.750500768525093 | 12.909426409916508 |
| idle_pss_bytes | 48985000.85106383 | 48997833.5319149 | -12832.680851064622 | 0.02619030256247149 |
| idle_rss_bytes | 57831336.85106383 | 57862971.91489362 | -31635.063829787076 | 0.054672379905264384 |
| image_host_allocated_bytes | 9342976 | 10637312 | -1294336 | 12.167886022333462 |
| installed_rootfs_bytes | 12119080 | 12209160 | -90080 | 0.737806695956151 |
| kernel_bytes | 3941376 | 5207040 | -1265664 | 24.30678466076696 |
| library_bytes | 8913872 | 9003952 | -90080 | 1.0004495803620455 |
| onion_connection_seconds | 4.315374477999995 | 4.761459074000001 | -0.4460845960000057 | 9.368653370053215 |
| peak_process_rss_bytes | 60395520 | 59785216 | 610304 | -1.0208276240065772 |
| peak_system_used_bytes | 89702400 | 89694208 | 8192 | -0.009133254178463786 |
| persistent_storage_bytes | 16660681 | 16599379 | 61302 | -0.3693029721172099 |
| production_peak_rss_bytes | 27475968 | 27508736 | -32768 | 0.11911852293031566 |
| production_pss_bytes | 26439680 | 26541056 | -101376 | 0.38195918052393996 |
| production_rss_bytes | 26902528 | 27021312 | -118784 | 0.4395937547370017 |
| production_state_bytes | 8296534 | 8295368 | 1166 | -0.014056037055860572 |
| production_state_free_bytes | 4890624 | 4890624 | 0 | 0.0 |
| production_state_volume_bytes | 14410752 | 14410752 | 0 | 0.0 |
| rootfs_bytes | 21901312 | 21929984 | -28672 | 0.1307433694434068 |
| startup_seconds | 0.4501322079999994 | 0.4786876239999991 | -0.028555415999999667 | 5.965354976463674 |
| throughput_bytes_per_second | 163062.67722908457 | 313747.1288022694 | -150684.45157318484 | 48.027356345354676 |
| tor_executable_bytes | 2428664 | 2428664 | 0 | 0.0 |

## Decision

- throughput_bytes_per_second exceeds regression limit 20%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
