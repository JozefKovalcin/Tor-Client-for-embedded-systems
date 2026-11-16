# W2 — Dependency and environment checker

Branch: `codex/task01-w2-dependencies`. Documentation base: `67b9d42`.
Integrated W1 base: `3997fa567a2d24758a0266add4c9cf3049e5f7f6`.
W1 exclusively owns all shared contracts.

## Files / behavior

- `scripts/dependencies/{__init__.py,__main__.py,checker.py}`: read-only CLI/API;
  normalized host OS/architecture/distribution/glibc facts, CPU/RAM/disk
  snapshots, fixed tool versions/compiler targets and library metadata probes.
- `config/dependencies/{manifest-v1.json,README.md}`: versioned detection
  policy, ownership and safe invocation. No installer/remediation executor.
- `tests/test_dependencies.py`: injected probe failures, compatibility/family,
  trusted lookup, direct argv, no shell/COMPILE/WSL, report and W1 validation.

Run `python -B -m scripts.dependencies`. JSON stdout; exit 0 only for all
required listed checks AVAILABLE, 1 for non-ready scope, 2 for invalid/report
failure. AVAILABLE/MISSING/INCOMPATIBLE/UNKNOWN/BLOCKED are distinct. No build,
Tor, board, runtime or public-network qualification is claimed by readiness.

No installation, download, compilation, filesystem report write,
COMPILE.cmd or WSL startup. `-B` is required for strict zero-write startup:
CPython can cache package `__init__.py` before the package suppresses subsequent
bytecode generation. W3 reuses `ReadOnlyProbe`, `collect_report` and
`dependency_check`; no shared schema copy is maintained in W2.

## Tests / evidence

Preparation command: `python -B -m unittest discover -s tests -p test_dependencies.py -v`.
Independent preparation: **18 PASS, 6 SKIPPED pending W1** (24 discovered).
After W1 integration, the temporary import/skip fallback was removed.
Final W1-integrated execution: **24 PASS, zero SKIPPED/FAIL/BLOCKED** tests,
including the real `-B` CLI subprocess and no-new-files assertion.
`git diff --check`: PASS.

Actual Windows sandbox CLI: valid JSON, exit 1/non-ready. Ten checks AVAILABLE;
six BLOCKED (user-local Scoop Git is outside trusted roots; five MSYS2 tools
returned Windows process initialization status `0xC0000142`). Known Windows
loader/access failures are BLOCKED, not mistaken for missing tools or PASS.
No toolchain repair or installation was attempted. This is diagnostic evidence,
not a declaration that the host lacks those tools.

An earlier synthetic lookup test failed because a real installed MSYS2 tool
correctly preceded its synthetic PATH file. The fixture now isolates candidate
lookup; production behavior was not weakened. No source pins, existing tests,
Windows patch, Buildroot inputs, historical results or baseline were modified.

## Limits / dependencies

- OS/tool roots are deliberately conservative. User-local installations such
  as Scoop may be BLOCKED rather than absent; future trust-policy expansion
  requires explicit review. OS package authenticity is not independently
  verified by version probing.
- Tool execution deadline is three seconds; output is size-checked after
  in-memory capture, not under a strict streaming memory cap. Raw stdout/stderr
  and environment dumps are never included in reports.
- RAM/disk are snapshots, not reservations or cgroup/WSL guarantees. No Yocto
  RAM threshold or missing physical PICO hardware gates W1–W3.
- Library modversion is development metadata, not runtime/ELF closure. W3
  separately checks its selected Ubuntu 24.04 x86_64/glibc 2.39 ABI, source pins,
  target/output safety and preflight readiness. It must integrate W2 after W1.

## Parent integration verification

- Full `python -B scripts/check.py` outside known sandbox limits: exit 0,
  90 discovered, **88 PASS / 2 platform SKIPPED**. Full log retained in
  ignored `build/task01-checks/W2-portable.txt`.
- Real CLI outside sandbox: exit 1, **15 AVAILABLE / 1 BLOCKED** (Scoop Git
  trust boundary). This is expected non-ready diagnostics, not a test failure.
- Whitespace/scope/preserved-input review PASS. No W4–W15 work.
