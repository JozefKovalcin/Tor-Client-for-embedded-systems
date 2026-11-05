## Repeated RAM capacity tests with resident reservation

Three cold starts at each of 96, 112, 128 and 256 MiB used the same `capacity32` image. The 256 MiB cases are within-protocol controls. Each run first executed the nine required tests and unreserved timing, then held 32 MiB of actual resident tmpfs while it repeated local TCP and bidirectional payload integrity. The reservation models additional queue-budget growth and is not observed Tor queue use. The observer checked the configured 32 MiB client queue limit through authenticated control and checked Shmem to prove the reservation.

| QEMU RAM MiB | Nine tests passed | Reserved integrity passed | Min available RAM MiB | Peak unreserved / reserved system MiB | Peak Tor HWM MiB | Min state free MiB | Direct-reclaim pages scanned | Complete group screen |
|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 96 | 3/3 | 3/3 | 15.41 | 36.31 / 64.12 | 28.31 | 15.00 | 3319 | BLOCKED_OR_REJECTED |
| 112 | 3/3 | 3/3 | 30.73 | 36.69 / 64.82 | 28.33 | 10.67 | 0 | BLOCKED_OR_REJECTED |
| 128 | 3/3 | 3/3 | 45.61 | 37.14 / 65.94 | 28.36 | 10.69 | 0 | BLOCKED_OR_REJECTED |
| 256 | 3/3 | 3/3 | 168.53 | 39.49 / 68.82 | 28.36 | 13.06 | 0 | PASS |

The group screen requires every planned functional and reservation assertion, intact observations, at least 20% of usable Linux RAM and 16 MiB absolute available, no observed state write errors and at least 4 MiB state free. Every local and public performance median must meet the existing 20% limit against the same image at 256 MiB. Samples include the reservation for the memory margin; unreserved and reserved rates remain separate. System peaks are sampled after boot. Linux MemTotal excludes the platform/kernel reservation; QEMU boot itself was exercised at every listed budget.

| QEMU RAM MiB | Bootstrap s | Onion s | Public KiB/s | Local TCP MiB/s | Reserved public KiB/s | Reserved local TCP MiB/s | Tor CPU s (whole case) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 96 | 10.71 | 4.75 | 235.55 | 35.43 | 161.86 | 35.18 | 19.89 |
| 112 | 9.26 | 2.32 | 180.16 | 36.15 | 140.01 | 33.28 | 19.84 |
| 128 | 9.78 | 2.81 | 158.12 | 34.06 | 166.55 | 36.63 | 19.31 |
| 256 | 10.55 | 2.93 | 153.56 | 46.49 | 123.72 | 46.21 | 19.10 |

All medians include every available value; any missing required metric prevents a group pass. The group comparisons are based on unreserved timings; reserved rates are descriptive, and the reserved integrity assertions must still pass. For per-case counters and paired public uncertainty, use [native group JSON](capacity32-native/native-analysis.json). The group entries, rather than individual local-only screening rows, determine the reported capacity result.

- **96 MiB:** RAM safety margin; local tcp; public onion_connection_seconds.
- **112 MiB:** local tcp.
- **128 MiB:** local tcp.
