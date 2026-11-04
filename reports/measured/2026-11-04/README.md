# Measured embedded Tor builds — 2026-11-04

All measurements below came from actual Buildroot images booted under Ubuntu WSL2/QEMU. The original baseline is immutable. No physical hardware minimum or causal public-network speed improvement is claimed.

QEMU: pc-i440fx-8.2, qemu64, single-thread TCG, one vCPU, 512 MiB, virtio disk/network/RNG, user NAT. Buildroot 2026.08.1; Linux 6.18.7; Tor 0.4.9.14; musl 1.2.6; OpenSSL 3.6.5; GCC 15.3.0.

## Built and tested iterations

| Image/run | Boot set MiB | Image reduction | A+B idle RSS MiB | RSS reduction | Required tests | Decision |
|---|---:|---:|---:|---:|---:|---|
| qemu-x86_64<br>20261104T162447Z-cd6efe9f | 262.35 | 0.00% | 122.30 | 0.00% | 9/9 | BASELINE |
| qemu-x86_64-small<br>20261104T163515Z-f2a9da00 | 70.35 | 73.19% | 50.47 | 58.73% | 9/9 | REJECTED |
| qemu-x86_64-tiny<br>20261104T165728Z-643dbbe1 | 52.22 | 80.10% | 47.90 | 60.83% | 9/9 | REJECTED |
| baseline-external-fixture-reference<br>20261104T171128Z-fixture-reference-546024 | 262.35 | 0.00% | 121.83 | 0.00% | 9/9 | REFERENCE |
| qemu-x86_64-production<br>20261104T171716Z-86a5a81b | 36.22 | 86.20% | 47.39 | 61.10% | 5/9 | INCOMPLETE |
| qemu-x86_64-production<br>20261104T173131Z-b63cb205 | 36.22 | 86.20% | 47.07 | 61.37% | 9/9 | REJECTED |
| qemu-x86_64-compressed<br>20261104T173443Z-2cd3ee20 | 25.89 | 90.13% | 47.76 | 60.79% | 9/9 | REJECTED |

The first two candidates use the original in-image test apparatus. Production candidates use the exact same immutable baseline test files from a separate read-only disk. The original image was remeasured with that same disk before production testing; its separately sealed reference is used for those RAM/performance comparisons. Both references have identical deployed image bytes. Neither includes the laboratory fixture in deployed storage.

1. **Data-only:** remove unused GeoIP country data and authority/diagnostic executables; 64 MiB ext4. Public client and onion functionality passed; country filtering and relay operation are outside this dedicated endpoint profile.
2. **Tiny:** `-Os`, supported upstream `--disable-module-relay --disable-module-dirauth`, uniprocessor kernel with unused audio/display/USB-host/storage drivers omitted; 48 MiB ext4.
3. **Production:** remove Python and test files; disable cron; install native `tor` uid/gid 905 service; 32 MiB ext4. First onion attempt timed out; unchanged diagnostic repeat passed all required tests and the native service probe. Both results are retained.
4. **Compressed production:** gzip SquashFS root, separate 16 MiB ext4 Tor-state partition, omit unused split libevent libraries and operator helpers. Complete image size includes the MBR, alignment, both partitions and kernel.

The kernel retains ASLR, strong stack protection, seccomp/filtering, TCP/IP, virtio entropy and required filesystems. Tor retains its cryptography, TLS and v3 onion support. ELF checks observed PIE, full RELRO, BIND_NOW and a non-executable stack.

## Observed performance

| Run | Bootstrap s | Initial onion s | Payload KiB/s | CPU % | Peak A+B RSS MiB | Peak guest used MiB |
|---|---:|---:|---:|---:|---:|---:|
| 20261104T162447Z-cd6efe9f | 20.92 | 2.28 | 238.11 | 16.88 | 124.00 | 152.68 |
| 20261104T163515Z-f2a9da00 | 14.92 | 3.20 | 329.37 | 15.07 | 53.84 | 81.34 |
| 20261104T165728Z-643dbbe1 | 15.80 | 3.81 | 186.32 | 39.22 | 51.21 | 80.27 |
| 20261104T171128Z-fixture-reference-546024 | 21.53 | 1.92 | 281.44 | 54.38 | 125.43 | 154.49 |
| 20261104T171716Z-86a5a81b | 18.81 | unavailable | unavailable | 13.18 | 48.60 | 78.56 |
| 20261104T173131Z-b63cb205 | 18.42 | 7.59 | 178.51 | 63.65 | 58.12 | 81.80 |
| 20261104T173443Z-2cd3ee20 | 83.44 | 161.35 | 159.66 | 15.49 | 53.52 | 84.16 |

RSS and PSS cover both test Tor processes; peak values are sampled, not guaranteed instantaneous maxima. CPU is percent of one guest CPU averaged over the whole test interval. Descriptor/reconnection waiting changes that denominator substantially; CPU percentage changes are not evidence of improved computational efficiency. Public Tor circuits and descriptor availability vary. No confidence interval or minimum device RAM has been established. The 512 MiB setting was held constant for comparisons.

The nine required checks cover security configuration, configuration verification, startup, SOCKS5, public bootstrap, reachable v3 onion service, two-node exchange, bidirectional SHA-256 payload integrity (including binary and 1 MiB payloads), and recovery of the same onion identity after service crash/restart. Production adds a native service bootstrap probe that verifies uid/gid, deployed libraries, absence of Python/test files, and read-only-root/state mounts where applicable.

## Comparison decisions

- `20261104T162447Z-cd6efe9f`: **BASELINE** — first fully validated build sealed; no optimization claim.
- `20261104T163515Z-f2a9da00`: **REJECTED** — onion_connection_seconds exceeds regression limit 20%.
- `20261104T165728Z-643dbbe1`: **REJECTED** — onion_connection_seconds exceeds regression limit 20%; throughput_bytes_per_second exceeds regression limit 20%.
- `20261104T171128Z-fixture-reference-546024`: **REFERENCE** — Original baseline remeasured with the exact same external test disk used for production candidates; original baseline remains immutable.
- `20261104T171716Z-86a5a81b`: **INCOMPLETE** — mandatory test not passed: onion_v3; mandatory test not passed: two_node; mandatory test not passed: bidirectional_integrity; mandatory test not passed: reconnection; missing/invalid measurement: onion_connection_seconds; missing/invalid measurement: throughput_bytes_per_second; functional regressions: onion_v3, two_node, bidirectional_integrity, reconnection.
- `20261104T173131Z-b63cb205`: **REJECTED** — onion_connection_seconds exceeds regression limit 20%; throughput_bytes_per_second exceeds regression limit 20%.
- `20261104T173443Z-2cd3ee20`: **REJECTED** — bootstrap_seconds exceeds regression limit 20%; onion_connection_seconds exceeds regression limit 20%; throughput_bytes_per_second exceeds regression limit 20%.

The final compressed candidate was built, booted and functionally tested. Its boot set is 25.89 MiB, 90.13% below the preserved baseline; combined idle RSS is 60.79% lower. The decision above remains authoritative; functional success alone does not override a rejected performance gate.

The separate native daemon probe observed one production Tor process immediately after public bootstrap: RSS 21.62 MiB, PSS 21.15 MiB, kernel VmHWM 22.00 MiB. This is an additional single-process observation, not the two-node idle measurement used for baseline percentages or a proven minimum system RAM requirement. Its state files occupied 5.38 MiB; the state filesystem reported 7.20 MiB available after bootstrap.

## Reproduce in the current Ubuntu environment

```sh
cd /home/jozef/embedded-tor
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
./scripts/validate.sh --profile qemu-x86_64-compressed \
  --baseline-ref reports/references/external-fixture/baseline \
  --change "Describe the next measured change"
python3 scripts/compare.py reports/references/external-fixture/baseline \
  reports/benchmarks/<new-run-id>
```

The SDK and reference prerequisites are described in `docs/OPTIMIZATION.md`. Exit status remains nonzero for blocked online tests, missing measurements, incomparable conditions or rejected performance. No gate was relaxed.

## Preserved evidence

- Original baseline: `reports/baseline/`, `build/baseline/`, Git tag `baseline`.
- Additional immutable measurement reference: `reports/references/external-fixture/baseline/`.
- Bootable iteration copies: `build/iterations/01-data/`, `02-tiny/`, `03-production/`, `04-compressed/`.
- Full native build outputs and failure logs: `build/embedded/` and `reports/benchmarks/<run-id>/`.
- Versioned JSON beside this report contains actual metrics, test results, artifact hashes, compiler flags, source-provenance digest and Git/configuration identifiers. CSV: `measurements.csv`. Full source archives/provenance remain in each native benchmark directory.
- `remeasure-reference.py` is the exact laboratory calibration command used, preserved as an experiment record. It refuses an existing reference and does not replace the original baseline.
