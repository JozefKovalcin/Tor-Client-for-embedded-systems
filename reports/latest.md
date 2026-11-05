# Latest ARM capacity outcome

The ARMv7 capacity32 boot set uses **38.6198 MiB** including 32 MiB
of persistent state. Its non-state footprint is **23.71% smaller**
than x86 capacity32. The frozen public suite and production service
passed at 128 and 256 MiB. All twelve paired native runs passed the
functions, reservation, RAM and state checks. The 128 MiB ARM group
missed the unchanged public Onion latency and throughput gates.
The 256 MiB ARM group passed the complete QEMU screen.
[ARM report](arm-2026-11-05/README.md) · [Hardware shortlist](../docs/ARM_PLATFORM.md)

## Previous x86 capacity outcome

The `capacity32` build and all twelve stressed public Tor functional runs completed successfully. At 128 MiB, the minimum available RAM was 45.61 MiB. The complete screen passed at 256 MiB. The 112 and 128 MiB groups fell below the local TCP floor. The 96 MiB group also missed the RAM margin. The boot-configured TCP profile cleared the local throughput floor at 112 and 128 MiB. Public Onion latency and throughput missed the limits at both lower RAM settings. The 128 MiB group also retained a blocked two-node run.

The kernel, read-only root and disk layout use 8.6768 MiB. The 32 MiB persistent partition brings the full boot set to 40.6768 MiB. Both matched 128 MiB image comparisons reject the intentional complete-image and root-disk growth.

[Capacity report and measurements](capacity-2026-11-05/README.md).

---

# Optimization validation

Run: `20261105T160654Z-f407769f`

**NON_COMPARABLE** — Configuration validation

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `62a99ece3177a97515a3807e1ece72201c3fc8fe`; dirty: `False`.

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
| bootstrap_seconds | 16.578146437 | 20.921977645000002 | -4.343831208000001 | 20.76204879722784 |
| complete_image_bytes | 42652672 | 275092480 | -232439808 | 84.49515159411119 |
| cpu_percent | 11.810782738456897 | 16.88222863518813 | -5.071445896731234 | 30.040144617877456 |
| idle_pss_bytes | 48998826.666666664 | 117296302.29787233 | -68297475.63120568 | 58.226452405775916 |
| idle_rss_bytes | 58007978.666666664 | 128241838.29787233 | -70233859.63120568 | 54.76672867716598 |
| image_host_allocated_bytes | 12660736 | 79142912 | -66482176 | 84.00269123279163 |
| installed_rootfs_bytes | 12209235 | 63362683 | -51153448 | 80.7311899971155 |
| kernel_bytes | 3941376 | 6657024 | -2715648 | 40.793724042455004 |
| library_bytes | 9003952 | 18515107 | -9511155 | 51.36970042895242 |
| onion_connection_seconds | 3.1683918219999967 | 2.2833709969999987 | 0.885020824999998 | -38.75939679372211 |
| peak_process_rss_bytes | 59752448 | 130019328 | -70266880 | 54.04341114576442 |
| peak_system_used_bytes | 89710592 | 160100352 | -70389760 | 43.96602450942769 |
| persistent_storage_bytes | 16666886 | 11499552 | 5167334 | -44.9350896452314 |
| production_peak_rss_bytes | 27508736 | None | None | None |
| production_pss_bytes | 26570752 | None | None | None |
| production_rss_bytes | 27037696 | None | None | None |
| production_state_bytes | 8329029 | None | None | None |
| production_state_free_bytes | 15736832 | None | None | None |
| production_state_volume_bytes | 26464256 | None | None | None |
| rootfs_bytes | 38711296 | 268435456 | -229724160 | 85.57891845703125 |
| startup_seconds | 0.38846606300000097 | 6.326056145000001 | -5.937590082 | 93.85926943903212 |
| throughput_bytes_per_second | 267306.4298971672 | 243826.73790498156 | 23479.69199218563 | -9.629662519348303 |
| tor_executable_bytes | 2428664 | 3397272 | -968608 | 28.511346751157987 |

## Decision

- test environment, architecture or validation policy differs; comparison refused
- no measured improvement in a policy-controlled metric

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
