# Optimization validation

Run: `20261104T173443Z-2cd3ee20`

**REJECTED** — Iteration 4: gzip SquashFS read-only production root, 16 MiB ext4 state, omit unused split libevent libraries

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `f33d5a3f345ebac146cf262e0c0c3c8043f3e4b9`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| elf_hardening | offline | PASS | PIE, RELRO, NOW and non-executable stack observed |
| security_config | offline | PASS | explicit assertions passed |
| tor_config | offline | PASS | explicit assertions passed |
| tor_startup | offline | PASS | explicit assertions passed |
| socks5 | online | PASS | explicit assertions passed |
| public_bootstrap | online | PASS | explicit assertions passed |
| onion_v3 | online | PASS | explicit assertions passed |
| two_node | online | PASS | explicit assertions passed |
| bidirectional_integrity | online | PASS | explicit assertions passed |
| reconnection | online | PASS | explicit assertions passed |
| production_service | online | PASS | native daemon bootstrapped as uid 905; no fixture libraries or Python in deployed root |
