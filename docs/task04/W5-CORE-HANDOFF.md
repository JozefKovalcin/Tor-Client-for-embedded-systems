# W5 Part A — task orchestration core

Base: verified main `1120723c806a01f9a923f5c4e489bc8a7ae23798`.
Branch: `codex/task04-w5-task-core`. W4 owns the desktop SDK pins/UI; W5 owns
C# bindings and backend interfaces; W6 owns the Python Linux executor. W1
schemas remain unchanged. No tracked files overlap another agent's work.

Implemented `Tor.Contracts` W1 TaskProgress v1 binding and BCL-only `Tor.Core`:
typed commands, dependency graph admission/scheduling, state machine,
slot/memory/disk and exclusive heavy/output reservations, bounded event/log
streams, cancellation/deadline cleanup, retry revalidation, explicit interrupted
recovery, preserved unsigned Windows exit/signal fields and verified file refs.
Progress persists before launch/publication; recovery never recreates executable
commands or repeats side effects. Windows ACL/Unix permissions are checked.

The backend is independent of Avalonia. `ITaskService`/`ITaskHandler` are owned
here; W4 uses its simulated UI feed, with future integration consuming W1 JSON.
W6 command/progress adapter wiring remains a later explicit integration step.
No shell, installation, downloader or device functionality is included.

Executed on Windows x64 with user-installed .NET SDK **11.0.100**, target
`net11.0`, runtime **11.0.0**, reference packs already present. Restore used
an **empty local feed**, NuGet auditing disabled and no package downloads.
The prior absence of SDK was resolved by the user's installation, not by the
agent. No external NuGet test framework is needed.

- BCL backend build: PASS, zero warnings/errors.
- Console behavior harness: **15 groups PASS**, including DAG ordering,
  exact raw exit `0xC0000142`, cancellation cleanup, retry hashing, heavy/memory/
  ancestor-output serialization, rejected cycles/budgets, crash recovery,
  bounded logs/events, missing outputs, required-field and integer-spelling
  compatibility (bool/fraction/nonfinite/overflow rejected), and task deadline.
  A separate regression verifies unexpected adapter exit0 remains FAILED while
  preserving the actual0 through the explicit FailureReason result axis.
  Subsequent review regressions cover whole-graph rejection of an unsupported
  public command subtype, cancellation/complete adapter teardown despite a
  deterministic journal I/O failure, and W1 reasonCode reference rejection.
  All commands are now frozen before queue mutation; cancellation delivery is
  independent of persistence success; shutdown attempts every adapter and
  releases journal ownership before reporting aggregated failures.
  Final admission regression injects failure in the structured-log destination:
  the complete progress/log batch is staged and written before mutating queue,
  log ring or subscriber events. Rejection preserves old snapshots, restores
  the prior durable task journal where storage permits, and cannot execute
  rejected work after storage recovers. Failed rollback is explicitly reported.
- W1 validation of 11 representative emitted synthetic snapshots: PASS;
  all eight task states plus determinate/indeterminate progress covered.
- `python -B scripts/check.py`: **118 discovered / 115 PASS / 3 platform
  SKIPPED**, exit 0. This includes the actual compiled harness on this host.
- Existing Windows pipeline: **6 PASS**.
- `git diff --check`: PASS. Logs retained under ignored `build/task04-checks/`.

The initial default-sandbox SDK startup/build could not access its normal
CLI home/compiler service; worktree-local CLI home and compilation outside
that restriction succeeded. Initial SDK first-run output mentioned creating a
development HTTPS certificate; subsequent commands disable certificate/tool-path
initialization. No certificate/key data is committed or printed. A deadline
regression exposed automatic-cancel transition/cleanup defects; they were fixed
before the final harness passed. All generated bin/obj and package caches are
ignored, preserved and excluded from commits.

Limits: platform-neutral code has compiled/executed on Windows, not on a Linux
SDK host here. Tests skip the compiled gate explicitly if .NET11 is absent;
static fixture PASS never substitutes for that gate. Application reservations
are not OS quotas. Trusted adapters must honor cancellation and perform bounded
process-tree cleanup; the scheduler awaits them and never silently abandons
their result. A bounded ring is not a historical audit archive. Input-file
root ownership/verification does not replace adapter-specific TOCTOU protections.
No Tor/public-network/native source build/firmware/device test was run in Part A.

Next: review Part A separately before the dependent Windows Tor adapter PR.
Integrate W4 SDK policy and desktop feed only in a separately reviewed step;
no W7–W15 implementation or automatic PR merge is authorized.
