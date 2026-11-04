# Permanent project instructions

Build a small maintainable embedded Linux Tor runtime. Required functionality:
public Tor bootstrap, SOCKS5, v3 Onion Services, two-node bidirectional TCP,
payload integrity and reconnection. Preserve upstream routing and cryptography.
Keep Tor state/private identities on the executing device; USB storage alone
cannot implement the intended independent endpoint. Current implementation is
an x86_64 QEMU laboratory, not a verified physical USB/Ethernet product.

Use `br2-external/` for Buildroot, kernel, BusyBox, Tor templates and patches;
`config/` for profiles, dependency locks and validation policy; `scripts/` and
`tests/` for stdlib automation; `reports/` for outcomes; `build/`, `downloads/`
and `runtime/` for ignored generated artifacts. Never commit private Onion
Service keys, cookies, secrets, upstream build trees or binaries.

On Linux without spaces in the path, run `python3 scripts/check.py`,
`python3 scripts/check_buildroot.py` and `./scripts/validate.sh`. The initial
fully tested clean commit can use `--record-baseline` once. Never overwrite or
delete a baseline, uncommitted work or unknown files. Never silently clean a
toolchain after changing architecture/libc/compiler: select a fresh output
directory using the input hash. Do not automatically revert rejected changes.

Do not disable signature/hash checks, TLS authentication, required randomness,
control authentication, safe logging or upstream security protections to reduce
size. Do not substitute cryptographic libraries without documented API/protocol
compatibility, upstream support and the complete regression suite. Maintain
source provenance and licensing information whenever dependencies change.

For each optimization, document the hypothesis/change, run the pipeline, review
storage/RAM/performance deltas and required tests. Compare equivalent hardware
or emulation conditions only. PASS requires executed assertions; unavailable
tests are BLOCKED/SKIPPED. Mandatory online tests may never be skipped for full
validation. Unavailable metrics are null. Preserve failure logs and update
`docs/PROGRESS.md` at each major stage. Never invent baseline data, performance
claims, hardware measurements or successful public-network tests.

Current state on 2026-11-04: original workspace was empty. Automation was
implemented and portable regression checks executed. No initial Linux image,
Tor distribution or physical device existed. Consult `docs/PROGRESS.md` for
current verified checks and blockers before making new claims.
