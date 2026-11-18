# Tor Manager desktop

The Avalonia desktop opens in **Real Offline** on Windows. This mode verifies
and starts the local pinned Tor package with networking and SOCKS disabled.
Use **Real Online** to configure a local SOCKS5 port and explicitly select
**Start Tor** to connect. Both modes share one Windows Tor session service.
Stop the active session before switching modes. Reopening the manager requires
another explicit start.

**Demo** offers the simulated task feed and the explanatory Run Only,
Developer and Embedded pages. Replay resets the simulated workspace. Real
mode shows observed session state, the latest private task records and up to
100 sanitized log events. Closing the window waits for owned process cleanup.

### Use the Online SOCKS5 listener

After selecting **Real Online**, enter the local port and select **Start Tor**.
The dashboard displays the observed `127.0.0.1` endpoint, bootstrap progress,
authenticated control, Tor version and verified executable hash. A published
listener, successful bootstrap and successful destination connectivity are
separate observations. **Test through Tor** explicitly checks a destination
through the authenticated session's SOCKS listener.

Configure each client to use SOCKS5 at the observed loopback port and enable
hostname resolution through SOCKS5. For a client supporting `socks5h://`, use
`socks5h://127.0.0.1:9150` with the port shown by the dashboard. Verify the
client's proxy DNS and bypass settings. The manager leaves the Windows system
proxy unchanged. Other processes on the same Windows host can access the
loopback listener.

**New Identity** requests Tor's rate-limited `NEWNYM` signal for new streams.
Tor can defer a request. Existing connections may continue and an exit IP can
be reused.

Preferences (mode, theme, simulation animation) are loaded from the current
user's application-data directory. Saving occurs only through Save UI
preferences; it never stores Tor configuration, cookies, keys or credentials.
Invalid/unknown settings retain their original file and use defaults.

## Versions and provenance

SDK **11.0.100**, TFM **net11.0**, runtime **11.0.0**, Avalonia,
Avalonia.Desktop and Avalonia.Themes.Fluent **12.1.6**. Direct package references
use exact version ranges. `desktop/global.json` disables SDK roll-forward and
prereleases. .NET 11 is STS; choosing the user's already installed stable SDK
avoids an extra SDK installation. Future servicing updates need a reviewed pin
and new validation; the selected versions are not a permanent support promise.

Primary metadata checked on November 17, 2026:

- [Microsoft .NET 11 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/11.0)
- [Avalonia release 12.1.6](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.6)
- [Avalonia package compatibility](https://www.nuget.org/packages/Avalonia/12.1.6),
  [Desktop](https://www.nuget.org/packages/Avalonia.Desktop/12.1.6),
  [Fluent](https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.6)

These packages target .NET 8+; their native dependency/display requirements
still apply. Intended hosts are Windows 11 x64 and Ubuntu 24.04 x86_64/glibc
2.39 with a supported desktop display. Windows 10, Linux launch and clean-host
self-contained packaging remain separate, unexecuted qualification gates.
Nothing here adds .NET to the PICO endpoint.

`dependency-provenance.json` records the metadata boundary. The user explicitly
approved the bounded NuGet restore for this project. The actual generated
`packages.lock.json` records all 25 resolved package versions/content hashes;
`resolved-package-inventory.json` records restored package source/license/repo
metadata and separately recomputed signed-archive hashes. NuGet's content hash
excludes signature data and is intentionally distinct from the complete signed
archive byte hash. Package/license metadata is not a vulnerability or native
component license audit.

The Windows build completed with zero warnings/errors and the integrator
launched the actual application for visual inspection. Linux launch and
self-contained publish remain NOT RUN; the current developer build requires
the .NET runtime installed on the host. No shipping run-only package is claimed.

## Permitted offline validation

The presentation/service harness has no NuGet PackageReference and includes
the actual UI models, settings service and fake feed source. With the pinned
SDK already installed, restore it against an **empty local package source**,
then build/run with `--no-restore`. This tests navigation, modes, status/progress
projection, raw exits, settings preservation, stream replay and cancellation;
it does not test Avalonia rendering or real Tor integration.

From the repository's `desktop/` directory, with per-worktree CLI home and
package cache configured outside tracked source:

```text
dotnet restore tests/Tor.Desktop.Tests/Tor.Desktop.Tests.csproj --source <absolute-empty-local-directory> -p:NuGetAudit=false
dotnet build tests/Tor.Desktop.Tests/Tor.Desktop.Tests.csproj --no-restore --disable-build-servers
dotnet <worktree>/build/desktop/w4-tests/bin/Debug/net11.0/Tor.Desktop.Tests.dll
```

`python -B -m unittest discover -s tests -p test_desktop_foundation.py -v`
validates every embedded example using the unchanged W1 validator, all task
and dependency classifications, exact direct pins, XML syntax and UI process
boundaries. Those checks do not substitute for a C#/XAML build and launch.

## Explicit restore/build boundary (not an automatic install)

The executed authorization covered only this project and its three pinned
Avalonia packages plus NuGet-resolved transitive compile/native dependencies
from the sole HTTPS source in `desktop/NuGet.Config`. Use isolated worktree
`build/desktop-packages` and `build/w4-dotnet-home` directories, disable .NET
telemetry/workload notifications, and use the installed SDK. Set both
`DOTNET_CLI_TELEMETRY_OPTOUT=1` and `AVALONIA_TELEMETRY_OPTOUT=1` for builds;
Avalonia's build-time opt-out is separate from the .NET CLI opt-out. No system package,
compiler, Tor source, template or workload installation is needed.

The first approved restore used `RestoreLockedMode=false` to produce the
genuine `packages.lock.json`. Its resolved versions and content hashes were
reviewed; raw cached archive SHA-512 values were also recomputed separately.
The subsequent locked restore used an empty local source and the populated
isolated cache, proving no further package retrieval was required. Future
dependency changes require a new scoped review; normal restores use locked mode.

```text
dotnet restore Tor.Desktop/Tor.Desktop.csproj --configfile NuGet.Config --locked-mode -p:NuGetAudit=false
dotnet build Tor.Desktop/Tor.Desktop.csproj --no-restore --disable-build-servers
dotnet <worktree>/build/desktop/w4/bin/Debug/net11.0/Tor.Desktop.dll
```

Disabling NuGet audit for this bounded restore avoids an additional advisory
service query; it does not claim the dependency graph has passed a vulnerability
audit. An authorized vulnerability/license audit remains a release gate.
No restore runs during discovery, startup or simulation.

## Integration seam

W1 v1 JSON schemas are authoritative. `SampleData/` contains copies of the eight
synthetic W1 examples plus a W1-validated status matrix. Internal presentation
projections are not replacement public contracts or full schema validators.
`ITaskFeed` accepts already validated/sanitized W1 progress through an adapter;
production W5 bindings and orchestration stay owned by W5. The UI dispatcher
owns collection updates. Future integration must retain sequence ordering,
bounded sanitized logs, cancellation, and actual availability/qualification
labels, without putting process or shell execution in views.
