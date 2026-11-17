# Tor Manager for Windows x64

The GUI preview ZIP is framework-dependent. It requires a previously installed
Microsoft .NET 11 x64 runtime. Bundled Tor is **BLOCKED** in this ZIP. Clean-host
qualification of a self-contained distribution is **NOT RUN**.

1. Extract the entire ZIP to a user-owned folder. Run `Tor.Desktop.exe`.
2. Select **Demo** to explore simulated tasks without a Tor runtime.
3. For local offline integration testing, close the app and start it with
   `Tor.Desktop.exe --tor-package "C:\path\to\reviewed-tor-package"`.
   The folder must contain the exact reviewed `tor.exe` and
   `libwinpthread-1.dll` from the repository's Windows static package.
4. In **Real Offline**, confirm the runtime is verified, then select **Start Tor**.
   The dashboard reports lifecycle and authentication as observed. **Stop Tor**
   waits for the owned process to terminate. Offline Tor does not open a SOCKS
   listener or connect to the public Tor network.

The default private state folder is `%LOCALAPPDATA%\TorManagerOffline`.
`--state-dir "C:\path\to\private-state"` can select another absolute path with
an existing safe parent outside the application and runtime folders. The app
creates a fresh private directory on Start or validates the existing owner-only
scope. Keep this directory out of ZIPs and backups intended for sharing. A
restart shows interrupted tasks and requires an explicit Start.

The reviewed runtime pins come from `windows-native/evidence/package-static.json`.
The executable SHA-256 is `3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9`
and the thread library SHA-256 is `94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c`.
Packages with other hashes are rejected, and the verified runtime is staged in
private state before launch. The platform adapter uses loopback SAFECOOKIE,
`DisableNetwork 1`, `SocksPort 0` and a Windows Job Object.

## Building the local preview

Use a Windows x64 checkout containing the service, UI and packaging changes.
Run from `desktop/` with the pinned .NET 11.0.100 SDK, an existing approved
NuGet package cache and an empty local package source. Set `NUGET_PACKAGES` to
the cache. Use a fresh ignored publish folder outside the project directory.

```powershell
dotnet restore Tor.Desktop/Tor.Desktop.csproj --locked-mode --source $emptyFeed -p:NuGetAudit=false
dotnet publish Tor.Desktop/Tor.Desktop.csproj -c Release --no-restore --self-contained false -o $publishDir
python -B packaging/pack_gui_preview.py --publish $publishDir --cache $env:NUGET_PACKAGES --out $previewZip
```

The preview builder checks the package archives against the recorded SHA-512
inventory and checks copied managed/native assets byte for byte. It copies
only the app assemblies and the chosen dependency assets for Windows x64,
with package notices, a provenance manifest and per-file SHA-256 hashes.
Private state, logs, cookies, developer configuration and other architectures'
native assets have no place in the output allowlist. Use the manifest's
source commit to identify the reviewed preview build. This artifact is for
local evaluation and has no release signature.

For a self-contained release, first review the `win-x64` lock and verified
runtime packs for the pinned servicing release. Produce a fresh publish and
verify the full app and OS dependency closure. Add only the two pinned Tor
binaries and required native and .NET component licenses and notices with
their provenance. Validate the resulting package on a clean supported Windows
x64 host without a separately installed .NET runtime or developer tools.
