param([Parameter(ValueFromRemainingArguments = $true)][string[]]$TestArguments)

function Get-UbuntuDistributions {
    # WSL emits UTF-16 on some Windows versions; remove NULs after capture.
    $names = @(& wsl.exe --list --quiet 2>$null)
    if ($LASTEXITCODE -ne 0) { return }
    foreach ($line in $names) {
        $name = ($line -replace "`0", '').Trim()
        if (-not $name -or $name -match '^docker-desktop(?:-data)?$') { continue }
        $release = @(& wsl.exe -d $name --exec /bin/cat /etc/os-release 2>$null)
        if ($LASTEXITCODE -ne 0) { continue }
        $text = ($release -join "`n") -replace "`0", ''
        if ($text -notmatch '(?m)^ID=["'']?ubuntu["'']?\s*$') { continue }
        $version = '0.0'
        if ($text -match '(?m)^VERSION_ID=["'']?([0-9.]+)') { $version = $Matches[1] }
        [pscustomobject]@{ Name = $name; Version = [version]$version }
    }
}

function Start-TorTest {
    if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
        Write-Host 'Chyba WSL2. Najprv nainstaluj WSL2 s Ubuntu: https://learn.microsoft.com/windows/wsl/install'
        return 1
    }
    $distros = @(Get-UbuntuDistributions | Sort-Object Version -Descending)
    if ($distros.Count -eq 0) {
        Write-Host 'Nenaslo sa Ubuntu vo WSL. Nainstaluj ho prikazom: wsl --install -d Ubuntu-24.04'
        return 1
    }
    $selected = 0
    if ($distros.Count -gt 1) {
        Write-Host 'Dostupne instalacie Ubuntu (podporovane su aj starsie a premenovane):'
        for ($index = 0; $index -lt $distros.Count; $index++) {
            Write-Host ('  {0}. {1} (Ubuntu {2})' -f ($index + 1), $distros[$index].Name, $distros[$index].Version)
        }
        $answer = Read-Host 'Vyber cislo [1]'
        if ($answer.Trim()) {
            $number = 0
            if (-not [int]::TryParse($answer, [ref]$number) -or $number -lt 1 -or $number -gt $distros.Count) {
                Write-Host 'Neplatny vyber. Spusti balik znova.'
                return 1
            }
            $selected = $number - 1
        }
    }
    $distro = $distros[$selected].Name
    Write-Host ('Pouzivam {0}. Pred testom overim Python a QEMU.' -f $distro)
    & wsl.exe -d $distro --cd $PSScriptRoot --exec /bin/bash ./run-wsl.sh @TestArguments | Out-Host
    return $LASTEXITCODE
}

# Dot sourcing exposes the discovery functions for read-only verification.
if ($MyInvocation.InvocationName -ne '.') {
    try { exit (Start-TorTest) }
    catch { Write-Host ('Spustenie zlyhalo: ' + $_.Exception.Message); exit 1 }
}
