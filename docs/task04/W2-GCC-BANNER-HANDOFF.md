# W2 correction discovered by actual W6 preflight

Base: main `1120723c806a01f9a923f5c4e489bc8a7ae23798`.
Owner: integration maintainer; separate branch `codex/task04-w2-gcc-banner`.

Actual native Ubuntu 24.04/glibc 2.39 CI observation in W6 run
`44066030954`, job `133979821031`, reported GCC 13.3.0 UNKNOWN at
`/usr/bin/x86_64-linux-gnu-gcc-13`. W2 resolved the system symlink and correctly
parsed the version, but its GNU family expression accepted only unsuffixed
`gcc`/`gcc.exe` banners. A standard version-suffixed executable therefore
could not establish its compiler family.

The correction accepts only an optional numeric release suffix before `.exe`
or the banner separator. Tool allowlists, trusted paths, subprocess arguments,
timeouts, version minima, target checks and every status remain unchanged.
Clang and nonnumeric wrapper/preview names remain incompatible or unknown.
No W3/W6 availability override or new compiler/ABI support is introduced.

Before: the added regression failed five assertions (four standard banners
UNKNOWN; old GCC also UNKNOWN instead of reaching minimum-version rejection).
After: all four variants AVAILABLE at 13.3.0; GCC 11.4 remains INCOMPATIBLE;
`gcc-preview` and `gcc-13-wrapper` remain UNKNOWN. Existing family/security
tests remain intact. Logs retained in ignored `build/task04-checks/`.

Executed on Windows/Python 3.14:

- Focused dependencies: 25 PASS, zero skips.
- Full portable `python -B scripts/check.py`: 117 discovered, 114 PASS,
  three platform SKIPPED, exit 0 (permitted loopback/temp-file context).
- Existing Windows pipeline: 6 PASS.
- `git diff --check`: PASS.

This is a separately reviewed W2 defect correction, not part of the Linux
recipe or a change to shared W1 schemas. W6 must consume the corrected W2
through an ordinary merge and retain authoritative classified decisions.
Its real build remains BLOCKED without exact source cache and explicit
compilation authorization; no package, download, installation or Tor test ran.
