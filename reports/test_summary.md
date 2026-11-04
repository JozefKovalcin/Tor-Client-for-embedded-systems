# Optimization validation

Run: `20261104T161505Z-bed18230`

**INCOMPLETE** — Baseline retry: guest JSON framing fixed; all network tests required

Build: PASS. baseline refused: mandatory test not passed: reconnection

Commit: `6e0cf4709995b4feaa5778d16ad3321c66ea59c4`; dirty: `False`.

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
| reconnection | online | BLOCKED | reconnection after service crash timed out |
