# Optimization validation

Run: `20261105T130046Z-719a45c7`

**REJECTED** — Selective Tor/OpenSSL O2 with Os userland and XZ kernel at 128 MiB

Build: PASS. make all succeeded; all required boot artifacts and ELF hardening verified

Commit: `b9d2bdecd79e1c94e7fc953c199c166809131a4f`; dirty: `False`.

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
