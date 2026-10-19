<#
.SYNOPSIS
Times otter-net alone on tools/bench's programs, for platforms where the
reference otterPS is not run (CI on macOS, Linux and Windows on Arm).

.EXAMPLE
pwsh tools/bench-native.ps1 -Otter artifacts/linux-x64/otter -Title 'otter-net on Debian 12 (linux-x64)' -ReportPath results/bench-linux-x64.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Otter,
    [string]$Title = 'otter-net',
    [string]$ReportPath = '',
    [int]$Runs = 11
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$Otter = (Resolve-Path $Otter).Path
$env:OTTER_ENGINE = 'native'
# A native executable runs directly; a plain .NET build (otter.dll) through dotnet.
$command = $Otter; $prefix = @()
if ($Otter.EndsWith('.dll')) { $command = 'dotnet'; $prefix = @($Otter) }
$lines = @("# $Title", '', '| Program | Median (ms) | Fastest | Slowest |', '|---|---:|---:|---:|')
foreach ($program in Get-ChildItem (Join-Path $here 'tools/bench') -Filter *.ot | Sort-Object Name) {
    $times = foreach ($i in 1..$Runs) {
        $clock = [Diagnostics.Stopwatch]::StartNew()
        & $command @prefix run $program.FullName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$($program.Name) failed: $LASTEXITCODE" }
        $clock.Elapsed.TotalMilliseconds
    }
    $sorted = @($times | Sort-Object)
    $lines += "| $($program.BaseName) | $([int]$sorted[[int][Math]::Floor($sorted.Count / 2)]) | $([int]$sorted[0]) | $([int]$sorted[-1]) |"
}
if ($ReportPath) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
    $lines | Set-Content $ReportPath
}
if ($env:GITHUB_STEP_SUMMARY) { $lines | Out-File -Append $env:GITHUB_STEP_SUMMARY }
$lines | Write-Output
