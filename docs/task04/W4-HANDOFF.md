# W4 — Avalonia desktop foundation — 2026-11-17

Branch: `codex/task04-w4-desktop`; starting point is verified remote main
`1120723c806a01f9a923f5c4e489bc8a7ae23798`. This package adds a desktop shell,
not a Tor runtime qualification, shipping run-only bundle or W13 integration.
The integrator records the final commit/PR after this handoff is committed.

## Ownership and integration agreement

W4 owns `desktop/Tor.Desktop/`, desktop SDK/NuGet pins and UI tests. W5 owns
`desktop/Tor.Contracts/`, task orchestration, Tor control and platform process
adapters. W6 owns Linux execution. No W1 schema or existing validator changed.
W1 v1 JSON remains authoritative. Internal UI projections do not replace public
bindings/full validation; a future W5 `ITaskFeed` adapter must supply validated,
sanitized, ordered progress through the UI dispatcher. It must not execute
processes in views. No tracked file was edited concurrently by two agents.
The integrator owns the additive shared PROGRESS/integration documentation.

## Implemented

- Avalonia/MVVM navigation: Dashboard, Build Manager placeholder, Dependencies,
  Devices, Tests, Reports, Logs and Progress, and Settings.
- Run Only / Developer / Embedded modes explain their separate capabilities;
  changing modes starts no installation, process, build or device operation.
- Eight W1 task states, five dependency classifications, determinate and
  indeterminate progress, failed exit 17 and explicit runner-crash interruption.
- Finite simulated event stream, dispatcher updates, cancellation on close,
  explicit replay, monotonically ordered task snapshots and a 100-entry log cap.
- UI preferences for mode/theme/simulation, explicit atomic save, invalid
  settings preservation, and no cookie/identity/credential settings.
- Theme-specific surface/text palettes and readable status badges. All screen
  data are labelled SIMULATION; actual Tor monitoring is NOT CONNECTED.
- All eight synthetic W1 fixture types are embedded as examples, never imported
  as real measured evidence or used to overwrite historical reports.

## Dependencies and provenance

The user installed stable SDK `11.0.100` during this task. The SDK is pinned
without roll-forward/prereleases; target `net11.0`, runtime `11.0.0`. .NET 11 is
STS; this avoids an additional SDK installation and needs future servicing
review. Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent are exact
`[12.1.6]` pins; official NuGet metadata targets .NET 8+.

After the project was concrete, the user explicitly approved its bounded NuGet
restore. It fetched only the three direct packages and their required
transitive graph from the sole configured nuget.org HTTPS source. The genuine
lock contains 25 resolved packages. Every NuGet content hash was compared with
restored metadata; complete signed-archive SHA-512 values were independently
recomputed and matched the cache's archive hashes. Those are distinct hashes
because NuGet content hashes exclude signature data. The lock was subsequently
validated offline using an empty source and the isolated populated cache.

`dependency-provenance.json` and `resolved-package-inventory.json` record source,
version, repository/license metadata, content hashes and separate archive
hashes. File-based license declarations additionally have SHA-256 digests.
This is not an independent author-signature, vulnerability or bundled-native
license audit. Those remain release gates. No system package, template,
workload, Tor source or compiler installation was performed by W4.

SDK home, NuGet cache and build outputs are isolated in this worktree's ignored
`build/` directory. Build commands disable .NET telemetry/workload notices;
subsequent builds also explicitly set Avalonia's separate
`AVALONIA_TELEMETRY_OPTOUT=1` after discovering its build-time telemetry opt-out.
No runtime telemetry is implemented in this shell.

## Executed validation

Native Windows, installed .NET SDK 11.0.100:

| Check | Result |
| --- | --- |
| Approved scoped Avalonia restore | PASS; genuine 25-package content-hash lock generated |
| Empty-local-source locked restore from isolated cache | PASS, no additional retrieval |
| Avalonia build `--no-restore --disable-build-servers` | PASS, zero warnings/errors |
| Actual UI launch/navigation inspected by integrator | PASS; simulation labels, Dashboard, Dependencies, Settings, mode switch and Logs/Progress observed |
| Package-free C# presentation/service harness | 40 assertions PASS; zero build warnings/errors |
| `python -B -m unittest discover -s tests -p test_desktop_foundation.py -v` | 5 PASS |
| `python -B scripts/check.py` | 121 discovered / 118 PASS / 3 platform SKIPPED, exit 0 |
| Existing `python -B -m unittest discover -s windows-native/tests -v` | 6 PASS |
| Whitespace/scope check | Final `git diff --check` recorded at commit |

C# behavior checks cover all states/classes, navigation/modes, W1 finite
integral numeric spellings, boolean/fraction/nonfinite and range rejection,
settings save/load/corrupt preservation, finite stream replay/cancellation,
300-event log truncation and stale-sequence rejection. They compile and execute
the actual presentation/service source without an Avalonia testing package.
Python checks validate fixtures with W1 and check exact pins/lock, XML and the
no-process UI boundary; they do not claim rendering coverage.

Windows skips in the full portable suite are POSIX atomic replacement, a
Linux-shell launcher case and creation of an inert target symlink requiring
Windows privileges. The integrator can complement them with existing Linux CI.
The first settings harness attempt failed under the known Windows temporary
file sandbox restriction; rerunning outside that restriction passed. Both
logs remain preserved. The initial visual inspection found low contrast when
Light was selected because surfaces were hardcoded dark; resource palettes
corrected that issue. The integrator's second actual launch confirmed Dark and
Light palettes, readable controls, navigation and mode selection, then closed
the application without saving preferences. This is Windows UI evidence only.

Raw logs are retained in ignored `build/task04-checks/w4-*`. No existing tests,
policy thresholds, sources, patches, source locks or historical outcomes were
modified. Baseline/stashes/older worktrees remain the integrator's preservation
check. No public Tor test, native Tor compilation, firmware build or device
operation was executed.

## Remaining gates

- Linux GUI launch/native display-library closure: NOT RUN. Intended reference
  is Ubuntu 24.04 x86_64/glibc 2.39; no Linux or Windows 10 compatibility claim.
- Clean-host self-contained run-only publishing, measured working set/package
  size, full accessibility/scaling and release vulnerability/native-license
  audit: NOT RUN, belong to separately approved release/integration work.
- Real Tor controls, build commands and physical devices are NOT CONNECTED.
  W5/W6 integration requires a new task; simulated success is not qualification.
- Exact PICO SKU/carrier/DTB remain BLOCKED; no embedded .NET dependency was added.
- No W7–W15 implementation or PR merge was performed.

## Changed files

The package contains only new desktop source/resources/pins, its independent
C# harness, `tests/test_desktop_foundation.py` and this handoff. The integrator
will add shared progress/overview in a separate documentation commit. See the
PR's complete file list and the desktop README for build/restore commands.
