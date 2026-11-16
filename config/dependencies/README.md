# Read-only dependency policy v1

Run from the repository root:

```sh
python -B -m scripts.dependencies
```

`-B` suppresses even the package's initial CPython import cache write. The
package suppresses subsequent bytecode writes itself, but plain `python -m`
can cache `__init__.py` before that code runs. Diagnostics do not install,
download, compile, launch WSL, call COMPILE.cmd, create reports or reserve space.
JSON goes to stdout. Exit 0 means all required **listed developer checks** are
AVAILABLE; exit 1 means at least one is not; exit 2 means invalid invocation or
report failure. This is not a Tor/runtime/build/board qualification.

`manifest-v1.json` records detection requirements and remediation ownership.
Minimum versions are planning policy, not claims that a source build has been
qualified. Versioned shared DependencyCheck validation belongs to W1 under
`config/contracts/` and `scripts/contracts.py`.

- AVAILABLE: an executed compatible observation, not just a path on PATH.
- MISSING: installed tool/library metadata not found.
- INCOMPATIBLE: observed version/architecture/target violates a requirement.
- UNKNOWN: output/nonzero exit does not establish compatibility.
- BLOCKED: lookup denied/untrusted, execution denied, or timeout.

Tool argv/package queries are fixed in code; callers cannot supply commands.
Only standard system/MSYS2 installation roots are executable trust roots;
relative PATH entries, worktree tools, Windows batch files and WSL launchers
are not executed. This deliberately reports nonstandard installations BLOCKED
until a separately reviewed trust policy supports them. It does not authenticate
OS-installed binaries or replace OS package signatures.

Probes run from their installed tool directory with a minimal environment;
startup/plugin/compiler/package overrides such as PERL5OPT, MAKEFLAGS,
LD_PRELOAD, BASH_ENV and PKG_CONFIG_PATH are not inherited.

The report contains normalized host facts, physical RAM/disk snapshots, W1
DependencyCheck rows, bounded version/exit/path observations and status counts.
Raw stdout/stderr or environment dumps are not exported. Missing measurements
stay null. Snapshots are not resource reservations; cgroup/WSL allocations need
their own preflight evidence. No Yocto RAM threshold or unavailable physical
PICO SKU blocks W1–W3.

Each installed-tool subprocess has a three-second deadline. Diagnostic content
is size-checked after capture; this is not a streaming memory cap. The trust
policy deliberately confines probes to installed system tools rather than
running untrusted version scripts with potentially unbounded output.

W3 may reuse `ReadOnlyProbe.host/resources/tool/library`, `collect_report` and
`dependency_check`. Package `--modversion` queries show development metadata,
not runtime binary closure or matching pinned libraries. W3 separately enforces
Ubuntu 24.04 x86_64-linux-gnu/glibc 2.39, cached source hashes and output safety.
