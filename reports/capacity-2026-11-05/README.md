# Tor endpoint RAM and storage capacity

`capacity32` completed the required public Tor workload on all three 128 MiB cold starts and retained at least **45.61 MiB of available RAM** while carrying a separate 32 MiB resident reservation. The 112 MiB group retained **30.73 MiB** and was the smallest tested setting to clear the RAM margin. All three 96 MiB boots missed the margin and recorded direct reclaim. The chosen screen required 20% of usable Linux RAM and at least 16 MiB available.

**256 MiB is the smallest setting that cleared the complete QEMU screen.** With default TCP ceilings, the 112 and 128 MiB groups cleared the public Tor timing limits but their local TCP medians of 36.15 and 34.06 MiB/s fell below the 37.19 MiB/s floor. This lab workload uses a QEMU forwarding path and a single emulated vCPU. The boot-configured TCP profile cleared the local gates at 112 and 128 MiB. Both lower RAM groups missed the public timing and throughput bounds, and the 128 MiB group retained a blocked two-node run.

`capacity32` is the practical profile for a **128 MiB RAM and 64 MiB usable block flash prototype**, with full lower-RAM qualification still open. Its **40.6768 MiB** boot set consists of **8.6768 MiB** of kernel, read-only root and disk layout plus **32 MiB** of writable Tor state. That leaves **23.3232 MiB** of a 64 MiB usable capacity for the actual firmware and update layout. Physical flash needs to account for controller reservations, alignment and any redundant slots. Physical RAM and flash requirements, power-loss behavior and endurance remain unverified.

## Built images and exact changes

All four rows are complete Buildroot builds. MiB below are logical boot-set bytes / 1,048,576. The state column is already included in the total. The separate diagnostic disks and host SDK are not installed on the device.

| Profile | Kernel MiB | Root partition MiB | Disk alignment/MBR MiB | State MiB | Non-state MiB | Total MiB | Tor bytes | Library bytes |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Qualified `ram` | 4.9658 | 3.9141 | 1 | 16 | 9.8799 | 25.8799 | 2,428,664 | 9,003,952 |
| `fulltor-slim` | 3.7588 | 3.9766 | 1 | 16 | 8.7354 | 24.7354 | 3,397,272 | 8,111,688 |
| `selective` | 3.7588 | 3.8867 | 1 | 16 | 8.6455 | 24.6455 | 2,428,664 | 8,913,872 |
| `capacity32` | 3.7588 | 3.9180 | 1 | 32 | 8.6768 | 40.6768 | 2,428,664 | 9,003,952 |

| Setting | Qualified `ram` | `fulltor-slim` | `selective` | `capacity32` |
|---|---|---|---|---|
| General target C optimization | `-O2` | `-Os` | `-Os` | `-O2` |
| Tor | `-O2`, client modules, section GC | `-O2`, full modules, `-rdynamic`, no section GC | same Tor options as `ram` | byte-identical Tor to `ram` |
| OpenSSL | `-O2` | `-Os` | `-O2` C compilation | byte-identical libcrypto/libssl to `ram` |
| Kernel boot compression | gzip | XZ | XZ | XZ |
| SquashFS access | `FILE_DIRECT` | `FILE_CACHE` | `FILE_DIRECT` | `FILE_DIRECT` |
| BusyBox | reduced archive tools | BUNZIP2, UNLZMA, UNXZ, XZ and UNLZOP enabled | same selections as `ram` | byte-identical BusyBox to `ram` |
| State / queue setting | 16 MiB, automatic queues | 16 MiB, automatic queues | 16 MiB, automatic queues | 32 MiB, explicit 32 MiB client queue limit |

All use Linux 6.18.7, kernel optimization for performance, SMP with `NR_CPUS=2`, one emulated vCPU, XZ SquashFS with 128 KiB blocks and single decompression thread. All resolve the fragment-cache size to **3**. The requested `1` in the older direct-access fragments did not take effect with `SQUASHFS_EMBEDDED` off. The old fragments and images are retained. The GCC 15.3 / musl 1.2.6 SDK, Tor 0.4.9.14, OpenSSL 3.6.5, libevent 2.1.13 and zlib 1.3.2 versions, upstream crypto/routing, security flags and source checks are preserved. The resolved kernel, BusyBox and Buildroot configurations, exact Makefile flags and installed library SHA-256/bytes are in [inputs](inputs/inventory.json) and [capacity32 inputs](capacity32-build/inputs/inventory.json).

The slim build saves 1.1445 MiB (4.42%) versus `ram`, primarily through kernel compression. Its Tor executable and root disk grow, so its direct zero-growth artifact gates still reject it. The selective build saves 1.2344 MiB (4.77%) versus `ram` and restores `-O2` OpenSSL with size optimization elsewhere. Its libcrypto `.text` is identical to `ram` and its libssl binary is identical. [Section hashes](inputs/openssl-section-hashes.json). `capacity32` retains byte-identical Tor, BusyBox and all eight shared libraries from `ram`. Its non-state boot footprint is **12.18% smaller than `ram`**, 60 KiB smaller than slim and 32 KiB larger than selective. The only installed-root byte increase versus `ram` is the 75-byte defaults-file change. Its compressed root is 4 KiB larger. The total grows intentionally for persistent capacity. Its complete build and all nine frozen functions, production-daemon and ELF checks passed. The default comparison against the 512 MiB original baseline is NON_COMPARABLE. A single matched 128 MiB pipeline run met all unchanged runtime bounds against both `ram` references. The automated comparisons are REJECTED solely on the intentional disk/image growth. See [build and matched comparisons](capacity32-build/matched128-comparisons.json).

## Public performance at a fixed 128 MiB

All 27 planned attempts passed every frozen test, including public bootstrap, v3 service, two-node payload integrity and reconnection. The same one-vCPU QEMU TCG settings, NAT, RNG and frozen two-Tor fixture were used for each attempt. Builds and timings ran in separate stages. Public circuits were uncontrolled. The original project baseline is separate from the contemporaneous `ram` control.

| Image | Bootstrap s, median [range] | Onion s, median [range] | Throughput KiB/s, median [range] | Idle A+B RSS MiB | Whole-run Tor CPU % | Integrity Tor CPU s |
|---|---:|---:|---:|---:|---:|---:|
| `ram` | 15.65 [13.96, 17.13] | 3.46 [2.46, 4.80] | 191.97 [70.97, 446.71] | 55.25 | 14.41 | 2.34 |
| `fulltor-slim` | 16.33 [14.15, 23.45] | 3.52 [2.68, 8.07] | 203.21 [125.64, 298.39] | 55.56 | 15.19 | 2.30 |
| `selective` | 15.45 [13.88, 17.19] | 4.61 [2.63, 23.54] | 226.97 [24.67, 361.78] | 55.17 | 15.47 | 2.22 |

The historical slim rate was 174.02 KiB/s versus 306.39 KiB/s for `ram`, below its unchanged 245.12 KiB/s floor. The gap did not reproduce against the fresh control. Slim was +5.9% and selective +18.2% at the median. The unchanged control itself was 37.3% slower than its earlier median and had a 6.3× throughput range. Throughput coefficients of variation were 56.0%, 29.0%, and 57.8%, respectively. Candidate/control ratios below are the medians with exploratory 95% paired-round bootstrap intervals (20,000 draws). The relevant boundaries are 0.80 for throughput and 1.20 for times.

| Candidate | Bootstrap ratio | Onion ratio | Throughput ratio |
|---|---:|---:|---:|
| `fulltor-slim` | 1.044 [0.974, 1.205] | 1.016 [0.751, 1.846] | 1.059 [0.448, 2.265] |
| `selective` | 0.988 [0.898, 1.043] | 1.331 [0.828, 5.559] | 1.182 [0.309, 2.409] |

The wide intervals prevent a confident throughput attribution or exclusion of a meaningful regression. Public-network and emulator/measurement variation remain plausible. No threshold was changed. Slim met the fresh runtime median bounds but still failed the Tor executable and root disk artifact limits. Selective failed the fresh Onion latency bound (4.61 s versus a 4.15 s limit). Both remain REJECTED against the older frozen throughput reference. The earlier original-image 512 MiB results cover a separate comparison. Full [nine-round measurements and gates](public-nine/README.md), [variance statistics](public-variability.json), and [old-reference comparisons](public-nine/frozen-ram128-comparisons.json) are retained.

## Memory, I/O and local controls

The original two-Tor/Python test uses about 85 MiB of guest memory at 128 MiB. The native protocol moves the second endpoint and controller to the host and retains a test-only native observer/backend in the guest. The smaller one-endpoint footprint measures the change in workload. At 128 MiB, the three native unreserved cases had 36.9–37.3 MiB post-boot system peak, 28.1–28.3 MiB Tor HWM, and 74.2–74.6 MiB MemAvailable at the sampled minimum. Integrity used 2.04–2.54 Tor CPU seconds, with 14.3–18.8% system CPU busy, zero observed I/O wait and zero Tor major faults in that phase. Per-process disk byte accounting was unavailable. The observed 128 MiB throughput gap has no demonstrated RAM-pressure or paging cause. The old two-node timing fixture uses temporary test data directories. The persistent-state failures occurred in the native production path.

| Local 128 MiB diagnostic (MiB/s) | `ram` | `fulltor-slim` | `selective` |
|---|---:|---:|---:|
| Direct framed TCP | 39.06 | 39.17 | 35.42 |
| 509-byte AES-CTR | 37.70 | 35.17 | 37.91 |
| 509-byte SHA3-256 | 44.06 | 35.39 | 41.01 |
| 509-byte SHAKE256 | 42.15 | 31.99 | 42.08 |

Three direct TCP trials and five small-buffer EVP trials ran per case. These are within-boot observations. Selective `-O2` recovered the tested SHAKE rate. Bulk AES/SHA256/X25519 controls across all nine public runs did not show a slowdown commensurate with the historical 43% throughput gap. An outlying slow SHA3/SHAKE sample with the same `ram` library code at 192 MiB is retained. Public circuit attribution remains inconclusive. The optional private Chutney network did not converge and remains BLOCKED. Private-network throughput is unavailable.

The six-capacity native sweep covered 96, 112, 128, 160, 192 and 256 MiB for each preserved 16 MiB-state image. **15/18** passed all nine native functions. `ram`/256, selective/112 and slim/96 retain circuit/transfer/reconnection BLOCKED tests. All 18 had the unreserved memory margin and no observed swap, direct reclaim or OOM. Both `ram` and selective passed all nine at 96 MiB. Twelve offline smoke runs are also retained separately and do not qualify any online workload. See [native results](native-measurements.csv), [CPU/paging details](native-analysis.json), and [offline results](offline-capacity.json).

## Persistent-state capacity

At least **12 of 18** retained native notice logs from the 16 MiB-state images record actual `No space left on device` microdescriptor writes. Some bursts were missed by 4 Hz free-space sampling. Tor notice logs exposed failures that final `du` and periodic samples missed. Tor's cache rebuild holds the old cache while preparing its replacement, so short-lived writes need headroom beyond the final directory size. All three 128 MiB unreserved cases had such write errors despite nine functional passes. The [failure evidence](state-exhaustion.json) and complete ignored run logs are preserved. The 32 MiB allocation accommodates transient cache writes. The automated size gates still reject the increase.

## Repeated RAM capacity tests with resident reservation

Three cold starts at each of 96, 112, 128 and 256 MiB used the same `capacity32` image. The 256 MiB cases are within-protocol controls. Each run first executed the nine required tests and unreserved timing, then held 32 MiB of actual resident tmpfs while it repeated local TCP and bidirectional payload integrity. The reservation models additional queue-budget growth. The measurement records tmpfs residency. The observer checked the configured 32 MiB client queue limit through authenticated control and checked Shmem to prove the reservation.

| QEMU RAM MiB | Nine tests passed | Reserved integrity passed | Min available RAM MiB | Peak unreserved / reserved system MiB | Peak Tor HWM MiB | Min state free MiB | Direct-reclaim pages scanned | Complete group screen |
|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 96 | 3/3 | 3/3 | 15.41 | 36.31 / 64.12 | 28.31 | 15.00 | 3319 | BLOCKED_OR_REJECTED |
| 112 | 3/3 | 3/3 | 30.73 | 36.69 / 64.82 | 28.33 | 10.67 | 0 | BLOCKED_OR_REJECTED |
| 128 | 3/3 | 3/3 | 45.61 | 37.14 / 65.94 | 28.36 | 10.69 | 0 | BLOCKED_OR_REJECTED |
| 256 | 3/3 | 3/3 | 168.53 | 39.49 / 68.82 | 28.36 | 13.06 | 0 | PASS |

The group screen requires every planned functional and reservation assertion, intact observations, at least 20% of usable Linux RAM and 16 MiB absolute available, no observed state write errors and at least 4 MiB state free. Every local and public performance median must meet the existing 20% limit against the same image at 256 MiB. Samples include the reservation for the memory margin. Unreserved and reserved rates remain separate. System peaks are sampled after boot. Linux MemTotal excludes the platform/kernel reservation; QEMU boot itself was exercised at every listed budget.

| QEMU RAM MiB | Bootstrap s | Onion s | Public KiB/s | Local TCP MiB/s | Reserved public KiB/s | Reserved local TCP MiB/s | Tor CPU s (whole case) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 96 | 10.71 | 4.75 | 235.55 | 35.43 | 161.86 | 35.18 | 19.89 |
| 112 | 9.26 | 2.32 | 180.16 | 36.15 | 140.01 | 33.28 | 19.84 |
| 128 | 9.78 | 2.81 | 158.12 | 34.06 | 166.55 | 36.63 | 19.31 |
| 256 | 10.55 | 2.93 | 153.56 | 46.49 | 123.72 | 46.21 | 19.10 |

All medians include every available value. Any missing required metric prevents a group pass. The group comparisons are based on unreserved timings. Reserved rates are descriptive, and the reserved integrity assertions must still pass. For per-case counters and paired public uncertainty, use [native group JSON](capacity32-native/native-analysis.json). The group entries determine the reported capacity result.

- **96 MiB** failed the RAM safety margin, local TCP floor and public Onion latency limit.
- **112 MiB** failed the local TCP floor.
- **128 MiB** failed the local TCP floor.

## Local TCP crossover

Three cold boots each at 112, 128 and 256 MiB repeated the direct framed TCP transfer. The only treatment was the guest TCP receive and send autotuning ceiling. The lower-RAM cases used the 256 MiB ceilings and the 256 MiB case used the 128 MiB ceilings. The default initial buffers and global `tcp_mem` thresholds were retained. The observer, fixed boot image, one vCPU, and host forwarding path were common to all cases. Tor stayed offline for this diagnostic.

| QEMU RAM MiB | Default ceilings MiB/s | Crossed ceilings MiB/s | Default maximum receive/send bytes | Crossed maximum receive/send bytes |
|---:|---:|---:|---:|---:|
| 112 | 35.58 | 40.66 | 742,528 / 742,528 | 1,895,552 / 1,895,552 |
| 128 | 36.07 | 47.56 | 872,384 / 872,384 | 1,895,552 / 1,895,552 |
| 256 | 39.84 | 34.21 | 1,895,552 / 1,895,552 | 872,384 / 872,384 |

With the higher ceilings, the 128 MiB median increased by **31.9%** and improved in all three cold-boot blocks. The 112 MiB median increased by **14.3%**, with two of three blocks improving. At 256 MiB, the lower ceilings reduced the median by **14.1%** and reduced each block's rate. The direction of both treatments supports a TCP-buffer contribution to this local forwarding benchmark. This crossover kept Tor offline. A separate public and reserved workload evaluated the same ceilings as boot settings.

[Trials and exact guest sysctls](tcp-buffer-control/results.json) · [Boot medians and trial ranges](tcp-buffer-control/summary.json) · [Reproduction script](tcp-buffer-control/reproduce.py).

## Controlled Tor cell-operation timings

12/12 planned benchmark processes completed with the expected mapped OpenSSL verified. Each unchanged Tor benchmark binary ran against the pinned `-O2` and `-Os` OpenSSL 3.6.5 builds in one fixed 128 MiB QEMU VM. The selected diagnostic process alone loaded the test DSO. The production image has its original libraries. Two reversed blocks control order. Each cell timing covers 1,048,576 operations, but there are only two process starts per combination. Values are descriptive medians for individual crypto operations. End-to-end throughput is measured separately.

| Tor benchmark binary | OpenSSL build | Tor1 outbound ns/cell | CGO outbound ns/cell | POLYVAL 512-byte add ns |
|---|---|---:|---:|---:|
| ram | O2 | 13950.3 (n=2) | 20634.3 (n=2) | 6403.8 (n=2) |
| ram | Os | 13633.5 (n=2) | 30359.4 (n=2) | 6399.6 (n=2) |
| selective | O2 | 13718.6 (n=2) | 20036.3 (n=2) | 6068.7 (n=2) |
| selective | Os | 13261.8 (n=2) | 22017.8 (n=2) | 6160.2 (n=2) |
| slim | O2 | 12828.8 (n=2) | 22578.2 (n=2) | 7183.2 (n=2) |
| slim | Os | 13404.2 (n=2) | 23589.3 (n=2) | 8019.7 (n=2) |

Raw [operations and mapped DSOs](cell-controls/plan.json) and [timing summary](cell-controls/summary.json) are retained. The protocol keeps public routing and cryptographic implementations unchanged. These diagnostics do not replace any mandatory online assertion.


Switching only OpenSSL from `-O2` to `-Os` changed median outbound Tor1 time by less than 5% for each binary. Median outbound CGO time increased by 4.5% for slim and 9.9% for selective. The `ram` binary showed a 47.1% increase, driven by one 38.3 µs trial among two process starts. The small repeat count and that spread limit attribution. These measurements support retaining the qualified `-O2` library build. Public transfer attribution remains inconclusive.

## Boot-configured TCP ceilings under the public workload

Three cold starts at each of 112, 128 and 256 MiB used the same `capacity32` image with the verified `capacity32-tcp` boot profile. The 256 MiB cases are within-protocol controls. Each run first executed the nine required tests and unreserved timing, then held 32 MiB of actual resident tmpfs while it repeated local TCP and bidirectional payload integrity. The reservation models additional queue-budget growth. The measurement records tmpfs residency. The observer checked the configured 32 MiB client queue limit through authenticated control and checked Shmem to prove the reservation.

| QEMU RAM MiB | Nine tests passed | Reserved integrity passed | Min available RAM MiB | Peak unreserved / reserved system MiB | Peak Tor HWM MiB | Min state free MiB | Direct-reclaim pages scanned | Complete group screen |
|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 112 | 3/3 | 3/3 | 30.88 | 36.82 / 64.67 | 28.46 | 12.90 | 0 | BLOCKED_OR_REJECTED |
| 128 | 2/3 | 3/3 | 45.70 | 37.04 / 65.84 | 28.45 | 11.52 | 0 | BLOCKED_OR_REJECTED |
| 256 | 3/3 | 3/3 | 168.52 | 39.59 / 68.83 | 28.41 | 10.56 | 0 | PASS |

The group screen requires every planned functional and reservation assertion, intact observations, at least 20% of usable Linux RAM and 16 MiB absolute available, no observed state write errors and at least 4 MiB state free. Every local and public performance median must meet the existing 20% limit against the same image at 256 MiB. Samples include the reservation for the memory margin. Unreserved and reserved rates remain separate. System peaks are sampled after boot. Linux MemTotal excludes the platform/kernel reservation; QEMU boot itself was exercised at every listed budget.

| QEMU RAM MiB | Bootstrap s | Onion s | Public KiB/s | Local TCP MiB/s | Reserved public KiB/s | Reserved local TCP MiB/s | Tor CPU s (whole case) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 112 | 10.45 | 4.15 | 135.35 | 45.48 | 115.25 | 38.86 | 22.05 |
| 128 | 11.05 | 4.15 | 141.99 | 43.97 | 205.97 | 35.13 | 20.44 |
| 256 | 10.96 | 3.24 | 212.56 | 45.59 | 168.07 | 37.15 | 21.04 |

All medians include every available value. Any missing required metric prevents a group pass. The group comparisons are based on unreserved timings. Reserved rates are descriptive, and the reserved integrity assertions must still pass. For per-case counters and paired public uncertainty, use [native group JSON](capacity32-tcp-native/native-analysis.json). The group entries determine the reported capacity result.

- **112 MiB:** public onion_connection_seconds, throughput_bytes_per_second.
- **128 MiB:** one or more mandatory assertions in the group or 256 MiB control; public onion_connection_seconds, throughput_bytes_per_second.

The boot-configured profile cleared all eight local gates at both 112 and 128 MiB. Local TCP medians were **45.48 and 43.97 MiB/s**, versus 45.59 MiB/s at 256 MiB. The 128 MiB group retained at least 45.70 MiB of available RAM, with all three reserved payload checks passing. At 112 MiB the corresponding minimum was 30.88 MiB, and all three complete functional runs passed. All nine state checks passed and sampled free state stayed above 10.55 MiB.

Both lower-RAM groups exceeded the 3.89-second Onion median limit with medians of 4.15 seconds. Public throughput medians of 135.35 and 141.99 KiB/s fell below the 170.05 KiB/s floor. The 128 MiB group also retained a blocked two-node SOCKS attempt. These public outcomes leave the lower-RAM settings unqualified. The minimum complete QEMU screen pass remained 256 MiB. Public timing attribution remains inconclusive given the measured variability and the three-run group size.
