# Task orchestration core

This BCL-only .NET 11 component has no Avalonia, shell, installer or download
dependency. W1 v1 JSON remains authoritative. `Tor.Contracts.TaskProgress` is
a strict binding, not a replacement schema validator for other W1 contracts.
It preserves integral JSON number spellings, required fields, explicit crash
states, null progress and original unsigned Windows exit codes/POSIX signals.

`ITaskService` exposes typed graph submission, explicit execution, cancellation,
retry, durable snapshots and a bounded asynchronous progress stream. Typed
handlers implement `ITaskHandler`; no command includes shell text or environment
injection. No Windows/Linux adapter is automatically registered. All side
effects need a separately configured, reviewed adapter; submitting a command
does not grant install/download/network/device consent.

The host provides a verified-file root and an existing owner-only journal
directory (checked ACL on Windows, owner-only mode on Unix). References exclude
private-state filenames, traversal, link components and Windows device/alias
names when mapped to files. Inputs are rehashed before each attempt; successful
output claims must exactly match rehashed produced outputs. Consumers still
must bind verification to the inputs they execute; this shared root API is not
a general untrusted-filesystem sandbox.

Admission validates the complete DAG atomically, rejects cycles/unknown edges,
freezes caller collections, reserves slots/memory/disk and serializes overlapping
ancestor/descendant output scopes and heavy jobs. Defaults: 256 retained tasks,
64 inputs/32 outputs per task, 16 subscribers, 64 pending events per subscriber,
256 structured log rows and a 30-minute task cancellation deadline (explicitly
configurable up to 24 hours). Resource budgets are application reservations,
not kernel-enforced quotas. Trusted handlers must honor cancellation and perform
bounded process-tree cleanup before returning; the core awaits their completion.

Slow event consumers detect sequence gaps and resynchronize from `Snapshot()`;
terminal state persists independently of dropped notifications. Disk journals
use flushed temporary writes and atomic replacement, retain failed temporary
writes, and use a single-writer OS file lock. Logs contain allowlisted event
codes, task references and raw exit fields, never adapter stdout or exceptions.
The last 256 log events are retained as a bounded ring, not an archival audit.

On startup, unreconciled running/cancelling work becomes `interrupted` with
`app-crash`, `runner-crash` or `runner-lost`; queued work becomes blocked for
review. No commands or side effects are automatically reconstructed. Recovery
retry requires a fresh explicit command and task ID linked to the old result.

Behavior harness (uses only local SDK/reference packs; no test package):

```powershell
dotnet restore desktop/tests/Tor.Core.Tests/Tor.Core.Tests.csproj --source build/empty-feed -p:NuGetAudit=false
dotnet build desktop/tests/Tor.Core.Tests/Tor.Core.Tests.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false
dotnet desktop/tests/Tor.Core.Tests/bin/Debug/net11.0/Tor.Core.Tests.dll
```

Create the empty feed directory first. SDK version policy is owned by W4;
these independent projects target `net11.0` and require an installed matching
reference pack. `tests/test_backend_foundation.py` executes this harness when
the SDK is available and explicitly skips that compiled gate otherwise.
