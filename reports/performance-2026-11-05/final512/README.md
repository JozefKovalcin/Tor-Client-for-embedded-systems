# Repeated embedded Tor performance study

Study control labeled baseline: `qemu-x86_64`, source build `20261104T162447Z-cd6efe9f`. The label does not replace the original project baseline.

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 3/3 | 262.35 [262.35, 262.35] | 128.34 [128.18, 128.43] | 19.11 [16.77, 53.15] | 21.52 [21.49, 22.59] | 3.34 [2.74, 10.53] | 263.57 [83.83, 391.24] | REFERENCE |
| fulltor | 3/3 | 24.99 [24.99, 24.99] | 56.90 [56.89, 57.08] | 14.04 [3.84, 14.77] | 16.22 [14.21, 16.50] | 6.30 [3.90, 166.10] | 226.42 [135.72, 317.12]; 2/3 available | INCOMPLETE |
| fulltor-slim | 3/3 | 24.74 [24.74, 24.74] | 55.33 [55.05, 55.42] | 40.17 [14.59, 40.30] | 15.32 [14.40, 16.88] | 3.88 [3.61, 5.71] | 240.11 [230.70, 265.62] | ACCEPTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/RAM/NAT/RNG (RAM is recorded in each result), baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| baseline | 6.56 | 12.37 | 2.42 | 38.80 | 100.89 | 3957.50 |
| fulltor | 0.31 | 13.11 | 2.04 | 37.57 | 95.03 | 3974.26 |
| fulltor-slim | 0.33 | 12.50 | 2.17 | 36.89 | 99.97 | 4374.11 |

**fulltor: INCOMPLETE.** failed test: bidirectional_integrity; failed test: reconnection; failed test: two_node; mandatory test not passed: two_node; mandatory test not passed: bidirectional_integrity; mandatory test not passed: reconnection; missing/invalid measurement: throughput_bytes_per_second; functional regressions: bidirectional_integrity, reconnection, two_node; onion_connection_seconds exceeds regression limit 20%.

**fulltor-slim: ACCEPTED.** all mandatory tests passed under equivalent conditions; measured thresholds satisfied.
