<#
.SYNOPSIS
Compares what otter-net showed on one platform with what was captured on
Windows (otterPS 1.0.1 on Windows PowerShell 5.1, the certified reference):
the cross-platform half of the certification (docs/CERTIFICATION.md).

.DESCRIPTION
Both roots hold captures written by tools/compare.ps1, compare-web.ps1,
compare-cli.ps1 and compare-site.ps1 with -CaptureDirectory, one folder per
gate: <root>/<gate>/reference/*.txt (otterPS, Windows only) and
<root>/<gate>/candidate/*.txt (otter-net). Every case of the Windows
reference (or, for a gate without one, of otter-net on Windows) is compared
with the platform's otter-net capture: exit code, standard output, standard
error and the files left behind (name and content hash).

Normalised before comparing, because it differs by the nature of the
platform (docs/CROSS-PLATFORM-CONSISTENCY.md):
  - the case's folder is <work> in both (done by the capture);
  - in a path that starts with <work>, \ becomes / (the path separator).

Any other difference fails, unless tests/cross-platform.psd1 declares it for
that platform, gate and case (and part), with the reason.

.EXAMPLE
pwsh tools/compare-captures.ps1 -ReferenceRoot downloaded/win-x64/captures -CandidateRoot downloaded/linux-x64/captures -Platform linux-x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReferenceRoot,
    [Parameter(Mandatory)][string]$CandidateRoot,
    [Parameter(Mandatory)][string]$Platform,
    [string]$Rules = '',
    [string]$ReportPath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
if (-not $Rules) { $Rules = Join-Path $here 'tests/cross-platform.psd1' }
$declared = @((Import-PowerShellDataFile -LiteralPath $Rules).Rules)
if (-not $ReportPath) { $ReportPath = Join-Path $here "compat-results/cross-platform-$Platform.md" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Capture {
    param([string]$Path)
    $text = [System.IO.File]::ReadAllText($Path)
    $parts = @{}
    $m = [regex]::Match($text, '(?s)^exit: (?<exit>[^\n]*)\n--- stdout\n(?<stdout>.*)\n--- stderr\n(?<stderr>.*)\n--- files\n(?<files>.*)$')
    if (-not $m.Success) { throw "Not a capture: $Path" }
    foreach ($name in 'exit', 'stdout', 'stderr', 'files') { $parts[$name] = $m.Groups[$name].Value }
    return $parts
}

function ConvertTo-Comparable {
    param([string]$Text)
    # The otterPS checkout's folder, which PowerShell's own texts name (its
    # module paths), differs from machine to machine.
    $Text = [regex]::Replace($Text, '(?:[A-Za-z]:\\|/)[^\s''"<>|]*?[\\/]otterps(?=[\\/])', '<otterps>')
    # A path below the case's folder or otterPS's: the platform's separator.
    return [regex]::Replace($Text, '<(work|otterps)>((?:\\[^\\\r\n"''<>|*?\s]+)+\\?)', { param($match) '<' + $match.Groups[1].Value + '>' + $match.Groups[2].Value.Replace('\', '/') })
}

# PowerShell's console view wraps its own texts at 120 columns, so a
# machine-specific path in them (otterPS's folder) moves the line breaks: such
# a text is also compared with its line breaks taken out.
function Test-SameUnwrapped {
    param([string]$A, [string]$B)
    $rawA = $A -replace '\r?\n', ''; $rawB = $B -replace '\r?\n', ''
    $x = ConvertTo-Comparable $rawA; $y = ConvertTo-Comparable $rawB
    return ($x -ceq $y) -and ($x.Contains('<otterps>') -or $x.Contains('<otter.ps1>'))
}

function Test-Declared {
    param([string]$Gate, [string]$Case, [string]$Part)
    foreach ($rule in $declared) {
        if (-not (@($rule.Platforms) | Where-Object { $Platform -like $_ })) { continue }
        if ($rule.Gates -and -not (@($rule.Gates) | Where-Object { $Gate -like $_ })) { continue }
        if (-not (@($rule.Cases) | Where-Object { $Case -like $_ })) { continue }
        if ($rule.Parts -and -not (@($rule.Parts) -contains $Part)) { continue }
        return $rule.Reason
    }
    return $null
}

$gates = [ordered]@{}
$script:unwrapped = 0
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# $Platform compared with Windows")
$lines.Add('')
$lines.Add('Reference: otterPS 1.0.1 on Windows PowerShell 5.1 (win-x64) where it ran; otherwise otter-net on win-x64. Normalised: the case folder (<work>), the otterPS checkout''s folder (<otterps>) and the path separator after them; in PowerShell''s own texts that name such a path, the line breaks of its 120-column console view. Differences not declared in `tests/cross-platform.psd1` fail.')
$lines.Add('')
$unexpectedAll = New-Object System.Collections.Generic.List[string]
$declaredAll = New-Object System.Collections.Generic.List[string]
foreach ($gateFolder in @(Get-ChildItem -LiteralPath $ReferenceRoot -Directory | Sort-Object Name)) {
    $gate = $gateFolder.Name
    $referenceSide = Join-Path $gateFolder.FullName 'reference'
    $referenceKind = 'otterPS'
    if (-not (Test-Path -LiteralPath $referenceSide)) { $referenceSide = Join-Path $gateFolder.FullName 'candidate'; $referenceKind = 'otter-net on win-x64' }
    if (-not (Test-Path -LiteralPath $referenceSide)) { continue }
    $candidateSide = Join-Path (Join-Path $CandidateRoot $gate) 'candidate'
    $compared = 0; $identical = 0; $declaredCount = 0; $unexpected = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $candidateSide)) {
        $unexpected.Add("no capture of the $gate gate on $Platform")
    }
    else {
        $referenceFiles = @(Get-ChildItem -LiteralPath $referenceSide -Filter '*.txt' -File)
        $candidateNames = @{}
        foreach ($f in @(Get-ChildItem -LiteralPath $candidateSide -Filter '*.txt' -File)) { $candidateNames[$f.Name] = $f.FullName }
        foreach ($file in $referenceFiles) {
            $compared++
            $case = $file.BaseName
            if (-not $candidateNames.ContainsKey($file.Name)) {
                $reason = Test-Declared $gate $case 'missing'
                if ($reason) { $declaredCount++; $declaredAll.Add("$gate ${case}: missing ($reason)") } else { $unexpected.Add("${case}: not captured on $Platform") }
                continue
            }
            $a = Read-Capture $file.FullName
            $b = Read-Capture $candidateNames[$file.Name]
            $candidateNames.Remove($file.Name)
            $differences = New-Object System.Collections.Generic.List[string]
            foreach ($part in 'exit', 'stdout', 'stderr', 'files') {
                $x = ConvertTo-Comparable $a[$part]; $y = ConvertTo-Comparable $b[$part]
                if ($x -ceq $y) { continue }
                if ($part -in @('stdout', 'stderr') -and (Test-SameUnwrapped $a[$part] $b[$part])) { $script:unwrapped++; continue }
                if ($part -eq 'files') {
                    $xl = @($x -split "`n" | Where-Object { $_ }); $yl = @($y -split "`n" | Where-Object { $_ })
                    $diff = @(Compare-Object $xl $yl -CaseSensitive)
                    # Same content once CRLF is LF: say so.
                    $lfOnly = ($xl.Count -eq $yl.Count)
                    if ($lfOnly) { for ($i = 0; $i -lt $xl.Count; $i++) { $p = $xl[$i] -split ' '; $q = $yl[$i] -split ' '; if ($p[0] -ne $q[0] -or $p[-1] -ne $q[-1]) { $lfOnly = $false; break } } }
                    $shown = ($diff | Select-Object -First 3 | ForEach-Object { "$(if ($_.SideIndicator -eq '<=') { 'Windows' } else { $Platform }) only: $($_.InputObject)" }) -join '; '
                    $differences.Add("files$(if ($lfOnly) { ' (line endings only)' }): $shown")
                }
                else {
                    $n = [Math]::Min($x.Length, $y.Length); $at = $n
                    for ($i = 0; $i -lt $n; $i++) { if ($x[$i] -ne $y[$i]) { $at = $i; break } }
                    $from = [Math]::Max(0, $at - 30)
                    $show = { param($t) ($t.Substring([Math]::Min($from, $t.Length), [Math]::Min(100, [Math]::Max(0, $t.Length - $from))) -replace "`r", '\r' -replace "`n", '\n') }
                    $differences.Add("${part} at $($at): Windows [$(& $show $x)] $Platform [$(& $show $y)]")
                }
            }
            if ($differences.Count -eq 0) { $identical++; continue }
            $undeclared = @($differences | Where-Object { -not (Test-Declared $gate $case (($_ -split '[ :]')[0])) })
            if ($undeclared.Count -eq 0) {
                $declaredCount++
                $declaredAll.Add("$gate ${case}: $((Test-Declared $gate $case (($differences[0] -split '[ :]')[0])))")
            }
            else { $unexpected.Add("${case}: $($undeclared -join '; ')") }
        }
        foreach ($extra in $candidateNames.Keys) { $unexpected.Add("$([System.IO.Path]::GetFileNameWithoutExtension($extra)): captured on $Platform but not on Windows") }
    }
    $gates[$gate] = [ordered]@{ reference = $referenceKind; compared = $compared; identical = $identical; declared = $declaredCount; unexpected = $unexpected.Count; differences = @($unexpected | Select-Object -First 100) }
    foreach ($u in $unexpected) { $unexpectedAll.Add("$gate $u") }
    $lines.Add("- **$gate** (against $referenceKind): $compared compared, $identical identical, $declaredCount declared differences, **$($unexpected.Count) unexpected**")
}
$lines.Add('')
if ($unexpectedAll.Count -gt 0) {
    $lines.Add('## Unexpected differences'); $lines.Add('')
    foreach ($u in $unexpectedAll) { $lines.Add("- $($u -replace '\|', '\|')") }
    $lines.Add('')
}
if ($declaredAll.Count -gt 0) {
    $lines.Add('## Declared differences'); $lines.Add('')
    foreach ($d in $declaredAll) { $lines.Add("- $d") }
}
$lines.Add('')
$lines.Add("PowerShell texts identical once their line breaks are taken out: $($script:unwrapped).")
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", $utf8)
$summary = [ordered]@{ tool = 'compare-captures'; platform = $Platform; gates = $gates; unexpected = $unexpectedAll.Count; declared = $declaredAll.Count; unwrapped = $script:unwrapped }
[System.IO.File]::WriteAllText([System.IO.Path]::ChangeExtension($ReportPath, '.json'), ($summary | ConvertTo-Json -Depth 6), $utf8)
Write-Host "$Platform against Windows: $($unexpectedAll.Count) unexpected, $($declaredAll.Count) declared differences. Report: $ReportPath"
foreach ($u in ($unexpectedAll | Select-Object -First 40)) { Write-Host "  $u" -ForegroundColor Red }
if ($unexpectedAll.Count -gt 0) { exit 1 }
exit 0
