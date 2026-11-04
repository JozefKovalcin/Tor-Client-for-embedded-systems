# Current Ubuntu WSL2 build and optimization state

The first fully validated embedded Linux baseline is sealed in
`reports/baseline/` with read-only boot artifacts in `build/baseline/` and Git
tag `baseline` (commit `da7c5281145589821ceeb4f6b7aabd4d98833587`). All nine mandatory
tests passed in QEMU, including public bootstrap, v3 onion communication,
SHA-256 bidirectional integrity and controlled crash/restart recovery.

Baseline: complete boot set 275,092,480 bytes; installed files 63,362,683 bytes;
combined A+B idle RSS 128,241,838 bytes; peak sampled process RSS 130,019,328
bytes; peak guest used memory 160,100,352 bytes. CPU observation: 16.88% of
one guest CPU averaged over the measured run. These are emulated observations,
not hardware minimum claims.

Iteration 1 was built and tested: omit 26,375,711 bytes of unused GeoIP country data
and authority/diagnostic helper tools, then right-size the filesystem to 64 MiB.
The Tor executable, cryptography, kernel and hardening are preserved. The exact
baseline compiler is reused as a hash-recorded SDK in a fresh output directory.
All nine functional tests passed. Complete image bytes fell 73.185%; combined
idle Tor RSS fell 58.730% and peak sampled Tor RSS fell 56.576%. The candidate
was REJECTED by the unchanged latency gate (3.197 s versus 2.283 s initial onion
connection), so these are measured footprint reductions, not an automatic
optimization acceptance or a causal performance claim. The image is preserved
in `build/iterations/01-data/`.

Iteration 2 is in progress: size compiler flags, upstream client-only Tor modules,
and a uniprocessor kernel without unused display/audio/USB-host/storage drivers.
ASLR, stack protection, seccomp, networking, entropy and ext4 are retained. The
root filesystem is 48 MiB. No result is claimed until the build and tests finish.

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
