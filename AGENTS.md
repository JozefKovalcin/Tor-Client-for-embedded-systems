# Permanent project instructions

Keep this repository Tor-only. Nym lives in
`JozefKovalcin/Nym-MixNet-Client-for-embedded-systems`.

- Start with [HANDOFF](docs/HANDOFF.md), [PROGRESS](docs/PROGRESS.md)
  and [ROADMAP](docs/ROADMAP.md). The next milestone is a physical board
  boot with serial, Ethernet, valid time and public Tor bootstrap.
- Preserve public bootstrap, SOCKS5, Onion Service v3, bidirectional TCP,
  payload integrity and reconnection. Keep private identities and writable
  state on the endpoint. Preserve upstream routing, crypto, TLS, entropy,
  authenticated control, safe logging and source hash/signature checks.
- Use `br2-external/` for target configuration, `config/` for profiles,
  locks and policy, `scripts/` and `tests/` for stdlib tooling, and
  `reports/` for evidence. Keep generated firmware, SDKs and upstream
  build trees in ignored locations. Never commit secrets or Tor private state.
- Use a native Linux path without spaces. Run `python3 scripts/check.py`,
  `python3 scripts/check_buildroot.py` and `./scripts/validate.sh`
  as appropriate. Isolate incompatible toolchain changes in fresh
  input-hash outputs. Preserve the `baseline` tag, sealed artifacts,
  failed logs, uncommitted work and large build outputs.
- Use equivalent workloads and environments for gates. Distinguish
  executed PASS, FAIL, SKIPPED and BLOCKED. Skipped mandatory online tests
  preclude full validation. Keep missing metrics null. Preserve thresholds
  and historical rejections. Update `docs/PROGRESS.md` with evidence.
- Treat x86 and ARM QEMU evidence as simulated. Reserve physical
  qualification for results from the actual board and storage layout.
  Keep source provenance and licensing records for dependency changes.
