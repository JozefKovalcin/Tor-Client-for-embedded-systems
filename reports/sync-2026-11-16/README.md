# Source and documentation synchronization — 2026-11-16

Local `main` and freshly fetched `origin/main` both started at
`bbcf1475230ec6cafb6f57ebc9ca15cdd20f0272` (0 ahead, 0 behind).
The missing tracked content was the native Windows source-build project,
its tests and historical evidence, and the November 12 Windows QEMU smoke
runner/report. These are added together with current documentation.

The original Tor/dependency archives are fetched and hash-checked from
`windows-native/source-manifest.json`; generated build trees and executables
are deliberately excluded from version control. No precompiled executable
is substituted for a source build. The small compilation/test ZIP can be
regenerated with `python windows-native/scripts/create-clean-zip.py`.

## Checks executed for this synchronization

| Check | Result |
| --- | --- |
| `python -m unittest discover -s windows-native/tests -v` | PASS: 6 tests |
| `python scripts/check.py` | PASS: 41 tests; 2 SKIPPED (Windows symlink privilege, Linux shell) |
| Native PowerShell parser, four source scripts | PASS |
| MSYS2 `bash -n`, four build shell scripts | PASS |
| Compact ZIP generation, CRC and single README assertions | PASS: 17 entries |
| JSON/Python syntax; candidate artifact/credential signature scan | PASS |
| Git whitespace check | PASS except preserved upstream patch context and raw compiler flags |
| `python scripts/check_buildroot.py` | BLOCKED: real Kconfig resolution requires Linux |
| New native compilation / public Tor / real two-PC test | SKIPPED for this source/documentation synchronization |
| Full firmware / physical board qualification | SKIPPED; remains open |

The native build and public-network evidence under `windows-native/evidence/`
was executed on November 12, not re-executed on November 16. Earlier failures
remain present. QEMU evidence is simulated and is not physical qualification.
No missing runtime measurements are filled in by this synchronization.

The `baseline` tag remains `da7c5281145589821ceeb4f6b7aabd4d98833587`.
Linux/ARM source/build configuration is unchanged. Ignored firmware, SDKs,
upstream trees, logs and private endpoint identities remain on the local disk.
