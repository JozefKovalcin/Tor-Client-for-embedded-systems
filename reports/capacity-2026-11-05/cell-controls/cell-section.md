## Controlled Tor cell-operation timings

12/12 planned benchmark processes completed with the expected mapped OpenSSL verified. Each unchanged Tor benchmark binary ran against the pinned `-O2` and `-Os` OpenSSL 3.6.5 builds in one fixed 128 MiB QEMU VM. The selected diagnostic process alone loaded the test DSO; the production image and services remained untouched. Two reversed blocks control order. Each cell timing covers 1,048,576 operations, but there are only two process starts per combination. Values are descriptive medians; these are cell operations, not end-to-end local or public throughput.

| Tor benchmark binary | OpenSSL build | Tor1 outbound ns/cell | CGO outbound ns/cell | POLYVAL 512-byte add ns |
|---|---|---:|---:|---:|
| ram | O2 | 13950.3 (n=2) | 20634.3 (n=2) | 6403.8 (n=2) |
| ram | Os | 13633.5 (n=2) | 30359.4 (n=2) | 6399.6 (n=2) |
| selective | O2 | 13718.6 (n=2) | 20036.3 (n=2) | 6068.7 (n=2) |
| selective | Os | 13261.8 (n=2) | 22017.8 (n=2) | 6160.2 (n=2) |
| slim | O2 | 12828.8 (n=2) | 22578.2 (n=2) | 7183.2 (n=2) |
| slim | Os | 13404.2 (n=2) | 23589.3 (n=2) | 8019.7 (n=2) |

Raw [operations and mapped DSOs](cell-controls/plan.json) and [timing summary](cell-controls/summary.json) are retained. The protocol keeps public routing and cryptographic implementations unchanged. These diagnostics do not replace any mandatory online assertion.
