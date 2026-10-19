<#
.SYNOPSIS
Compares what `otter check` decides for many programs at once - otterPS
1.0.1's lexer, parser and language contract against otter-net's - without
running them.

.DESCRIPTION
tools/compare.ps1 starts otterPS once per program (seconds each). This
loads otterPS's modules once, in one PowerShell process, and parses every
program there; otter-net parses them all in one process too (tools/NodeCensus
--dump). For each program both sides give either the syntax errors in full
(FormatDetailed) or the tree as -DebugAst shows it (otter.ps1's
Show-OtterAst), and the two are compared. Thousands of programs take a
minute, so it suits parser fuzzing (tools/New-FuzzCorpus.ps1).

Not compared: what happens when the program runs, module resolution (`use`
is parsed, not followed), and the empty program (an internal error on both
sides, SPEC-QUESTIONS Q12).

Exit code 0 when every program matched. The differences are written to
-ReportPath.

.EXAMPLE
pwsh -NoProfile -Command "& tools/compare-parse.ps1 -OtterPs ../otterPS-1.0.1 -Corpus @('tests/compat', 'fuzz')"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string[]]$Corpus,
    [string]$ReportPath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/parse-report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null

$files = @(foreach ($item in $Corpus) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { (Resolve-Path -LiteralPath $item).Path }
    else { Get-ChildItem -LiteralPath $item -Recurse -Filter '*.ot' -File | ForEach-Object { $_.FullName } }
}) | Sort-Object -Unique
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-compare-parse-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$list = Join-Path $work 'files.txt'
[System.IO.File]::WriteAllLines($list, [string[]]$files)

function Read-Blocks([string[]]$Lines) {
    $blocks = @{}
    $current = $null
    $body = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $Lines) {
        if ($line.StartsWith('=== ')) {
            if ($null -ne $current) { $blocks[$current] = ($body -join "`n").TrimEnd() }
            $current = $line.Substring(4); $body.Clear(); continue
        }
        $body.Add($line)
    }
    if ($null -ne $current) { $blocks[$current] = ($body -join "`n").TrimEnd() }
    return $blocks
}

try {
    # otter-net: tools/NodeCensus --dump, in batches.
    $census = Join-Path $here 'tools/NodeCensus'
    & dotnet build $census -c Release -nologo -v quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'tools/NodeCensus did not build.' }
    $dll = Join-Path $census 'bin/Release/net10.0/NodeCensus.dll'
    $netLines = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $files.Count; $i += 200) {
        $batch = @($files[$i..([Math]::Min($i + 199, $files.Count - 1))])
        foreach ($l in (& dotnet $dll --dump @batch)) { $netLines.Add($l) }
    }
    $net = Read-Blocks $netLines

    # otterPS: its modules loaded once; Show-OtterAst taken from otter.ps1.
    $otterScript = Join-Path $OtterPs 'otter.ps1'
    $child = Join-Path $work 'reference.ps1'
    $script = @"
using module '$(Join-Path $OtterPs 'Otter.Contract.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Runtime.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Lexer.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Parser.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Validation.psm1')'
`$ErrorActionPreference = 'Stop'
`$tokens = `$null; `$errors = `$null
`$ast = [System.Management.Automation.Language.Parser]::ParseFile('$otterScript', [ref]`$tokens, [ref]`$errors)
`$definition = `$ast.Find({ param(`$n) `$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and `$n.Name -eq 'Show-OtterAst' }, `$true)
. ([scriptblock]::Create(`$definition.Extent.Text))
`$out = [System.Collections.Generic.List[string]]::new()
foreach (`$path in [System.IO.File]::ReadAllLines('$list')) {
    `$out.Add("=== `$path")
    `$source = [System.IO.File]::ReadAllText(`$path)
    try {
        if (`$source.Length -eq 0) { throw [System.InvalidOperationException]::new("Cannot bind argument to parameter 'Source' because it is an empty string.") }
        `$t = ConvertTo-OtterTokens -Source `$source
        `$program = ConvertTo-OtterAst -Tokens `$t
        Assert-OtterLanguageContract -Program `$program -SourceLines (`$source -split "``r?``n")
        foreach (`$line in @(Show-OtterAst -Node `$program -Depth 1 6>&1)) { `$out.Add("`$line") }
    }
    catch {
        if (`$_.Exception -is [OtterError]) { `$out.Add(`$_.Exception.FormatDetailed().Replace("``r``n", "``n")) }
        else { `$out.Add("INTERNAL " + `$_.Exception.Message) }
    }
}
[System.IO.File]::WriteAllLines('$(Join-Path $work 'reference.txt')', `$out)
"@
    [System.IO.File]::WriteAllText($child, $script)
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' }
    & $hostExe -NoProfile -File $child
    if ($LASTEXITCODE -ne 0) { throw 'The otterPS side failed.' }
    $ref = Read-Blocks ([System.IO.File]::ReadAllLines((Join-Path $work 'reference.txt')))

    $different = [System.Collections.Generic.List[object]]::new()
    $valid = 0; $refused = 0
    foreach ($path in $files) {
        $a = $ref[$path]; $b = $net[$path]
        if ($a -ceq $b) { if ($a -match '^\s+Program') { $valid++ } else { $refused++ }; continue }
        $different.Add([pscustomobject]@{ File = $path; Reference = $a; OtterNet = $b })
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# otter check: otterPS 1.0.1 vs otter-net (parse only)')
    $lines.Add('')
    $lines.Add("$($files.Count) programs: **$($different.Count) different**; identical: $valid parsed to the same tree, $refused refused with the same errors.")
    foreach ($d in $different) {
        $lines.Add('')
        $lines.Add("## $($d.File)")
        $lines.Add('')
        $lines.Add('otterPS:'); $lines.Add('```'); $lines.Add([string]$d.Reference); $lines.Add('```')
        $lines.Add('otter-net:'); $lines.Add('```'); $lines.Add([string]$d.OtterNet); $lines.Add('```')
    }
    [System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "$($files.Count) programs: $($different.Count) different, $valid same tree, $refused same errors. Report: $ReportPath"
    if ($different.Count -gt 0) { exit 1 }
    exit 0
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
