# Real host Tor integration evidence

Executed on 2026-11-04 in Docker WSL using a signed Alpine distribution package,
**Tor 0.4.9.11**, libevent 2.1.12, OpenSSL 3.3.7, GCC 13.2.1.
This is a host harness smoke test, not the pinned Buildroot Tor 0.4.9.14 image.
No optimization or target hardware baseline is accepted by this result.

| Test | Status | Seconds |
|---|---|---:|
| security_config | PASS | 0.010 |
| tor_config | PASS | 0.349 |
| tor_startup | PASS | 0.289 |
| socks5 | PASS | 0.002 |
| public_bootstrap | PASS | 9.225 |
| onion_v3 | PASS | 2.434 |
| two_node | PASS | 0.718 |
| bidirectional_integrity | PASS | 6.411 |
| reconnection | PASS | 18.591 |

Both endpoints used independent ephemeral identities. A reached B through the public
Tor network and a v3 onion hostname, verified server-push and echoed payloads with
SHA-256, then successfully reconnected after B was forcibly stopped and restarted.

Observed resource values (single run; both Tor processes combined):

| Metric | Observed value |
|---|---:|
| startup_seconds | 0.28726388299998007 |
| bootstrap_seconds | 8.513993948000007 |
| idle_rss_bytes | 49208115.2 |
| idle_pss_bytes | 39480524.8 |
| onion_connection_seconds | 2.4311122400000045 |
| throughput_bytes_per_second | 397135.91290725407 |
| peak_process_rss_bytes | 50614272 |
| peak_system_used_bytes | 663371776 |
| cpu_percent | 9.598869416626215 |
| persistent_storage_bytes | 11566373 |

Idle window: 2 seconds; sampling interval: 200 ms. These settings differ from the
10-second target profile and must not be compared with it. RSS/PSS are process
observations in WSL; system memory includes the host WSL environment. Public Tor
timing and throughput describe only this one run, not a guaranteed device minimum.

Detailed sanitized JSON and CSV: `reports/benchmarks/20261104-host-smoke/`.
Private service keys and control cookies remain only in ignored `runtime/` directories.
