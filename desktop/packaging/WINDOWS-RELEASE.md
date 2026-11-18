# Tor Manager for Windows x64

Extract the entire ZIP and run `Tor.Desktop.exe`. The package includes .NET
and the native Tor client. Verify the ZIP SHA-256 against the accompanying
release evidence before extracting it. `manifest.json` inventories every
payload with its byte count and SHA-256.

1. In **Real Offline**, start Tor and check that the dashboard reports a
   verified runtime and authenticated Control. Select **Stop Tor** when done.
2. For peer communication, select **Real Online**, start Tor and wait for
   bootstrap. On **Peers**, create the local TLS identity and verify the
   other peer's full fingerprint over a trusted channel before pairing.
   The host can create a saved onion address and share it with the contact.
3. Use **Developer** for dependency checks, approved local installation
   plans, building from verified sources and testing or exporting builds.
   Use **Settings** to choose the bundled or approved runtime for the next
   launch. Stop Tor before approving a build or rolling back a runtime.

The default writable state is under `%LOCALAPPDATA%\TorManagerOffline`.
For an isolated instance, pass `--state-dir` with an absolute path outside
the extracted directory. Keep that directory private. It can contain Tor
identities, contact pins and encrypted message state.

## Build the local candidate

Run from the repository root on Windows x64 with SDK 11.0.100. Verify the
local NuGet feed against the application package inventory and
`windows-runtime-packs.json` before restoring. Supply a previously verified
static Tor package with its original source manifest and license files.

```powershell
dotnet restore desktop/Tor.Desktop/Tor.Desktop.csproj --locked-mode -p:RuntimeIdentifier=win-x64 -p:SelfContained=true --source $feed --packages $cache -p:NuGetAudit=false
dotnet publish desktop/Tor.Desktop/Tor.Desktop.csproj -c Release -r win-x64 --self-contained --no-restore -p:DebugType=None -p:DebugSymbols=false -o $publish
python -B -m desktop.packaging.pack_windows_release --publish $publish --feed $feed --tor-runtime $tor --objdump $objdump --system32 "$env:SystemRoot\System32" --version $version --out $zip
```

The packager checks the reviewed assets, signed archive hashes, framework
runtime list, bundled licenses and native DLL import closure on the local
host. It packages the selected files, then verifies the ZIP entries against
the inventory. Clean-host and public Tor qualification require separate runs.
