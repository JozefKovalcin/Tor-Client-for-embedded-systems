# 00 — Repository audit / Phase 0

Audit date: 2026-11-16. Repository-relative paths below are rooted at this
checkout; Markdown links resolve from this directory. This is a documentation
baseline, not a new firmware, runtime or performance qualification.

## Repository and scope

| Observation | Executed evidence / disposition |
| --- | --- |
| Branch / HEAD | `main`, `018923de4376fb8c030c670fcb61124ce1386d4b` (`git log -1 --oneline`: Add native Windows source build and synchronize documentation). Matches TASK 00 planning HEAD. |
| Initial worktree | `git status --short`: empty. No reset, checkout, stash application, cleanup or commit performed. |
| Stashes | `stash@{0}`: Windows capacity drafts (4 files); `stash@{1}`: Windows performance drafts (2 files). Inspected names and diff statistics only; both retained. |
| Immutable tag | `git rev-parse baseline^{commit}` = `da7c5281145589821ceeb4f6b7aabd4d98833587`; unchanged. Sealed local artifacts were not regenerated or requalified. |
| Instructions | Root [AGENTS.md](../../AGENTS.md) is the only repository AGENTS.md found. No AGENTS.md was present in the inspected ancestors through `C:/`. No nested architecture instructions existed. |
| Authorized changes | Only the six new `docs/architecture/00-…05-*.md` files. TASK 00 is applied within the user's explicit Phase 0 request. It authorizes no application changes, installations, firmware writes or network tests. |
| Instruction reconciliation | AGENTS.md normally requires updating `docs/PROGRESS.md`. The narrower current request and TASK 00 allow only six new documents: Phase 0 evidence is recorded here; existing PROGRESS is preserved. |
| Source of truth | Current source tree and retained reports. No remote fetch or assertion that origin/main is current. Existing PROGRESS synchronization notes describe their earlier comparison, not a live remote audit. |

Required starting documents inspected: [HANDOFF](../HANDOFF.md),
[PROGRESS](../PROGRESS.md), [ROADMAP](../ROADMAP.md),
[ARM_PLATFORM](../ARM_PLATFORM.md), [Windows README](../../windows-native/README.md),
[BUILD](../../windows-native/BUILD.md), [Windows evidence](../../windows-native/evidence/README.md)
and [policy](../../config/policy.json). Additional source inspection is mapped below.

## Existing modules and entry points

| Area / languages | Exact sources / entry points | Current classification |
| --- | --- | --- |
| Buildroot integration / Kconfig, Make, shell | `br2-external/{Config.in,external.mk}`, `br2-external/configs/`, `br2-external/board/`, `br2-external/patches/` | IMPLEMENTED; Linux x86_64 and ARMv7 results VERIFIED ONLY IN SIMULATION. No PICO machine configuration. |
| Build/validation engine / Python stdlib, shell | `scripts/validate.sh` → `scripts/validate.py`; `bootstrap.py`, `benchmark.py`, `compare.py`, `common.py` | IMPLEMENTED: pinned download/extraction, input hashes, build logs, ELF checks, QEMU execution, metrics and exclusive baseline creation. |
| Functional tests / Python | `tests/run_all.py`, `tests/echo_server.py`, `tests/test_*.py`, `tests/test_two_pc_launcher.ps1` | IMPLEMENTED. Public Tor suite is separate from lightweight offline units. |
| Capacity lab / Python, C | `scripts/capacity_native.py`, `capacity_peer.py`, `build_capacity_fixture.py`, `analyze_capacity.py`; `tests/native_agent.c`, `crypto_capacity.c`, `io_probe.c` | IMPLEMENTED lab observer/host peer. `native_agent.c` is a test fixture, not an authenticated production device-management agent. |
| Performance studies / Python, C | `scripts/performance_study.py`, `study_target.py`, `analyze_study.py`, `bench_tor_cells.py`, `tests/crypto_probe.c` | IMPLEMENTED; historical comparisons and rejected experiments retained. |
| Native Windows source build / cmd, Bash, Python | `windows-native/COMPILE.cmd` → `scripts/compile.sh` → `build-windows.sh`; `scripts/prepare.py`, `scripts/package.py` under that directory | IMPLEMENTED, historically verified on one native Windows x64 host, not two physical PCs. |
| Windows runtime / cmd, PowerShell | `windows-native/scripts/start-tor.cmd`, `START_ONION.cmd`, `smoke-test.ps1`, `windows-native/two-pc-test/TEST.cmd`, `test.ps1` | PARTLY IMPLEMENTED product controls; interactive test helpers exist. Packaged `TEST.cmd`: SOCKS 9150, local server 8081, onion port 80. No GUI wizard. |
| Existing QEMU demo and Windows runner | `scripts/build_two_pc_demo.py`, `scripts/two_pc_demo/`, `scripts/windows_qemu_smoke.py` | IMPLEMENTED launchers; WSL/QEMU demonstration remains simulation. |
| CI / YAML | `.github/workflows/validate.yml` | IMPLEMENTED Linux quick checks + Kconfig; full build is manual/resource gated. Current full job defaults to x86; no Yocto or GUI lane. Not dispatched in Phase 0. |
| Native Linux desktop Tor package | Existing x86 Linux binaries are Buildroot/musl target artifacts | NOT IMPLEMENTED as a standalone desktop source-build/package adapter. Do not relabel QEMU output as a host distribution. |
| Desktop / .NET, Avalonia, orchestration | No tracked `.cs`, `.csproj` or `.sln` found | NOT IMPLEMENTED. SDK/framework/package versions UNVERIFIED and unselected. |
| Dependency wizard, device RPC, firmware deployment | No production implementation found | NOT IMPLEMENTED. Existing launchers/preflights are limited precursors, not this architecture. |
| Yocto / `meta-tor-minimal` | No tracked Yocto layer/manifest found | NOT IMPLEMENTED; no Yocto build executed. |
| PICO board, USB management, eMMC recovery | No board serial/flash qualification evidence found in reviewed reports | BLOCKED on exact hardware; nothing PHYSICALLY VERIFIED by this audit. |

## Pins, security and artifacts

| Boundary | Repository evidence / audit finding |
| --- | --- |
| Linux versions | `config/sources.lock.json`: Buildroot 2026.08.1, kernel 6.18.7, Tor 0.4.9.14. `config/upstream/*.hash` and `br2-external/patches/{linux,linux-headers}/*.hash` preserve hashes. Handoff and measured ARM inventory record GCC 15.3.0, binutils 2.45.1, musl 1.2.6, BusyBox 1.38.0, OpenSSL 3.6.5, libevent 2.1.13, zlib 1.3.2, libseccomp 2.6.1. |
| Windows sources | `windows-native/source-manifest.json` pins the same Tor/OpenSSL/libevent/zlib archives and license hashes. One Windows-only CONNECTED-before-DATA patch has patch/preimage/postimage hashes. `windows-native/PORTING.md` distinguishes it from unchanged Linux sources. |
| Toolchains differ | Windows evidence records GCC 16.2.0, binutils 2.47, CRT 14.0.0. Rolling MSYS2 packages are inventoried by `pacman -Q`, not locked by a compiler-version resolver. Do not reuse Linux flags or objects. |
| Source trust limits | Existing manifests explicitly say detached upstream signatures were not independently verified. HTTPS + pinned hashes are the recorded boundary; do not describe this as signature verification. Add future signature verification without weakening current checks. |
| Endpoint security | `br2-external/board/qemu-production/overlay/etc/tor/torrc`: UID `tor`, `/var/lib/tor`, loopback SOCKS/control, cookie auth, SafeLogging, AvoidDiskWrites. Capacity defaults set 32 MB queues; this is not an RSS cap. Windows shipped control is disabled; test harnesses enable authenticated loopback control. |
| Windows install side effect | `windows-native/scripts/compile.sh` invokes `pacman ... --noconfirm` if dependencies are missing. Future GUI must not invoke this path before explicit installation consent; direct `build-windows.sh --check-deps` is read-only. No script changed here. |
| Generated state/output | Root `.gitignore` and `windows-native/.gitignore` exclude build/download/runtime/dist and private state. Observed existing ignored build directories include ARM images, validated-images, native Windows work/downloads and failed Windows QEMU attempts. Left intact; no runtime secret directories opened. |
| Output rules | Current Linux outputs: `build/embedded/<profile>/<input-hash>/`, `reports/benchmarks/<run-id>/`; Windows: work root `<input-hash>/<timestamp-pid>/`, `windows-native/dist*`. Full trees/SDKs and private state must remain outside versioned evidence. |

## Preserved executed evidence and limits

| Evidence | Recorded result (historical, not rerun here) | Limit / failure retained |
| --- | --- | --- |
| [ARM report](../../reports/arm-2026-11-05/README.md), `build-results.json`, `online-128.json` | ARMv7 hard-float: 38.6198 MiB boot set including 32 MiB state; nine functions + production check PASS at 128/256 MiB; complete capacity screen PASS at 256 MiB | QEMU TCG, PL011/PL031/VirtIO, not i.MX hardware. ARM 128 MiB public timing/throughput limits missed. Global ARM vs x86 baseline NON_COMPARABLE. |
| [Capacity report](../../reports/capacity-2026-11-05/README.md) | x86 capacity32: 40.6768 MiB including 32 MiB state | 16 MiB state experiments had ENOSPC. Lower RAM/tuned TCP cases did not fully qualify; no threshold changed. |
| [Performance report](../../reports/performance-2026-11-05/README.md) | Historical accepted scopes coexist with candidate rejections | `fulltor-slim` 512 MiB acceptance does not erase fresh 128 MiB throughput rejection; selective/fast/slim failures remain. |
| [Native Windows](../../windows-native/evidence/README.md), `functional-static.json`, `functional-shared.json`, `functional-history.json` | Patched optimized static and shared each nine PASS on Windows 11 build 26100 | Unpatched ordering failures, patched timeouts retained. Separate memory run had reconnection BLOCKED. Windows 10, sustained reliability and real two-PC run NOT RUN. |
| [Windows QEMU smoke](../../reports/windows-qemu-2026-11-12/README.md) | ARM short smoke PASS; x86 public bootstrap deadline BLOCKED at 180 s | Not the frozen nine-assertion suite; original raw FAIL correction documented; normal bootstrap limit remains 600 s. |
| [Synchronization](../../reports/sync-2026-11-16/README.md) | Source import and offline checks documented | Not new firmware/public/physical qualification. |

Frozen mandatory IDs from `config/policy.json`:
`security_config`, `tor_config`, `tor_startup`, `socks5`, `public_bootstrap`,
`onion_v3`, `two_node`, `bidirectional_integrity`, `reconnection`.
Preserve payloads, same-identity reconnection and all timeouts.
Artifact-size maximum regressions are 0%; idle/peak process RSS 5%; bootstrap,
onion latency and throughput 20% (throughput higher is better);
`require_improvement=true`. Missing required metrics remain null and prevent
acceptance. Equivalent environment/protocol/reference is mandatory.

## Phase 0 checks executed

Host: native Windows 11 `10.0.26100`, PowerShell; `python` = CPython 3.14.2.
`python3` resolves to an inaccessible WindowsApps alias, so `python` was used
for the identical scripts. No WSL build or installation was attempted.

| Command / activity | Actual outcome |
| --- | --- |
| `git status --short`, `git log -1 --oneline`, HEAD/branch/tag inspection, `git stash list` and `git stash show --stat` | PASS; initial clean tree; HEAD/tag/stashes recorded above. |
| `python3 --version` | BLOCKED by WindowsApps alias; not a project defect. |
| `python scripts/check.py`, first sandbox run | Exit 1, 43 discovered: 37 PASS, 2 SKIPPED, 4 ERROR from sandbox temporary-file/hardlink permissions and forbidden loopback connections. Configuration/script check passed. Retained here rather than hidden by retry. |
| `python scripts/check.py`, permitted retry outside sandbox | Exit 0: 43 discovered, 41 PASS, 2 SKIPPED (Windows symlink privilege and Linux shell). No Tor binary or public network used. Windows branch omits Linux shell syntax/shellcheck checks. |
| `python -m unittest discover -s windows-native/tests -v`, first sandbox run | Exit 1: 6 methods, 8 reported errors including archive subtests/cleanup; sandbox temp-file permissions. Source/patch checks passed. |
| Same Windows pipeline command, permitted retry outside sandbox | Exit 0: 6 PASS, no skips. Synthetic PE, archive, pin and patch tests; no build/download. |
| `python scripts/check_buildroot.py` | Exit 1, BLOCKED: `Real Kconfig resolution requires Linux`. Guard exited before `source_tree()`; no download/config resolution. |
| `./scripts/validate.sh` (including offline/`--checks-only`) | NOT RUN / SKIPPED by Phase 0 scope. Validator can build, fetch sources and publish existing reports; not a substitute for the portable check. |
| Tor public tests, native builds, QEMU boots, Yocto, flashing, physical tests | NOT RUN / SKIPPED by Phase 0 scope; no new full validation claim. |
| Documentation structural review | PASS: exactly six new files; all 59 relative Markdown links resolve; eight typed interfaces present; exactly baseline/minimal Yocto image recipes; balanced fences, final newlines and no trailing whitespace. `git diff --check` PASS. Tracked inputs, HEAD, baseline tag and two-stash count unchanged. |

Read-only host resource snapshot: 16 logical CPUs, 33,377,124,352 B physical
RAM, 16,555,892,736 B available, 560,488,251,392 B free on the checkout volume.
Windows CIM query was BLOCKED; memory snapshot used `GlobalMemoryStatusEx`.
These are volatile host observations, not reserved build resources. WSL Linux
RAM, disk, swap and toolchain capabilities are UNVERIFIED. No `dotnet` found on
the current PATH; this does not prove no installation exists elsewhere.

## Reading order / deliverables

1. [01 — system architecture and eight contracts](01-system-architecture.md)
2. [02 — six dependency environments](02-dependency-matrix.md)
3. [03 — PICO and two-profile Yocto plan](03-hardware-yocto-plan.md)
4. [04 — DAG, PRs and bounded execution](04-task-dag-and-codex-execution.md)
5. [05 — decisions, risks and Phase 1 approval](05-decisions-risks-open-questions.md)

All future designs in these documents are ASSUMPTION/proposals until their
work package passes its gates. External vendor documentation establishes
documented support only, never executed hardware proof; sources and access
limits are recorded in [03](03-hardware-yocto-plan.md).

## TASK 01 review addendum (before implementation)

The original Phase 0 observations above remain historical. The user now
authorizes a separate documentation branch/commit/PR, then W1–W3 only.
Blocking contract ambiguities found and resolved before implementation:

- TaskProgress prose mentioned interruption but its state enum omitted it:
  add explicit `interrupted` and crash/lost-runner reason codes.
- DependencyCheck could not distinguish a denied/timed-out probe: use exactly
  AVAILABLE, MISSING, INCOMPATIBLE, UNKNOWN and BLOCKED. No false PASS.
- Native Linux ABI was unspecified: select Ubuntu 24.04 x86_64-linux-gnu/glibc
  2.39 as v1 reference and record exact observed revisions.
- PICO priority is now resolved: PICO-IMX6UL primary physical v1, DHCOM
  historical only. Exact SKU/carrier/DTB remain BLOCKED.

Integration is W1 → W2 → W3, with W1 owning contracts. Discovery/preflight
performs no installation, download or COMPILE.cmd execution. Yocto build RAM
reservations do not block this work. Existing HANDOFF/ROADMAP hardware entries
are historical context, superseded for new v1 planning by this explicit review.
