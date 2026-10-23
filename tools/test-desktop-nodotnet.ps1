<#
.SYNOPSIS
Runs desktop-window programs from the installed otter-net package on a
Windows where no .NET runtime can be found: proves otter-desktop.exe is
self-contained.

.DESCRIPTION
Runs on a disposable Windows runner only: it hides the machine's .NET.

How the machine is made one "without .NET" for this test (and the
processes it starts):

1. The machine-wide .NET folders are renamed: C:\Program Files\dotnet and
   C:\Program Files (x86)\dotnet (the default places the .NET host looks in).
2. The registered install locations are renamed in the registry:
   HKLM\SOFTWARE\dotnet and HKLM\SOFTWARE\WOW6432Node\dotnet (the host reads
   ...\Setup\InstalledVersions\<arch>\InstallLocation there).
3. Every PATH folder holding dotnet.exe is removed from the test PATH.
4. The test environment has DOTNET_ROOT, DOTNET_ROOT_X64, DOTNET_ROOT_ARM64
   and DOTNET_ROOT(x86) pointing to an empty folder, and
   DOTNET_MULTILEVEL_LOOKUP=0.
5. A negative control: a framework-dependent build of the same
   otter-desktop.exe (-FrameworkDependentDesktop) must fail to start there
   ("You must install or update .NET"); if it starts, the test stops, since
   .NET was still found.

Then it installs the MSI and runs, with the installed otter-net.exe:
- tests\compat's desktop programs that close themselves (ui_run_create*.ot):
  they create WPF controls, so WPF must load;
- with -Harness (a self-contained desktop-harness) and -OtterPs (otterPS
  1.0.1's otter.ps1), every tests\desktop program, driven through UI
  Automation and compared with otterPS 1.0.1 (tests\desktop\README.md);
then the same compat programs from the extracted ZIP. The folders and keys
are put back at the end.

.EXAMPLE
powershell -NoProfile -File tools\test-desktop-nodotnet.ps1 -Msi packages\otter-net-0.9.0-preview.1-win-x64.msi -Zip packages\otter-net-0.9.0-preview.1-win-x64.zip -FrameworkDependentDesktop fdd\otter-desktop.exe -Harness harness\desktop-harness.exe -OtterPs otterps\otter.ps1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Msi,
    [string]$Zip = '',
    [Parameter(Mandatory)][string]$FrameworkDependentDesktop,
    [string]$Harness = '',
    [string]$OtterPs = '',
    [string]$Results = 'desktop-nodotnet',
    [string]$LogDirectory = 'msi-logs'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'package-test-common.ps1')
$Msi = (Resolve-Path $Msi).Path
if ($Zip) { $Zip = (Resolve-Path $Zip).Path }
$FrameworkDependentDesktop = (Resolve-Path $FrameworkDependentDesktop).Path
if ($Harness) { $Harness = (Resolve-Path $Harness).Path }
if ($OtterPs) { $OtterPs = (Resolve-Path $OtterPs).Path }
New-Item -ItemType Directory -Force -Path $Results, $LogDirectory | Out-Null
$Results = (Resolve-Path $Results).Path
$LogDirectory = (Resolve-Path $LogDirectory).Path
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\otter-net'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-nodotnet-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $scratch | Out-Null
$emptyRoot = Join-Path $scratch 'empty-dotnet-root'
New-Item -ItemType Directory -Path $emptyRoot | Out-Null

$script:failures = 0
$script:summary = @()
function Pass([string]$Text) { Write-Host "ok: $Text" }
function Fail([string]$Text) { Write-Host "FAIL: $Text"; $script:failures++ }
function Note([string]$Text) { Write-Host $Text; $script:summary += $Text }

# ---- 1-4: hide .NET -----------------------------------------------------------
$hiddenFolders = @()
$hiddenKeys = @()
function Hide-DotNet {
    foreach ($folder in @("$env:ProgramFiles\dotnet", "${env:ProgramFiles(x86)}\dotnet")) {
        if ($folder -and (Test-Path -LiteralPath $folder)) {
            $target = "$folder.hidden-by-otter-test"
            Move-Item -LiteralPath $folder -Destination $target
            $script:hiddenFolders += [pscustomobject]@{ From = $folder; To = $target }
            Write-Host "hid $folder"
        }
    }
    foreach ($key in 'HKLM:\SOFTWARE\dotnet', 'HKLM:\SOFTWARE\WOW6432Node\dotnet') {
        if (Test-Path -LiteralPath $key) {
            $name = Split-Path -Leaf $key
            Rename-Item -LiteralPath $key -NewName "$name.hidden-by-otter-test"
            $script:hiddenKeys += [pscustomobject]@{ Parent = (Split-Path -Parent $key); Name = $name }
            Write-Host "hid $key"
        }
    }
}
function Restore-DotNet {
    foreach ($item in $script:hiddenFolders) { if (Test-Path -LiteralPath $item.To) { Move-Item -LiteralPath $item.To -Destination $item.From } }
    foreach ($item in $script:hiddenKeys) {
        $hidden = Join-Path $item.Parent "$($item.Name).hidden-by-otter-test"
        if (Test-Path -LiteralPath $hidden) { Rename-Item -LiteralPath $hidden -NewName $item.Name }
    }
}

# The test environment: the new terminal's PATH without folders holding
# dotnet.exe (PowerShell stays: the harness starts otterPS through it), and
# the DOTNET_ROOT variables on an empty folder.
function Get-NoDotNetEnvironment {
    $folders = @((Get-NewTerminalPath) -split ';' | Where-Object { $_ -and -not (Test-Path -LiteralPath (Join-Path $_ 'dotnet.exe')) })
    @{
        'PATH' = ($folders -join ';')
        'DOTNET_ROOT' = $emptyRoot
        'DOTNET_ROOT_X64' = $emptyRoot
        'DOTNET_ROOT_ARM64' = $emptyRoot
        'DOTNET_ROOT(x86)' = $emptyRoot
        'DOTNET_ROOT_X86' = $emptyRoot
        'DOTNET_MULTILEVEL_LOOKUP' = '0'
    }
}

function Invoke-NoDotNet {
    param([string]$File, [string[]]$Arguments, [string]$Directory, [hashtable]$Extra = @{}, [int]$TimeoutSeconds = 120)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $File
    $psi.Arguments = (@($Arguments) | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    # No DOTNET_ variable of the runner's may leak in.
    foreach ($name in @($psi.EnvironmentVariables.Keys)) { if ($name -like 'DOTNET_*' -or $name -like 'DOTNET_ROOT*') { $psi.EnvironmentVariables.Remove($name) } }
    $environment = Get-NoDotNetEnvironment
    foreach ($name in $environment.Keys) { $psi.EnvironmentVariables[$name] = $environment[$name] }
    foreach ($name in $Extra.Keys) { $psi.EnvironmentVariables[$name] = $Extra[$name] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Close() } catch { }
    $code = if ($process.WaitForExit($TimeoutSeconds * 1000)) { $process.ExitCode } else { try { $process.Kill() } catch { }; 'timeout' }
    [void]$stdout.Wait(5000); [void]$stderr.Wait(5000)
    [pscustomobject]@{ Code = $code; Out = $stdout.Result; Err = $stderr.Result }
}

# The compat programs that create WPF controls and end by themselves.
function Test-DesktopPrograms {
    param([string]$Otter, [string]$Label)
    $expected = @{
        'ui_run_create.ot' = 'before'
        'ui_run_create_try.ot' = 'w exists?'
    }
    foreach ($name in $expected.Keys) {
        $work = Join-Path $scratch ("run-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
        New-Item -ItemType Directory -Path $work | Out-Null
        Copy-Item (Join-Path $root "tests\compat\$name") $work
        $result = Invoke-NoDotNet $Otter @('run', $name) $work @{ OTTER_NET_TRACE = '1'; OTTER_ENGINE = 'native' }
        $ranInHost = $result.Err -match 'otter-net desktop: .* runs in .*otter-desktop\.exe'
        if ($result.Code -eq 0 -and $ranInHost -and $result.Out -match [regex]::Escape($expected[$name]) -and $result.Err -notmatch 'install or update \.NET|Unhandled exception') {
            Pass "${Label}: $name ran in otter-desktop.exe without .NET: $(($result.Out -split "`r?`n" | Where-Object { $_ }) -join ' | ')"
        } else {
            Fail "${Label}: $name exit $($result.Code)`n  out: $($result.Out)`n  err: $($result.Err)"
        }
    }
    # A window that is shown, then closed by this script (WM_CLOSE), as a user would.
    $work = Join-Path $scratch ("show-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
    New-Item -ItemType Directory -Path $work | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $work 'shown.ot'), "create window into app`ntitle of app is `"no dotnet`"`ncreate button into b`ntext of b is `"OK`"`nput b in app`nwhen app is closed`n    say `"closed`"`n.`nshow app`nsay `"after show`"`n")
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Otter
    $psi.Arguments = 'run shown.ot'
    $psi.WorkingDirectory = $work
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($name in @($psi.EnvironmentVariables.Keys)) { if ($name -like 'DOTNET_*') { $psi.EnvironmentVariables.Remove($name) } }
    $environment = Get-NoDotNetEnvironment
    foreach ($name in $environment.Keys) { $psi.EnvironmentVariables[$name] = $environment[$name] }
    $psi.EnvironmentVariables['OTTER_NET_TRACE'] = '1'
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $window = [IntPtr]::Zero
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($window -eq [IntPtr]::Zero -and $clock.Elapsed.TotalSeconds -lt 60 -and -not $process.HasExited) {
        Start-Sleep -Milliseconds 250
        $window = [OtterNetTest.Win32]::FindWindow([NullString]::Value, 'no dotnet')
    }
    $sawWindow = $window -ne [IntPtr]::Zero
    if ($sawWindow) { Start-Sleep -Milliseconds 500; [void][OtterNetTest.Win32]::PostMessage($window, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    if (-not $process.WaitForExit(30000)) { try { $process.Kill() } catch { } }
    [void]$stdout.Wait(5000); [void]$stderr.Wait(5000)
    $out = $stdout.Result
    if ($sawWindow -and $process.ExitCode -eq 0 -and $out -match 'closed' -and $out -match 'after show') { Pass "${Label}: a window opened ('no dotnet'), was closed, and the program went on: $(($out -split "`r?`n" | Where-Object { $_ }) -join ' | ')" }
    else { Fail "${Label}: window seen: $sawWindow, exit $($process.ExitCode)`n  out: $out`n  err: $($stderr.Result)" }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace OtterNetTest {
    public static class Win32 {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    }
}
'@

try {
    if (Test-Path $installDir) { throw "$installDir already exists on this machine." }
    Hide-DotNet

    # ---- 5: proof that no .NET can be found ----
    $environment = Get-NoDotNetEnvironment
    $dotnetOnPath = @($environment['PATH'] -split ';' | Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ 'dotnet.exe')) })
    $registered = @('HKLM:\SOFTWARE\dotnet', 'HKLM:\SOFTWARE\WOW6432Node\dotnet' | Where-Object { Test-Path -LiteralPath $_ })
    $defaults = @("$env:ProgramFiles\dotnet", "${env:ProgramFiles(x86)}\dotnet" | Where-Object { $_ -and (Test-Path -LiteralPath $_) })
    if ($dotnetOnPath -or $registered -or $defaults) { throw "Still visible: $($dotnetOnPath + $registered + $defaults -join ', ')" }
    $control = Invoke-NoDotNet $FrameworkDependentDesktop @('--version') $scratch @{} 60
    $controlText = "$($control.Out) $($control.Err)".Trim()
    if ($control.Code -ne 0 -and $controlText -match '\.NET') {
        Pass "negative control: a framework-dependent otter-desktop.exe cannot start (exit $($control.Code)): $(($controlText -split "`r?`n" | Select-Object -First 2) -join ' ')"
        Note "- no .NET: Program Files\dotnet and Program Files (x86)\dotnet renamed, HKLM\SOFTWARE\dotnet and WOW6432Node\dotnet renamed, no dotnet.exe on the PATH, DOTNET_ROOT* on an empty folder, DOTNET_MULTILEVEL_LOOKUP=0; a framework-dependent otter-desktop.exe fails to start there (exit $($control.Code), `"$(($controlText -split "`r?`n" | Select-Object -First 1))`")"
    } else {
        throw "The negative control started (exit $($control.Code)): .NET is still found. $controlText"
    }

    # ---- The installed package ----
    $process = Start-Process msiexec.exe -ArgumentList @('/i', "`"$Msi`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $LogDirectory 'nodotnet-install.log')`"") -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "msiexec /i failed ($($process.ExitCode))" }
    $otter = Join-Path $installDir 'otter-net.exe'
    $desktop = Join-Path $installDir 'otter-desktop.exe'
    if (-not (Test-Path $desktop)) { throw "no $desktop" }
    Note ("- installed otter-desktop.exe: {0:N0} bytes; beside it: {1}" -f (Get-Item $desktop).Length, ((Get-ChildItem $installDir -Filter '*_cor3.dll' | ForEach-Object Name) -join ', '))
    $version = Invoke-NoDotNet $desktop @('--version') $scratch
    if ($version.Code -eq 0 -and $version.Out.Trim() -eq 'Otter 1.0.1') { Pass "otter-desktop.exe --version without .NET: $($version.Out.Trim())" } else { Fail "otter-desktop.exe --version: $($version.Code) $($version.Out) $($version.Err)" }
    Test-DesktopPrograms -Otter $otter -Label 'msi'
    Note '- msi: tests\compat ui_run_create.ot and ui_run_create_try.ot, and a window shown and closed, ran in the installed otter-desktop.exe without .NET'

    if ($Harness -and $OtterPs) {
        $harnessResult = Invoke-NoDotNet $Harness @('--otterps', $OtterPs, '--otter', $otter, '--tests', (Join-Path $root 'tests\desktop'), '--out', $Results) $root @{} 3600
        $harnessResult.Out | Write-Host
        if ($harnessResult.Err) { $harnessResult.Err | Write-Host }
        $line = @($harnessResult.Out -split "`r?`n" | Where-Object { $_ -match '\d+ of \d+' } | Select-Object -Last 1)
        if ($harnessResult.Code -eq 0) { Pass "desktop harness without .NET: $line"; Note "- msi: tests\desktop with the self-contained harness, the installed otter-net.exe and no .NET, against otterPS 1.0.1: $line" }
        else { Fail "desktop harness without .NET (exit $($harnessResult.Code)): $line"; Note "- msi: tests\desktop harness without .NET FAILED (exit $($harnessResult.Code)): $line" }
    }

    $process = Start-Process msiexec.exe -ArgumentList @('/x', "`"$Msi`"", '/qn', '/norestart', '/l*v', "`"$(Join-Path $LogDirectory 'nodotnet-uninstall.log')`"") -Wait -PassThru
    if ($process.ExitCode -ne 0) { Fail "msiexec /x failed ($($process.ExitCode))" }
    if (Test-Path $installDir) { Fail "uninstall left $installDir" } else { Pass 'uninstalled' }

    # ---- The ZIP, in place ----
    if ($Zip) {
        $extract = Join-Path $scratch 'zip'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($Zip, $extract)
        $folder = (Get-ChildItem $extract -Directory | Select-Object -First 1).FullName
        Test-DesktopPrograms -Otter (Join-Path $folder 'otter-net.exe') -Label 'zip'
        Note '- zip: the same desktop programs ran from the extracted ZIP without .NET'
    }
}
finally {
    Restore-DotNet
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Summary (desktop without .NET, $([System.IO.Path]::GetFileName($Msi))):"
$script:summary | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { (@("### desktop windows without .NET: $([System.IO.Path]::GetFileName($Msi))") + $script:summary + '') | Out-File -Append -Encoding utf8 $env:GITHUB_STEP_SUMMARY }
if ($script:failures) { Write-Host "$($script:failures) check(s) failed"; exit 1 }
Write-Host 'All checks passed'
