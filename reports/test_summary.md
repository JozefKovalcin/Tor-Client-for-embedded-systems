# Optimization validation

Run: `20261105-device128-fulltor-slim`

**REJECTED** — All mandatory tests passed in three 128 MiB repeats; throughput preservation versus fresh RAM control rejected. Original-image 512 MiB comparison accepted. Preserve the earlier qualified RAM option.

Build: PASS. Aggregate of repeated measurements of the same hash-verified previously built image; no rebuild claimed

Commit: `d4b5c60711507a8bb59a676461fc6f1fcafdfd88`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| bidirectional_integrity | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| onion_v3 | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| production_service | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| public_bootstrap | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| reconnection | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| security_config | offline | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| socks5 | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| tor_config | offline | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| tor_startup | offline | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
| two_node | online | PASS | All planned repeats required; statuses: PASS, PASS, PASS |
