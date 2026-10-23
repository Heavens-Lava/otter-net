<#
otter-net preview kit (Windows): runs real Otter programs with the installed
`otter-net` and compares them with what Otter 1.0.1 (otterPS) printed for the
same programs, then writes a report with this machine's details to attach to
feedback.

    powershell -ExecutionPolicy Bypass -File check.ps1 [-OtterNet <path to otter-net.exe>]

Changes nothing outside a temporary folder; sends nothing anywhere. Works in
Windows PowerShell 5.1 and PowerShell 7.
#>
param([string]$OtterNet = '')

$ErrorActionPreference = 'Stop'
$kit = $PSScriptRoot
if (-not $OtterNet) {
    $found = Get-Command otter-net -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $OtterNet = $found.Source }
}
if (-not $OtterNet -or -not (Test-Path -LiteralPath $OtterNet)) {
    Write-Host 'otter-net was not found. Open a new terminal after installing it, or run: check.ps1 -OtterNet <path to otter-net.exe>'
    exit 2
}
$report = Join-Path $kit 'preview-kit-report.txt'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-net-kit-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$script:results = New-Object System.Collections.Generic.List[string]
$script:passed = 0; $script:failed = 0
function Line([string]$Text) { Write-Host $Text; $script:results.Add($Text) }

# Runs otter-net with the arguments in the folder, at most 60 seconds.
function Invoke-OtterNet {
    param([string]$Directory, [string[]]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $OtterNet
    $psi.Arguments = ($Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Close()
    $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
    $code = if ($process.WaitForExit(60000)) { $process.ExitCode } else { try { $process.Kill() } catch { }; 'timeout' }
    [void]$out.Wait(5000); [void]$err.Wait(5000)
    [pscustomobject]@{ Code = $code; Out = $out.Result; Err = $err.Result }
}
function Get-Lf([string]$Text) { $Text -replace "`r", '' }

try {
    Line '== Programs (output compared with Otter 1.0.1)'
    foreach ($program in (Get-ChildItem (Join-Path $kit 'programs') -Filter '*.ot' | Sort-Object Name)) {
        $name = $program.BaseName
        $run = Join-Path $work "run-$name"
        New-Item -ItemType Directory -Path $run | Out-Null
        Copy-Item (Join-Path $kit 'programs\*.ot') $run
        $result = Invoke-OtterNet $run @('run', "$name.ot")
        $expected = Get-Lf ([System.IO.File]::ReadAllText((Join-Path $kit "expected\$name.out")))
        if ($result.Code -eq 0 -and (Get-Lf $result.Out) -eq $expected -and -not $result.Err) { Line "PASS  $name"; $script:passed++ }
        else {
            Line "FAIL  $name (exit $($result.Code))"; $script:failed++
            Line ('      expected: ' + (($expected -split "`n" | Select-Object -First 5) -join ' | '))
            Line ('      printed:  ' + (((Get-Lf $result.Out) -split "`n" | Select-Object -First 5) -join ' | '))
            if ($result.Err) { Line "      stderr: $((Get-Lf $result.Err).Trim())" }
        }
    }

    Line "== Web pages (compiled HTML compared with Otter 1.0.1's)"
    foreach ($program in (Get-ChildItem (Join-Path $kit 'web') -Filter '*.ot' | Sort-Object Name)) {
        $name = $program.BaseName
        $run = Join-Path $work "web-$name"
        New-Item -ItemType Directory -Path $run | Out-Null
        Copy-Item $program.FullName $run
        $result = Invoke-OtterNet $run @('web', "$name.ot", '-NoOpen')
        $html = Join-Path $run "$name.html"
        $expected = Get-Lf ([System.IO.File]::ReadAllText((Join-Path $kit "expected\$name.html")))
        if ($result.Code -eq 0 -and (Test-Path $html) -and (Get-Lf ([System.IO.File]::ReadAllText($html))) -eq $expected) { Line "PASS  $name.html"; $script:passed++ }
        else { Line "FAIL  $name.html (exit $($result.Code)): $($result.Out) $($result.Err)"; $script:failed++ }
    }

    Line '== A new project'
    [void](Invoke-OtterNet $work @('new', 'console', 'demo'))
    $demo = Join-Path $work 'demo'
    if (Test-Path $demo) {
        $ran = Invoke-OtterNet $demo @('run', '.')
        $tested = Invoke-OtterNet $demo @('test', '.')
        if ($ran.Out.Trim() -eq 'Hello from demo!' -and $tested.Out -match '1 passed') { Line 'PASS  new console demo, run, test'; $script:passed++ }
        else { Line "FAIL  new console demo: run printed '$($ran.Out.Trim())'; test printed '$($tested.Out.Trim())'"; $script:failed++ }
    } else { Line 'FAIL  new console demo: no project was made'; $script:failed++ }

    Line ''
    Line "$($script:passed) passed, $($script:failed) failed"
    Line 'Desktop windows: run desktop\greeter.ot and desktop\calculator.ot yourself (README.md says what to look for).'
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

# The machine's details, for the report.
$os = try { Get-CimInstance Win32_OperatingSystem -ErrorAction Stop } catch { $null }
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("otter-net preview kit report - $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm')) UTC")
$lines.Add('')
$lines.Add('== Machine')
if (-not $os) { $lines.Add("$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription), $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)") }
if ($os) { $lines.Add("$($os.Caption) $($os.Version) (build $($os.BuildNumber)), $env:PROCESSOR_ARCHITECTURE, display language $((Get-Culture).Name)") }
$lines.Add('')
$lines.Add('== otter-net')
$lines.Add("path: $OtterNet")
$version = Invoke-OtterNet $kit @('--version')
$lines.Add("--version: $($version.Out.Trim())")
$folder = Split-Path -Parent $OtterNet
$versionFile = Join-Path $folder 'VERSION'
if (Test-Path $versionFile) { $lines.Add("package version: $((Get-Content -Raw $versionFile).Trim())") }
$lines.Add("otter-desktop.exe beside it: $(Test-Path (Join-Path $folder 'otter-desktop.exe'))")
$installed = try { Get-ItemProperty 'HKCU:\Software\otter-net' -ErrorAction Stop } catch { $null }
$lines.Add("installed by: $(if ($installed -and $installed.InstallFolder.TrimEnd('\') -eq $folder.TrimEnd('\')) { "the MSI ($($installed.Version))" } else { 'not the MSI (the ZIP, in place)' })")
$lines.Add('')
$lines.Add('== otter (should be otterPS, or not found)')
$otters = @(Get-Command otter -All -ErrorAction SilentlyContinue | ForEach-Object { "$($_.CommandType) $($_.Source)" })
if ($otters) { $otters | ForEach-Object { $lines.Add("otter: $_") } } else { $lines.Add('otter: not found') }
$lines.Add('')
$lines.Add('== Results')
foreach ($r in $script:results) { $lines.Add($r) }
[System.IO.File]::WriteAllText($report, (($lines -join "`r`n") + "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
Write-Host ''
Write-Host "Report written to $report - attach it to your feedback."
if ($script:failed) { exit 1 }
