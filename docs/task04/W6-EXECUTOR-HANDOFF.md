# W6 source executor handoff — TASK 04, 2026-11-17

Base: verified merged main `1120723c806a01f9a923f5c4e489bc8a7ae23798`.
Branch: `codex/task04-w6-linux-executor`. No existing W1–W3 implementation,
schema, source lock, Tor/Windows patch, Buildroot configuration, policy or
historical evidence was edited. Packaging follows a separate dependent PR.

## Implementation

- `scripts/native_linux/executor.py`: read-only default CLI; fresh W3
  qualification; deterministic exact-approval digest; validated build-mode
  W1 BuildRequest and W1 TaskProgress events; no installer or downloader.
- Pinned local source snapshots and safe bounded extraction; unique regular
  files/directories, no links/devices/traversal/duplicate entries; post-copy
  archive and extracted-license verification. Snapshot and failed output stay
  in a new exclusive private run.
- Fixed Linux recipe for zlib/OpenSSL/libevent/libseccomp/Tor into a private
  prefix, preserved upstream security features and explicit hardening/PIE,
  private-only pkg-config, fixed argv/minimal environment, one make job.
- Verify exact tool bytes before commands; workspace writer lock; source/log/
  deadline budgets and between-stage disk headroom checks; cancellation with
  POSIX process-group teardown and retained original exit/signal/log digest.
- Exclusive log open precedes process start. `finally` tears down the process
  after any subsequent error, including reader-thread start failure.
- Partial build manifests are distinct from publishable W1 BuildArtifact.
  No package or qualified ELF artifact is emitted here; runtime, frozen online
  assertions and comparison acceptance remain NOT RUN.
- `scripts/native_linux/EXECUTOR.md`: precise approval/recipe/trust/resource
  boundaries and future commands, explicitly not executed.

## Actual host qualification — BLOCKED

`reports/task04/w6-host-preflight.json` preserves the parent's actual read-only
W3 probe. Host is WSL2, Ubuntu **26.04**, glibc **2.43**, x86_64, 16 CPUs,
16,284,250,112 B RAM total, 15,515,529,216 B available and 934,258,556,928 B
free disk at observation. This is incompatible with selected Ubuntu 24.04 /
glibc 2.39. GCC/pkg-config requirements are unmet; all five pinned archives
were missing in the explicitly checked native cache. Source provenance was
BLOCKED because the observed native checkout and policy/module root differ.
No output was created, source fetched, dependency installed or Tor built.
These facts are observations, not host qualification or a reservation.

## Executed validation

Only synthetic archives and tiny Python processes were executed; no source
configure/make command was run against any real Tor/dependency source.

| Check | Executed result |
| --- | --- |
| Focused W6 unittest, Windows | 19 discovered / 17 PASS / 2 POSIX SKIPPED |
| Focused W6 unittest, available WSL | 19 PASS / 0 skipped |
| Full portable `python -B scripts/check.py`, Windows | 135 discovered / 130 PASS / 5 platform SKIPPED |
| Full portable suite, native `/tmp` WSL input snapshot | 135 discovered / 132 PASS / 3 Windows SKIPPED |
| Unchanged `windows-native/tests` | 6 PASS |
| `git diff --check` | PASS |

Logs remain in ignored `build/task04-checks/`. Initial new tests found a
Windows path rendering assertion and duplicated inherited test discovery;
both were corrected before the final 19-case run. A first full WSL run on
`/mnt/c` failed the existing W3 nanosecond-mtime restoration assertion due to
DrvFS precision (the archive rejection still passed). That failure log is
preserved. No old test was changed or weakened. An isolated native Linux test
snapshot `/tmp/tor-task04-w6-2a2d3s2o` passed the full suite. This snapshot is
unit-test input only, not source-build or supported-ABI qualification evidence.

New tests cover stale/wrong approval, blocked W3 facts, recipe hardening,
changed tool bytes, hash/license/snapshot tampering, traversal/link/device/
duplicate/budget rejection, original exit 23, output budget and deadline,
descendant cancellation, log-open/no-spawn, thread-error teardown, successful
synthetic DAG/output hashes, failure exit 37 with no later commands and
pre-cancellation with no process launch. Emitted progress and prepared request
validate under unchanged W1 contracts.

## Risks, dependencies and next integration

- Real Linux source compilation **BLOCKED / NOT RUN**: supported 24.04/2.39
  host, compatible tools and cached exact archives are absent; separate
  explicit compilation/download/install authorization is required.
- Release configure flags, upstream archive link policy and actual ABI/ELF/
  runtime closure are unverified until that gate. Rejection is fail-closed;
  no feature/security fallback or QA bypass exists.
- W3 resource snapshots and per-workspace lock are not kernel RAM/disk or
  cross-user/global-host reservations. W5 owns admission of host resources.
- Pinned configure/make scripts execute trusted third-party code; argv
  allowlisting is not an OS sandbox. Use an appropriately isolated runner.
- Independent source signatures remain unverified under existing hash trust.
- W5 should launch this explicit adapter command and consume W1 progress,
  raw outcomes and verified manifest refs. W4 stays simulated until a later
  reviewed integration; UI never executes shell strings.
- No W7–W15 work, full firmware, public Tor, deployment or flashing occurred.

Progress insertion for the integrator: W6 adds opt-in exact-input native
source execution infrastructure and synthetic process/archive regression
coverage; actual WSL observation is incompatible/missing inputs, so real
build and package/runtime qualification remain BLOCKED/NOT RUN.

## Real Linux CI observation follow-up

A Linux-only offline smoke now executes the actual W3 probes on whichever
Linux test host is running. It records host/ABI, actual tool versions/statuses,
resources and source/archive statuses in ignored
`build/task04-checks/native-ci-preflight.json` and the job-log sentinel
`W6_NATIVE_PREFLIGHT_OBSERVATION=`. Source probing uses the actual checkout;
the cache is an explicitly empty temporary test cache, so MISSING applies to
that cache only and is not a global archive-inventory claim. The test proves
no build output/cache mutation and never calls the executor.

The existing CI runner may provide real Ubuntu 24.04 / glibc 2.39 environment
observation even though the available local WSL host differs. Supported-host
matching is recorded from observed facts, not assumed from the workflow label.
The integrator must inspect that CI output before claiming supported host
facts. Source build, complete qualification and runtime gates remain NOT RUN.
This adds one Linux-only case: Windows now 20 discovered / 17 PASS / 3 skipped;
CI/Linux should execute the case rather than skip it.
