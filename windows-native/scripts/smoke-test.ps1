param(
    [string]$Dist = (Join-Path $PSScriptRoot '..\dist'),
    [switch]$Online,
    [switch]$Reference,
    [int]$BootstrapTimeout = 600
)
$ErrorActionPreference = 'Stop'
$Dist = (Resolve-Path -LiteralPath $Dist).Path
$exe = Join-Path $Dist 'tor.exe'
$run = Join-Path $PSScriptRoot ('..\runtime\smoke-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $run
$run = (Resolve-Path -LiteralPath $run).Path
$data = Join-Path $run 'data'
$null = New-Item -ItemType Directory -Path $data
$rows = [System.Collections.Generic.List[object]]::new()
$metrics = @{ idle_working_set_bytes=$null; peak_working_set_bytes=$null; bootstrap_seconds=$null }
$process = $null
function Record($name, $status, $detail) { $rows.Add([pscustomobject]@{id=$name;status=$status;detail=$detail}) }
function FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $listener.Start(); $number=$listener.LocalEndpoint.Port; $listener.Stop(); return $number
}
function Control($command) {
    $client=[Net.Sockets.TcpClient]::new('127.0.0.1',$script:controlPort)
    try {
        $client.ReceiveTimeout=5000; $client.SendTimeout=5000
        $stream=$client.GetStream()
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::ASCII)
        $writer=[IO.StreamWriter]::new($stream,[Text.Encoding]::ASCII)
        $writer.NewLine="`r`n"; $writer.AutoFlush=$true
        $cookie=[IO.File]::ReadAllBytes((Join-Path $data 'control_auth_cookie'))
        $hex=([BitConverter]::ToString($cookie)).Replace('-','')
        $writer.WriteLine('AUTHENTICATE '+$hex)
        if ($reader.ReadLine() -ne '250 OK') { throw 'Cookie authentication failed' }
        $writer.WriteLine($command)
        $answer=''
        do {
            $line=$reader.ReadLine()
            if (!$line) { throw 'Control connection closed' }
            $answer += $line + "`n"
        } while ($line.Length -lt 4 -or $line[3] -ne ' ')
        if (!$line.StartsWith('250 ')) { throw 'Control command rejected' }
        return $answer
    } finally { $client.Dispose() }
}
function StartNode {
    $script:process=Start-Process -FilePath $exe -ArgumentList @('--defaults-torrc',('"'+$defaults+'"'),'-f',('"'+$config+'"')) -WorkingDirectory $run -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run ('stdout-'+[guid]::NewGuid()+'.log')) -RedirectStandardError (Join-Path $run ('stderr-'+[guid]::NewGuid()+'.log'))
    # Retain the OS process handle before a fast exit (Windows PowerShell 5.1).
    $null=$process.Handle
    $deadline=[DateTime]::UtcNow.AddSeconds(45)
    do {
        if ($process.HasExited) { throw 'Tor exited during startup; inspect retained logs' }
        try { $null=Control 'GETINFO version'; return } catch { Start-Sleep -Milliseconds 250 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Tor startup timeout'
}
function InvokeTor([string[]]$Arguments, [string]$Directory) {
    # tor.exe uses the Windows subsystem. Windows PowerShell 5.1 does not
    # reliably wait for it when using the call operator, even with 2>&1.
    $info=[Diagnostics.ProcessStartInfo]::new($exe)
    $info.Arguments=($Arguments | ForEach-Object { '"'+$_+'"' }) -join ' '
    $info.WorkingDirectory=$Directory
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $probe=[Diagnostics.Process]::new()
    try {
        $probe.StartInfo=$info
        if (!$probe.Start()) { throw 'Could not start Tor probe' }
        $stdout=$probe.StandardOutput.ReadToEndAsync()
        $stderr=$probe.StandardError.ReadToEndAsync()
        if (!$probe.WaitForExit(15000)) { $probe.Kill(); $probe.WaitForExit(); throw 'Tor probe timed out' }
        [pscustomobject]@{ExitCode=$probe.ExitCode;Text=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()}
    } finally { $probe.Dispose() }
}
try {
    # Restrict PATH to the package and Windows components: no MSYS2 or developer DLLs.
    $env:PATH=$Dist+';'+$env:SystemRoot+'\System32;'+$env:SystemRoot
    $env:OPENSSL_CONF=$null; $env:OPENSSL_MODULES=$null
    foreach ($arg in @('--version','--help')) {
        $result=InvokeTor @($arg) $run
        if ($result.ExitCode -ne 0) { throw "Tor $arg failed" }
        Record $arg 'PASS' $result.Text
    }
    foreach ($profile in @('torrc','torrc-onion-service')) {
        $result=InvokeTor @('--defaults-torrc','config\defaults-torrc','-f',('config\'+$profile),'--verify-config') $Dist
        if ($result.ExitCode -ne 0) { throw "Invalid shipped configuration: $profile" }
    }
    Record 'shipped_configs' 'PASS' 'Both deployment profiles verified by native Tor'
    $modules=InvokeTor @('--list-modules') $run
    if ($modules.ExitCode -ne 0) { throw 'Cannot inspect compiled Tor modules' }
    if (!$Reference -and ($modules.Text -match '(?m)^(relay|dirauth|dircache): yes')) {
        throw 'Optimized profile unexpectedly contains relay/authority/cache modules'
    }
    Record 'compiled_modules' 'PASS' $modules.Text
    $script:controlPort=FreePort
    $socks=FreePort
    while ($socks -eq $controlPort) { $socks=FreePort }
    $config=Join-Path $run 'torrc'
    $defaults=Join-Path $run 'defaults-torrc'
    [IO.File]::WriteAllText($defaults,"# No inherited defaults`n")
    $offline=1; if ($Online) { $offline=0 }
    [IO.File]::WriteAllText($config,@"
DataDirectory "$($data.Replace('\','/'))"
SocksPort 127.0.0.1:$socks
ControlPort 127.0.0.1:$controlPort
CookieAuthentication 1
CookieAuthFileGroupReadable 0
ClientOnly 1
SafeLogging 1
RunAsDaemon 0
Log notice stdout
DisableNetwork $offline
MaxMemInQueues 32 MB
"@)
    $bad=Join-Path $run 'invalid-torrc'
    [IO.File]::WriteAllText($bad,"ThisIsNotATorOption 1`n")
    $result=InvokeTor @('--defaults-torrc',$defaults,'-f',$bad,'--verify-config') $run
    if ($result.ExitCode -eq 0) { throw 'Invalid configuration was accepted' }
    Record 'invalid_config_rejected' 'PASS' 'Nonzero exit for an unknown option'
    $start=[Diagnostics.Stopwatch]::StartNew()
    StartNode
    Record 'startup_and_writable_state' 'PASS' 'Authenticated using generated control cookie'
    if ($Online) {
        $deadline=[DateTime]::UtcNow.AddSeconds($BootstrapTimeout)
        while ((Control 'GETINFO status/bootstrap-phase') -notmatch 'PROGRESS=100') {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Public bootstrap timed out' }
            Start-Sleep -Seconds 1
        }
        $metrics.bootstrap_seconds=$start.Elapsed.TotalSeconds
        Record 'public_bootstrap' 'PASS' 'Authenticated control reports 100%'
        $reply=& curl.exe --fail --silent --show-error --max-time 120 --socks5-hostname "127.0.0.1:$socks" https://check.torproject.org/api/ip
        if ($LASTEXITCODE -ne 0 -or !(($reply | ConvertFrom-Json).IsTor)) { throw 'SOCKS remote-DNS Tor check failed' }
        Record 'socks_remote_dns' 'PASS' 'curl --socks5-hostname uses a domain-name SOCKS request; Tor exit confirmed'
    } else {
        Record 'public_bootstrap' 'SKIPPED' 'Run with -Online'
        Record 'socks_remote_dns' 'SKIPPED' 'Run with -Online'
    }
    # Match the functional harness's ten-second idle observation interval.
    Start-Sleep -Seconds 10
    $process.Refresh()
    $metrics.idle_working_set_bytes=$process.WorkingSet64
    $metrics.peak_working_set_bytes=$process.PeakWorkingSet64
    $null=Control 'SIGNAL SHUTDOWN'
    if (!$process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Clean shutdown failed' }
    Record 'clean_shutdown' 'PASS' 'Authenticated SIGNAL SHUTDOWN returned exit code 0'
    StartNode
    $null=Control 'SIGNAL SHUTDOWN'
    if (!$process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Restart shutdown failed' }
    Record 'restart' 'PASS' 'Same writable DataDirectory restarted and shut down cleanly'
} catch {
    Record 'smoke' 'FAIL' $_.Exception.Message
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $report=@{platform='native Windows';tests=$rows.ToArray();metrics=$metrics;runtime_directory=$run}
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'results.json') -Encoding UTF8
    $rows | Format-Table id,status,detail -Wrap
    Write-Host "Evidence: $run\results.json"
}
if (@($rows | Where-Object status -eq 'FAIL').Count) { exit 1 }
exit 0
