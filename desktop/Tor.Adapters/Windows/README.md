# Managed native Windows Tor foundation

This adapter reuses the existing reviewed static Windows package. Tor executable
and libwinpthread hashes in `PinnedWindowsRuntime` match the preserved
`windows-native/evidence/package-static.json`. No upstream source, Windows patch,
compiler flag, source lock, build launcher or historical report is modified.
Changing a runtime version/hash requires a separate provenance review.

`ManagedTorSession.StartOfflineAsync` rehashes and read-locks both runtime files,
copies only those verified open streams into a fresh owner-only execution
directory, then rehashes and read-locks the staged copies. It creates private
state, writes its own defaults/config, and
starts Tor directly with an argument array/minimal environment. It never invokes
COMPILE.cmd, a package manager, shell, installer or downloader. No caller-supplied
torrc or arbitrary Tor arguments are accepted. Offline sessions retain
`DisableNetwork 1`, `SocksPort 0`, loopback ephemeral ControlPort and SAFECOOKIE.

An explicit online task adds one configured IPv4 loopback SOCKS5 port. Tor starts
with `DisableNetwork 1`. After SAFECOOKIE and process ownership succeed, the
adapter enables networking through the authenticated control connection. It
requires the expected loopback listener in both the Tor response and Windows
TCP ownership table. The app's connectivity test verifies the accepted socket's
peer process before sending any SOCKS bytes, including during port reuse.
Port diagnostics test bind availability without reserving the endpoint.
Only the launched process is stopped on failure or cancellation.

The SOCKS policy accepts connections from IPv4 localhost, uses Tor's
`SafeSocks 1` for proxy-side DNS and isolates differing SOCKS credentials.
Loopback is the local access boundary. SOCKS credentials provide isolation,
and other local processes with loopback access may use the listener.
Tor is a client and does not accept relay exit traffic. Bootstrap and circuit
loss have a bounded retry window. A new session always requires user action.

The original package directory is excluded from the executable directory,
working directory and PATH used by the child. Extra DLLs, external manifests
and `.local` loader-redirection entries are never staged or searched. This
protects the verified package boundary without modifying the source package.
Windows system DLLs remain OS dependencies. The caller's user account and OS
are trusted; this is not protection against a compromised same-user process.
See [Microsoft DLL search order](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order)
and [redirection](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-redirection).

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
