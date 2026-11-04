# Optimization validation

Run: `20261104T155711Z-ad98aa0a`

**INCOMPLETE** — Complete Linux baseline: corrected compiler metadata and QEMU tests

Build: PASS. target produced no complete test result; see QEMU log

Commit: `95cc6f54e7c513a81e657528a45e705031ed2198`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| elf_hardening | offline | PASS | PIE, RELRO, NOW and non-executable stack observed |
| security_config | offline | BLOCKED | target produced no complete test result; see QEMU log |
| tor_config | offline | BLOCKED | target produced no complete test result; see QEMU log |
| tor_startup | offline | BLOCKED | target produced no complete test result; see QEMU log |
| socks5 | online | BLOCKED | target produced no complete test result; see QEMU log |
| public_bootstrap | online | BLOCKED | target produced no complete test result; see QEMU log |
| onion_v3 | online | BLOCKED | target produced no complete test result; see QEMU log |
| two_node | online | BLOCKED | target produced no complete test result; see QEMU log |
| bidirectional_integrity | online | BLOCKED | target produced no complete test result; see QEMU log |
| reconnection | online | BLOCKED | target produced no complete test result; see QEMU log |
