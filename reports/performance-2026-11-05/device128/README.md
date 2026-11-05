# Repeated embedded Tor performance study

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| balanced | 3/3 | 25.88 [25.88, 25.88] | 55.25 [54.94, 55.42] | 36.45 [14.37, 48.07] | 16.24 [15.78, 17.61] | 3.25 [2.64, 3.44] | 227.75 [163.15, 431.39] | ACCEPTED |
| baseline | 3/3 | 70.35 [70.35, 70.35] | 57.09 [56.76, 57.14] | 35.21 [14.14, 37.88] | 16.88 [15.79, 18.40] | 5.84 [5.60, 7.88] | 145.37 [116.96, 452.53] | REFERENCE |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| balanced | 0.32 | 12.89 | 2.40 | 35.19 | 91.34 | 4015.01 |
| baseline | 0.39 | 13.73 | 2.54 | 37.78 | 92.55 | 3819.60 |

**balanced: ACCEPTED.** all mandatory tests passed under equivalent conditions; measured thresholds satisfied.
