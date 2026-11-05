## Boot-configured TCP ceilings under the public workload

Three cold starts at each of 112, 128 and 256 MiB used the same `capacity32` image with the verified `capacity32-tcp` boot profile. The 256 MiB cases are within-protocol controls. Each run first executed the nine required tests and unreserved timing, then held 32 MiB of actual resident tmpfs while it repeated local TCP and bidirectional payload integrity. The reservation models additional queue-budget growth and is not observed Tor queue use. The observer checked the configured 32 MiB client queue limit through authenticated control and checked Shmem to prove the reservation.

| QEMU RAM MiB | Nine tests passed | Reserved integrity passed | Min available RAM MiB | Peak unreserved / reserved system MiB | Peak Tor HWM MiB | Min state free MiB | Direct-reclaim pages scanned | Complete group screen |
|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 112 | 3/3 | 3/3 | 30.88 | 36.82 / 64.67 | 28.46 | 12.90 | 0 | BLOCKED_OR_REJECTED |
| 128 | 2/3 | 3/3 | 45.70 | 37.04 / 65.84 | 28.45 | 11.52 | 0 | BLOCKED_OR_REJECTED |
| 256 | 3/3 | 3/3 | 168.52 | 39.59 / 68.83 | 28.41 | 10.56 | 0 | PASS |

The group screen requires every planned functional and reservation assertion, intact observations, at least 20% of usable Linux RAM and 16 MiB absolute available, no observed state write errors and at least 4 MiB state free. Every local and public performance median must meet the existing 20% limit against the same image at 256 MiB. Samples include the reservation for the memory margin; unreserved and reserved rates remain separate. System peaks are sampled after boot. Linux MemTotal excludes the platform/kernel reservation; QEMU boot itself was exercised at every listed budget.

| QEMU RAM MiB | Bootstrap s | Onion s | Public KiB/s | Local TCP MiB/s | Reserved public KiB/s | Reserved local TCP MiB/s | Tor CPU s (whole case) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 112 | 10.45 | 4.15 | 135.35 | 45.48 | 115.25 | 38.86 | 22.05 |
| 128 | 11.05 | 4.15 | 141.99 | 43.97 | 205.97 | 35.13 | 20.44 |
| 256 | 10.96 | 3.24 | 212.56 | 45.59 | 168.07 | 37.15 | 21.04 |

All medians include every available value; any missing required metric prevents a group pass. The group comparisons are based on unreserved timings; reserved rates are descriptive, and the reserved integrity assertions must still pass. For per-case counters and paired public uncertainty, use [native group JSON](capacity32-tcp-native/native-analysis.json). The group entries, rather than individual local-only screening rows, determine the reported capacity result.

- **112 MiB:** public onion_connection_seconds, throughput_bytes_per_second.
- **128 MiB:** one or more mandatory assertions in the group or 256 MiB control; public onion_connection_seconds, throughput_bytes_per_second.
