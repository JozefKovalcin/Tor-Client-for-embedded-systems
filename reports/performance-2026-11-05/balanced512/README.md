# Repeated embedded Tor performance study

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| balanced512 | 3/3 | 25.88 [25.88, 25.88] | 55.20 [55.19, 55.30] | 36.17 [33.91, 49.99] | 16.58 [15.69, 16.71] | 4.04 [1.91, 4.07] | 126.32 [97.53, 477.95] | REJECTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| balanced512 | 0.30 | 13.66 | 2.83 | 39.56 | 99.73 | 3942.47 |

**balanced512: REJECTED.** throughput_bytes_per_second exceeds regression limit 20%.
