# Permanent project instructions

This repository contains the Tor project only. Keep Nym implementation in the
separate `JozefKovalcin/Nym-MixNet-Client-for-embedded-systems` repository.

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

Current state on 2026-11-04: the original Linux baseline is fully validated under Ubuntu WSL2/QEMU and sealed with tag `baseline`. Smaller data/tiny/production/compressed candidates have actual build and test evidence. Consult `reports/measured/2026-11-04/README.md` and `docs/PROGRESS.md` for measured outcomes and acceptance decisions. Do not confuse functional PASS with automatic optimization acceptance or physical hardware verification.

Performance work on 2026-11-05: consult `reports/performance-2026-11-05/README.md`.
Repeated 128 MiB device-budget qualifications and original-image 512 MiB gates
have separate named references. Never equate a PASS against the data-only
128 MiB control with repairing a rejected original-reference performance gate.
Keep all failed attempts and the original `baseline` tag/results immutable.
Current selected 128 MiB configuration: ram, 25.8799 MiB including state. Consult final-selection.json for the original-reference recovery decision and every preservation gate.
The 24.7354 MiB fulltor-slim candidate is ACCEPTED against both original-image 512 MiB references, but its 128 MiB throughput-preservation gate is REJECTED. Do not describe combined device qualification as complete or overwrite that rejection.

Capacity characterization is recorded in `reports/capacity-2026-11-05/README.md`. The 16 MiB state volumes logged transient write failures. The built `capacity32` profile has 32 MiB state and an 8.6768 MiB non-state footprint. Use the report's separate functional, RAM-margin, and throughput outcomes. At 112 and 128 MiB the stressed workload passed functionality and RAM margins. Both groups fell below the local TCP floor. Only the 256 MiB group cleared the complete QEMU screen. Physical board qualification requires measurements on the actual target.
