# Managed native Windows Tor foundation

This adapter reuses the existing reviewed static Windows package. Tor executable
and libwinpthread hashes in `PinnedWindowsRuntime` match the preserved
`windows-native/evidence/package-static.json`. No upstream source, Windows patch,
compiler flag, source lock, build launcher or historical report is modified.
Changing a runtime version/hash requires a separate provenance review.

`ManagedTorSession.StartOfflineAsync` rehashes and read-locks both runtime files,
creates a fresh owner-only state directory, writes its own defaults/config, and
starts Tor directly with an argument array/minimal environment. It never invokes
COMPILE.cmd, a package manager, shell, installer or downloader. No caller-supplied
torrc or arbitrary Tor arguments are accepted. Networking is fixed disabled:
`DisableNetwork 1`, `SocksPort 0`, loopback ephemeral ControlPort and SAFECOOKIE.
There is no online-enable command in this PR.

Cookie/state/config remain within that private session scope and are neither
packaged nor exported. Existing state is never overwritten or automatically
deleted. stdout/stderr are continuously drained through fixed-size buffers and
discarded because raw output may reveal sensitive paths/identifiers. The core
logs only structured lifecycle codes and original exits; private Tor state is
not an evidence artifact. Applications must explicitly manage private-state
retention under their privacy policy.

The adapter combines Tor controller ownership with a Windows kill-on-close Job
Object ([Microsoft job documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)).
Authenticated graceful shutdown is bounded, followed by owned process-tree
termination when required. Cancellation waits for cleanup and preserves the
observed unsigned exit. Startup errors preserve observed exits when available;
unobserved exits remain null. An unexpected exit0 is an explicit adapter failure,
not success. Handles/private scopes belong to one session; no unrelated process
is enumerated or terminated.

`WindowsTorTaskHandler` is registered explicitly by the host; it accepts only the
pinned typed Windows session command, reports observed bootstrap percentages,
and returns its actual exit to the platform-neutral core. W4 does not invoke it
in this phase. A bootstrap observation is phase-specific and never an overall
build/qualification percentage.

The explicit Windows smoke tests use fresh private state and the existing
package; they exercise SAFECOOKIE, actual unauthenticated GETINFO denial, offline
bootstrap0, clean stop, unexpected termination, task cancellation, application
crash with no Tor orphan, and rejected runtime hashes. They do not execute the
public network suite, create an onion service or claim Windows10/Linux runtime
qualification. Run-only distribution and GUI wiring remain later integration.
