param(
    [Parameter(Mandatory)][ValidateSet("A", "B")][string]$Role,
    [Parameter(Mandatory)][string]$DesktopExe,
    [Parameter(Mandatory)][ValidatePattern("^[0-9a-fA-F]{64}$")][string]$DesktopSha256,
    [Parameter(Mandatory)][ValidatePattern("^[0-9a-fA-F]{64}$")][string]$DesktopAssemblySha256,
    [Parameter(Mandatory)][string]$TorPackage,
    [Parameter(Mandatory)][ValidatePattern("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}$")][string]$RunId,
    [switch]$ApprovePublicTor
)
$ErrorActionPreference = "Stop"
if (!$ApprovePublicTor) { throw "A separately approved public Tor qualification is required." }
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw "This qualification requires Windows x64."
}
$app = (Resolve-Path -LiteralPath $DesktopExe).Path
$package = (Resolve-Path -LiteralPath $TorPackage).Path
$expectedTor = "3a65024937320fb53a76615273d9cdf7718d9cf90855081dab4b73ae52d2c7f9"
$expectedThreadDll = "94334eab25e1a4043a3efdcf88636e722f387fe62fcc57c2533876a019baa04c"
function VerifiedFileHash([string]$File, [string]$Expected) {
    if ((Get-Item -LiteralPath $File).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked runtime file." }
    $observed = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($observed -ne $Expected.ToLowerInvariant()) { throw "A pinned qualification input changed." }
    return $observed
}
$appHash = VerifiedFileHash $app $DesktopSha256
$assemblyHash = VerifiedFileHash (Join-Path (Split-Path -Parent $app) "Tor.Desktop.dll") $DesktopAssemblySha256
$torHash = VerifiedFileHash (Join-Path $package "tor.exe") $expectedTor
$dllHash = VerifiedFileHash (Join-Path $package "libwinpthread-1.dll") $expectedThreadDll
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$evidence = Join-Path $repo "artifacts/windows-peer/$RunId-$Role"
$null = New-Item -ItemType Directory -Path $evidence
$state = Join-Path $evidence "private-state"
$process = Start-Process -FilePath $app -ArgumentList @("--tor-package", ('"' + $package + '"'),
    "--state-dir", ('"' + $state + '"')) -PassThru -WindowStyle Normal
@{
    role = $Role
    windowsVersion = [Environment]::OSVersion.Version.ToString()
    desktopSha256 = $appHash
    desktopAssemblySha256 = $assemblyHash
    torSha256 = $torHash
    runtimeDllSha256 = $dllHash
    launchObservedUtc = [DateTime]::UtcNow.ToString("o")
    processId = $process.Id
    result = "NOT RUN"
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence "launch.json") -Encoding UTF8
Write-Output "The GUI is open. Follow the project two-PC checklist. Keep private-state and credentials on this PC."
