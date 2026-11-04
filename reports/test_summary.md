# Optimization validation

Run: `20261104T153211Z-4ef44872`

**INCOMPLETE** — First Ubuntu WSL2 complete Linux baseline

Build: FAIL. CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2.

Commit: `5cd931b1ac8a25869df120ff32a341eb7e5414bd`; dirty: `False`.

Hardware behavior: unverified. Measurements describe only the recorded test environment.

## Test results

| Test | Mode | Status | Detail |
|---|---|---|---|
| repository_checks | offline | PASS | see checks.log |
| security_config | offline | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| tor_config | offline | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| tor_startup | offline | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| socks5 | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| public_bootstrap | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| onion_v3 | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| two_node | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| bidirectional_integrity | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
| reconnection | online | BLOCKED | CalledProcessError: Command '['make', '--no-print-directory', '-C', '/home/jozef/embedded-tor/build/sources/34fb7bea8a213ffd/buildroot-2026.08.1', 'O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973', 'BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external', 'BR2_DL_DIR=/home/jozef/embedded-tor/downloads/packages', '-j8', 'all']' returned non-zero exit status 2. |
