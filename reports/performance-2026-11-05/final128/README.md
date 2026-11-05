# Repeated embedded Tor performance study

Study control labeled baseline: `qemu-x86_64-ram`, source build `20261105T101727Z-3c2822d0`. The label does not replace the original project baseline.

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 3/3 | 25.88 [25.88, 25.88] | 55.18 [55.11, 55.42] | 44.54 [35.47, 46.01] | 16.00 [15.81, 16.38] | 4.76 [3.54, 6.15] | 306.39 [161.93, 380.58] | REFERENCE |
| fulltor | 3/3 | 24.99 [24.99, 24.99] | 56.99 [56.90, 56.99] | 45.57 [32.23, 46.39] | 15.48 [14.97, 15.95] | 3.64 [2.54, 6.00] | 336.52 [111.78, 371.55] | REJECTED |
| fulltor-slim | 3/3 | 24.74 [24.74, 24.74] | 55.22 [55.16, 55.30] | 25.39 [20.81, 42.64] | 15.97 [15.33, 16.22] | 3.47 [2.99, 5.41] | 174.02 [95.55, 225.15] | REJECTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| baseline | 0.34 | 12.98 | 2.24 | 36.50 | 92.67 | 4002.55 |
| fulltor | 0.30 | 12.79 | 2.08 | 36.87 | 100.62 | 3827.58 |
| fulltor-slim | 0.33 | 13.02 | 2.49 | 36.13 | 97.85 | 4370.92 |

**fulltor: REJECTED.** rootfs_bytes exceeds regression limit 0%; tor_executable_bytes exceeds regression limit 0%.

**fulltor-slim: REJECTED.** rootfs_bytes exceeds regression limit 0%; throughput_bytes_per_second exceeds regression limit 20%; tor_executable_bytes exceeds regression limit 0%.
