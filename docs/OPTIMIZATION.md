# Build, measurement and regression contract

## Source and configuration inputs

`br2-external/` is a native Buildroot external tree. Its defconfig pins the
architecture, internal toolchain, musl, compiler series, kernel, packages,
hardening and image format. `board/qemu/linux.config` and `busybox.config` were
copied from the official pinned Buildroot archive, rather than reconstructed
from guesses. Kernel and BusyBox resolved `.config` files are retained per run.
`patches/` is the versioned global patch directory. Tor configuration templates,
tests, profiles, build scripts and benchmark policy are also versioned.

The inspected release is Buildroot 2026.08.1, which supplies Tor 0.4.9.14,
OpenSSL 3.6.5, libevent 2.1.13, zlib 1.3.2, musl 1.2.6 and BusyBox 1.38.0.
The profile selects GCC 15.x (15.3.0 in this release), binutils 2.45.1 and
kernel 6.18.7. These are source-inspected versions, not locally built versions.
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
It reuses matching output directories only for unchanged inputs. Unchanged
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
| Libraries | Unique regular shared-library files under target lib directories; symlinks/hardlinks not double counted; includes test-harness dependencies |
| Kernel | Deployed `bzImage` bytes, not uncompressed vmlinux |
| Root filesystem | `rootfs.ext2` artifact length, formatted as ext4; fixed 256 MiB capacity is reported honestly |
| Complete image | Sum of distinct kernel and filesystem boot artifacts; not a fabricated SD-card image |
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
