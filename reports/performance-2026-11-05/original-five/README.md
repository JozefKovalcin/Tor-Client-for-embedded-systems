# Repeated embedded Tor performance study

Fixed interleaved order; all attempts retained. Values are median [min, max]. The original baseline and 20% thresholds are unchanged. Medians are a separate matched measurement reference, not edits to historical measurements. Any missing planned result, mandatory BLOCKED/FAIL, or unavailable required metric prevents aggregate acceptance. Three repeats are descriptive evidence, not a tight statistical confidence bound.

| Image | Runs | Boot MiB | Idle A+B RSS MiB | CPU % | Bootstrap s | Onion s | Throughput KiB/s | Median gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 3/3 | 262.35 [262.35, 262.35] | 127.35 [127.20, 127.57] | 20.26 [17.18, 51.14] | 22.55 [20.68, 23.76] | 3.04 [2.54, 104.39] | 327.22 [288.38, 349.16] | REFERENCE |
| compressed | 3/3 | 25.89 [25.89, 25.89] | 52.84 [52.73, 52.92] | 42.28 [14.52, 46.50] | 15.09 [13.87, 18.00] | 3.32 [2.90, 3.66] | 255.15 [191.36, 332.97] | REJECTED |
| production | 3/3 | 36.22 [36.22, 36.22] | 52.92 [52.37, 52.97] | 20.36 [8.46, 48.90] | 17.14 [16.10, 17.17] | 6.43 [4.43, 8.43] | 190.19 [107.33, 273.05] | INCOMPLETE |
| small | 3/3 | 70.35 [70.35, 70.35] | 55.87 [55.87, 56.12] | 39.82 [14.41, 42.95] | 13.18 [12.68, 17.20] | 2.47 [2.38, 3.30] | 300.72 [195.93, 484.67] | ACCEPTED |
| tiny | 3/3 | 52.22 [52.22, 52.22] | 52.81 [52.70, 52.83] | 19.75 [13.43, 44.64] | 16.08 [15.62, 16.29] | 3.87 [2.86, 86.16] | 279.75 [247.89, 290.15] | REJECTED |

CPU percentage covers the whole observation, including public-network waiting; phase CPU seconds below isolate active work more closely. All images use identical QEMU CPU/machine/512 MiB RAM/NAT/RNG, baseline functional suite, and read-only diagnostic disk. Public relays/circuits are not held identical. No host builds run during the series.

| Image | Startup CPU s | Bootstrap CPU s | Integrity CPU s | AES CTR MiB/s | SHA256 MiB/s | X25519 ops/s |
|---|---:|---:|---:|---:|---:|---:|
| baseline | 6.52 | 12.20 | 1.85 | 38.41 | 100.89 | 4170.56 |
| compressed | 0.27 | 11.72 | 2.59 | 39.05 | 105.72 | 4524.16 |
| production | 0.29 | 12.83 | 3.04 | 40.29 | 112.76 | 4444.66 |
| small | 0.27 | 10.24 | 1.94 | 43.38 | 110.09 | 4280.25 |
| tiny | 0.31 | 13.41 | 2.52 | 38.66 | 105.00 | 4737.43 |

**compressed: REJECTED.** throughput_bytes_per_second exceeds regression limit 20%.

**production: INCOMPLETE.** mandatory test not passed: onion_v3; mandatory test not passed: two_node; mandatory test not passed: bidirectional_integrity; mandatory test not passed: reconnection; missing/invalid measurement: onion_connection_seconds; missing/invalid measurement: throughput_bytes_per_second; functional regressions: bidirectional_integrity, onion_v3, reconnection, two_node.

**small: ACCEPTED.** all mandatory tests passed under equivalent conditions; measured thresholds satisfied.

**tiny: REJECTED.** onion_connection_seconds exceeds regression limit 20%.
