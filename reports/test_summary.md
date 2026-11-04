# Optimization validation

Run: `20261104T153343Z-b65345cc`

**INCOMPLETE** — First Ubuntu WSL2 Linux baseline with native PATH

Build: FAIL. ValueError: cannot identify unique target compiler

Commit: `60254c8a7b08fd06e9682180213ecfc935acc694`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| security_config | offline | BLOCKED | ValueError: cannot identify unique target compiler |
| tor_config | offline | BLOCKED | ValueError: cannot identify unique target compiler |
| tor_startup | offline | BLOCKED | ValueError: cannot identify unique target compiler |
| socks5 | online | BLOCKED | ValueError: cannot identify unique target compiler |
| public_bootstrap | online | BLOCKED | ValueError: cannot identify unique target compiler |
| onion_v3 | online | BLOCKED | ValueError: cannot identify unique target compiler |
| two_node | online | BLOCKED | ValueError: cannot identify unique target compiler |
| bidirectional_integrity | online | BLOCKED | ValueError: cannot identify unique target compiler |
| reconnection | online | BLOCKED | ValueError: cannot identify unique target compiler |
