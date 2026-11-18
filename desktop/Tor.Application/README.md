# Managed Windows Tor application service

`IOfflineTorService` is the UI boundary for package inspection, starting and
stopping one managed Windows session, and watching its state. The production
factory uses the W5 core, the verified Windows adapter and its SAFECOOKIE
control client. Tests can supply a handler and a typed command with fixture
hashes. The production factory always chooses the reviewed runtime pins.

`RefreshAsync` probes the chosen local package and state directory without
launching Tor. It opens an existing task journal to recover interrupted work.
`StartAsync` verifies the package again and submits one typed task. The Windows
adapter revalidates the pinned files, creates a privately staged session and
enforces `DisableNetwork 1`, `SocksPort 0` and loopback-only control. A missing
private data directory can be created on explicit Start if its parent exists.
Existing directory permissions must meet the owner-only policy.

`StartAsync` returns after submission. `WatchAsync` sends complete snapshots
of the latest typed progress, process observation and bounded structured core
log. Each subscriber can have one pending snapshot. A slow view can miss
intermediate updates and still receive the latest task and exit metadata.
The adapter reports authentication only after the SAFECOOKIE handshake and
control setup. Bootstrap is shown only when observed through the authenticated
connection. No CPU or RAM estimate is included.

`StopAsync` requests W5 cancellation and waits for the run to finish. The
adapter uses its existing authenticated shutdown, timeout and owned process
cleanup. Cancelling the caller's wait leaves requested cleanup active. The
service reports failure and blocks a new start if the adapter has reported a
PID and cannot confirm its termination. Disposal joins the same cleanup and
closes subscriptions. Task recovery never relaunches anything automatically.
Explicit restart records a new task and links a preceding non-successful task.
The core retains bounded history, and the session uses its maximum 24-hour
task deadline. Private state and state from prior attempts remain on disk.

The same service implements `IOnlineTorService`. `StartOnlineAsync` accepts only
a numbered loopback SOCKS5 port. A real offline task and a real online task
share one admission and cleanup gate. Online restart serializes the stop and
start and waits for confirmed process cleanup before the replacement. Existing
journal ownership prevents a second app using the same private state workspace
from starting another Tor session. There is no automatic relaunch on failure
or crash recovery.

Snapshots distinguish authenticated control, the observed bootstrap, an owned
local SOCKS5 listener and a usable Tor circuit. After bootstrap and circuit
confirmation, the app accepts explicit connectivity and NEWNYM requests.
An HTTPS response through the SOCKS listener is tested separately from local
listener availability. NEWNYM requests affect future streams subject to Tor's
rate limits and provide no IP-address or complete identity-isolation guarantee.
