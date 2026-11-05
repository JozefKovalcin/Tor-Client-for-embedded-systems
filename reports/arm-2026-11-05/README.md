# ARMv7 capacity32 build and qualification

The ARMv7-A hard-float capacity32 boot set is **38.6198 MiB**, down
**5.06%** from the x86-64 capacity32 build. Excluding the 32 MiB writable
state partition, it is **6.6198 MiB**, down **23.71%**. The existing Tor
workload passed on the ARM QEMU guest. This is a software and capacity
comparison using ARM Cortex-A7 and x86-64 QEMU machines on one WSL2 host.
The physical platform shortlist is in [ARM_PLATFORM.md](../../docs/ARM_PLATFORM.md).

## Storage

| Artifact | x86-64 capacity32 | ARMv7 capacity32 | ARM reduction |
| --- | ---: | ---: | ---: |
| Stripped Tor executable | 2,428,664 B | 2,207,484 B | 9.11% |
| Shared libraries | 9,003,952 B | 6,232,916 B | 30.78% |
| Boot kernel | 3.7588 MiB | 2.3698 MiB | 36.95% |
| Read-only SquashFS root partition | 3.9180 MiB | 3.2500 MiB | 17.05% |
| MBR and partition alignment | 1.0000 MiB | 1.0000 MiB | 0% |
| Writable Tor state allocation | 32.0000 MiB | 32.0000 MiB | 0% |
| Boot set excluding state | 8.6768 MiB | 6.6198 MiB | 23.71% |
| Complete boot set | 40.6768 MiB | 38.6198 MiB | 5.06% |

The total is kernel plus the logical size of `disk.img`. The latter
already includes state. Tor and shared library rows describe uncompressed
files inside the root image. The disk layout uses 38,010,880 bytes,
including exactly 33,554,432 bytes for state. A 64 MiB usable block device
would have 25.3802 MiB outside the measured boot set. The raw NAND budget
must also include the board's boot firmware, DTB, volume geometry and
reserved eraseblocks. [Storage JSON](storage.json) · [Storage CSV](storage.csv)

## Build and tests

The full ARM Buildroot build is `20261105T183526Z-c95d541a`. It used
GCC 15.3.0, binutils 2.45.1, musl 1.2.6, Linux 6.18.7, Tor 0.4.9.14,
OpenSSL 3.6.5 and the existing BusyBox configuration. It kept the `-O2`
Tor and OpenSSL settings, Tor client-only modules, section collection,
reproducible options, forced upstream hashes and existing ELF hardening.
The ARM kernel selected PL011, PL031 and generic VirtIO PCI in place of
the PC drivers. The first QEMU command placed PCI configuration space
beyond the guest's addressable range. `highmem=off` resolved the mount
failure. PL031 set the guest clock before Tor's TLS connections.

The corrected workflow `20261105T190223Z-7002ec13` reused the compiled
output, rebuilt the image, and observed identical `zImage` and `disk.img`
SHA-256 values. It passed all nine mandatory assertions, ELF PIE, RELRO,
NOW, non-executable stack, and the production service check. The latter
bootstrapped as UID/GID 905 with read-only SquashFS and writable ext4.
The separately built ARM fixture was mounted read-only. Its suite inputs
are byte-identical to those sealed with the `baseline` tag. At 128 MiB,
the frozen offline suite passed its three offline assertions and marked
the six online assertions SKIPPED. A separate frozen online run at 128 MiB
passed all nine assertions and the production service. The default comparison
to the original x86-64 baseline is `NON_COMPARABLE` under the unchanged policy.

[Build result and exact configuration](build-results.json) ·
[Offline 128 MiB](offline-128.json) · [Online 128 MiB](online-128.json) ·
[Test provenance](test-provenance.json) ·
[Build log digests](build-log-digests.json) · [Target ELF](build-tor-elf.txt)

## RAM and CPU

Three sequential cold starts at each of 128 and 256 MiB per architecture
used one vCPU, QEMU 10.2.1 TCG on the same WSL2 host, VirtIO networking,
user-mode NAT and the same host Tor peer. The run order reversed in the
second round. ARM and x86 observer disks contain the same C sources and
compiler options for their respective ABIs. Each run executed the native
one-endpoint suite, three local TCP transfers, small-buffer and bulk
cryptography, then a 32 MiB resident tmpfs reservation and another
bidirectional integrity test. The reservation models queue growth.

All 12 runs passed all nine required functions, the reserved transfer,
the 20% plus 16 MiB RAM margin and the state checks. No swap, direct
reclaim or OOM kills were observed. RSS, PSS, peak system memory and CPU
are medians of the three cold boots. Available RAM and state are the
lowest sampled values across those boots. Peak system memory is
`MemTotal - MemAvailable`.

| QEMU | Idle Tor RSS / PSS MiB | Tor HWM MiB | Peak system before / with reserve MiB | Minimum available RAM MiB | Minimum state free MiB | Tor CPU s / % |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ARM 128 MiB | 22.93 / 22.36 | 25.96 | 29.98 / 59.68 | 58.03 | 10.56 | 31.32 / 11.55 |
| ARM 256 MiB | 22.92 / 22.37 | 26.06 | 32.66 / 62.43 | 182.34 | 10.39 | 31.05 / 22.87 |
| x86 128 MiB | 26.34 / 25.76 | 28.28 | 37.09 / 65.33 | 46.07 | 11.94 | 21.00 / 15.26 |
| x86 256 MiB | 26.23 / 25.63 | 28.23 | 39.16 / 68.69 | 167.96 | 10.84 | 21.60 / 11.45 |

ARM at 128 MiB used **13.2% less idle Tor PSS** and **8.7% less peak
system memory under reservation** than x86 at the same budget. Tor CPU
time was **49.1% higher**. CPU percent divides the sampled process
CPU time by the case duration, which includes public-network waits.
Both architectures stored a median of 7.94 MiB of allocated state
before the reservation workload.

## Performance

| QEMU | Public bootstrap s | Onion latency s | Public transfer KiB/s | Local TCP MiB/s | Reconnection s | Capacity screen |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| ARM 128 MiB | 15.23 | 4.32 | 143.80 | 18.44 | 143.31 | Rejected on public medians |
| ARM 256 MiB | 14.84 | 3.15 | 191.67 | 17.94 | 26.75 | PASS |
| x86 128 MiB | 11.96 | 3.92 | 155.22 | 34.02 | 26.03 | Rejected on local TCP and bootstrap medians |
| x86 256 MiB | 9.29 | 4.01 | 157.70 | 42.54 | 49.06 | PASS |

Performance decisions compare 128 MiB against the 256 MiB group for
the same image and architecture using the existing 20% limits. ARM
128 MiB cleared all eight local gates and the public bootstrap limit.
Its Onion median was **4.32 s** against a **3.78 s** limit and its
public throughput was **143.80 KiB/s** against a **153.33 KiB/s** floor.
Its throughput ranged from **90.80 to 299.40 KiB/s**. The three-round
exploratory paired intervals for Onion and throughput ratios both
crossed their rejection boundaries. Public circuits varied and the
observed RAM counters give no evidence of paging pressure causing
the misses. The recorded screen decision remains rejected. The x86
128 MiB local TCP shortfall was 20.0165% and its bootstrap was 28.78%
longer. No thresholds changed.

The local ARM TCP rate was about 46% lower at 128 MiB. At that budget
the ARM and x86 509-byte AES-CTR medians were 36.88 and 34.19 MiB/s,
SHA3-256 medians were 18.65 and 38.51 MiB/s, and X25519 rates were
1,154 and 3,624 operations/s. These local tests separate the guest
network and crypto workloads from public Tor variability. TCG,
the different guest kernels and ISA-specific library code all affect
the observed rates. Physical SoC speed and Ethernet line rate require
board measurements.

[Comparison CSV](comparison.csv) · [Summary and ranges](native-summary.json) ·
[Per-run tests and counters](native-analysis.json) ·
[Observer provenance](observer-provenance.json) ·
[Raw log digests](native-log-digests.json) · [Reproduction script](reproduce.py)

## Hardware decision

The first hardware prototype should use **DHCOM i.MX6ULL HI00096 with
256 MiB RAM and 128 MiB NAND**. It matches the smallest RAM budget in
this ARM study that passed the full screen and has a documented carrier
with USB OTG and wired Ethernet. The **100ASK T113s3-PRO V1.3 LTS with
128 MiB RAM and the 128 MiB SPI NAND configuration** is the smaller
RAM candidate. Its qualification needs board-specific boot and storage
integration, simultaneous USB device and Ethernet validation, and
repeated public and local tests. [Platform evidence and build guide](../../docs/ARM_PLATFORM.md)
