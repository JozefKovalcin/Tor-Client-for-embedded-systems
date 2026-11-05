# Repeated embedded Tor performance study

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 3/3 | 262.35 [262.35, 262.35] | 128.29 [128.10, 128.65] | 49.05 [19.51, 54.13] | 22.53 [22.06, 22.73] | 3.96 [2.52, 17.25] | 255.82 [173.11, 433.95] | REFERENCE |
| fast | 3/3 | 26.16 [26.16, 26.16] | 55.29 [55.19, 55.45] | 15.36 [13.88, 32.93] | 15.42 [15.41, 20.90] | 3.89 [3.63, 5.81] | 133.31 [120.51, 260.16] | REJECTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/512 MiB RAM/NAT/RNG, baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| baseline | 6.28 | 13.19 | 2.15 | 40.85 | 106.32 | 4024.65 |
| fast | 0.30 | 12.24 | 2.94 | 40.06 | 105.06 | 4182.86 |

**fast: REJECTED.** throughput_bytes_per_second exceeds regression limit 20%.
