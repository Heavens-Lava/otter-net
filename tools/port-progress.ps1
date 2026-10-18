<#
.SYNOPSIS
Measures how much of otterPS 1.0.1's program corpus otter-net takes on natively.

.DESCRIPTION
Runs `otter check` on every .ot program in the reference checkout (and in
otter-net's tests) with OTTER_ENGINE=native, so otter-net refuses instead of
handing a program to the PowerShell Otter, and counts the reasons it
refuses. Nothing is executed: `check` stops before running. Writes a
Markdown report.

A program counts as native when otter-net parses and checks all of it (a
syntax error that otter-net reports itself counts too). The measure is a
lower bound on fallbacks: a program can still fall back at run time only if
it imports a module otter-net cannot resolve.

.EXAMPLE
pwsh -NoProfile -File tools/port-progress.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OtterNet,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/port-progress.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null

$files = @(Get-ChildItem -LiteralPath $OtterPs -Recurse -Filter *.ot -File | Where-Object { $_.FullName -notmatch '[\\/](otter-studio|scratch)[\\/]' })
$files += @(Get-ChildItem -LiteralPath (Join-Path $here 'tests') -Recurse -Filter *.ot -File)

$command = $OtterNet; $prefix = @()
if ($OtterNet.EndsWith('.dll')) { $command = 'dotnet'; $prefix = @($OtterNet) }
$env:OTTER_ENGINE = 'native'

$results = foreach ($file in $files) {
    Push-Location -LiteralPath $file.DirectoryName
    try { $stderr = (& $command @prefix check $file.Name 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -like "otter-net:*" }) -join "`n" }
    finally { Pop-Location }
    $reason = ''
    if ($stderr -match 'otter-net: OTTER_ENGINE=native, but (.+?)\.?\s*$') { $reason = $Matches[1].Trim() }
    $group = if ($file.FullName.StartsWith($OtterPs)) { ($file.FullName.Substring($OtterPs.Length + 1) -split '[\\/]')[0] } else { 'otter-net tests' }
    [pscustomobject]@{ File = $file.FullName; Group = $group; Native = (-not $reason); Reason = $reason }
}

# "the 'button' statement (line 4) is not native yet" -> "the 'button' statement"
$feature = { param($r) ($r -replace ' \(line \d+\)', '' -replace ' is not native yet$', '') }

$total = $results.Count
$native = @($results | Where-Object Native).Count
$lines = @(
    '# otter-net port progress',
    '',
    "Measured $(Get-Date -Format 'yyyy-MM-dd HH:mm') with ``otter check`` and OTTER_ENGINE=native against $total programs.",
    '',
    "**$native of $total programs ($([Math]::Round(100 * $native / [Math]::Max(1, $total)))%) are taken on natively; $($total - $native) need the PowerShell Otter.**",
    '',
    '| Corpus | Programs | Native | Fall back |',
    '|---|---:|---:|---:|'
)
foreach ($g in ($results | Group-Object Group | Sort-Object Name)) {
    $n = @($g.Group | Where-Object Native).Count
    $lines += "| $($g.Name) | $($g.Count) | $n | $($g.Count - $n) |"
}
$lines += @('', '## Why programs fall back', '', '| Feature | Programs |', '|---|---:|')
foreach ($r in ($results | Where-Object { -not $_.Native } | Group-Object { & $feature $_.Reason } | Sort-Object Count -Descending)) {
    $lines += "| $($r.Name) | $($r.Count) |"
}
$lines | Set-Content -LiteralPath $ReportPath -Encoding utf8
$lines[4]
"Report: $ReportPath"
