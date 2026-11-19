# Native Linux source executor (W6)

This is source infrastructure, not a successful native Tor build. The selected
target remains Ubuntu 24.04 x86_64-linux-gnu / glibc 2.39. W3 and all existing
source/license pins are unchanged. No Windows source patch or PE flag is used.

## Approval boundary

`python3 -B -m scripts.native_linux.executor` is read-only by default. It runs
fresh W3 diagnostics, then hashes the fixed recipe, adapter, observed tool
bytes, source pins, source epoch and W3 input metadata. A non-ready W3 report
cannot generate an execution approval digest.

The following is an example for a separately prepared supported host; **none
of these source-build execution commands were run for TASK 04**:

```sh
python3 -B -m scripts.native_linux.executor \
  --source-dir /home/operator/tor-manager \
  --output-dir /home/operator/native-builds/new-input-run \
  --archive-cache /home/operator/verified-archives
# Review the returned executionInputSha256 and separately authorize compilation.
python3 -B -m scripts.native_linux.executor \
  --source-dir /home/operator/tor-manager \
  --output-dir /home/operator/native-builds/new-input-run \
  --archive-cache /home/operator/verified-archives \
  --execute --approved-input-sha256 EXACT_REVIEWED_DIGEST
```

`--execute` and the exact digest are explicit operator inputs, not an
authorization inferred from an old JSON report. A caller must obtain the
separate compilation authorization required by TASK 04. The executor reacquires
fresh W3 facts and fails if approval no longer matches. It never downloads or
installs missing inputs. Use an existing workspace parent owned by the build
user with mode 0700. Path components must be directories under trusted
ownership. Group- or other-writable ancestors are refused unless sticky
protection covers the child. Select a different private location if an
existing directory does not meet these conditions.

## Fixed execution behavior

- One advisory writer reservation per native workspace parent. W5 additionally
  owns host-wide RAM/job scheduling; this lock is not a global physical-host
  reservation across arbitrary directories/users.
- New exclusive mode-0700 run directory. Existing output, failed logs and partial
  sources are never cleaned, overwritten or reused. A retry needs a new output
  and task ID; there is no silent automatic resume.
- The lock and run directory are created relative to the admitted parent
  directory handle. The executor keeps a run directory handle open and uses
  Linux procfs to keep evidence and source writes bound to that run. It checks
  the named workspace around toolchain invocations and stops on identity changes.
- SHA-256 checked private snapshots of local pinned archives, with descriptor
  identity/state checks during copy and a strict byte bound at the admitted
  file length. Extraction uses unique regular files and
  directories only, rejects traversal, links/devices/duplicates, bounds member
  counts, file sizes and expanded bytes, and rechecks archive/license hashes.
- Build zlib, OpenSSL, libevent, libseccomp and Tor in that order into the isolated
  prefix. Static source-built dependency libraries retain the glibc host ABI.
  `pkg-config` searches the private prefix only. No system installation prefix.
- Linux flags: `-O2 -fPIC -fstack-protector-strong`, `_FORTIFY_SOURCE=3`,
  RELRO/NOW/NX linker flags; Tor additionally PIE and upstream compiler/linker
  hardening plus seccomp. Relay/authority are disabled as in the existing client
  profile. Crypto, TLS, randomness, SOCKS and v3 service implementation remain
  upstream. Source release/configure compatibility is unqualified until an
  authorized real build; no configure check is bypassed.
- Minimal environment, argument arrays, no supplied shell fragments. Pinned
  upstream configure/make programs are executable trusted inputs, not an OS
  sandbox. Building third-party code still needs host containment and review.
- Recheck tool bytes before each command. Fresh W3 RAM/disk checks, one make
  job, a four-hour build deadline, 30-minute command deadline and 16 MiB log cap
  bound execution. Source preparation checks cancellation and the build deadline
  during copies, extraction and hash reads. Disk headroom is rechecked between
  commands by the application.
- POSIX process-group termination on cancel/deadline/log overflow, bounded grace
  then kill. Exclusive log creation precedes spawning; all failures tear down
  children. Original nonnegative exits and termination signals stay separate.

## Outputs and integration

`events.jsonl` contains W1 `TaskProgress` records with increasing sequence,
indeterminate compiler progress, observed raw exit/signal and explicit task
state. Every attempt has a new ID. W5 can consume these events through a typed
adapter; UI views never invoke this module directly.

`build-manifest.json` records deterministic inputs, command outcomes/log digests,
partial status and hashes of produced build outputs. A source-command PASS is
only a build result. It is not runtime qualification, an accepted comparison or
a publishable package. No `BuildArtifact` is emitted by the executor because
packaging/ELF/runtime closure is a separate PR gate. Interrupted RUNNING manifests
must be reconciled as interrupted by W5; this executor never reuses them.

See [runtime packaging](PACKAGING.md) for an allowlisted archive and the
Manager workflow. Do not include the source run,
home/tmp/logs, cookies, credentials, DataDirectory or hidden-service state in a
runtime package. Frozen public Tor assertions remain an explicitly authorized
later integration gate.

## Host evidence

`reports/task04/w6-host-preflight.json` is an actual read-only WSL observation,
not synthetic readiness: Ubuntu 26.04 / glibc 2.43 is incompatible with selected
v1; required archives were absent and provenance blocked for the explicitly
observed older native checkout versus the policy/module root. No output was
created. WSL unit-test PASS cannot qualify a native desktop runtime or build.
