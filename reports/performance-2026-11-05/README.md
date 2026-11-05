# Embedded Tor performance investigation — 2026-11-05

**Qualified configuration: `ram` at 128 MiB under QEMU.** All three planned online repeats passed every mandatory test and the native service probe. The unchanged regression limits pass against the explicitly named matched 128 MiB control. This is an emulation qualification, not physical board verification.

## Measured outcome

| Metric | Qualified image | Scope |
|---|---:|---|
| Kernel plus complete disk | 25.8799 MiB | Includes 16 MiB state partition, MBR/alignment, read-only root and kernel |
| Reduction versus original image | 90.14% | Original 262.35 MiB image preserved |
| Combined idle Tor RSS | 55.22 MiB | Two target Tor processes, median of three |
| Sampled peak guest memory used | 85.57 MiB | Includes both Tor processes and external laboratory Python |
| Native single-daemon RSS | 25.70 MiB | Additional observation immediately after bootstrap; not minimum total RAM |
| Bootstrap / onion connection | 17.73 / 3.66 s | Median; public paths remain variable |
| Framed bidirectional throughput | 247.16 KiB/s | Original fixed integrity workload, unchanged timing definition |
| Mean whole-run Tor CPU | 39.10% | Percent of one guest CPU; includes network waiting |

The original 25.89 MiB boot set **already included** the 16 MiB persistent-state partition. The qualified successor also includes it. `image-layout.json` decodes the actual MBR instead of relying only on configuration. A 64 MiB flash budget has room for this boot set, but board boot firmware/bootloader, flash endurance and long-term state growth have not been qualified.

## All five existing configurations

[Three complete rounds for all five configurations, with storage/RAM/CPU/latency/throughput ranges](original-five/README.md). Every planned attempt is retained, including the blocked production run. Missing runtime measurements remain null; that group is INCOMPLETE.

| First observed failure in the original history | Observation | Interpretation |
|---|---|---|
| Data/helper removal | Onion latency first exceeded 20% | Crypto libraries were byte-identical to baseline; not evidence of a crypto-code regression |
| `-Os`, smaller kernel and client-only build | Throughput first exceeded 20%; latency also failed | Several variables changed together; subsequent ablations separate what can be established |
| Remove Python/test files, 32 MiB ext4 | One onion timeout; repeat functional PASS with slow public rate | Failed attempt retained, not silently replaced |
| Gzip SquashFS and trimmed libraries | Bootstrap and onion outliers, lower rate | Same Tor/crypto binaries as preceding tiny/production builds; cold I/O measured separately |

## Exact limits and noise

The original external-fixture reference allowed bootstrap ≤25.833 s, onion ≤2.309 s, and throughput ≥225.154 KiB/s. The first small/tiny comparisons used the earlier original baseline, whose corresponding limits were 25.106 s, 2.740 s and 190.490 KiB/s.

No percentages, required metrics or mandatory tests in `config/policy.json` were relaxed. The original image was rerun in predetermined interleaved rounds; medians use all planned samples, and missing/blocked mandatory results prevent aggregate acceptance. The unchanged baseline itself produced a 104-second onion outlier and large rate changes, demonstrating why historical public-network point measurements cannot identify a software cause. Three repeats remain descriptive evidence with wide ranges, not a tight confidence interval or per-boot SLA.

A pilot with unavailable process-I/O counters incorrectly discarded CPU counters and is excluded from claims; its raw logs remain preserved. The study runner initially carried old runtime values into a blocked new run. Those records were corrected from the authoritative target output, the original reporting files were retained beside them, and a regression test now requires fresh runtime metrics or null. No blocked attempt was deleted or promoted to PASS.

The later fast candidate still failed a fresh contemporaneous three-pair comparison; see [the complete rejected recovery experiment](recovery-paired/README.md). The investigation did not stop or change limits at a favorable sample.

WSL reported an 8 KiB difference in host physical capacity during the balanced build. The comparator now preserves raw values and tolerates at most 1 MiB of **host** page-reservation noise; guest RAM/CPU/emulator/protocol remain exact. Tests reject a 128-versus-512 MiB comparison and larger host changes. This fixes a NON_COMPARABLE artifact and does not change performance limits.

## Exact historical observations and gates

| Configuration | Bootstrap observed / maximum s | Onion observed / maximum s | Throughput observed / minimum KiB/s |
|---|---:|---:|---:|
| data-only | 14.920 / 25.106 | 3.197 / 2.740 | 329.373 / 190.490 |
| tiny | 15.796 / 25.106 | 3.813 / 2.740 | 186.316 / 190.490 |
| production | 18.422 / 25.833 | 7.588 / 2.309 | 178.510 / 225.154 |
| compressed | 83.439 / 25.833 | 161.348 / 2.309 | 159.664 / 225.154 |

## CPU, cryptography and I/O

The unchanged upstream Tor operation benchmark was run with three already-built binaries under both runtime-library/kernel combinations. Client `-Os` POLYVAL512 cost 13,715 / 13,658 ns, compared with client `-O2` 8,890 / 9,473 ns: a measured 30–35% CPU-time recovery. The `-O2` POLYVAL machine-code section was byte-identical across the baseline, data-only and Tor-only-O2 builds; the `-Os` section differed. This establishes an operation-level compiler effect. It does not prove that every live circuit negotiated that operation or that it explains the entire network-rate difference.

OpenSSL bulk AES/SHA256/X25519 showed no large slowdown. Cell-sized AES differences were much smaller than the public-rate variation. Whole-run CPU averages change when a run waits on descriptors; per-phase CPU also includes concurrent circuit/directory work and varied in the unchanged baseline. Process I/O accounting was unavailable in these kernels and is explicitly marked unavailable. VM reclaim/swap/OOM counters and major faults were recorded; 128 MiB trials can reclaim cache without OOM.

Direct I/O diagnostics measured baseline cold Tor/libcrypto reads at about 6/7 ms and gzip SquashFS at 47/83 ms. Warm reads caused no primary-disk block reads. A 1 MiB state-file write/fsync took 7/24 ms in disposable snapshots. These are emulated observations, not real flash IOPS. The online test data directories use tmpfs, and the measured cold-read overhead cannot explain minute-scale onion delays. [Raw operation and I/O observations](operation-summary.json) accompany the full source-derived outputs.

## Recovery and device-budget qualification

Tor-only `-O2` was built and tested; its final stripped executable was 3.03 MB (an intermediate 3.49 MB install size was corrected before reporting final metrics). Restoring userland `-O2` plus section GC reduced Tor to 2.43 MB, but end-to-end throughput still failed the paired controls. The balanced image restored kernel SMP/`-O2` with two possible CPUs and one emulated vCPU, kept driver trimming, and offset code growth with XZ and fewer unused applets. Its complete boot set was 4 KiB smaller than the earlier 25.89 MiB candidate. All original rejection records remain intact.

[Matched 128 MiB results](device128/README.md) use the already accepted data-only image as the explicitly named control. The original two-process baseline RSS alone exceeds 128 MiB, so it is not presented as a working 128 MiB control. No 128-versus-512 MiB runtime comparison is accepted. The original baseline remains immutable and supplies the storage/history lineage.

For a same-512-MiB footprint observation, balanced idle RSS was 57.08% lower than the contemporaneous original-image reference; sampled peak guest used memory was 43.29% lower. Those figures do not establish a minimum physical RAM requirement.

## Further RAM/storage iteration

After 128 MiB performance qualification, the next complete build directly decompressed SquashFS into the page cache, reduced fragment buffers from three to one, and removed unused BusyBox archive codecs while retaining kernel XZ and the gzip/base64 validation bootstrap. Its repeated comparison is **ACCEPTED**: all mandatory tests passed under equivalent conditions; measured thresholds satisfied. [Complete measured iteration](ram-iteration/README.md). The previously qualified balanced image was preserved, and rejected changes were not reverted.

## Original-image performance lineage at 512 MiB

The balanced candidate was also measured in three further planned 512 MiB runs against the original image's frozen contemporaneous three-run reference: **REJECTED**. throughput_bytes_per_second exceeds regression limit 20%. [Full results](balanced512/README.md).

Acceptance against the matched data-only control at 128 MiB does not erase any original-image or historical performance rejection. These are explicitly separate qualification scopes; a new device-budget reference is not proof that a rejected original-reference gate has been repaired.

## Reproduction and limits

Builds run in `/home/jozef/embedded-tor` under Ubuntu WSL2, with sanitized native PATH. Selected profiles pin the exported baseline SDK and diagnostic fixture; source hashes, exact compiler/linker flags, resolved configs, artifact hashes and QEMU commands remain in each native run. The read-only baseline and diagnostic disks are laboratory fixtures, not deployed storage. No Python/test harness is installed in production. All required bootstrap/SOCKS/v3-onion/two-node/bidirectional SHA-256/reconnection assertions were executed online; no skipped online check qualifies an image.

The production daemon probe verifies uid/gid905, authenticated loopback control, absence of Python and fixture libraries in the deployed process, and immutable-root/writable-state mounts. ASLR, seccomp support, strong stack protection, compiler CET/unwind settings, PIE, full RELRO and NX stack were retained. No source hashes, TLS/signature authentication, cryptographic implementations or upstream routing rules were replaced.

Scope: x86_64 QEMU, qemu64, single-thread TCG, one vCPU and public NAT. These tests verify the stated emulated resource budget; physical USB/Ethernet devices, minimum hardware RAM, sustained flash wear, power loss and long-term cache growth remain unverified.
