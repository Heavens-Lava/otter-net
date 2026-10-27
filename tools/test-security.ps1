<#
.SYNOPSIS
Runs otter-net's security regression programs (tests/security) and compares
each with its expected result.

.DESCRIPTION
These programs check where otter-net deliberately differs from otterPS
1.0.1 for security (docs/SECURITY_LEDGER.md), so they are not part of the
otterPS comparison corpus: each <name>.ot has a <name>.out with the exit
code ("exit: N") and the standard output and error otter-net must print.
Each runs in its own temporary folder, time-limited.

.EXAMPLE
pwsh tools/test-security.ps1 -OtterNet artifacts/publish/osx-arm64/otter
.EXAMPLE
pwsh tools/test-security.ps1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterNet,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$OtterNet = (Resolve-Path $OtterNet).Path
$failed = 0; $passed = 0
foreach ($program in (Get-ChildItem (Join-Path $root 'tests/security') -Filter '*.ot' | Sort-Object Name)) {
    $expectedPath = [System.IO.Path]::ChangeExtension($program.FullName, '.out')
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-security-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        Copy-Item $program.FullName $work
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        if ($OtterNet -like '*.dll') { $psi.FileName = 'dotnet'; $psi.ArgumentList.Add($OtterNet) } else { $psi.FileName = $OtterNet }
        foreach ($a in @('run', $program.Name)) { $psi.ArgumentList.Add($a) }
        $psi.WorkingDirectory = $work
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($psi)
        $process.StandardInput.Close()
        $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "$($program.Name) did not finish in $TimeoutSeconds s" }
        $actual = ("exit: $($process.ExitCode)`n" + $out.Result + $err.Result) -replace "`r`n", "`n"
        $expected = [System.IO.File]::ReadAllText($expectedPath) -replace "`r`n", "`n"
        if ($actual -ceq $expected) { Write-Host "PASS  $($program.Name)"; $passed++ }
        else {
            Write-Host "FAIL  $($program.Name)"; $failed++
            $a = $actual -split "`n"; $e = $expected -split "`n"
            for ($i = 0; $i -lt [Math]::Max($a.Count, $e.Count); $i++) {
                if ($i -ge $a.Count -or $i -ge $e.Count -or $a[$i] -cne $e[$i]) { Write-Host "  line $($i + 1): expected '$(if ($i -lt $e.Count) { $e[$i] })'"; Write-Host "  line $($i + 1): printed  '$(if ($i -lt $a.Count) { $a[$i] })'"; break }
            }
        }
    } finally { Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue }
}
Write-Host "$passed passed, $failed failed"
if ($failed) { exit 1 }
