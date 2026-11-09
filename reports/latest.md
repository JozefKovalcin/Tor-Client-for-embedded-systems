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


## Repository audit — 2026-11-09

Windows and native WSL sources were synchronized with GitHub. The current validation audit and preserved-work details are in [the synchronization report](sync-2026-11-09/README.md). The measurements below remain the archived 2026-11-05 ARM online run; they were not remeasured during this audit.

Source: [ARM build result](arm-2026-11-05/build-results.json) and [unchanged comparison](arm-2026-11-05/build-comparison.json). Its pre-commit dirty-tree provenance is retained.

---

# Optimization validation

Run: `20261105T190223Z-7002ec13`

**NON_COMPARABLE** — Exercise the built ARMv7 capacity32 image with low PCI addresses and the unchanged frozen suite

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `fec5f291d41599acb3499f3f1639768a44388a78`; dirty: `True`.

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
| bootstrap_seconds | 19.722238735999998 | 20.921977645000002 | -1.1997389090000041 | 5.734347533282645 |
| complete_image_bytes | 40495840 | 275092480 | -234596640 | 85.27919047441792 |
| cpu_percent | 18.905216107316477 | 16.88222863518813 | 2.022987472128346 | -11.982940853625054 |
| idle_pss_bytes | 41288521.95555556 | 117296302.29787233 | -76007780.34231678 | 64.7998094170915 |
| idle_rss_bytes | 49291081.95555556 | 128241838.29787233 | -78950756.34231678 | 61.56396180077735 |
| image_host_allocated_bytes | 10502144 | 79142912 | -68640768 | 86.73015215816169 |
| installed_rootfs_bytes | 9318905 | 63362683 | -54043778 | 85.29275504321684 |
| kernel_bytes | 2484960 | 6657024 | -4172064 | 62.6716082141209 |
| library_bytes | 6232916 | 18515107 | -12282191 | 66.33605196016421 |
| onion_connection_seconds | 3.9119959200000025 | 2.2833709969999987 | 1.6286249230000038 | -71.32546244739767 |
| peak_process_rss_bytes | 53071872 | 130019328 | -76947456 | 59.18155183820055 |
| peak_system_used_bytes | 78852096 | 160100352 | -81248256 | 50.74833064701819 |
| persistent_storage_bytes | 16609951 | 11499552 | 5110399 | -44.4399834010925 |
| production_peak_rss_bytes | 24084480 | None | None | None |
| production_pss_bytes | 23244800 | None | None | None |
| production_rss_bytes | 23724032 | None | None | None |
| production_state_bytes | 8300625 | None | None | None |
| production_state_free_bytes | 15765504 | None | None | None |
| production_state_volume_bytes | 26464256 | None | None | None |
| rootfs_bytes | 38010880 | 268435456 | -230424576 | 85.83984375 |
| startup_seconds | 1.717394176 | 6.326056145000001 | -4.608661969000001 | 72.85205605774782 |
| throughput_bytes_per_second | 198710.86818946814 | 243826.73790498156 | -45115.86971551343 | 18.50324952183666 |
| tor_executable_bytes | 2207484 | 3397272 | -1189788 | 35.021864601951215 |

## Decision

- test environment, architecture or validation policy differs; comparison refused
- no measured improvement in a policy-controlled metric

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
