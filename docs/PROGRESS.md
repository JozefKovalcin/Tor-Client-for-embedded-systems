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
