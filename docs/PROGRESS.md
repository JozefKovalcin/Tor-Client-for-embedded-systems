# Windows Manager interface redesign — 2026-11-18

Light and dark themes distinguish actions, field labels and live state
through color, shape and type. The navigation and mode selectors show their
active selection. The Peers page groups pairing, Onion Service hosting and
messages separately. Dashboard controls wrap on narrower windows. The
Win-x64 preview reuses the existing verified bundled Tor EXE and DLL.

# Windows public persistent hosting — 2026-11-18

The Manager now allows the verified Tor runtime to finish its native
two-minute SOCKS rendezvous deadline. It separates local SOCKS setup from
onion connection and can refresh the one requested descriptor after a
recoverable fault. Offline checks passed the deadline, cancellation and
authenticated descriptor cases.

Two separately approved follow-ups using the same custom EXE and DLL
passed native bootstrap, initial Onion publication and SOCKS tunnel
establishment. Both stopped at the initial mutual TLS deadline. The
second repeated that step once. Restored hosting and reconnect in these
follow-ups were NOT RUN. Public qualification remains FAIL.

One authorized single-host run used the verified `optimized-static` runtime
`run-mm7s33j8`. Both clients passed authenticated bootstrap, the initial
Onion Service published, and pinned TLS carried matching text in both
directions. A restarted and imported the same DPAPI-protected v3 identity.
Tor returned the same address and confirmed publication again. B stayed
running, but the restored exchange failed with an `IOException`. The
public persistent-host gate is FAIL.

The separately approved repeat restored and republished the same address
again. The original B stayed alive and all three restored connection
attempts timed out in the SOCKS tunnel stage. Restored TLS and transfer
were NOT RUN. Both processes exited with code 0. The broader online suite,
clean-host and two-PC gates remain
NOT RUN. See [the single-host evidence](../reports/windows-persistent-host-2026-11-18/README.md).

# Windows self-contained candidate and Developer build — 2026-11-18

Draft #32 adds independent connection and workspace selectors and a
verified runtime preference for future launches. The integrated source
published for win-x64 from an offline locked restore. The local release
ZIP passed DLL closure, file inventory and extraction checks. The Manager
loaded its packaged .NET runtime and passed launch and shutdown on this
host. The bundled Tor passed the offline lifecycle and rollback tests.

The real Developer GUI preflight accepted the prepared pinned input cache.
Two native attempts identified concrete GCC temporary-directory
and windres command-processor failures. Both were reproduced and fixed.
The next GUI run built the optimized static package with exit 0 and passed
eight offline smoke checks. The verified GUI export passed all 21 member
checks. See
[the Windows candidate evidence](../reports/windows-product-2026-11-18/README.md)
for the artifact, hashes and run IDs.

# Persistent Windows Onion Service hosting — 2026-11-18

The Peers page can explicitly create, host or rotate a saved v3 address.
The authenticated Tor Control connection supplies the key. The app protects
it with user-scoped DPAPI in the owner-private profile, separately from the
peer TLS credential and contact trust store. A Tor restart restores the
address for an active hosted session. Reopen Tor Manager and select **Host
saved address** to reuse the same address from the same profile.

Locked Avalonia Release build PASS. The Windows repository suite passed
174 tests with 10 platform skips, including two new loopback Control
groups. The Windows peer fixture passed 23 assertions, including simulated
Tor and Manager restarts, explicit rotation and refusal of corrupt storage.
At this offline milestone, public Tor persistent lifecycle, visual/DPI
inspection, physical two-PC and clean-host validation were NOT RUN.
See [the Peers workflow](WINDOWS-PEER.md).

# Windows product integration — 2026-11-18

GitHub main at `68002b9` includes the reviewed peer and Developer stacks
through PR #29. The follow-up Windows worktree adds a Peers page in both real
modes, persistent onion endpoint editing, explicitly confirmed remote pin
replacement, per-contact connection events and runtime package export.
The task journal retries short Windows rename conflicts and still surfaces
permanent write failures.

Offline Windows regression returned 174 PASS and 10 platform-specific
SKIPPED. The native pipeline returned 6 PASS. The Avalonia Release build
compiled without warnings, presentation returned 63 PASS, and the isolated
peer workflow returned 16 PASS. Pinned native Tor offline lifecycle and
runtime rollback passed, as did loopback mutual-TLS delivery/recovery.
An earlier journal failure and its diagnosis are retained in the
[Windows continuation record](AUTONOMOUS_PROGRESS.md). Physical two-PC,
public Tor and clean-host qualification remain NOT RUN.

# Windows messaging cycle — 2026-11-18

The peer review stack adds persistent Windows TLS identities, explicit
pairing and revocation, authenticated acknowledgements, a durable encrypted
outbox and bounded deduplication. The desktop shows delivery state, retries
the selected pinned endpoint and offers opt-in encrypted history. Offline
tests used a new profile per endpoint, and the Avalonia preview displayed a
pending test message with Tor stopped. See the
[cycle handoff](AUTONOMOUS_PROGRESS.md) and the [Windows peer guide](WINDOWS-PEER.md).
The current GUI remains unqualified on two physical PCs and a clean host.

# TASK 12 — Windows Developer Mode — 2026-11-18

The Windows Developer stack is based on the reviewed PR #19 head. Static
and shared native preflight accepted the host UCRT64 toolchain, cached source
and license pins, and the reviewed patch. The existing native recipe also
passed its read-only dependency check. Avalonia rendered the Developer pages
and completed a real dependency scan. The Windows application regression
used the reviewed Tor binary to verify activation refusal during an active
session, offline startup from a staged approval, modified-binary rejection
and rollback. See the [Developer workflow](WINDOWS-DEVELOPER-BOUNDARIES.md).

Package installation and both heavyweight source builds remain BLOCKED
pending separate authorization. No new runtime release or public-network
qualification is claimed by this milestone.

# TASK 11 — Windows two-PC qualification — 2026-11-18

PR #19 review found a malformed-record error could bypass the GUI's normal
stream-failure handling. The fix closes the invalid session and reports a
sanitized channel error, so the host can continue accepting peers. Offline
regressions cover authenticated empty, oversized and invalid UTF-8 records.
The existing session scope rejects invalid sequence numbers. Identical text
is accepted after a fresh authenticated connection as an independent send.

# TASK 10 — Windows client-to-client — 2026-11-18

The authorized public test used two independent pinned Windows Tor processes
and private state scopes on one host. Both reached bootstrap 100%, and Tor
confirmed publication of the host's v3 onion descriptor. The peers verified
each other's TLS certificate fingerprint and exchanged both messages unchanged.
Stopping and restarting Client B's Tor interrupted the stream. Both peers
then authenticated and exchanged messages again through the same hosted onion.
Authenticated close, onion removal and the final Tor exits passed. Seven
offline peer groups passed. The implementation is submitted for review.

See the [Windows peer workflow](WINDOWS-PEER.md) for the protocol and trust
model. Separate-host and clean Windows release qualification remain open.

# TASK 09 — Windows online Tor Manager — 2026-11-18

Windows x64 is the active product target. The online verification harness
requires an explicit public-Tor opt-in. Public bootstrap, proxy destination
reachability and clean-host release qualification remain NOT RUN. The pinned
Microsoft runtime packs were verified and restored from an isolated local feed.
A separate candidate RID lock enabled locked self-contained publication of the
starting main tree. Release packaging and clean-host validation remain open.

# TASK 08 — Windows preview integration — 2026-11-18

The preview packager now checks its locked Windows asset inventory before
creating a ZIP. In a local reproduction, removing the publish metadata entry
for `libSkiaSharp.dll` previously yielded an incomplete ZIP. The corrected
packager rejects the same input before writing a ZIP. Four focused asset
inventory tests passed. The self-contained distribution still requires the
pinned Windows x64 runtime packs and a reviewed RID lock.

# TASK 07 — Windows offline desktop — 2026-11-17

TASK 04 PRs #5, #10, #8, #6, #9 and #7 landed as merge commits. Their combined
tree at `3a33f1a83461674b5c9301bbea59ec2ea7d64931` matches the reviewed TASK 06
tree. Windows portable checks passed 155 with 10 skips. WSL portable checks
passed 160 with five skips. All 15 cached Kconfig profiles, six Windows
pipeline tests, 19 task-core groups, nine control groups, seven native offline
Tor groups and 46 desktop presentation assertions passed on that main tree.

The [Windows offline desktop guide](../desktop/packaging/WINDOWS-OFFLINE.md)
covers the offline application and local GUI preview. The self-contained
package is BLOCKED on the missing Windows x64 runtime packs and reviewed RID
lock. Clean-host qualification remains NOT RUN. The local Tor validation uses
the existing separately verified Windows runtime and a new private state scope.

# TASK 04 — Parallel W4/W5/W6 foundations — 2026-11-17

Started from verified remote main `1120723c806a01f9a923f5c4e489bc8a7ae23798`.
Three independent agents/worktrees implemented separate desktop, core/Windows
control, and Linux execution/inventory boundaries. Draft PRs: W4 #7; W5 core
#5 and Windows #10; W6 executor #6 and package plan #9. A real supported-host
probe exposed the narrow W2 version-suffixed GCC banner defect; separate PR #8
fixes it and precedes #6. No PR was merged and no history was rewritten.

W4 pins the user-installed SDK11.0.100/net11.0 and Avalonia12.1.6; the user
explicitly approved the isolated 25-package NuGet restore. Genuine lock and
provenance are committed. Actual Windows GUI build/launch/navigation and
Dark/Light checks PASS, 40 compiled UI assertions and five W1/pin/lock guards
PASS. The GUI remains explicitly simulated and does not execute shell commands.

W5 adds BCL-only typed orchestration, durable W1 progress and bounded lifecycle/
resource/log handling, then separate SAFECOOKIE/managed Windows control.
Core15 and protocol9 compiled groups PASS. Actual verified existing Tor offline
smoke PASS: unauthorized514, authenticated bootstrap0/stop/raw0, unexpected
exit, cancellation, owner crash/no orphan, hash rejection and verified-only
runtime staging. Networking/SOCKS remain disabled; private state stays ignored.
Owning-core atomicity, journal-failure cancellation and ref-validation defects
were corrected with independent before/after reproductions and preserved logs.

W6 adds exact-input approved execution infrastructure and separate bounded ELF/
package planning. Actual native Ubuntu24.04/glibc2.39 CI observed all required
tools AVAILABLE after PR #8; the explicit empty test cache has no sources and
no BuildRequest. Before/after and incompatible WSL observations are retained.
No real source build or package artifact was produced. Distribution readiness
remains false, signature trust unchanged, and runtime/ABI acceptance NOT RUN.

Combined validation: Windows156 discovered/149PASS/7platformSKIPPED; native
Linux unit snapshot156/151PASS/5SKIPPED (including two explicit absent-Linux-SDK
compiled gates). Core/control also compile in applicable Linux CI. Existing
Windows pipeline6PASS; combined app build0warnings/errors; whitespace PASS.
No Tor compilation, public-network Tor test, full Buildroot/Yocto, deployment,
flashing or W7–W15 implementation. Baseline, stashes, protected source/configs,
historical evidence and thresholds are preserved. Linux GUI/clean-host release
and actual local UI/backend wiring remain unverified or unimplemented.

See [TASK 04 integration overview](task04/INTEGRATION.md) and its per-lane
handoffs for branch/commit/PR mapping, commands, dependencies and remaining gates.

# TASK 01 W3 — Native Linux build preflight — 2026-11-16

W3 integrates W2 `239e5ad47de360a2c1b603a16f9456c97c43a957` (including W1).
Selected native v1 target is Ubuntu 24.04 x86_64-linux-gnu/glibc 2.39.
Read-only preflight checks trusted toolchain probes, libc headers, local source/
license hashes, safe source/output/cache paths, clean Git provenance and native
RAM/disk/cgroup-v2 limits. It emits reproducible input metadata and a descriptive
DAG with every stage NOT RUN; no executor/build/download/install exists here.
Only genuinely available required inputs permit a plan-mode BuildRequest.

Executed on Windows/Python 3.14.2: isolated focused W3 **15 PASS**, no skips;
full `python -B scripts/check.py` exit 0, 105 discovered, **103 PASS / 2 platform
SKIPPED**; Windows source-pipeline **6 PASS**. Full offline tests used the
permitted context outside known sandbox loopback/hardlink restrictions. Real
Windows preflight returns exit 1/NOT RUN with no request/output creation;
positive Linux readiness is synthetic test evidence only. No Linux runtime,
ELF linkage, Tor compilation/public test, Buildroot/Yocto or physical-board
qualification was run. Unsupported cgroup mappings remain UNKNOWN.

Preservation audit: all 566 protected Git inputs are unchanged in W1/W2/W3.
Initial raw-byte comparison flagged 36 checkout line-ending differences in each
worktree; normalized content is identical, with no other differences. Original
checkout files, main, both exact stashes and baseline tag remain unchanged. Shared contracts remain owned by W1; no W4–W15 implementation.
See `docs/task01/W3-HANDOFF.md` for changed files, commands, limits and next gates.

# TASK 01 W2 — Read-only dependency diagnostics — 2026-11-16

W2 integrates W1 `3997fa567a2d24758a0266add4c9cf3049e5f7f6` and adds fixed,
trusted-root tool/version/target probes plus OS/architecture/libc and resource
facts. DependencyCheck output uses W1 validation and five distinct statuses.
Discovery never invokes COMPILE.cmd, WSL startup, installers, downloads or builds.
Use `python -B -m scripts.dependencies` for bytecode-free CLI startup.

Executed: W2 focused **24 PASS**; full `python -B scripts/check.py` exit 0,
90 discovered, **88 PASS / 2 platform SKIPPED**, outside known sandbox limits.
Real CLI JSON outside sandbox: exit 1/non-ready, **15 AVAILABLE / 1 BLOCKED**;
Git installed via user-local Scoop is outside conservative trusted roots.
Sandbox observation separately retained: 10 AVAILABLE / 6 BLOCKED, including
MSYS2 initialization status 0xC0000142. No repair/install attempted. Reporting
non-ready is correct diagnostics, not a failed unit test or missing-tool claim.

Source/policy/history/existing tests and baseline remain unchanged. Hardware
and Yocto RAM are not W2 gates. W3 must integrate this commit after W1;
`docs/task01/W2-HANDOFF.md` records changed files and remaining limits.

# TASK 01 W1 — Contracts and fixtures — 2026-11-16

Reviewed TASK 00 documentation is preserved separately at `67b9d423`.
TechNexion PICO-IMX6UL is the primary physical v1 platform; DHCOM is historical
only. Exact PICO SKU/carrier/DTB remain BLOCKED. No board work is included here.

W1 adds eight versioned contract schemas, an offline stdlib validator, sixteen
synthetic fixtures and 23 behavior tests. Shared contracts explicitly distinguish
AVAILABLE/MISSING/INCOMPATIBLE/UNKNOWN/BLOCKED and interrupted app/runner tasks.
Selected native Linux v1 is Ubuntu 24.04 x86_64-linux-gnu/glibc 2.39; generic
future ABI descriptors do not imply supported implementations.

Executed on native Windows with Python 3.14.2: focused W1 23 PASS; full
`python -B scripts/check.py` exit 0, 66 discovered, **64 PASS / 2 SKIPPED**
(Windows symlink privilege and Linux shell). Full offline check ran outside the
sandbox for its known loopback/hardlink restrictions. CLI fixtures and
preservation/whitespace checks PASS. No source/config threshold, existing test,
historical result, Windows patch or baseline change. No compilation, public Tor,
Buildroot/Yocto, install, download or flashing. W2 must integrate W1; W3 follows
W2. See `docs/task01/W1-HANDOFF.md` for files, API and limits.

# Repository synchronization — 2026-11-16

Compared local main with freshly fetched origin/main at
`bbcf1475230ec6cafb6f57ebc9ca15cdd20f0272`: no committed divergence.
Added the previously local-only Windows source project, source preparation,
reviewed patch, compile/test launchers, pipeline tests and November 12 native
Windows/QEMU evidence. Updated entry-point documentation for COMPILE.cmd,
TEST.cmd PC A/B, local port 8081 and exact upstream source locations.
Historical evidence, Linux/ARM build inputs, failed runs and baseline are retained.

Re-executed: Windows pipeline 6 PASS; repository 41 PASS, 2 SKIPPED
(Windows symlink privilege and Linux shell); PowerShell parsing and shell syntax
PASS; compact ZIP generation/CRC/one-README checks PASS. Linux Buildroot Kconfig
resolution is BLOCKED on Windows. No new native Tor compilation, public-network
qualification, full Linux image validation or physical-board test was run for
this synchronization. Build outputs, downloaded upstream trees, runtime state
and private keys are excluded from Git. See the
[synchronization record](../reports/sync-2026-11-16/README.md).

# Native Windows source build — 2026-11-12

The compact delivery now combines compilation and tests with one short Slovak
README. TEST.cmd selects PC A/B and starts/stops its own Tor and loopback echo
server automatically. Both server and onion target use 8081; SOCKS remains
9150. Native offline startup/cleanup passed for A and interactive selection B;
the local mock-SOCKS 1 MiB SHA-256 round trip passed on 8081. A physical two-PC
public-network run was not executed here. Tor sources/compiler flags are unchanged.

Added an optional two-PC PowerShell/curl test pack with a loopback-only HTTP
echo fixture, 1 MiB round-trip SHA-256 check and Slovak instructions. Its local
HTTP/SOCKS-domain transport check passed, including a `NO_PROXY=*` environment;
that helper check used a mock SOCKS proxy, not two physical PCs or public Tor.
The actual two-PC and restart procedure is supplied for user execution.

Added a separate compilation-only ZIP with 16 allowlisted files and a
double-click `COMPILE.cmd` entry point. It checks/installs missing MSYS2 build
tools and selects the optimized static profile. The extracted bundle's native
launcher, standalone source preparation, verified patching and packaging were
checked without the Linux tree, reports or tests. Six pipeline tests passed;
Tor compiler flags and the reviewed source patch remain unchanged.

Added an isolated `windows-native/` source project using native MSYS2 UCRT64,
MinGW-w64 GCC 16.2.0 and the original Tor 0.4.9.14 Autotools build. Tor,
OpenSSL 3.6.5, libevent 2.1.13 and zlib 1.3.2 source/license hashes match the
embedded pins. No WSL, Linux or QEMU compilation is used. Relay, authority and
directory-cache modules remain disabled; O2, section collection, stripping,
stack protection and native PE hardening are retained. Existing Linux/ARM
build inputs, sealed images, thresholds and the baseline tag are unchanged.

Native server-first TCP testing exposed CONNECTED/DATA ordering loss. A
reviewable, hash-locked Windows-only source patch queues CONNECTED before
read activation can synchronously process an already buffered greeting. It
preserves protocol, routing, crypto, authentication, entropy and logging.
Initial failures and later public-network timeout results are retained.

The clean default source build and PE/DLL checks passed. Both optimized static
and shared releases passed all nine original public Tor assertions natively
on Windows x64 build 26100, including bidirectional payload hashes and
same-identity crash/restart. Configuration, remote-DNS SOCKS, authenticated
clean shutdown and restart smoke tests also passed. Pipeline tests: 6 PASS;
repository tests: 41 PASS, 2 Linux-specific SKIPPED. Linux Kconfig resolution
requires Linux; full firmware and physical-board qualification were not rerun.

Default Tor is 8,642,974 B plus a 73,308 B native MinGW DLL. The measured shared
alternative totals 9,748,697 B for executable plus DLLs, versus 8,716,282 B for
the default. Windows 10 was not separately executed. Public-network and Windows
resident-memory samples are descriptive, not direct ARM comparisons or a
controlled performance qualification. [Build, patch and executed evidence](../windows-native/evidence/README.md).

# Native Windows QEMU short smoke test — 2026-11-12

Installed QEMU 11.1.0 for Windows after verifying the publisher's SHA-512;
the expired Authenticode certificate and HTTPS checksum trust boundary are
recorded in [Windows test evidence](../reports/windows-qemu-2026-11-12/README.md).
The preserved ARMv7 and x86_64 capacity32 images were exercised natively on
Windows with TCG, one vCPU and 256 MiB RAM, using disposable snapshots and
read-only diagnostic observers. Original image hashes remained unchanged.

Both passed Linux/serial boot, Ethernet DHCP, valid time, immutable root and
writable state checks, production UID/GID 905, SOCKS5 negotiation and guest-local
SAFECOOKIE authentication. ARM reached public bootstrap 100% in 16.43 seconds
from QEMU launch: short smoke PASS. x86 reached 62% before the requested short
180-second bootstrap deadline: BLOCKED, not full validation or a demonstrated
firmware failure. Raw timeout status FAIL is retained with an explicit reporting
correction to BLOCKED. The reusable Windows runner now distinguishes this case.

Repository checks ran 43 tests: 41 passed, 2 platform-specific SKIPPED. Full
integration, Onion Service transfer, payload integrity, reconnection, performance,
Kconfig/build and physical-board checks were not executed in this short scope.
Historical qualification decisions and the normal 600-second bootstrap timeout
are unchanged. Failed harness attempts and raw logs were preserved. The result
PNG is labelled as a generated JSON evidence summary; window screenshot capture
was stopped by Computer Use because it could not confidently verify browser URL.

# Slovak report readability revision — 2026-11-09

Revised only page 4 of the current executive PDF: neutral RAM comparison
title, separate ARMv7/x86 explanations and a prominent methodology panel
covering the 20% rule, three-repeat medians and observed transfer variability.
All measured values, charts, tables and acceptance decisions are unchanged.
Visual review passed; all five other pages render identically to the previous
revision, including the two-PC launcher note. Reproduction uses
`reports/executive/update_sk_report.py`; [revision checksums and verification](../reports/executive/revisions/2026-11-09-page4.json)
record the change. No new firmware or network measurements were required.

# Repository and report synchronization — 2026-11-09

Windows and GitHub initially matched `938b6d2`; the native WSL checkout was
still at `fec5f29` with 37 changed/untracked ARM files. Preserved that entire
working tree in stash `ce09ea68b91503598dfdefba1c135c404a544f50` before a
fast-forward. Thirty-two files matched Windows after line-ending normalization;
the remaining five were documentation/report differences, including newer ARM
test summaries that had not reached Git. Existing Windows stashes, the
`baseline` tag, sealed images, failed logs and large build outputs were retained.

Updated `reports/latest.md`, `test_results.json` and `test_summary.md` from the
archived ARM online run `20261105T190223Z-7002ec13`, preserving its original
dirty-tree provenance and `NON_COMPARABLE` decision. Verified 102 SHA-256
records for baseline, ARM/x86 images, build and native logs, fixtures, policy,
frozen tests and report sources. Executive HTML/JSON regeneration is unchanged.

The supplied Slovak PDF is now the current executive report. Measurements and
pages 1–5 remain unchanged; page 6 describes the corrected two-PC launcher.
The launcher discovers all Ubuntu installations by OS identity, including older
and renamed ones, and offers selection. It checks Python 3.11+ and QEMU's
`virt-8.2` support before starting Tor. Insufficient tools trigger an opt-in
Ubuntu upgrade prompt; no distribution was upgraded during this audit.
Versioned launcher sources and a SHA-256-checking ZIP packager retain the
original ARM images. The supplied original PDF and ZIP provenance are preserved.

Linux regression/script checks and all 15 Buildroot profile resolutions passed.
The launcher tests cover UTF-16 distribution discovery, older/renamed Ubuntu,
selection, argument/exit-code forwarding and five dependency/upgrade scenarios.
Actual Ubuntu discovery and dependency preflight also passed. Full image builds,
public-network target tests and a real two-PC session were not rerun; physical
qualification remains open. See the [audit and execution details](../reports/sync-2026-11-09/README.md).

## Previous measured stages (retained)

# Measured performance recovery — 2026-11-05

Continued from `e1abc78` without replacing the original baseline. Executed three
interleaved rounds of all five original images, crossed offline crypto/I/O
probes, complete Tor-O2/fast/balanced/RAM builds, three original-image/fast pairs,
three matched 128 MiB control/balanced pairs and the final fixed repeats.
See [the investigation](../reports/performance-2026-11-05/README.md) for all
metrics, source runs, failures, acceptance scopes and partition geometry.

Original-image performance lineage for `fulltor-slim` at 512 MiB: **ACCEPTED**.
Further RAM iteration versus frozen balanced 128 MiB reference: **ACCEPTED**.
Combined new-candidate preservation qualification: **NOT QUALIFIED**; selected 128 MiB option remains `ram`. See final-selection.json for the failing gate; the smaller fulltor-slim candidate passes 512 MiB original-reference comparisons but fails fresh 128 MiB throughput preservation.
The matched 128 MiB balanced qualification passed all nine functional tests
and the native deployed-service probe in every planned run. Device-budget
acceptance does not erase historical or original-image rejections. Thresholds
remain unchanged; host page-reservation tolerance applies only to capacity
metadata, not performance percentages or guest RAM.

The 25.89 MiB historical boot set includes its 16 MiB persistent-state partition.
The new complete images preserve that partition. Read-only roots contain no
Python/test harness. Full logs and immutable images remain in native ignored
directories. This verifies QEMU behavior, not physical hardware or minimum RAM.

## Previous stages (retained)

# Current measured Ubuntu WSL2 state

The complete original baseline and four smaller candidates were built and tested. See [the measured results](../reports/measured/2026-11-04/README.md) for all successful and incomplete runs, precise image/RAM reductions and conservative acceptance decisions. The original baseline remains immutable. Production images omit Python and test files. Physical hardware and minimum RAM have not been verified.

## Earlier implementation and build history

# Verified project state — 2026-11-04

- Initial recursive inventory: workspace empty, no Git repository, Tor executable,
  existing Linux build, source project, user runtime state or baseline.
- Git initialized; external-tree configurations, source hash lock, scripts,
  tests, reporting and GitHub Actions implemented.
- Supplied GitHub repository inspected later in the session: its main branch
  contained only a placeholder README. Existing history is preserved; the new
  work extends its documented Tor scope.
- Implementation published on `codex/optimization-tracking`. GitHub Actions
  [run 41855759505](https://github.com/JozefKovalcin/Tor-Client-for-embedded-systems/actions/runs/41855759505)
  passed regression/script checks and real Buildroot configuration resolution,
  and uploaded configuration evidence. Full image building was intentionally
  skipped by the workflow's manual/resource gate; it is not a passing full build.
- Repository scope split on 2026-11-04: this repository is Tor-only and retains
  its original Git history. Nym has a separate repository and an initial project
  scaffold; no existing Nym implementation was present to transfer. The Tor
  implementation and documentation were promoted to `main` by fast-forward,
  preserving the original commits. Both repositories use `main` as the default.
- Official Buildroot 2026.08.1 archive downloaded; SHA-256 computed as
  `34fb7bea8a213ffde39cc02e61703391560f26e0850f03138c722d0b09880f2b`.
  Package versions/configuration and upstream hash files inspected directly.
  Detached PGP signature was not verified; trust boundary recorded in lock.
- 34 regression tests and ShellCheck executed successfully using an isolated
  signed Alpine tool root under `build/linux-check/` in Docker WSL/Python 3.12.
  Tests include real fixture sockets, failure log/report preservation, source
  extraction safety and synthetic decision inputs. Windows tests also ran
  successfully; the absolute-symlink test is Linux-only.
- Actual pinned Buildroot Kconfig resolution passed in the isolated Linux
  environment. The check exposed and corrected a missing kernel-header series
  selection, and unsupported glibc-only Fortify configuration was removed from
  the musl profile. PIE, stack protector and full RELRO remain selected.
- Real offline and online host harness tests executed with signed Alpine Tor
  0.4.9.11 (different from pinned target Tor 0.4.9.14). **All nine online checks
  passed**, including both public bootstraps, v3 onion connectivity,
  bidirectional SHA-256 payload integrity and crash/restart reconnection.
  See [host evidence](../reports/host-smoke.md). Those host measurements are not
  target-image benchmarks and do not establish an embedded baseline.
- Full cross-toolchain/image build, target ELF checks and QEMU target boot
  remain unexecuted. Windows has only Docker's service WSL distribution; the
  Docker Linux engine is unavailable. The isolated tool root was used for
  configuration and host testing and is not a configured full build runner.
- The workspace path contains whitespace. Linux Buildroot work must use a path
  without spaces. The scripts detect and report this prerequisite.
- Baseline sealing, real resource metrics, embedded minimum requirements,
  Windows distribution minimization and physical hardware/USB behavior remain
  unverified. No target baseline is fabricated; no optimization is accepted yet.

Next independent validation: on the documented Linux host, resolve Kconfig,
commit the reviewed inputs, run `./scripts/validate.sh --record-baseline`, then
test one configuration change with the same runner. Keep the initial successful
image and baseline result archive outside Git. Inspect `reports/latest.md` for
the actual most recent pipeline outcome.

Ubuntu WSL2 build session: prerequisites and all 34 checks passed. The first complete
build attempt exposed Windows PATH entries containing spaces; subsequent builds
use the native Linux PATH. Target baseline build and measurements are in progress.

The first complete cross-build produced bzImage and a 256 MiB ext4 rootfs.
Runtime validation was initially blocked by two aliases of the same generated
compiler. The validator now selects GNU_TARGET_NAME; --resume-run permits only
host-reporting fixes with identical target inputs. Original logs are retained.

The first QEMU run passed public bootstrap, v3 onion service reachability, two-node
communication and bidirectional integrity. Service restart reconnection timed out
(BLOCKED); therefore no baseline was sealed. The serial output also exposed an
extra blank line before its end marker; the parser now accepts that whitespace.
Recovered diagnostic JSON is preserved alongside the original serial log.

Two target runs reproduced stale onion-client state after service crash/restart.
Recovery now waits for an authenticated HS_DESC UPLOADED event and requests
SIGNAL NEWNYM on the client before retrying the same onion identity and verifying
payload integrity. This is Tor upstream control behavior, not a protocol change.
The measured test protocol is versioned v2; no prior run qualified as a baseline.


Iteration 2 built and passed all nine functional tests. Complete image:
54,752,256 bytes; Tor: 2,592,728 bytes; kernel: 4,420,608 bytes; combined idle
Tor RSS: 50,226,023 bytes. Policy rejected latency and throughput observations;
no gate was relaxed. Image preserved in `build/iterations/02-tiny/`.

Iteration 3 is in progress: a 32 MiB production root without Python or test
files, using the unchanged baseline driver on a read-only external lab disk.
A separate measurement of the original immutable image under this exact lab
setup will be preserved for an equivalent-condition comparison. The production
daemon starts under uid 905 with cookie-authenticated loopback control.

The immutable original image also passed all nine tests using the read-only
external test disk. That separately sealed reference is at
reports/references/external-fixture/baseline; the original baseline is unchanged.
Production candidates use this explicit equivalent-condition reference.

Iteration 4 begins: gzip SquashFS immutable root and a separate 16 MiB ext4 Tor-state partition. The reported disk image includes both partitions and alignment overhead; the auxiliary test fixture is excluded consistently from reference and candidate deployment sizes. No outcome is claimed before build and regression execution.

2026-11-05: isolated Tor -O2 ablation prepared. Hypothesis: compiler-size optimization adds CPU work per identical payload even though OpenSSL bulk crypto is unchanged. Kernel, libraries, filesystem and security configuration stay the same. No improvement claimed before build and matched repeat measurements.

Performance recovery experiment: restore baseline -O2 userland compilation while retaining the smaller -Os kernel and client-only Tor. Recover storage by removing diagnostic-only executable symbol exports and unreachable code with function/data sections and linker GC. All unwind/CET/stack/PIE/RELRO protections remain. The separate relink prototype reached 2,699,264 bytes but is not a complete-image optimization result. Full build and required online validation pending.

Next recovery experiment: restore baseline kernel SMP and -O2 execution choices (two possible CPUs, one QEMU vCPU), keep trimmed drivers, remove unused performance/debug/input/initrd support and development/network-server BusyBox applets, and use 128 KiB XZ SquashFS blocks to offset code growth. Keep the 16 MiB ext4 state partition, seccomp, ASLR, stack protection, upstream crypto and authenticated control. Previous fast image remains REJECTED; this candidate has no claimed result before build and regression execution.

The balanced image passed all three mandatory-online repeats and the unchanged performance gates at128 MiB against the accepted data-only128 control. Next targeted RAM/storage iteration: directly decompress SquashFS into the page cache, reduce cached fragment blocks from3 to1, and omit unused BusyBox archive codecs. Kernel XZ and the gzip/base64 validation bootstrap remain. No improvement claimed until the new complete image is built, tested and measured.


2026-11-05 final compiler/module ablation: restore the original full upstream
Tor module/link configuration at -O2, keeping the production root and trimmed
libraries. Kernel XZ compression offsets executable growth without changing
the running kernel's code/configuration. This tests whether client-module or
symbol-GC changes explain remaining throughput loss. Hypothesis only: no
performance or size improvement claimed until the complete build/tests run.

The first full-Tor ablation resolved to gzip because Buildroot's compression
choice overrides the kernel fragment. Run 20261105T103950Z-d842416e is retained
with a quality note and actual configs: full functional/native PASS and a
single-run original-reference ACCEPTED, but 26.196 MiB exceeds the prior
smallest image. No XZ improvement is claimed for that attempt. The corrected
defconfig explicitly selects BR2_LINUX_KERNEL_XZ; a fresh build follows.
The study defaults now select the valid frozen v2 fixture, and report headers
identify the actual control/reference rather than an ambiguous baseline label.

The corrected full-Tor XZ build is 24.9893 MiB including 16 MiB state. The
full-O2 libraries increase process RSS above the earlier compressed image.
Next hypothesis: retain full upstream Tor at -O2 and its original linker/module
settings, compile the other userland packages at -Os. The crossed crypto probe
showed no clear bulk OpenSSL penalty from those library flags; Tor's isolated
POLYVAL operation benefits from -O2. Keep kernel/XZ/root/service settings
identical to fulltor so the comparison isolates userland compiler settings.
This candidate is not accepted until it is actually built and measured.


2026-11-05 capacity study continued from baa5458. Preserve both immutable
25.8799 MiB RAM and 24.7354 MiB fulltor-slim images. Compare at 96, 112, 128,
160, 192 and 256 MiB; separate the two-node/Python laboratory workload from
a native single-endpoint workload. Offline diagnostics have a 180-second
collection deadline and cannot qualify mandatory online requirements. Existing
online timeouts and 20% performance/5% RSS thresholds remain unchanged.

Selective compiler hypothesis: retain qualified client/GC Tor O2 and OpenSSL
O2, use Os for remaining userland and XZ for the kernel. Keep the RAM profile's
direct SquashFS decompression, one fragment buffer and production service.
This is a new complete build; no size/performance acceptance is assumed.


### 2026-11-05 — Native endpoint capacity measurements

Added an additional host-controlled native workload with one production Tor endpoint, SAFECOOKIE control authentication and a test-only read-only C observer disk. The frozen two-node Python regression fixture and thresholds remain unchanged. The successful 128 MiB pilot used about 37 MiB at peak but did not execute all nine tests; it is not a device qualification. The first complete 256 MiB sample retained a public circuit timeout (BLOCKED), followed by successful large integrity and crash/reconnection assertions. A predeclared three-image six-RAM sweep is in progress. First sample persistent-state free space fell below 1 MiB transiently: partition headroom, not just final du size, must inform recommendations. The optional local Chutney network failed convergence; it is BLOCKED and supplies no claimed Tor performance evidence.


The native slim/192 MiB case exhausted its 16 MiB state partition and logged microdescriptor write failures despite nine functional PASS results. Preserved the case and logs. A new capacity32 profile changes only the state partition to 32 MiB relative to selective, to be built after timing cohorts finish; it is a reliability capacity experiment, not a storage reduction or an accepted optimization. No historical acceptance record is changed.


Before the capacity32 build or measurements began, upstream `compute_real_max_mem_in_queues` showed that automatic detection has a 256 MiB minimum. Updated the unbuilt capacity32 experiment to also set supported client MaxMemInQueues 32 MB through production defaults. This is a queue bound, not a process RSS bound; upstream warns below 64 MiB on clients. The native suite verifies the effective setting through SAFECOOKIE control. This supersedes the preliminary state-only hypothesis; original ram/slim/selective images remain unchanged. Frozen legacy tests use their unchanged empty defaults, so the additional native tests, rather than legacy results alone, validate the new policy.


### 2026-11-05 14:51 UTC — Six-capacity native sweep complete

Completed all 18 predeclared cases (ram/slim/selective at 96, 112, 128, 160, 192, 256 MiB). 15/18 executed all nine functional assertions successfully. ram/256, selective/112 and slim/96 retained public circuit/transfer/reconnection BLOCKED results, not memory failures. Every case met the post-boot 20%/16 MiB RAM margin; no swap, direct reclaim stalls or OOM kills. Qualified ram and selective passed at 96 MiB with approximately 43.5 MiB available. These short tests do not establish long-duration reliability. Explicit state exhaustion in slim/192, and small transient headroom elsewhere, prevent recommending the original 16 MiB state allocation for robust deployment. Exact configuration/library inventories, raw-result copies and recomputed CPU/I/O profiles are in reports/capacity-2026-11-05. Nine fixed interleaved public trials per image at 128 MiB began at 14:51 UTC; the state32/bounded-queue complete build and three cold starts per selected RAM follow sequentially. No accepted baseline or threshold was replaced.


Before any capacity32 build/run, added the separately named native-one-endpoint-v2-queue-reserve workload: retain an additional 32 MiB resident tmpfs reservation after unreserved timings, verify it through Shmem, and repeat local TCP plus the full bidirectional integrity workload. The planning margin includes this stage; it is not represented as observed Tor queue occupancy. Reserved rates are kept separate and v1 results remain unchanged. Audit of retained notices found explicit ENOSPC writes in at least 12/18 v1 cases, including some with no sampled zero-free-space event, so full notice capture is mandatory for the new cohort.


Before the capacity32 build started, selected full O2 userspace for that practical hardware candidate, preserving the qualified ram package/code settings with XZ kernel compression. The preserved ram root disk is only 28 KiB larger than selective, while its libraries retain the qualified settings. This is a pre-build trade-off hypothesis, not a measured new image size or a claim that Os caused the public-network regression. The 24.6455 MiB selective profile remains intact for all nine public repeats. capacity32 still has 32 MiB state, explicit 32 MiB queue budget and the separately named resident-reservation workload. Earlier preliminary capacity32 descriptions based on selective Os userspace are superseded before measurements.


Effective-config audit: all three preserved kernels resolve SQUASHFS_FRAGMENT_CACHE_SIZE to 3, despite ram/selective fragments requesting 1. Linux 6.18.7 exposes that setting only under SQUASHFS_EMBEDDED, which was not enabled. Do not attribute measured improvements to a one-fragment cache. ram/selective use FILE_DIRECT and slim FILE_CACHE; exact effective diffs are archived. The still-unbuilt capacity32 profile now has its own kernel fragment requesting the actual value 3, retaining effective qualified behavior. No preserved kernel image or old fragment was changed.


The queued offline cell-operation control now isolates all three unchanged upstream Tor benchmark binaries against the O2 and Os builds of the same OpenSSL 3.6.5 in one fixed ram/128 MiB VM (two fixed reversed blocks, 12 processes). Only each diagnostic process selects those pinned shared objects; no production Tor service or image is altered. API/configure options/source version are identical and both builds have complete public-image regression evidence. This avoids confounding library flags with different kernel/root settings. It measures upstream Tor1/CGO/POLYVAL execution, not end-to-end local Tor throughput, and cannot qualify missing online tests. No local cell measurements have started yet.


### 2026-11-05 16:24 UTC — Public repetitions and practical capacity build

All 27 frozen public regression attempts completed at 128 MiB (nine per preserved image); all mandatory functions and daemon assertions passed. Fresh median throughput is 191.97 KiB/s for ram, 203.21 for slim, 226.97 for selective. The earlier 43% slim loss did not reproduce against fresh ram, but the paired intervals cross the 0.80 boundary and both candidates still fail the older frozen throughput reference. Slim also fails the unchanged direct artifact limits; selective fails the fresh Onion latency limit (4.61 s versus 4.15 s). No rejection or threshold was removed.

Built capacity32 completely from the qualified O2 userland and XZ kernel: 42,652,672 logical boot-set bytes = 40.6768 MiB including 32 MiB state. Its non-state footprint is 8.6768 MiB, 12.18% below ram's 9.8799 MiB. All installed programs and libraries match ram; only Tor defaults (75 extra bytes) and the same-length build version string differ in the installed files. The frozen full validation, hardening and daemon tests passed. The default original 512 MiB comparison is NON_COMPARABLE; matched 128 MiB comparisons fail only intentional complete-image/root-disk growth. Build 20261105T160654Z-f407769f and both comparisons are archived.

The first stressed 128 and 112 MiB cold starts passed functions, reservation integrity, state and memory margins, retaining 46.06 and 31.22 MiB MemAvailable. The first 96 MiB cold start passed functions and integrity but failed the margin at 15.41 MiB, with direct reclaim observed. No OOM or swap. Three cold starts per RAM, including 256 MiB controls, remain required before the group conclusion. The 32 MiB reserve is a resident planning stress, not observed Tor queue occupancy.


### Completed capacity characterization

All twelve cold starts passed the nine public Tor functions, the 32 MiB resident reservation integrity check and the state checks. The lowest sampled available RAM was 30.73 MiB at 112 MiB and 45.61 MiB at 128 MiB. The 96 MiB runs missed the 16 MiB absolute margin and scanned 3319 pages in direct reclaim across all three boots. Public timings passed at 112 and 128 MiB. The local TCP floor was 37.19 MiB/s and both groups fell below it. The smallest full QEMU screen pass was 256 MiB.

The TCP buffer crossover completed 36 transfers in nine cold boots. The fixed-VM cell benchmark verified the selected OpenSSL library and completed 12 of 12 planned processes. The full findings, source and artifact provenance, archived failures and separate flash budget are in `reports/capacity-2026-11-05/README.md`.

### 2026-11-05 — ARMv7 capacity32 port

Built the complete ARMv7 hard-float image under Ubuntu WSL2. It uses
38.6198 MiB including the unchanged 32 MiB state partition. The non-state
boot set is 23.71% smaller than x86 capacity32. The frozen online suite
and production check passed at 128 and 256 MiB. The original baseline and
policy are preserved.

All twelve paired ARM and x86 native runs passed functions, the 32 MiB
resident reservation and RAM and state checks. ARM retained at least
58.03 MiB available at 128 MiB. ARM 128 MiB passed all local gates and
missed the public Onion latency and throughput limits. ARM 256 MiB
passed the complete QEMU screen. Select DHCOM i.MX6ULL HI00096 at
256 MiB RAM and 128 MiB NAND for the first hardware prototype. The
100ASK T113s3-PRO 128 MiB configuration remains the smaller board
candidate. [Measured ARM results](../reports/arm-2026-11-05/README.md)

### Final handover

The ARM port and its archived QEMU evidence are committed at `a7edcb8`.
The [technical handover](HANDOFF.md) and [roadmap](ROADMAP.md) define the
next physical milestone. The [executive report](../reports/executive/index.html)
uses the existing evidence and preserves the 128 MiB performance rejections.


# TASK 03 Fix A — Integer semantics — 2026-11-17

W1 now accepts finite integral JSON numbers independent of decimal/exponent
spelling, while rejecting booleans, fractions and nonfinite values. Existing
range/version constraints and legacy reports remain unchanged. Positive and
negative fixtures plus regression tests cover 1, 1.0, 2e0 and range boundaries.
Executed `python -B -m unittest discover -s tests -p test_contracts.py -v`: 26 PASS, zero skips; `git diff --check`: PASS. No build/install/download, Tor
network test or later-phase implementation. Fix A belongs to PR #2 only.


# TASK 03 Fixes B/C — W3 integration and archive consistency — 2026-11-17

W3 consumes classified W2 checks for every required tool (including the running
Python interpreter alias); raw availability cannot override MISSING,
INCOMPATIBLE, UNKNOWN or BLOCKED. W2 implementation/policy remain unchanged.
Archive hashing and bounded license inspection now share one unbuffered open
descriptor. A second full digest plus file identity/state checks rejects
in-place changes and pathname replacement without extraction or cache writes.
Windows ctime has inconsistent path/descriptor semantics; content digests and
identity remain mandatory, with ctime additionally checked on POSIX.

Executed locally on Windows/Python 3.14: W1 contracts 26 PASS; W2 dependencies
24 PASS; W3 preflight 22 PASS / 1 POSIX-only SKIPPED; full portable suite 116
discovered / 113 PASS / 3 platform SKIPPED, exit 0; Windows pipeline 6 PASS;
`git diff --check` PASS. Logs and original before/after reproductions retained
under ignored `build/task03-checks/`. The five original outdated-tool vectors
now reject BuildRequest; maxJobs 1.0 now validates. The original pathname-reopen
archive hook is no longer reached; equivalent same-descriptor mutation and
replacement regressions reject the archive. Linux CI must execute the POSIX
replacement regression before merge readiness is declared.

An initial mutation regression exposed buffered stale reads; using an
unbuffered descriptor fixed it. A subsequent positive regression exposed
Windows ctime inconsistency, handled as above. The first Windows pipeline
attempt was sandbox-blocked on temporary directories; its log is preserved,
and rerunning outside that restriction passed all six tests. No builds,
installations, downloads, public Tor tests or later-phase implementation ran.

TASK 03 CI follow-up: the initial updated W3 CI failed in the new simulated
identity test because a global stat call-count mock also intercepted Python
3.12 POSIX pathlib.resolve directory checks. The real POSIX atomic-replace
regression passed. The mock now intercepts only explicit no-follow stats of
the tested archive; unrelated filesystem calls remain real. This is a test
portability correction, with no product behavior change. Full local validation
was repeated after correction; the first CI failure remains preserved.
