$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../scripts/two_pc_demo/Start-Tor-Test.ps1"

function wsl.exe {
    if ($args[0] -eq '--list') {
        $global:LASTEXITCODE = 0
        foreach ($name in @('Ubuntu-18.04', 'Ubuntu-20.04', 'My Linux', 'Debian', 'docker-desktop')) {
            # Reproduce the embedded NULs seen with WSL's UTF-16 output.
            Write-Output ($name.ToCharArray() -join "`0")
        }
    } elseif ($args -contains '/etc/os-release') {
        $global:LASTEXITCODE = 0
        switch ($args[1]) {
            'Ubuntu-18.04' { Write-Output 'ID=ubuntu'; Write-Output 'VERSION_ID="18.04"' }
            'Ubuntu-20.04' { Write-Output 'ID=ubuntu'; Write-Output 'VERSION_ID="20.04"' }
            'My Linux' { Write-Output 'ID="ubuntu"'; Write-Output 'VERSION_ID="22.04"' }
            'Debian' { Write-Output 'ID=debian'; Write-Output 'VERSION_ID="12"' }
            default { throw 'Unexpected distro probe' }
        }
    } else {
        $script:launched = @($args)
        $global:LASTEXITCODE = 23
        Write-Output 'Mock test output'
    }
}
function Read-Host { return '2' }
$found = @(Get-UbuntuDistributions)
if ($found.Count -ne 3) { throw "Wrong discovery count: $($found.Count)" }
if ($found[0].Name -ne 'Ubuntu-18.04') { throw 'Old Ubuntu was lost' }
if ($found[2].Name -ne 'My Linux') { throw 'Renamed Ubuntu was lost' }
$TestArguments = @('--role', 'A', '--onion', 'example.onion')
$code = Start-TorTest
if ($code -ne 23) { throw "Exit code was lost: $code" }
if ($script:launched[1] -ne 'Ubuntu-20.04') { throw 'Distro selection was lost' }
if (($script:launched[-4..-1] -join '|') -ne '--role|A|--onion|example.onion') { throw 'Test arguments changed' }
Write-Host 'PASS: older and renamed Ubuntu, UTF-16 listing, selection, arguments and exit code.'
