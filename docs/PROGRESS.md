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
