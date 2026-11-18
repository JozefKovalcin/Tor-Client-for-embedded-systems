# Windows developer integration boundaries

The Windows developer stack depends on the reviewed peer candidate in PR #19.
The peer messaging, ControlPort and Run Only workflows retain their existing
entry points. Developer actions are separate and explicitly initiated.

The scanner consumes W2 host and tool probes, W1 DependencyCheck validation and
the actual UCRT64 recipe. The GUI reads its structured output. Neither
discovery nor preflight starts COMPILE.cmd, installs packages or fetches
archives. The SDK and Git have separate roles from tools invoked by the recipe.

Installation plans describe a closed set of reviewed package files. The GUI
confirms a specific plan before an installer can run. The executor accepts
typed inputs and forces local signature verification. It serializes mutations
and asks a fresh scan to establish the result. A signed package from a local
cache is the supported automatic method. Wider MSYS2 or .NET setup has an
official manual path.

Native builds call the existing windows-native pipeline from an isolated
snapshot. The Windows source manifest, patches, compiler options and package
recipe retain their existing ownership. Missing source archives block the
managed workflow. Build measurement, qualification and activation remain
separate decisions. Developer operations use owned Windows jobs.

Artifacts have immutable versioned paths and a locally checked inventory.
Activation requires its own validation and approval. A selection change must
preserve the former verified runtime and leave live sessions untouched.

## Operator workflow

Open **Developer** to scan both recipes, select a local cache with all four
pinned archives, and choose an owner-only workspace on a fixed drive. The
native recipe requires a workspace path without spaces. A ready preflight
allows an explicit build. It records observations of available memory and
disk, including the archive staging requirement. Peak compiler disk demand
depends on the host and profile.

The installer currently automates only a missing, reviewed MSYS2 Make
4.4.1-3 or UCRT64 pkgconf 1~3.0.7-1 when the exact archive and detached
signature are available locally. The explicit approval is bound to the fresh
plan and a second gate precedes the pacman transaction. Cancel before the
transaction begins. Once it starts, an ordinary UI close waits for its
result. An abrupt machine or application failure can still interrupt pacman.
Retry requires a fresh scan and approval, and an existing pacman database
lock requires manual resolution. Developer workflows perform zero downloads.

Use **Builds** to view hashes, sizes and compiler/linker flags, compare the
records and run the offline smoke tests. Approval requires a successful
pin-matched offline test plus the exact package inventory. The runtime is
copied into a new per-user versioned directory with no unlisted DLLs or
configuration. Restart Tor Manager to use an approved generation or rollback.
Managed sessions in this application hold shared leases, while activation
and rollback require an exclusive lease. A selection does not affect
an explicit `--tor-package` override, which keeps the bundled runtime pins.

Developer approval trusts the Windows user's own build host, installed
MSYS2, and write access to the application and its owner-only directories.
Source authenticity uses the reviewed source SHA-256 pins, license SHA-256
pins and Windows patch hashes. This does not add new upstream signature
verification or release signing. Approval selects a local runtime and makes
no claims about distributable binaries.
