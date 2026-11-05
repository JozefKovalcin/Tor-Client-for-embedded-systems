# Repeated embedded Tor performance study

Study control labeled baseline: `qemu-x86_64-ram`, source build `20261105T101727Z-3c2822d0`. The label does not replace the original project baseline.

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. 9 planned repeats per image; ranges are descriptive. See a separately reported confidence interval where available.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 9/9 | 25.88 [25.88, 25.88] | 55.25 [55.17, 55.45] | 14.41 [11.76, 47.97] | 15.65 [13.96, 17.13] | 3.46 [2.46, 4.80] | 191.97 [70.97, 446.71] | REFERENCE |
| selective | 9/9 | 24.65 [24.65, 24.65] | 55.17 [55.04, 55.48] | 15.47 [11.19, 45.95] | 15.45 [13.88, 17.19] | 4.61 [2.63, 23.54] | 226.97 [24.67, 361.78] | REJECTED |
| slim | 9/9 | 24.74 [24.74, 24.74] | 55.56 [55.34, 55.79] | 15.19 [13.03, 40.82] | 16.33 [14.15, 23.45] | 3.52 [2.68, 8.07] | 203.21 [125.64, 298.39] | REJECTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| baseline | 0.30 | 12.89 | 2.34 | 38.66 | 98.73 | 3783.82 |
| selective | 0.30 | 13.10 | 2.22 | 38.81 | 100.04 | 4065.61 |
| slim | 0.28 | 13.36 | 2.30 | 37.56 | 102.87 | 4415.55 |

**selective: REJECTED.** onion_connection_seconds exceeds regression limit 20%.

**slim: REJECTED.** rootfs_bytes exceeds regression limit 0%; tor_executable_bytes exceeds regression limit 0%.
