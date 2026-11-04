# Optimization validation

Run: `20261104T151444Z-a17869fa`

**INCOMPLETE** — Committed implementation: target image requires Linux build runner

Build: BLOCKED. Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner

Commit: `17b6179edcbc8730cc7f0e048a970a9c19625ad9`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| security_config | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| tor_config | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| tor_startup | offline | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| socks5 | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| public_bootstrap | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| onion_v3 | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| two_node | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| bidirectional_integrity | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
| reconnection | online | BLOCKED | Buildroot requires a Linux host; use native Linux, WSL2 Linux or a local Linux runner |
