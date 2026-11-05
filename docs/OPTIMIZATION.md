# Build, measurement and regression contract

## Source and configuration inputs

`br2-external/` is a native Buildroot external tree. Its defconfig pins the
architecture, baseline internal toolchain or pinned derived SDK, musl, compiler series, kernel, packages,
hardening and image format. `board/qemu/linux.config` and `busybox.config` were
copied from the official pinned Buildroot archive, rather than reconstructed
from guesses. Kernel and BusyBox resolved `.config` files are retained per run.
`patches/` is the versioned global patch directory. Tor configuration templates,
tests, profiles, build scripts and benchmark policy are also versioned.

The inspected release is Buildroot 2026.08.1, which supplies Tor 0.4.9.14,
OpenSSL 3.6.5, libevent 2.1.13, zlib 1.3.2, musl 1.2.6 and BusyBox 1.38.0.
The profile selects GCC 15.x (15.3.0 in this release), binutils 2.45.1 and
kernel 6.18.7. These versions were built and executed in the 2026-11-04 Ubuntu WSL2 baseline and candidate runs.
The authoritative inputs are `config/sources.lock.json`, the upstream package
hash snapshots under `config/upstream/` and the verified archive itself.

The archive digest was computed after downloading the official HTTPS archive;
its detached PGP signature was **not** verified in this session. This trust
boundary is recorded in the lock. The downloader requires that exact digest;
all subsequent package downloads use Buildroot's forced hash checks. The lock
also fingerprints every upstream `.hash` file. Each successful build records
Buildroot `show-info`, actual download digests, compiler identity, expanded
compiler/linker flags and Tor configure options. Never disable source hashes
or cryptographic validation to make a build succeed.

To update dependencies, review upstream security/compatibility notes, retrieve
and authenticate the official archive, update the lock and relevant hash/config
snapshots together, resolve Kconfig and run the full validation. Do not silently
replace Tor or its TLS backend. OpenSSL, libevent and zlib remain mandatory
upstream Tor dependencies. No cryptographic implementation was replaced here.
Check package license manifests before redistributing images; run Buildroot's
`legal-info` target in the recorded output directory for source/license bundles.

Official references: [Buildroot external trees and configuration storage](https://buildroot.org/downloads/manual/manual.html),
[Buildroot releases](https://buildroot.org/download.html),
[Tor control protocol](https://spec.torproject.org/control-spec/commands.html).

## Pipeline and reproducibility

`scripts/validate.sh` delegates to Python's standard library. Every run:

1. Hashes all configuration, external-tree, test and script inputs, records Git
   commit and dirty diff digest, and runs repository regression tests.
2. Verifies the upstream archive and extracted source files; loads the selected
   defconfig and refuses settings dropped or changed by Kconfig.
3. Executes `make all` in `build/embedded/<profile>/<input-hash>/` with pinned
   `SOURCE_DATE_EPOCH`, UTC and C locale. Any build/test input change selects a
   fresh output directory, including incompatible toolchains or architectures.
4. Checks boot artifacts and Tor ELF hardening; records resolved configurations,
   compiler flags, dependency provenance and file hashes.
5. Boots the actual target kernel/rootfs using a disposable QEMU disk snapshot.
   There are no host-forwarded ports. Offline NAT explicitly restricts external
   connections. A host UTC RTC supplies the laboratory clock.
6. Runs the target test harness, collects runtime observations, compares the
   immutable baseline and writes JSON/CSV/Markdown even after failures.

The pipeline uses an OS lock to prevent concurrent builds/history updates. It
retains a source-input tar snapshot alongside the file-hash manifest, so dirty
configuration candidates can be reproduced. It refuses secret-like filenames
in that snapshot and refuses acceptance if inputs change during a run. It
never invokes `make clean`, `git reset`, `git clean` or recursive project deletion.
It reuses matching output directories for unchanged inputs. Explicit `--resume-run` also permits recorded host reporting/test-fixture fixes after verifying that compiled package, kernel and toolchain inputs are unchanged; it records the original and current source provenance. Configuration changes remain isolated. Unchanged
Buildroot outputs are incremental; for an independent rebuild use a separate
checkout at the same commit with the same tool inputs. Artifact hashes permit
checking byte identity. `BR2_REPRODUCIBLE` is not proof of byte identity across
untested hosts: host tool versions and filesystem/image construction can matter.
The workflow records compiler and QEMU versions and host identity; a hermetic
container build environment remains future work.

## Functional tests

The standalone harness (`tests/run_all.py`) creates two exclusive fresh private
runtime directories. It validates configurations before starting Tor, verifies
authenticated control readiness, rejects unauthenticated GETINFO, and exercises
SOCKS5 negotiation when networking is enabled. Offline mode sets DisableNetwork,
which intentionally closes Tor's SOCKS listener, so this test is SKIPPED then.
SAFECOOKIE authenticates the server before any controller
proof is sent. Listeners are loopback-only. Data directories are 0700 on POSIX.

Online tests require both nodes to reach bootstrap 100%. B hosts an explicitly
v3 Onion Service and a loopback TCP fixture. A connects through its SOCKS5
interface using the onion hostname as a SOCKS domain-name request (no ordinary
DNS resolution). The tests require an actual successful onion connection,
server-initiated binary data, SHA-256 checked text/binary/seeded random payloads
up to 1 MiB, repeated connections, forced termination/restart of B with its
existing service identity, and post-restart integrity verification.

Tests report PASS, FAIL, SKIPPED or BLOCKED with explicit durations. A bootstrap
or publication timeout is BLOCKED, not a passing result or a proven application
bug. Tor exit/configuration failures are FAIL. Failed prerequisites block
dependent tests. Offline mode skips public-network tests. Both modes are useful,
but only all mandatory PASS results can qualify a baseline/candidate.

The harness verifies its own application routing and listener/authentication
settings. It does not claim packet-level DNS-leak auditing, all side-channel
properties, USB isolation or hardware security. No private test network is
configured. Public Tor integration is not replaced by two offline daemons.

## Measurements and comparisons

| Metric | Measurement and scope |
|---|---|
| Tor executable | Bytes of stripped target `/usr/bin/tor` |
| Libraries | Unique regular shared-library files under target lib directories; symlinks/hardlinks not double counted; includes test dependencies only in development/reference images |
| Kernel | Deployed `bzImage` bytes, not uncompressed vmlinux |
| Root filesystem | Selected deployable artifact length: ext4 capacity for baseline/small/tiny/production, complete partitioned `disk.img` for compressed production |
| Complete image | Sum of distinct kernel and complete root disk artifacts; compressed variant includes its state partition and alignment exactly once; no bootloader is included in this direct-kernel QEMU boot set |
| Installed rootfs | Logical regular-file bytes in target tree; useful when fixed filesystem capacity masks size changes |
| Persistent storage | Logical bytes of both test nodes' state plus service identity, excluding harness configs/logs; VM state is ephemeral, real flash allocation unverified |
| Idle RSS/PSS | Mean sum for A+B during a fixed post-bootstrap idle window; PSS from smaps_rollup where available |
| Peak process RSS | Largest sampled simultaneous sum for A+B at 200 ms intervals; may miss short spikes |
| Per-node peaks | Kernel VmHWM and sampled PSS maxima retained separately in target JSON |
| System peak | Maximum sampled MemTotal minus MemAvailable in guest, including Python, cache and other guest processes |
| Bootstrap | Maximum elapsed process-start-to-100% for A and B, fresh state |
| Onion connection | First successful connection including descriptor availability and retry delay |
| CPU | Sum of observed process CPU ticks for initial/restarted Tor PIDs divided by observation elapsed time, expressed as percent of one CPU |
| Throughput | Application payload bytes delivered in both directions divided by framed exchange time; excludes initial connection/server greeting |

Unavailable measurements are JSON null, never zero placeholders. Exact Git
commit, timestamp, architecture, source versions, compiler/flags, input hashes,
policy digest, QEMU machine/CPU/accelerator/vCPU/RAM, QEMU version, host CPU hash,
host platform/memory, mode and timing protocol are retained. Environment and
policy mismatches produce NON_COMPARABLE. Online timings vary with public Tor
relays, load, congestion and guard selection, even on the same host. Repeat
multiple independent runs and inspect dispersion before claiming a speedup.
The threshold decision is a regression gate, not statistical evidence of cause.

`config/policy.json` is the authoritative mandatory-test and measurement list;
candidates cannot opt out by changing result flags. Any failed test or regression
of a baseline PASS prevents acceptance. Size must not increase, peak/idle RSS
have 5% margins, and key latency/throughput metrics have 20% margins. At least
one policy-controlled metric must improve. Gate thresholds are a conservative
starting policy, not a hardware performance guarantee. Changing the policy
requires a separate comparison lineage; it cannot relax an existing baseline
silently. PSS is optional because some kernels restrict access.

## Baseline preservation

No baseline was available on entry. `--record-baseline` is an explicit first-run
operation. It requires a complete working image, all required tests/measurements
and a clean Git commit. It exclusively creates `reports/baseline/` and read-only
result/checksum files. Further attempts fail without replacement, even if the
existing directory is partial or corrupt. Comparisons verify the checksum.
Readonly files/checksums prevent accidental edits, not a privileged adversary;
retain a signed external backup or immutable artifact store for durable audit.
The validator exclusively copies the boot artifacts to read-only
`build/baseline/` and records their hashes before sealing. It also creates the
lightweight Git tag `baseline` at the verified clean commit, refusing an existing
tag. Neither baseline image copies nor result files enter Git.

Back up the complete run directory, baseline results/checksum and build artifacts
together outside Git. The runtime fixture's Onion Service keys are never included
in reports/artifacts, and QEMU snapshot writes do not alter the built rootfs.
Standalone harness state under `runtime/` is ignored and is never deleted by
the scripts. Do not run it against production service identities.

## GitHub Actions and a local runner

All pushes/PRs run Python regression tests, shell syntax/ShellCheck and actual
Buildroot defconfig resolution. Complete builds run only via workflow_dispatch.
The full-build resource gate requires 40 GiB available disk and 6 GiB RAM; an
insufficient host produces an incomplete report and a failed job. Nothing is
deleted to force a hosted runner under its storage quota.

For a self-hosted runner, use a dedicated non-root Linux account, a dedicated
workspace without spaces, the README prerequisites, and labels
`self-hosted`, `linux`, `x64`, `embedded-tor`. Do not expose this runner to
untrusted pull-request jobs. The full job never runs automatically on PRs.
Choose self-hosted in workflow dispatch. Online mode needs outbound Internet
access and a correct clock; actual hardware remains a separate validation task.

Use `record_baseline=true` once. The baseline is uploaded as a separate artifact;
pin its workflow run ID and download/archive it before retention expires. A
candidate job can set `baseline_run_id` to that exact run. Restoration refuses
an existing local baseline; omit the run ID when using the preserved local one.
GitHub hosted runners may differ physically, so performance comparisons can
rightly return NON_COMPARABLE. Keep baseline and candidate on the same local
runner for equivalent emulation conditions. Uploads contain reports and Tor
diagnostics, not runtime keys, toolchains or generated binaries.

## New targets and optimization iterations

Add a separate profile/defconfig/kernel configuration per hardware architecture.
The current emulator adapter supports x86_64 direct-kernel QEMU boot only;
additional boards need explicit boot/test adapters and hardware environment IDs.
Do not label USB device support, physical Ethernet, flash endurance, entropy
quality or hardware RAM minima verified using this QEMU profile. Real hardware
must provide Ethernet, secure randomness, trusted time, persistent private Tor
state, suitable RAM and a genuine USB device controller for PC connectivity.

Prefer one optimization per change: state a hypothesis, preserve the original
configuration in Git, build/test/measure, review deltas and only retain accepted
functionality. Rejections never revert files automatically. Compiler LTO,
section GC, static linking and footprint reductions are future candidates to
measure; none has been claimed beneficial without an executed target build.


## Current WSL2 builds and production variants

The actual build host is Ubuntu 26.04.1 on WSL2, with 15 GiB available RAM,
native GCC 15.2 and QEMU 10.2.1. Build as `jozef` in
`/home/jozef/embedded-tor`, not under the Windows directory containing a space.
WSL inherits Windows PATH entries with spaces; sanitize PATH before Buildroot:

```sh
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
cd /home/jozef/embedded-tor
```

The complete baseline and four optimization iterations are recorded in
[`reports/measured/2026-11-04/README.md`](../reports/measured/2026-11-04/README.md).
Do not run `--record-baseline` again in this checkout. The immutable original
baseline is tagged `baseline`; the separate external-fixture reference does not
replace it. Nonzero exit after a successful build can still mean a conservative
public-network performance rejection; read the report rather than discarding
the build or changing thresholds.

The optimized profiles are `qemu-x86_64-small`, `qemu-x86_64-tiny`,
`qemu-x86_64-production` and `qemu-x86_64-compressed`. All use separate outputs.
Production removes target Python, development tests, GeoIP country databases,
relay/authority modules and unused tools. Country-based routing rules and relay
operation are intentionally outside this endpoint configuration. Upstream
client/onion protocols and cryptographic libraries remain in use.

### Exact SDK prerequisite

To avoid rebuilding the same cross-compiler on every compatible target
iteration, the existing validated baseline output was exported with Buildroot's
standard `sdk` target. The archive is local, ignored by Git, and required by the
optimized profiles. Its digest is checked before building:

```text
downloads/toolchains/x86_64-buildroot-linux-musl_sdk-buildroot.tar.gz
SHA256 5fd7659021ec80cf0bb47794be75776867270e672166210453acbaab89800e1e
Extracted/relocated: build/toolchains/baseline-sdk
Compiler: x86_64-buildroot-linux-musl-gcc.br_real (Buildroot -g60254c8) 15.3.0
Source baseline commit: da7c5281145589821ceeb4f6b7aabd4d98833587
```

It is already present in the current WSL2 checkout. To reconstruct this setup
from the preserved baseline output, export without modifying the baseline image:

```sh
make -C build/sources/34fb7bea8a213ffd/buildroot-2026.08.1 \
  O=/home/jozef/embedded-tor/build/embedded/qemu-x86_64/34b2e7eb0bd6128a8973 \
  BR2_EXTERNAL=/home/jozef/embedded-tor/br2-external sdk
```

Copy the resulting `images/*_sdk-buildroot.tar.gz` into
`downloads/toolchains/`, verify its digest, extract into a **new**
`build/toolchains/baseline-sdk` directory with the archive's top directory
removed, then run its `relocate-sdk.sh`. Never overwrite an existing SDK or
assume a newly built compiler/archive has the same bytes. A different SDK digest
requires an explicit provenance/profile update and fresh output directories;
compiler/ABI changes require a new compatible measurement lineage. SDK
relocation is Buildroot's supported mechanism, not a copied toolchain build tree.

### External test fixture and reference

Production images contain no Python or test suite. Validation attaches the
hash-verified original baseline rootfs as a read-only second disk, runs its exact
test harness/Python, and launches candidate `/usr/bin/tor` through a wrapper
that clears fixture library/Python paths. Candidate Tor maps are also checked
by the native service probe. This prevents accidentally measuring the original
Tor executable/libraries while reporting the smaller candidate.

The original kernel/rootfs were remeasured under this same external apparatus
before production testing. That additional immutable reference lives at
`reports/references/external-fixture/baseline/`. The exact calibration command
is saved as `reports/measured/2026-11-04/remeasure-reference.py`; it verifies
original artifact hashes, executes real public tests, refuses replacement and
only seals a complete valid result. On another host, remeasure both reference
and candidate; do not copy timing numbers and claim hardware equivalence.

```sh
./scripts/validate.sh --profile qemu-x86_64-compressed \
  --baseline-ref reports/references/external-fixture/baseline \
  --change "Next actual production optimization"
```

The extra disk and laboratory Python memory are present in both reference and
candidate runtime measurements. They are not deployed storage requirements.
Original baseline versus externally driven candidates is refused by the default
exact-environment comparison; select the explicit compatible reference.

### Immutable production root and persistent state

The compressed variant boots `bzImage` and `disk.img` directly in QEMU with
`root=/dev/vda1 ro`. The disk contains a gzip SquashFS system partition and a
16 MiB ext4 `/var/lib/tor` state partition, including MBR/alignment overhead.
`/tmp`, `/run` and `/dev/shm` use tmpfs; init does not attempt to remount the
SquashFS root writable. The native init service starts Tor as uid/gid 905,
creates mode-0700 state and run directories, and uses authenticated loopback
control and SOCKS listeners. Runtime keys/state remain outside Git. Snapshot
mode keeps test-generated keys out of distributable artifacts.

Sixteen MiB is a tested laboratory state budget, not an unlimited long-term
storage guarantee. Monitor state growth and reserve more space for other
deployment workloads. No USB-controller, physical Ethernet board, bootloader,
flash endurance, minimum RAM, suspend/resume or power-loss behavior is verified
by these QEMU runs. The native service probe verifies a real public bootstrap
and privilege drop; the full two-node suite exercises the same deployed Tor
executable/libraries with its controlled laboratory configurations.

The partition generator follows the upstream
[genimage configuration format](https://github.com/pengutronix/genimage/blob/v20/README.rst).
Run Buildroot `legal-info` before distributing binaries and preserve its source
and license obligations. No generated compiler or Linux/Tor image is committed.

## Performance recovery and 128 MiB qualification (2026-11-05)

Read `reports/performance-2026-11-05/README.md` before using a new reference.
The original baseline/tag and its historical failures remain immutable.
`qemu-x86_64-balanced` defaults to 512 MiB; the capacity study explicitly used
`--memory-mib 128`. `qemu-x86_64-ram` defaults to 128 MiB. Never compare runtime
measurements across these RAM settings. The matched 128 MiB initial control is
the accepted data-only image, not the original two-process baseline, whose RSS
alone exceeds that budget. This control substitution cannot repair a rejected
original-image performance gate; both results are reported separately.

All online profiles reuse diagnostic fixture v2 (SHA-256 pinned in profiles).
Its measurement wrapper is `scripts/study_target.py` at commit `2dc10ad`; the
functional driver comes unchanged from the read-only original baseline. The
native fixture directory `build/performance-probe-v2/` and ext4 disk are preserved.
On another host, compile `tests/crypto_probe.c` with the pinned SDK against
candidate OpenSSL, install it as `crypto-probe` beside the frozen wrapper, and
create a fresh read-only ext4 lab disk with `mke2fs -d`. Record its actual hash,
update the profile and remeasure both controls and candidates. Do not assume
ext4 UUID/time metadata regenerates byte-identical disks. The newer wrapper and
`tests/io_probe.c` support separate offline I/O/upstream-operation diagnostics;
that fixture is a different experiment and does not replace online fixture v2.

```sh
cd /home/jozef/embedded-tor
export PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
python3 scripts/check.py
python3 scripts/check_buildroot.py
./scripts/validate.sh --profile qemu-x86_64-ram \
  --baseline-ref reports/references/device128-balanced/baseline \
  --change "Describe the next actual measured change"
# Fixed images/orders are saved in each study's plan.json and build/*-matrix.json.
python3 scripts/performance_study.py --rounds 3 \
  --matrix build/ram-matrix.json --fixture build/performance-probe-v2.ext4 \
  --memory-mib 128
python3 scripts/analyze_study.py reports/benchmarks/<study-directory> \
  --output reports/<new-study> \
  --reference reports/references/device128-balanced/baseline
```

Every planned attempt contributes to the summary. Medians are component-wise,
not a fabricated single run. Any missing/blocked mandatory assertion or missing
required metric blocks acceptance. Three rounds describe observed variation;
they do not establish a tight confidence interval or a per-boot latency SLA.
The policy file still uses its original 20% performance limits and no size
growth. Host physical capacity alone tolerates <=1 MiB WSL page-reservation
noise; raw values remain recorded and guest RAM/CPU/protocol must match exactly.

Balanced/RAM candidates use upstream `-O2`, function/data section GC and linker GC with
the diagnostic `-rdynamic` export removed on musl. PIE, RELRO, SSP, CET/unwind,
TLS authentication and upstream crypto remain intact. Balanced restores kernel
SMP/`-O2`, retains trimmed devices and uses XZ SquashFS. The further RAM variant
decompresses files directly to page cache, uses one fragment buffer and removes
unused BusyBox archive codecs. Kernel XZ filesystem decoding and validation
gzip/base64 remain. Full-Tor candidates restore the original upstream modules
and linker exports; fulltor-slim retains Tor O2 with other userland at Os.

`reports/latest.md` describes the most recent published scoped qualification;
the investigation report also retains original-reference failures. Production
state was tested on disposable VM snapshots and is not bundled into artifacts.
Sixteen MiB is only the current tested state allocation; long-term growth,
power-loss recovery, physical bootloader/flash geometry and endurance require
separate deployment testing. The QEMU boot set includes kernel plus disk;
it is not a board-specific flash/firmware image.

### Final recovered and unresolved scopes

`qemu-x86_64-fulltor-slim` is 24.7354 MiB, including the same 16 MiB state.
Its original-image 512 MiB performance comparisons pass both the freshly
interleaved control and the earlier frozen original-image repeated reference.
At 128 MiB all nine mandatory tests/native probes passed in all three repeats,
but throughput preservation against the fresh RAM control is rejected. The
predeclared combined selector therefore retains qemu-x86_64-ram; it does not
silently promote a favorable reference or remove a rejection.

After a full build, the recorded 512/128 matrices can be remeasured using
`scripts/performance_study.py --rounds 3 --matrix
reports/performance-2026-11-05/final512/matrix.json --memory-mib 512` (or the
final128 matrix/128 override). Frozen fixture v2 is the script default.
The run IDs/artifact locations refer to preserved native builds; a fresh host
must build the profiles, update its matrix to its new IDs and remeasure controls.
Use analyze_study.py on the new study directory and compare.py for the explicit
matched reference; benchmark collection exiting zero is not an acceptance gate.
`reports/latest.md` intentionally shows the rejected final 128 MiB aggregate;
the accepted 512 MiB aggregate is also in optimization-history.csv.
