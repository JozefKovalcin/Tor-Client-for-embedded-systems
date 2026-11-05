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

## Measurements

Null means unavailable; bytes use base 1024 for KiB conversions.

| Metric | Candidate | Baseline | Delta | Reduction % |
|---|---:|---:|---:|---:|
| bootstrap_seconds | 15.966137452999998 | 16.003011715 | -0.03687426200000132 | 0.2304207648953866 |
| complete_image_bytes | 25936896 | 27137024 | -1200128 | 4.4224746235991095 |
| cpu_percent | 25.386880096684255 | 44.5449750122731 | -19.158094915588844 | 43.00843116493022 |
| idle_pss_bytes | 48969728.0 | 48997833.5319149 | -28105.531914897263 | 0.057360764525620576 |
| idle_rss_bytes | 57907200.0 | 57862971.91489362 | 44228.085106380284 | -0.07643590303559263 |
| image_host_allocated_bytes | 9437184 | 10637312 | -1200128 | 11.282248748556027 |
| installed_rootfs_bytes | 12285504 | 12209160 | 76344 | -0.6253010035088409 |
| kernel_bytes | 3941376 | 5207040 | -1265664 | 24.30678466076696 |
| library_bytes | 8111688 | 9003952 | -892264 | 9.90969298814565 |
| onion_connection_seconds | 3.466445176999997 | 4.761459074000001 | -1.295013897000004 | 27.197837403905066 |
| peak_process_rss_bytes | 60514304 | 59785216 | 729088 | -1.2195121951219512 |
| peak_system_used_bytes | 89653248 | 89694208 | -40960 | 0.045666270892318936 |
| persistent_storage_bytes | 16599369 | 16599379 | -10 | 6.024321753241492e-05 |
| production_peak_rss_bytes | 27279360 | 27508736 | -229376 | 0.8338296605122096 |
| production_pss_bytes | 26338304 | 26541056 | -202752 | 0.7639183610478799 |
| production_rss_bytes | 26832896 | 27021312 | -188416 | 0.6972866454448992 |
| production_state_bytes | 8295368 | 8295368 | 0 | 0.0 |
| production_state_free_bytes | 4890624 | 4890624 | 0 | 0.0 |
| production_state_volume_bytes | 14410752 | 14410752 | 0 | 0.0 |
| rootfs_bytes | 21995520 | 21929984 | 65536 | -0.2988419872992155 |
| startup_seconds | 0.44194007599999985 | 0.4786876239999991 | -0.03674754799999924 | 7.676728237285556 |
| throughput_bytes_per_second | 178191.5529819057 | 313747.1288022694 | -135555.5758203637 | 43.205359786987536 |
| tor_executable_bytes | 3397272 | 2428664 | 968608 | -39.882338602622674 |

## Decision

- rootfs_bytes exceeds regression limit 0%
- throughput_bytes_per_second exceeds regression limit 20%
- tor_executable_bytes exceeds regression limit 0%

Positive reduction means lower resource use. Throughput improvement has the opposite sign.
Public Tor timing varies with network conditions; one run does not establish a causal speedup.
Complete image bytes sum the boot kernel and rootfs artifacts, without double-counting aliases.
Logs and complete provenance are in the run directory. Private runtime state is never exported.
