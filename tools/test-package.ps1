<#
.SYNOPSIS
Tests otter-net's Windows packages the way a user gets them, as on a machine
without PowerShell: installs the MSI (after an older one, to test the
upgrade), runs the installed `otter` from a new terminal's PATH with every
folder holding powershell.exe or pwsh.exe removed, uninstalls it and checks
that nothing is left behind; then runs the ZIP in place.

.DESCRIPTION
Runs on a disposable Windows (CI): it installs and uninstalls for the current
user. The script itself needs Windows PowerShell 5.1 or PowerShell 7; every
`otter` it starts gets the PATH without PowerShell, so a program that needed
the PowerShell Otter would say so ("needs the PowerShell Otter") instead of
running. Desktop windows and remote commands still go to the PowerShell Otter
on Windows; their programs are the only ones allowed to say so.

.EXAMPLE
powershell -NoProfile -File tools\test-package.ps1 -Msi artifacts\packages\otter-net-1.0.1-rc.1-win-x64.msi -OlderMsi older\otter-net-0.9.0-win-x64.msi -Zip artifacts\packages\otter-net-1.0.1-rc.1-win-x64.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [string]$OlderMsi = '',
    [string]$Zip = '',
    [string]$LogDirectory = 'msi-logs'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$Msi = (Resolve-Path $Msi).Path
if ($OlderMsi) { $OlderMsi = (Resolve-Path $OlderMsi).Path }
if ($Zip) { $Zip = (Resolve-Path $Zip).Path }
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
$LogDirectory = (Resolve-Path $LogDirectory).Path
$packageVersion = ([System.IO.File]::ReadAllText((Join-Path $root 'VERSION'))).Trim()
$numericVersion = ($packageVersion -split '-')[0]
$languageVersion = if ((Get-Content -Raw (Join-Path $root 'src\Otter.Cli\Program.cs')) -match 'const string Version = "([^"]+)"') { $Matches[1] } else { throw 'Program.Version not found.' }
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\otter-net'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-net-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $scratch | Out-Null

$script:failures = 0
$script:summary = @()
function Pass([string]$Text) { Write-Host "ok: $Text" }
function Fail([string]$Text) { Write-Host "FAIL: $Text"; $script:failures++ }
function Note([string]$Text) { Write-Host $Text; $script:summary += $Text }

function Invoke-Msiexec {
    param([string[]]$Arguments, [string]$Log)
    $process = Start-Process msiexec.exe -ArgumentList (@($Arguments) + @('/qn', '/norestart', '/l*v', "`"$Log`"")) -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) { throw "msiexec $($Arguments -join ' ') failed ($($process.ExitCode)); see $Log" }
}

# The PATH a new terminal gets (the machine's, then the user's, as Windows
# builds it), without any folder that holds Windows PowerShell or PowerShell 7.
function Get-PathWithoutPowerShell {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $folders = @(("$machine;$user" -split ';') | Where-Object { $_ } | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) })
    $kept = @($folders | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_ 'powershell.exe')) -and -not (Test-Path -LiteralPath (Join-Path $_ 'pwsh.exe')) })
    ($kept -join ';')
}

# The installed otter-net products, as Settings > Apps lists them. A per-user
# MSI is not under ...\Uninstall in HKCU: Windows Installer registers it under
# its UserData for the user's SID, which is what Settings > Apps reads.
function Get-OtterEntries {
    $sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $products = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\$sid\Products"
    @(Get-ChildItem $products -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ItemProperty (Join-Path $_.PSPath 'InstallProperties') -ErrorAction SilentlyContinue
    } | Where-Object { $_ -and $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -like 'Otter (otter-net)*' })
}

# Runs a program with the PATH without PowerShell; returns exit code and output.
function Invoke-Clean {
    param([string]$File, [string[]]$Arguments, [string]$Directory, [string]$InputText = '', [int]$TimeoutSeconds = 60)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = (@($Arguments) | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['PATH'] = $script:cleanPath
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Write($InputText); $process.StandardInput.Close() } catch { }
    $code = if ($process.WaitForExit($TimeoutSeconds * 1000)) { $process.ExitCode } else { try { $process.Kill() } catch { }; 'timeout' }
    [void]$stdout.Wait(5000); [void]$stderr.Wait(5000)
    [pscustomobject]@{ Code = $code; Out = $stdout.Result; Err = $stderr.Result }
}

# The installed or extracted otter, from a terminal with the clean PATH.
function Test-Otter {
    param([string]$Otter, [string]$Label)
    Write-Host "== $Label ($Otter)"
    $version = Invoke-Clean $Otter @('--version') $scratch
    if ($version.Out.Trim() -eq "Otter $languageVersion") { Pass "otter --version: $($version.Out.Trim())" } else { Fail "otter --version printed '$($version.Out.Trim())' $($version.Err)" }

    # tests\compat, each program in a fresh copy of the folder's files.
    $allowed = @('ui_run_create.ot', 'ui_run_create_try.ot', 'ui_run_create_unknown.ot', 'p2_remote_command.ot')
    $count = 0; $native = 0; $handed = @(); $expected = @(); $crashed = @(); $timedOut = @()
    foreach ($program in (Get-ChildItem (Join-Path $root 'tests\compat') -Filter '*.ot' | Sort-Object Name)) {
        $count++
        $work = Join-Path $scratch "run-$count"
        New-Item -ItemType Directory -Path $work | Out-Null
        Get-ChildItem (Join-Path $root 'tests\compat') -File | Copy-Item -Destination $work
        $inputPath = "$($program.FullName).input"
        $inputText = if (Test-Path -LiteralPath $inputPath) { [System.IO.File]::ReadAllText($inputPath) } else { '' }
        $result = Invoke-Clean $Otter @('run', $program.Name) $work $inputText
        if ($result.Err -match 'needs the PowerShell Otter') {
            if ($allowed -contains $program.Name) { $expected += $program.Name } else { $handed += $program.Name }
        } elseif ($result.Code -eq 'timeout') { $timedOut += $program.Name; $native++ }
        elseif ($result.Err -match 'Unhandled exception' -or ($result.Code -is [int] -and ($result.Code -lt 0 -or $result.Code -gt 255))) { $crashed += "$($program.Name)($($result.Code))" }
        else { $native++ }
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
    if (-not $handed -and -not $crashed) { Pass "tests\compat: $native of $count ran natively" }
    if ($handed) { Fail "tests\compat: needed the PowerShell Otter: $($handed -join ', ')" }
    if ($crashed) { Fail "tests\compat: crashed: $($crashed -join ', ')" }
    $extra = @()
    if ($expected) { $extra += "handed to the PowerShell Otter as on every Windows (desktop windows, remote commands; not installed here, so they stop with its message): $($expected -join ', ')" }
    if ($timedOut) { $extra += "stopped after 60 s: $($timedOut -join ', ')" }
    Note ("- ${Label}: tests\compat $native of $count native, without PowerShell" + $(if ($extra) { " (" + ($extra -join '; ') + ")" } else { '' }))

    # A new project.
    $project = Join-Path $scratch 'project'
    if (Test-Path $project) { Remove-Item -Recurse -Force $project }
    New-Item -ItemType Directory -Path $project | Out-Null
    $new = Invoke-Clean $Otter @('new', 'console', 'demo') $project
    if ($new.Code -eq 0 -and (Test-Path (Join-Path $project 'demo\otter.json'))) { Pass 'otter new console demo' } else { Fail "otter new console demo: $($new.Out) $($new.Err)" }
    $demo = Join-Path $project 'demo'
    $ran = Invoke-Clean $Otter @('run', '.') $demo
    if ($ran.Out.Trim() -eq 'Hello from demo!') { Pass "otter run . printed: $($ran.Out.Trim())" } else { Fail "otter run . printed '$($ran.Out.Trim())' $($ran.Err)" }
    $checked = Invoke-Clean $Otter @('check', '.') $demo
    if ($checked.Code -eq 0) { Pass 'otter check .' } else { Fail "otter check . ($($checked.Code)) $($checked.Out)" }
    $tested = Invoke-Clean $Otter @('test', '.') $demo
    if ($tested.Out -match '1 passed') { Pass 'otter test .' } else { Fail "otter test . printed: $($tested.Out)" }
    Note "- ${Label}: otter --version, new, run, check, test"

    $times = foreach ($i in 1..11) {
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        [void](Invoke-Clean $Otter @('--version') $scratch)
        $clock.Elapsed.TotalMilliseconds
    }
    $sorted = @($times | Sort-Object)
    Note "- ${Label}: startup (otter --version, median of 11): $([int]$sorted[5]) ms"
}

try {
    if (Test-Path $installDir) { throw "$installDir already exists on this machine." }
    if (Get-OtterEntries) { throw 'otter-net is already installed on this machine.' }

    # ---- Install (after an older version: the upgrade) ----
    if ($OlderMsi) {
        Invoke-Msiexec @('/i', "`"$OlderMsi`"") (Join-Path $LogDirectory 'install-older.log')
        Write-Host "installed the older MSI: $((Get-OtterEntries | ForEach-Object DisplayName) -join ', ')"
    }
    Invoke-Msiexec @('/i', "`"$Msi`"") (Join-Path $LogDirectory 'install.log')
    $entries = Get-OtterEntries
    if ($entries.Count -eq 1 -and $entries[0].DisplayVersion -eq $numericVersion) { Pass "Settings > Apps: one entry, $($entries[0].DisplayName) $($entries[0].DisplayVersion)" }
    else {
        Fail "Settings > Apps entries: $(($entries | ForEach-Object { "$($_.DisplayName) $($_.DisplayVersion)" }) -join '; ')"
        Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData' -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -eq 'InstallProperties' } | ForEach-Object { Get-ItemProperty $_.PSPath } |
            Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -like '*otter*' } | ForEach-Object { Write-Host "  found: $($_.PSPath) $($_.DisplayName) $($_.DisplayVersion)" }
    }
    if ($OlderMsi) { Note '- msi: upgrade from an older MSI leaves one Apps entry and one PATH entry' }
    if (Test-Path (Join-Path $installDir 'otter.exe')) { Pass "$installDir\otter.exe" } else { Fail "no $installDir\otter.exe" }
    $userPath = @(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_ })
    $mine = @($userPath | Where-Object { $_.TrimEnd('\') -eq $installDir })
    if ($mine.Count -eq 1 -and $userPath[0].TrimEnd('\') -eq $installDir) { Pass 'user PATH: the install folder, once, first' } else { Fail "user PATH: $($userPath -join ';')" }
    $registered = (Get-ItemProperty 'HKCU:\Software\otter-net' -ErrorAction SilentlyContinue)
    if ($registered -and $registered.Version -eq $packageVersion) { Pass "HKCU\Software\otter-net: $($registered.Version)" } else { Fail 'HKCU\Software\otter-net has no Version' }

    # ---- A new terminal without PowerShell ----
    $script:cleanPath = Get-PathWithoutPowerShell
    $where = Invoke-Clean "$env:SystemRoot\System32\where.exe" @('otter') $scratch
    $first = ($where.Out -split "`r?`n" | Where-Object { $_ } | Select-Object -First 1)
    if ($first -eq (Join-Path $installDir 'otter.exe')) { Pass "a new terminal's otter: $first" } else { Fail "a new terminal's otter is '$first'" }
    foreach ($shell in 'powershell.exe', 'pwsh.exe') {
        $found = Invoke-Clean "$env:SystemRoot\System32\where.exe" @($shell) $scratch
        if ($found.Code -eq 0) { throw "$shell is still found on the test PATH: $($found.Out)" }
    }
    Pass 'no powershell.exe or pwsh.exe on the test PATH'
    Test-Otter -Otter $first -Label 'msi (installed, on the user PATH)'

    # ---- Uninstall ----
    Invoke-Msiexec @('/x', "`"$Msi`"") (Join-Path $LogDirectory 'uninstall.log')
    $left = @()
    if (Test-Path $installDir) { $left += $installDir }
    if (Test-Path 'HKCU:\Software\otter-net') { $left += 'HKCU\Software\otter-net' }
    if (Get-OtterEntries) { $left += 'the Settings > Apps entry' }
    if (@(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_.TrimEnd('\') -eq $installDir })) { $left += 'the user PATH entry' }
    $script:cleanPath = Get-PathWithoutPowerShell
    $where = Invoke-Clean "$env:SystemRoot\System32\where.exe" @('otter') $scratch
    if ($where.Code -eq 0) { $left += "otter on a new terminal's PATH ($($where.Out.Trim()))" }
    if (-not $left) { Pass 'uninstall: nothing left behind'; Note '- msi uninstall: files, folder, PATH entry, registry key and Apps entry removed' }
    else { Fail "uninstall left behind: $($left -join ', ')" }

    # ---- The ZIP, run in place ----
    if ($Zip) {
        $extract = Join-Path $scratch 'zip'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $extract)
        $folder = Get-ChildItem $extract -Directory | Select-Object -First 1
        $bad = @()
        foreach ($line in (Get-Content (Join-Path $folder.FullName 'SHA256SUMS'))) {
            $hash, $name = $line -split '  ', 2
            if ((Get-FileHash (Join-Path $folder.FullName $name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { $bad += $name }
        }
        if (-not $bad) { Pass 'zip: SHA256SUMS matches' } else { Fail "zip: checksum differs: $($bad -join ', ')" }
        $script:cleanPath = Get-PathWithoutPowerShell
        $zipped = Invoke-Clean (Join-Path $folder.FullName 'otter.exe') @('--version') $scratch
        $hello = Join-Path $scratch 'hello.ot'
        [System.IO.File]::WriteAllText($hello, "say `"Hello`"`n")
        $said = Invoke-Clean (Join-Path $folder.FullName 'otter.exe') @('run', 'hello.ot') $scratch
        if ($zipped.Out.Trim() -eq "Otter $languageVersion" -and $said.Out.Trim() -eq 'Hello') { Pass 'zip: otter.exe runs in place'; Note '- zip: checksums match; otter.exe runs in place without PowerShell' }
        else { Fail "zip: '$($zipped.Out.Trim())' '$($said.Out.Trim())' $($said.Err)" }
    }
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Summary ($([System.IO.Path]::GetFileName($Msi))):"
$script:summary | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { (@("### msi $([System.IO.Path]::GetFileName($Msi))") + $script:summary + '') | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY }
if ($script:failures) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'All checks passed'
