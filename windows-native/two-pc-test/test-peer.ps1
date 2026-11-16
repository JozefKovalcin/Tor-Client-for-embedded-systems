param([string]$Onion,[ValidateRange(1,65535)][int]$SocksPort=9150)
$ErrorActionPreference='Stop'
function HashFile([string]$path) {
    $hash=[Security.Cryptography.SHA256]::Create()
    $file=[IO.File]::OpenRead($path)
    try { return ([BitConverter]::ToString($hash.ComputeHash($file))).Replace('-','') }
    finally { $file.Dispose(); $hash.Dispose() }
}
try {
    if (!$Onion) { $Onion=Read-Host 'Vloz onion adresu DRUHEHO pocitaca (z jeho suboru onion-service\hostname)' }
    $Onion=$Onion.Trim().ToLowerInvariant()
    if ($Onion -notmatch '^[a-z2-7]{56}\.onion$') { throw 'Vloz iba 56-znakovu adresu .onion, bez http:// a bez lomky.' }
    $ownAddress=Join-Path $PSScriptRoot 'onion-service\hostname'
    if ((Test-Path -LiteralPath $ownAddress) -and ([IO.File]::ReadAllText($ownAddress).Trim() -eq $Onion)) {
        throw 'Toto je vlastna onion adresa. Pouzi adresu druheho PC.'
    }
    $null=Get-Command curl.exe -ErrorAction Stop
    $work=Join-Path $env:TEMP ('TorTwoPcTest\'+[guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $work
    $healthFile=Join-Path $work 'health.txt'
    $inputFile=Join-Path $work 'sent.bin'; $outputFile=Join-Path $work 'received.bin'
    $proxy="127.0.0.1:$SocksPort"
    Write-Host 'Overujem spojenie cez verejnu Tor siet...'
    # A nonempty no-proxy override works with Windows PowerShell 5.1 too.
    # The validated .onion target cannot match localhost, so it always uses Tor.
    & curl.exe --fail --silent --show-error --noproxy localhost --socks5-hostname $proxy --connect-timeout 180 --max-time 300 --output $healthFile "http://$Onion/health"
    if ($LASTEXITCODE -ne 0) { throw 'Onion sluzba nie je dostupna. Skontroluj Tor 100%, testovaci server a adresu. Pri novej sluzbe pockaj a zopakuj test.' }
    if ([IO.File]::ReadAllText($healthFile).Trim() -ne 'TOR TEST SERVER OK') { throw 'Odpoved nezodpoveda testovaciemu serveru.' }
    $payload=New-Object byte[] 1048576
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($payload) } finally { $rng.Dispose() }
    [IO.File]::WriteAllBytes($inputFile,$payload)
    Write-Host 'Posielam 1 MiB a prijimam rovnake data spat...'
    & curl.exe --fail --silent --show-error --noproxy localhost --socks5-hostname $proxy --connect-timeout 180 --max-time 600 --header 'Expect:' --data-binary ('@'+$inputFile) --output $outputFile "http://$Onion/echo"
    if ($LASTEXITCODE -ne 0) { throw 'Prenos cez onion spojenie zlyhal.' }
    $sent=HashFile $inputFile
    $received=HashFile $outputFile
    if ((Get-Item -LiteralPath $outputFile).Length -ne 1048576 -or $sent -ne $received) { throw 'FAIL: data sa nezhoduju.' }
    Write-Host 'PASS: 1 MiB tam aj spat cez Tor, velkost aj SHA-256 sa zhoduju.' -ForegroundColor Green
    Write-Host "SHA-256: $sent"
    Write-Host "Testovacie subory: $work"
} catch {
    Write-Host ('FAIL: '+$_.Exception.Message) -ForegroundColor Red
    exit 1
}
