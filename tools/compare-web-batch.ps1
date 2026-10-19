<#
.SYNOPSIS
Compares the web page each program compiles to - otterPS 1.0.1's web
compiler against otter-net's - for thousands of programs, loading otterPS
once.

.DESCRIPTION
tools/compare-web.ps1 runs `otter web` through otter.ps1 once per program
(seconds each). This parses and compiles every program in one PowerShell
process with otterPS's modules (ConvertTo-OtterWeb, as
Export-OtterWebApplication calls it, with the file's name as the default
title), and runs `otter web <file> -NoOpen` of otter-net for each. Compared:
the page (otter-net's Windows PowerShell 5.1 form turned into PowerShell 7's
on a PowerShell 7 reference, as compare-web.ps1 does: no byte order mark,
LF ending, < > & ' not escaped in JSON), or the syntax and compile errors in
full. Suited to fuzzing the web compiler with mutated UI programs.

Not compared: sidecar stylesheets (none is copied) and what
otter.ps1 itself prints. Exit code 0 when every program matched.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OtterNet,
    [Parameter(Mandatory)][string[]]$Corpus,
    # Files copied next to every program (modules it uses, for example
    # otter-docs/pages/_shell.ot for mutated documentation pages).
    [string[]]$SupportFiles = @(),
    [string]$ReportPath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/web-batch-report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$pwsh7Reference = [System.IO.Path]::DirectorySeparatorChar -ne '\'

$files = @(foreach ($item in $Corpus) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { (Resolve-Path -LiteralPath $item).Path }
    else { Get-ChildItem -LiteralPath $item -Recurse -Filter '*.ot' -File | ForEach-Object { $_.FullName } }
}) | Sort-Object -Unique
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-compare-web-batch-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$list = Join-Path $work 'files.txt'
[System.IO.File]::WriteAllLines($list, [string[]]$files)
# Each program gets its own folder, the same for both sides, so paths in
# messages agree: run/<n>/<name>.ot with the support files beside it.
$runRoot = Join-Path $work 'run'
New-Item -ItemType Directory -Path $runRoot | Out-Null
$supportList = Join-Path $work 'support.txt'
[System.IO.File]::WriteAllLines($supportList, [string[]]@($SupportFiles | ForEach-Object { (Resolve-Path -LiteralPath $_).Path }))
$utf8 = [System.Text.UTF8Encoding]::new($false)

function ConvertTo-Pwsh7Form([string]$Text) {
    if ($Text.Length -gt 0 -and $Text[0] -eq [char]0xFEFF) { $Text = $Text.Substring(1) }
    if ($Text.EndsWith("`r`n")) { $Text = $Text.Substring(0, $Text.Length - 2) }
    return $Text.Replace('\u003c', '<').Replace('\u003e', '>').Replace('\u0026', '&').Replace('\u0027', "'")
}

try {
    # otterPS: one process, the modules loaded once; one result file per program.
    $refDir = Join-Path $work 'reference'
    New-Item -ItemType Directory -Path $refDir | Out-Null
    $child = Join-Path $work 'reference.ps1'
    $script = @"
using module '$(Join-Path $OtterPs 'Otter.Contract.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Runtime.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Lexer.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Parser.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Validation.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Module.psm1')'
using module '$(Join-Path $OtterPs 'src/Otter.Web.psm1')'
`$ErrorActionPreference = 'Stop'
`$utf8 = [System.Text.UTF8Encoding]::new(`$false)
`$index = 0
`$support = [System.IO.File]::ReadAllLines('$supportList')
foreach (`$original in [System.IO.File]::ReadAllLines('$list')) {
    `$index++
    `$dir = Join-Path '$runRoot' "`$index"
    [void][System.IO.Directory]::CreateDirectory(`$dir)
    foreach (`$file in `$support) { if (`$file) { Copy-Item -LiteralPath `$file -Destination `$dir } }
    `$path = Join-Path `$dir ([System.IO.Path]::GetFileName(`$original))
    Copy-Item -LiteralPath `$original -Destination `$path
    `$resolved = `$null
    try {
        # Export-OtterWebApplication: the source through module resolution, so
        # an error quotes the file's own line.
        `$resolved = Resolve-OtterModuleSource -FilePath `$path
        `$source = `$resolved.CombinedSource
        `$tokens = ConvertTo-OtterTokens -Source `$source
        `$ast = ConvertTo-OtterAst -Tokens `$tokens
        Assert-OtterLanguageContract -Program `$ast -SourceLines (`$source -split "``r?``n")
        `$html = ConvertTo-OtterWeb -Program `$ast -Title ([System.IO.Path]::GetFileNameWithoutExtension(`$path))
        `$text = "PAGE``n" + `$html
    }
    catch {
        if (`$_.Exception -is [OtterError]) {
            `$err = `$_.Exception
            if (`$resolved) { `$err = ConvertTo-OtterRemappedDiagnostics -Error `$err -ResolvedProgram `$resolved -RootFile `$path }
            `$text = "ERROR``n" + `$err.FormatDetailed()
        }
        else { `$text = "INTERNAL``n" + `$_.Exception.Message }
    }
    [System.IO.File]::WriteAllText((Join-Path '$refDir' "`$index.txt"), `$text, `$utf8)
}
"@
    [System.IO.File]::WriteAllText($child, $script)
    $hostExe = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell.exe' }
    & $hostExe -NoProfile -File $child
    if ($LASTEXITCODE -ne 0) { throw 'The otterPS side failed.' }

    $different = [System.Collections.Generic.List[object]]::new()
    $pages = 0; $errors = 0
    for ($i = 0; $i -lt $files.Count; $i++) {
        $path = $files[$i]
        $reference = [System.IO.File]::ReadAllText((Join-Path $refDir "$($i + 1).txt"))
        # otter-net: `otter web` in the folder the reference used.
        $runDir = Join-Path $runRoot "$($i + 1)"
        $copy = Join-Path $runDir ([System.IO.Path]::GetFileName($path))
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        if ($OtterNet.EndsWith('.dll')) { $psi.FileName = 'dotnet'; $psi.ArgumentList.Add($OtterNet) } else { $psi.FileName = $OtterNet }
        foreach ($a in @('web', [System.IO.Path]::GetFileName($path), '-NoOpen')) { $psi.ArgumentList.Add($a) }
        $psi.WorkingDirectory = $runDir
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.EnvironmentVariables['OTTER_ENGINE'] = 'native'
        $process = [System.Diagnostics.Process]::Start($psi)
        $stdout = $process.StandardOutput.ReadToEnd()
        [void]$process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $htmlPath = [System.IO.Path]::ChangeExtension($copy, '.html')
        if ($process.ExitCode -eq 0 -and (Test-Path -LiteralPath $htmlPath)) {
            $page = [System.IO.File]::ReadAllText($htmlPath)
            $candidate = 'PAGE' + "`n" + $(if ($pwsh7Reference) { ConvertTo-Pwsh7Form $page } else { $page.TrimEnd("`r", "`n") })
        }
        else {
            # Show-OtterFailure's blank lines around the message.
            $text = ($stdout -replace "`r`n", "`n").Trim("`n")
            $candidate = if ($text.StartsWith('Otter hit a problem inside itself')) { "INTERNAL`n" + $text } else { "ERROR`n" + $text }
        }
        $referenceText = $reference -replace "`r`n", "`n"
        if ($referenceText.StartsWith('INTERNAL')) { $candidateMatch = $candidate.StartsWith('INTERNAL') }
        else { $candidateMatch = ($referenceText.TrimEnd("`n") -ceq $candidate.TrimEnd("`n")) }
        if ($candidateMatch) { if ($candidate.StartsWith('PAGE')) { $pages++ } else { $errors++ }; continue }
        $at = 0
        $n = [Math]::Min($referenceText.Length, $candidate.Length)
        while ($at -lt $n -and $referenceText[$at] -ceq $candidate[$at]) { $at++ }
        $from = [Math]::Max(0, $at - 60)
        $show = { param($t) ($t.Substring([Math]::Min($from, $t.Length), [Math]::Min(160, [Math]::Max(0, $t.Length - $from))) -replace "`n", '\n') }
        $different.Add([pscustomobject]@{ File = $path; Detail = "at $($at): otterPS [$(& $show $referenceText)] otter-net [$(& $show $candidate)]" })
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# otter web: otterPS 1.0.1 vs otter-net (in-process reference)')
    $lines.Add('')
    $lines.Add("$($files.Count) programs: **$($different.Count) different**; identical: $pages pages, $errors refusals.")
    $lines.Add('')
    foreach ($d in $different) { $lines.Add("- ``$($d.File)``: $($d.Detail)") }
    [System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", $utf8)
    Write-Host "$($files.Count) programs: $($different.Count) different, $pages same page, $errors same refusal. Report: $ReportPath"
    if ($different.Count -gt 0) { exit 1 }
    exit 0
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
