[CmdletBinding(DefaultParameterSetName = 'Package')]
param(
    [switch] $ApprovePublicTor,
    [Parameter(ParameterSetName = 'Package')]
    [switch] $PeerToPeer,
    [Parameter(Mandatory, ParameterSetName = 'Package')]
    [string] $TorPackage,
    [Parameter(Mandatory, ParameterSetName = 'Persistent')]
    [string] $RuntimeCandidate,
    [Parameter(Mandatory)] [string] $NewStatePath
)

$ErrorActionPreference = 'Stop'
if (-not $ApprovePublicTor) {
    Write-Output 'NOT RUN: obtain separate approval for public Tor testing before passing -ApprovePublicTor.'
    exit 2
}
if (-not $IsWindows) {
    Write-Output 'BLOCKED: this workflow requires PowerShell 7 on Windows.'
    exit 2
}

$repo = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $repo 'build/task09-online-qualification'
$feed = (New-Item -ItemType Directory -Force (Join-Path $workspace 'empty-feed')).FullName
$env:DOTNET_CLI_HOME = $workspace
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:NUGET_PACKAGES = Join-Path $workspace 'packages'
$persistent = $PSCmdlet.ParameterSetName -eq 'Persistent'
$inputPath = if ($persistent) { $RuntimeCandidate } else { $TorPackage }
$runtime = (Resolve-Path -LiteralPath $inputPath).Path
$state = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($NewStatePath)

Push-Location (Join-Path $repo 'desktop')
try {
    $testName = if ($PeerToPeer -or $persistent) { 'Tor.Peer.Tests' } else { 'Tor.Online.Tests' }
    $mode = if ($persistent) { '--public-persistent-host' } else { '--public-tor' }
    $project = "tests/$testName/$testName.csproj"
    dotnet restore $project --source $feed -p:NuGetAudit=false
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet build $project --no-restore --disable-build-servers -p:UseSharedCompilation=false
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    dotnet "tests/$testName/bin/Debug/net11.0/$testName.dll" $mode --approve-public-tor $runtime $state
    exit $LASTEXITCODE
}
finally { Pop-Location }
