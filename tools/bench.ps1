<#
.SYNOPSIS
Times the same Otter programs on otterPS and on otter-net.

.DESCRIPTION
Each program in tools\bench runs -Runs times on each implementation, from a
fresh process, the way a user runs it. Reported: the median, fastest and
slowest wall-clock time from process start to exit, and the largest working
set seen while it ran. The first run of each program on each side is a
warm-up and is reported separately as "first run" (otterPS compiles and
caches a program on its first run). Every run's output must be identical
between the two implementations, or the benchmark fails.

Native builds of otter-net run in CI, not on developer machines (see
docs/ARCHITECTURE.md, phase 5); locally, pass otter.dll and it runs through
`dotnet`.

.EXAMPLE
powershell -NoProfile -File tools\bench.ps1 -OtterPs ..\otter-101 -OtterNet artifacts\win-x64\otter.exe
#>
[CmdletBinding()]
param(
    # The otterPS checkout to measure (its otter.ps1 is run).
    [Parameter(Mandatory)][string]$OtterPs,
    # otter-net's otter executable, or otter.dll (run through dotnet).
    [Parameter(Mandatory)][string]$OtterNet,
    [int]$Runs = 5,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
$referenceScript = Join-Path $OtterPs 'otter.ps1'
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results\bench.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$programs = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'bench') -Filter '*.ot' | Sort-Object Name)

function Measure-OtterRun {
    param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $peak = 0
    while (-not $process.HasExited) {
        try { $process.Refresh(); if ($process.PeakWorkingSet64 -gt $peak) { $peak = $process.PeakWorkingSet64 } } catch { }
        Start-Sleep -Milliseconds 5
    }
    $process.WaitForExit()
    $clock.Stop()
    return [pscustomobject]@{
        Milliseconds = $clock.Elapsed.TotalMilliseconds
        PeakMB = [Math]::Round($peak / 1MB, 1)
        ExitCode = $process.ExitCode
        Output = $stdout.Result
        Errors = $stderr.Result
    }
}

function Get-Median { param([double[]]$Values) $sorted = @($Values | Sort-Object); return $sorted[[int][Math]::Floor($sorted.Count / 2)] }

# otterPS on Windows PowerShell 5.1 on Windows, PowerShell 7 elsewhere.
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$sides = [ordered]@{
    'otterPS 1.0.1' = if ($onWindows) { @{ File = 'powershell.exe'; Arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $referenceScript, 'run') } }
                      else { @{ File = 'pwsh'; Arguments = @('-NoProfile', '-File', $referenceScript, 'run') } }
    'otter-net' = if ($OtterNet.EndsWith('.dll')) { @{ File = 'dotnet'; Arguments = @($OtterNet, 'run') } } else { @{ File = $OtterNet; Arguments = @('run') } }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-bench-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$rows = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($program in $programs) {
        Copy-Item -LiteralPath $program.FullName -Destination $work -Force
        $outputs = @{}
        foreach ($side in $sides.Keys) {
            $command = $sides[$side]
            $arguments = @($command.Arguments) + @($program.Name)
            $environment = if ($side -eq 'otter-net') { 'native' } else { '' }
            $env:OTTER_ENGINE = $environment
            $first = Measure-OtterRun -FileName $command.File -Arguments $arguments -WorkingDirectory $work
            $times = [System.Collections.Generic.List[double]]::new()
            $peaks = [System.Collections.Generic.List[double]]::new()
            for ($i = 0; $i -lt $Runs; $i++) {
                $run = Measure-OtterRun -FileName $command.File -Arguments $arguments -WorkingDirectory $work
                if ($run.ExitCode -ne 0) { $failures.Add("$($program.Name) on $($side): exit $($run.ExitCode) $($run.Errors)") }
                if ($i -eq 0) { $outputs[$side] = $run.Output }
                $times.Add($run.Milliseconds)
                $peaks.Add($run.PeakMB)
            }
            $rows.Add([pscustomobject]@{
                Program = $program.BaseName; Side = $side
                First = [int]$first.Milliseconds
                Median = [int](Get-Median $times.ToArray())
                Fastest = [int](($times | Measure-Object -Minimum).Minimum)
                Slowest = [int](($times | Measure-Object -Maximum).Maximum)
                PeakMB = ($peaks | Measure-Object -Maximum).Maximum
            })
        }
        $env:OTTER_ENGINE = ''
        if ($outputs['otterPS 1.0.1'] -ne $outputs['otter-net']) { $failures.Add("$($program.Name): the two implementations printed different output") }
    }
}
finally {
    $env:OTTER_ENGINE = ''
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# otterPS 1.0.1 vs otter-net: benchmarks')
$lines.Add('')
$lines.Add("Machine: $([Environment]::MachineName), $([Environment]::ProcessorCount) logical processors, $((Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue).Caption). otter-net: $OtterNet. $Runs timed runs after one warm-up; times are process start to exit, in milliseconds.")
$lines.Add('')
$lines.Add('| Program | Implementation | First run | Median | Fastest | Slowest | Peak memory (MB) | Speed-up (median) |')
$lines.Add('|---|---|---:|---:|---:|---:|---:|---:|')
foreach ($group in ($rows | Group-Object Program)) {
    $reference = $group.Group | Where-Object Side -eq 'otterPS 1.0.1'
    foreach ($row in $group.Group) {
        $speedup = if ($row.Side -eq 'otter-net' -and $row.Median -gt 0) { '{0:N1}x' -f ($reference.Median / $row.Median) } else { '' }
        $lines.Add("| $($row.Program) | $($row.Side) | $($row.First) | $($row.Median) | $($row.Fastest) | $($row.Slowest) | $($row.PeakMB) | $speedup |")
    }
}
if ($failures.Count -gt 0) {
    $lines.Add('')
    $lines.Add('**Problems:**')
    foreach ($failure in $failures) { $lines.Add("- $failure") }
}
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
$lines | ForEach-Object { Write-Host $_ }
if ($failures.Count -gt 0) { exit 1 }
exit 0
