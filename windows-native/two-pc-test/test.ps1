param([ValidateSet('A','B')][string]$SelectedPC,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
$exitCode=0
$root=$PSScriptRoot
$tor=$null; $server=$null; $controlPort=0; $data=Join-Path $root 'data-onion'
function PortBusy([int]$number) {
    $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$number)
    try { $probe.Start(); return $false } catch { return $true } finally { $probe.Stop() }
}
function Control([string]$command) {
    $client=[Net.Sockets.TcpClient]::new('127.0.0.1',$controlPort)
    try {
        $client.ReceiveTimeout=5000; $client.SendTimeout=5000
        $stream=$client.GetStream()
        $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::ASCII)
        $writer=[IO.StreamWriter]::new($stream,[Text.Encoding]::ASCII)
        $writer.NewLine="`r`n"; $writer.AutoFlush=$true
        $cookie=[IO.File]::ReadAllBytes((Join-Path $data 'control_auth_cookie'))
        if ($cookie.Length -ne 32) { throw 'Neplatny riadiaci cookie.' }
        $writer.WriteLine('AUTHENTICATE '+([BitConverter]::ToString($cookie)).Replace('-',''))
        if ($reader.ReadLine() -ne '250 OK') { throw 'Overenie riadenia zlyhalo.' }
        $writer.WriteLine($command); $response=''
        do {
            $line=$reader.ReadLine()
            if (!$line) { throw 'Riadiace spojenie sa zavrelo.' }
            $response+=$line+"`n"
        } while ($line.Length -lt 4 -or $line[3] -ne ' ')
        if (!$line.StartsWith('250 ')) { throw 'Tor odmietol riadiaci prikaz.' }
        return $response
    } finally { $client.Dispose() }
}
try {
    if ($SelectedPC) { $choice=$SelectedPC } else {
        Write-Host '1 - PC A'
        Write-Host '2 - PC B'
        $choice=Read-Host 'Vyber PC'
    }
    switch ($choice.Trim().ToUpperInvariant()) {
        '1' { $role='A'; $other='B' }
        'A' { $role='A'; $other='B' }
        '2' { $role='B'; $other='A' }
        'B' { $role='B'; $other='A' }
        default { throw 'Vyber 1 alebo 2.' }
    }
    if (!(Test-Path -LiteralPath (Join-Path $root 'tor.exe'))) { throw 'TEST.cmd spusti z priecinka dist po kompilacii.' }
    foreach ($port in @(8081,9150)) {
        if (PortBusy $port) { throw "Port $port je obsadeny. Ukonci predchadzajuci Tor alebo testovaci server." }
    }
    $probe=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    $probe.Start(); $controlPort=$probe.LocalEndpoint.Port; $probe.Stop()
    $logs=Join-Path $root ('test-results\'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $logs
    $config=Join-Path $logs 'torrc'
    $onionDirectory=Join-Path $root 'onion-service'
    [IO.File]::WriteAllText($config,@"
DataDirectory "$($data.Replace([char]92,[char]47))"
SocksPort 127.0.0.1:9150
ControlPort 127.0.0.1:$controlPort
CookieAuthentication 1
CookieAuthFileGroupReadable 0
ClientOnly 1
SafeLogging 1
AvoidDiskWrites 1
RunAsDaemon 0
Log notice stdout
MaxMemInQueues 32 MB
DisableNetwork $(if ($CheckOnly) { 1 } else { 0 })
GeoIPFile ""
GeoIPv6File ""
HiddenServiceDir "$($onionDirectory.Replace([char]92,[char]47))"
HiddenServiceVersion 3
HiddenServicePort 80 127.0.0.1:8081
"@)
    $server=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $root 'test-server.ps1')+'"'),'-Port','8081') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'server.log') -RedirectStandardError (Join-Path $logs 'server-error.log')
    $null=$server.Handle
    $defaults=Join-Path $root 'config\defaults-torrc'
    $tor=Start-Process -FilePath (Join-Path $root 'tor.exe') -ArgumentList @('--defaults-torrc',('"'+$defaults+'"'),'-f',('"'+$config+'"')) -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logs 'tor.log') -RedirectStandardError (Join-Path $logs 'tor-error.log')
    $null=$tor.Handle
    Write-Host "PC $role - spustam Tor a server 8081..."
    $deadline=[DateTime]::UtcNow.AddSeconds(600); $previous=-1
    while ($true) {
        if ($tor.HasExited -or $server.HasExited) { throw "Tor alebo server sa ukoncil. Zaznamy: $logs" }
        $response=''
        try { $response=Control 'GETINFO status/bootstrap-phase' } catch { }
        if ($response -match 'PROGRESS=(\d+)') {
            $progress=[int]$Matches[1]
            if ($progress -ne $previous) { Write-Host "Tor: $progress %"; $previous=$progress }
            if ($progress -eq 100 -or $CheckOnly) { break }
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw "Tor nedosiahol 100 %. Zaznamy: $logs" }
        Start-Sleep -Milliseconds 500
    }
    $hostname=Join-Path $onionDirectory 'hostname'
    if (!(Test-Path -LiteralPath $hostname)) { throw 'Tor nevytvoril onion adresu.' }
    $own=[IO.File]::ReadAllText($hostname).Trim()
    if ($CheckOnly) {
        Start-Sleep -Seconds 1
        & curl.exe --fail --silent --show-error --noproxy '*' --max-time 10 'http://127.0.0.1:8081/health'
        if ($LASTEXITCODE -ne 0) { throw 'Server na porte 8081 nie je pripraveny.' }
        Write-Host 'CHECK PASS: lokalny server 8081, Tor a overene riadenie. Verejna siet sa netestovala.'
        return
    }
    Write-Host "PC $role - tvoja adresa:" -ForegroundColor Cyan
    Write-Host $own -ForegroundColor Cyan
    Write-Host "Na druhom PC vyber PC $other. Vymen si s nim iba onion adresu."
    Write-Host 'Prve zverejnenie sluzby moze trvat niekolko minut. Koniec: Q.'
    $last=''
    while ($true) {
        $peer=Read-Host "Adresa PC $other (Enter = opakovat, Q = koniec)"
        if ($peer.Trim().ToUpperInvariant() -eq 'Q') { break }
        if (!$peer.Trim()) { $peer=$last }
        if (!$peer) { continue }
        $last=$peer.Trim()
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'test-peer.ps1') -Onion $last -SocksPort 9150
    }
} catch {
    Write-Host ('CHYBA: '+$_.Exception.Message) -ForegroundColor Red
    $exitCode=1
} finally {
    if ($tor -and !$tor.HasExited) {
        try { $null=Control 'SIGNAL SHUTDOWN'; $null=$tor.WaitForExit(15000) } catch { }
        if (!$tor.HasExited) { $tor.Kill(); $tor.WaitForExit() }
    }
    if ($server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit() }
}
exit $exitCode
