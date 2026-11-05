# Repeated embedded Tor performance study

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| ram | 3/3 | 25.88 [25.88, 25.88] | 55.22 [55.13, 55.58] | 39.10 [14.34, 42.23] | 17.73 [16.37, 18.03] | 3.66 [3.41, 4.67] | 247.16 [180.57, 270.81] | ACCEPTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| ram | 0.34 | 13.95 | 2.43 | 38.42 | 95.68 | 3782.86 |

**ram: ACCEPTED.** all mandatory tests passed under equivalent conditions; measured thresholds satisfied.
