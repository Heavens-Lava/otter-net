<#
.SYNOPSIS
Tests otter-net's Windows packages installed beside otterPS 1.0.1 (the
PowerShell Otter, installed from its own release ZIP with its own
Install-Otter.ps1 -AddToUserPath): which `otter` a new terminal gets, that
both still work, and that uninstalling either, in both orders, leaves the
other intact.

.DESCRIPTION
Changes the machine (installs for the current user): a disposable Windows
runner only. "A new terminal" is checked as tools\package-test-common.ps1
does: where.exe with the PATH from the registry, and where.exe in a fresh
process from Task Scheduler.

  A  otterPS, then the otter-net MSI; uninstall otter-net, then otterPS
  B  the otter-net MSI, then otterPS; uninstall otterPS, then otter-net
  Z  otterPS, then the otter-net ZIP run in place; delete it, then otterPS

.EXAMPLE
powershell -NoProfile -File tools\test-coexistence.ps1 -Msi packages\otter-net-0.9.0-preview.1-win-x64.msi -Zip packages\otter-net-0.9.0-preview.1-win-x64.zip -OtterPsZip otterps\otter-1.0.1.zip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [string]$Zip = '',
    [Parameter(Mandatory)][string]$OtterPsZip,
    [string]$LogDirectory = 'msi-logs'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-test-common.ps1')
$Msi = (Resolve-Path $Msi).Path
if ($Zip) { $Zip = (Resolve-Path $Zip).Path }
$OtterPsZip = (Resolve-Path $OtterPsZip).Path
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
$LogDirectory = (Resolve-Path $LogDirectory).Path
$netDir = Join-Path $env:LOCALAPPDATA 'Programs\otter-net'
$psDir = Join-Path $env:LOCALAPPDATA 'Otter\1.0.1'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-coexist-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $scratch | Out-Null
[System.IO.File]::WriteAllText((Join-Path $scratch 'hello.ot'), "say `"Hello from`" `"both`"`n")

$script:failures = 0
$script:summary = @()
function Pass([string]$Text) { Write-Host "ok: $Text" }
function Fail([string]$Text) { Write-Host "FAIL: $Text"; $script:failures++ }
function Note([string]$Text) { Write-Host $Text; $script:summary += $Text }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$payload = Join-Path $scratch 'otterps'
[System.IO.Compression.ZipFile]::ExtractToDirectory($OtterPsZip, $payload)
$psPayload = (Get-ChildItem $payload -Directory | Select-Object -First 1).FullName

function Invoke-Program {
    param([string]$File, [string[]]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '\s') { "`"$_`"" } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $scratch
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['PATH'] = Get-NewTerminalPath
    $process = [System.Diagnostics.Process]::Start($psi)
    $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) { try { $process.Kill() } catch { } }
    [pscustomobject]@{ Code = $process.ExitCode; Out = $out.Result.Trim(); Err = $err.Result.Trim() }
}

# --version and a program, through the given command.
function Test-Works {
    param([string]$Label, [string]$Command)
    $version = Invoke-Program $Command @('--version')
    $ran = Invoke-Program $Command @('run', 'hello.ot')
    if ($version.Out -eq 'Otter 1.0.1' -and $ran.Out -eq 'Hello from both') { Pass "$Label works ($Command)" }
    else { Fail "$Label ($Command): --version '$($version.Out)' run '$($ran.Out)' $($ran.Err)" }
}

# Which otter a new terminal finds first; checks it is $Expect.
function Test-Terminal {
    param([string]$Label, [string]$Expect)
    $terminal = Get-NewTerminalOtter
    $first = @($terminal.Found)[0]
    $kind = Get-OtterKind $first
    $sessionText = ''
    if ($null -ne $terminal.Session) {
        $sessionFirst = @($terminal.Session)[0]
        $sessionKind = Get-OtterKind $sessionFirst
        $sessionText = "; new session: $sessionKind"
        if ($sessionKind -ne $Expect) { Fail "${Label}: a new session (Task Scheduler) finds $sessionKind ($sessionFirst), expected $Expect" }
    }
    if ($kind -eq $Expect) { Pass "${Label}: a new terminal's otter is $kind ($first)$sessionText" } else { Fail "${Label}: a new terminal's otter is $kind ($first), expected $Expect" }
    Note "- ${Label}: a new terminal's otter is $kind$(if ($first) { " ($first)" })$sessionText; all found: $(@($terminal.Found | ForEach-Object { Get-OtterKind $_ }) -join ', ')"
}

function Get-UserPathEntries { @(([Environment]::GetEnvironmentVariable('Path', 'User') -split ';') | Where-Object { $_ } | ForEach-Object { $_.TrimEnd('\') }) }

function Test-PathEntries {
    param([string]$Label, [bool]$Net, [bool]$Ps)
    $entries = Get-UserPathEntries
    $netCount = @($entries | Where-Object { $_ -eq $netDir }).Count
    $psCount = @($entries | Where-Object { $_ -eq $psDir }).Count
    $ok = ($netCount -eq [int]$Net) -and ($psCount -eq [int]$Ps)
    if ($Net -and $entries[0] -ne $netDir) { $ok = $false }
    if ($ok) { Pass "${Label}: user PATH has otter-net $netCount time(s)$(if ($Net) { ' (first)' }), otterPS $psCount time(s)" }
    else { Fail "${Label}: user PATH: $($entries -join ';')" }
}

function Get-Fingerprint {
    param([string]$Folder)
    if (-not (Test-Path $Folder)) { return '' }
    (Get-ChildItem -LiteralPath $Folder -Recurse -File | Sort-Object FullName | ForEach-Object { "$($_.FullName.Substring($Folder.Length)) $((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)" }) -join "`n"
}

function Install-OtterPs {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $psPayload 'Install-Otter.ps1') -AddToUserPath 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $psDir 'otter.cmd'))) { throw "otterPS's Install-Otter.ps1 -AddToUserPath failed: $output" }
    $output | Write-Host
}
function Uninstall-OtterPs {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $psDir 'Uninstall-Otter.ps1') 2>&1 | ForEach-Object { "$_" }
    $output | Write-Host
    if (Test-Path $psDir) { Fail "otterPS's Uninstall-Otter.ps1 left $psDir" }
}
function Install-Net { param([string]$Log) $p = Start-Process msiexec.exe -ArgumentList @('/i', "`"$Msi`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $LogDirectory $Log)`"") -Wait -PassThru; if ($p.ExitCode -ne 0) { throw "msiexec /i failed ($($p.ExitCode))" } }
function Uninstall-Net { param([string]$Log) $p = Start-Process msiexec.exe -ArgumentList @('/x', "`"$Msi`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $LogDirectory $Log)`"") -Wait -PassThru; if ($p.ExitCode -ne 0) { Fail "msiexec /x failed ($($p.ExitCode))" } }

function Test-NetGone {
    param([string]$Label)
    $left = @()
    if (Test-Path $netDir) { $left += $netDir }
    if (Test-Path 'HKCU:\Software\otter-net') { $left += 'HKCU\Software\otter-net' }
    if ((Get-UserPathEntries) -contains $netDir) { $left += 'its PATH entry' }
    if ($left) { Fail "${Label}: otter-net left $($left -join ', ')" } else { Pass "${Label}: otter-net fully removed" }
}
function Test-PsGone {
    param([string]$Label)
    if ((Test-Path $psDir) -or ((Get-UserPathEntries) -contains $psDir)) { Fail "${Label}: otterPS left files or its PATH entry" } else { Pass "${Label}: otterPS fully removed" }
}

try {
    if ((Test-Path $netDir) -or (Test-Path $psDir)) { throw 'otter-net or otterPS is already installed here.' }
    $psCmd = Join-Path $psDir 'otter.cmd'
    $netCmd = Join-Path $netDir 'otter.exe'

    # ---- A ----
    Write-Host '== A: otterPS, then otter-net (MSI); uninstall otter-net, then otterPS'
    Install-OtterPs
    $psPrint = Get-Fingerprint $psDir
    Test-Terminal 'A, otterPS alone' 'otterPS'
    Install-Net 'coexist-a-install.log'
    Test-PathEntries 'A, both installed' $true $true
    Test-Terminal 'A, both installed' 'otter-net'
    Test-Works 'otter-net' $netCmd
    Test-Works 'otterPS' $psCmd
    if ((Get-Fingerprint $psDir) -eq $psPrint) { Pass "A: otterPS's files unchanged by otter-net's install" } else { Fail "A: otterPS's files changed" }
    Uninstall-Net 'coexist-a-uninstall.log'
    Test-NetGone 'A, otter-net uninstalled'
    Test-PathEntries 'A, otter-net uninstalled' $false $true
    Test-Terminal 'A, otter-net uninstalled' 'otterPS'
    Test-Works 'otterPS (otter-net uninstalled)' $psCmd
    if ((Get-Fingerprint $psDir) -eq $psPrint) { Pass "A: otterPS's files unchanged by otter-net's uninstall" } else { Fail "A: otterPS's files changed by otter-net's uninstall" }
    Uninstall-OtterPs
    Test-PsGone 'A, both uninstalled'
    Test-Terminal 'A, both uninstalled' 'none'

    # ---- B ----
    Write-Host '== B: otter-net (MSI), then otterPS; uninstall otterPS, then otter-net'
    Install-Net 'coexist-b-install.log'
    $netPrint = Get-Fingerprint $netDir
    Test-Terminal 'B, otter-net alone' 'otter-net'
    Install-OtterPs
    Test-PathEntries 'B, both installed' $true $true
    Test-Terminal 'B, both installed' 'otter-net'
    Test-Works 'otter-net' $netCmd
    Test-Works 'otterPS' $psCmd
    Uninstall-OtterPs
    Test-PsGone 'B, otterPS uninstalled'
    Test-PathEntries 'B, otterPS uninstalled' $true $false
    if ((Get-Fingerprint $netDir) -eq $netPrint -and (Test-Path 'HKCU:\Software\otter-net')) { Pass "B: otter-net's files and registry key unchanged by otterPS's install and uninstall" } else { Fail "B: otter-net changed" }
    Test-Terminal 'B, otterPS uninstalled' 'otter-net'
    Test-Works 'otter-net (otterPS uninstalled)' $netCmd
    Uninstall-Net 'coexist-b-uninstall.log'
    Test-NetGone 'B, both uninstalled'
    Test-Terminal 'B, both uninstalled' 'none'

    # ---- Z: the ZIP, run in place ----
    if ($Zip) {
        Write-Host '== Z: otterPS, then the otter-net ZIP in place; delete it, then otterPS'
        Install-OtterPs
        $psPrint = Get-Fingerprint $psDir
        $extract = Join-Path $scratch 'zip'
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $extract)
        $zipOtter = Join-Path (Get-ChildItem $extract -Directory | Select-Object -First 1).FullName 'otter.exe'
        Test-Works 'otter-net (ZIP, in place)' $zipOtter
        Test-Works 'otterPS (beside the ZIP)' $psCmd
        Test-Terminal 'Z, ZIP extracted' 'otterPS'
        Remove-Item -Recurse -Force $extract
        if ((Get-Fingerprint $psDir) -eq $psPrint) { Pass "Z: otterPS unchanged" } else { Fail 'Z: otterPS changed' }
        Test-Works 'otterPS (ZIP deleted)' $psCmd
        Uninstall-OtterPs
        Test-PsGone 'Z, otterPS uninstalled'
        Note '- Z: the ZIP runs in place beside otterPS and changes nothing: a new terminal keeps otterPS'
    }
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Summary (coexistence with otterPS 1.0.1, $([System.IO.Path]::GetFileName($Msi))):"
$script:summary | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { (@("### coexistence with otterPS 1.0.1: $([System.IO.Path]::GetFileName($Msi))") + $script:summary + '') | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY }
if ($script:failures) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'All checks passed'
