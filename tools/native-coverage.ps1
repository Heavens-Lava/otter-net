<#
.SYNOPSIS
Writes docs/NATIVE_COVERAGE.md: every capability of otterPS 1.0.1 mapped to
otter-net's C# source and to the tests that exercise it, found mechanically.

.DESCRIPTION
Four inventories, each read from the sources rather than written by hand:

  1. Every NodeKind of otterPS's Otter.Contract.psm1 (226): the otterPS node
     class, the parser functions that build it (`[Class]::new(` in
     src/Otter.Parser.psm1), the interpreter case that runs it (`'Kind' {`
     in src/Otter.Interpreter.psm1) and the JavaScript compiler case
     (`([NodeKind]::Kind) {` in src/Otter.Compiler.JavaScript.psm1); then
     otter-net's syntax class (the mapping -DebugAst uses, AstDump.cs), the
     parser method that builds it (`new Class(` in src/Otter.Language), the
     interpreter case (`case Class` in src/Otter.Runtime) and the web
     compiler case (src/Otter.Web); and the test programs that contain it.
     Which programs contain which kinds is measured by tools/NodeCensus (a
     developer tool, not shipped): otter-net's parser reads every program of
     tests/compat, tests/web, the .ot files of tests/cli's cases and otterPS
     1.0.1's corpus, and every node of each tree is named as -DebugAst names
     it.
  2. Every `otter` command and flag of otter.ps1 (its param() block and its
     command dispatch), with otter-net's location and the tests/cli cases
     that use it.
  3. Every function exported by src/Otter.Library.psm1 and
     src/Otter.Runtime.psm1, where otterPS calls it from, and its C#
     counterpart (found by the otterPS name otter-net's code cites, or by
     the table below for the ones it does not cite).
  4. Every place otter-net can still hand a program to the PowerShell Otter
     (`NotSupportedYet(`, `OtterNetGap`), with its reason: since 2026-10-20
     none hands anything to PowerShell; each is reported as Otter's internal error.

With -Cobertura (the report tools/coverage.ps1 measures), each otter-net
interpreter, web compiler and library location also says whether a corpus
run reached it.

Runs on Windows PowerShell 5.1 and PowerShell 7 (needs the .NET SDK).

.EXAMPLE
pwsh -NoProfile -File tools/native-coverage.ps1 -OtterPs ../otterPS-1.0.1 -Cobertura /tmp/otter-coverage.cobertura.xml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [string]$OutFile = '',
    [string]$Cobertura = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
if (-not $OutFile) { $OutFile = Join-Path $here 'docs/NATIVE_COVERAGE.md' }
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Lines([string]$Path) { return [System.IO.File]::ReadAllLines($Path) }
function Get-Relative([string]$Path, [string]$Root) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.StartsWith($Root)) { return ($full.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/') }
    return ($full -replace '\\', '/')
}
function Format-Code([string]$Text) { return ([string][char]96) + $Text + ([string][char]96) }
function Join-Cell($Items, [int]$Max = 6) {
    $list = @($Items | Where-Object { $_ })
    if ($list.Count -eq 0) { return '-' }
    $shown = @($list | Select-Object -First $Max)
    $text = ($shown -join '<br>')
    if ($list.Count -gt $Max) { $text += "<br>(+$($list.Count - $Max) more)" }
    return $text
}

# ---- line hits (tools/coverage.ps1) ----
$hits = @{}
if ($Cobertura) {
    [xml]$report = [System.IO.File]::ReadAllText((Resolve-Path $Cobertura).Path)
    foreach ($class in $report.SelectNodes('//class')) {
        $name = $class.filename -replace '\\', '/'
        $at = $name.IndexOf('/src/')
        if ($at -ge 0) { $name = $name.Substring($at + 1) } elseif (-not [System.IO.Path]::IsPathRooted($name)) { $name = 'src/' + $name }
        if ($name -match '/obj/') { continue }
        foreach ($line in $class.SelectNodes('lines/line')) {
            $key = "${name}:$($line.number)"
            $value = [long]$line.hits
            if ($hits.ContainsKey($key)) { $hits[$key] += $value } else { $hits[$key] = $value }
        }
    }
}
# Did a corpus run reach this location (or the next few lines, for a `case`
# label, which has no sequence point of its own)?
function Get-Reached([string]$File, [int]$Line) {
    if (-not $Cobertura) { return '' }
    for ($l = $Line; $l -le $Line + 3; $l++) {
        $key = "${File}:$l"
        if ($hits.ContainsKey($key)) { if ($hits[$key] -gt 0) { return ' (run)' } else { return ' (**never run**)' } }
    }
    return ''
}

# ---- otterPS: node kinds and classes ----
$contractPath = Join-Path $OtterPs 'Otter.Contract.psm1'
$contract = [System.IO.File]::ReadAllText($contractPath)
$enum = [regex]::Match($contract, '(?s)\nenum NodeKind \{(.*?)\n\}').Groups[1].Value
$kinds = @(foreach ($line in ($enum -split "`n")) { $t = ($line -replace '#.*$', '').Trim(); if ($t -match '^[A-Za-z]+$') { $t } })
$contractLines = Read-Lines $contractPath
$classKind = @{}; $classLine = @{}; $classBase = @{}
foreach ($m in [regex]::Matches($contract, '(?ms)^class (\w+) : (\w+) \{(.*?)^\}')) {
    $name = $m.Groups[1].Value
    $classBase[$name] = $m.Groups[2].Value
    $classLine[$name] = $contract.Substring(0, $m.Index).Split("`n").Count
    $found = @([regex]::Matches($m.Groups[3].Value, 'base\(\[NodeKind\]::(\w+)') | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
    if ($found.Count -eq 1) { $classKind[$name] = $found[0] }
}
foreach ($name in @($classBase.Keys)) {
    $c = $name
    while (-not $classKind.ContainsKey($name) -and $classBase.ContainsKey($c) -and $classBase[$c] -ne 'Node') {
        $c = $classBase[$c]
        if ($classKind.ContainsKey($c)) { $classKind[$name] = $classKind[$c] }
    }
}
$kindClasses = @{}
foreach ($pair in $classKind.GetEnumerator()) {
    if (-not $kindClasses.ContainsKey($pair.Value)) { $kindClasses[$pair.Value] = New-Object System.Collections.Generic.List[string] }
    $kindClasses[$pair.Value].Add($pair.Key)
}

# otterPS functions enclosing a line of a .psm1.
function Get-PsFunctionIndex([string[]]$Lines) {
    $index = New-Object 'int[]' ($Lines.Count + 1)
    $names = @{ 0 = '(module body)' }
    $current = 0
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^function ([\w-]+)') { $current = $i + 1; $names[$current] = $Matches[1] }
        $index[$i + 1] = $current
    }
    return @{ Index = $index; Names = $names }
}
$psParserPath = Join-Path $OtterPs 'src/Otter.Parser.psm1'
$psParser = Read-Lines $psParserPath
$psParserFunctions = Get-PsFunctionIndex $psParser
$psInterpreterPath = Join-Path $OtterPs 'src/Otter.Interpreter.psm1'
$psInterpreter = Read-Lines $psInterpreterPath
$psJsPath = Join-Path $OtterPs 'src/Otter.Compiler.JavaScript.psm1'
$psJs = Read-Lines $psJsPath

# otterPS's compiled engine (`otter run` of a program in its subset): the
# kinds its ConvertTo-OtterNative* switches handle.
$psNative = Read-Lines (Join-Path $OtterPs 'src/Otter.Compiler.Native.psm1')
$psNativeCases = @{}
$psNativeExperimental = @([regex]::Matches(($psNative -join "`n"), "NativeExperimentalKinds = @\(([^)]*)\)").Groups[1].Value -split ',' | ForEach-Object { $_.Trim().Trim("'") })
for ($i = 0; $i -lt $psNative.Count; $i++) {
    foreach ($m in [regex]::Matches($psNative[$i], "^\s+'(\w+)'\s*\{|'(\w+)',\s*'")) {
        $k = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        if ($kinds -contains $k -and -not $psNativeCases.ContainsKey($k)) { $psNativeCases[$k] = $i + 1 }
    }
    if ($psNative[$i] -match "\`$_ -in ('[\w']+(,\s*'\w+')*)") {
        foreach ($w in [regex]::Matches($Matches[1], "'(\w+)'")) { $k = $w.Groups[1].Value; if ($kinds -contains $k -and -not $psNativeCases.ContainsKey($k)) { $psNativeCases[$k] = $i + 1 } }
    }
}
$psBuilds = @{}
for ($i = 0; $i -lt $psParser.Count; $i++) {
    foreach ($m in [regex]::Matches($psParser[$i], '\[(\w+)\]::new\(')) {
        $cls = $m.Groups[1].Value
        if (-not $classKind.ContainsKey($cls)) { continue }
        $kind = $classKind[$cls]
        $fn = $psParserFunctions.Names[$psParserFunctions.Index[$i + 1]]
        if (-not $psBuilds.ContainsKey($kind)) { $psBuilds[$kind] = [ordered]@{} }
        if (-not $psBuilds[$kind].Contains($fn)) { $psBuilds[$kind][$fn] = $i + 1 }
    }
}
$psCases = @{}
for ($i = 0; $i -lt $psInterpreter.Count; $i++) {
    if ($psInterpreter[$i] -match "^\s{8}'(\w+)'\s*\{" -and $kinds -contains $Matches[1]) {
        if (-not $psCases.ContainsKey($Matches[1])) { $psCases[$Matches[1]] = $i + 1 }
    }
}
$psJsCases = @{}
for ($i = 0; $i -lt $psJs.Count; $i++) {
    foreach ($m in [regex]::Matches($psJs[$i], '\(\[NodeKind\]::(\w+)\)\s*\{')) {
        if (-not $psJsCases.ContainsKey($m.Groups[1].Value)) { $psJsCases[$m.Groups[1].Value] = $i + 1 }
    }
}

# ---- otter-net: syntax classes and where they are built, run and compiled ----
$csFiles = @{}
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $here 'src') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
    $csFiles[(Get-Relative $file.FullName $here)] = Read-Lines $file.FullName
}
# C# methods enclosing a line (members of a class: four spaces in).
$methodPattern = '^    (?! )(?!(?:return|throw|if|else|case|new|for|foreach|while|switch|var|await|using|yield|break|continue|do|try|catch|finally|lock|get|set|init)\b)(?:[\w<>\[\],.?()]+\s+)+?(\w+)\s*(?:<[^>]*>)?\('
function Get-CsMethod([string]$File, [int]$Line) {
    $lines = $csFiles[$File]
    for ($i = $Line - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match $methodPattern) { return $Matches[1] }
    }
    return '?'
}
function Find-Cs([string]$PathPattern, [string]$Pattern, [int]$Max = 4, [switch]$Comments) {
    $found = New-Object System.Collections.Generic.List[object]
    foreach ($file in ($csFiles.Keys | Where-Object { $_ -match $PathPattern } | Sort-Object)) {
        $lines = $csFiles[$file]
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $Pattern -and ($Comments -or $lines[$i] -notmatch '^\s*//')) {
                $found.Add([pscustomobject]@{ File = $file; Line = $i + 1 })
                if ($found.Count -ge $Max) { return $found }
            }
        }
    }
    return $found
}

# otter-net class -> otterPS class, as -DebugAst maps them (AstDump.PsClass).
$astDump = $csFiles['src/Otter.Cli/AstDump.cs']
$netToPs = @{}
$outer = $null
foreach ($line in $astDump) {
    if ($line -match '^\s*static string PsClass') { $outer = ''; continue }
    if ($null -eq $outer) { continue }
    if ($line -match '^\s*_ => node\.GetType\(\)\.Name') { break }
    if ($line -match '^\s*([\w ]+?)(?: \w+)? => \w+\.\w+ switch') { $outer = $Matches[1]; continue }
    if ($line -match '^\s*\},') { $outer = ''; continue }
    $targets = @([regex]::Matches($line, '"(\w+)"') | ForEach-Object { $_.Groups[1].Value })
    if ($targets.Count -eq 0) { continue }
    $sources = if ($outer) { @($outer) } else { @(([regex]::Match($line, '^\s*([\w ]+?)(?: [a-z]\w*)? =>').Groups[1].Value -split ' or ') | ForEach-Object { $_.Trim() }) }
    foreach ($s in $sources) {
        if (-not $s) { continue }
        if (-not $netToPs.ContainsKey($s)) { $netToPs[$s] = New-Object System.Collections.Generic.List[string] }
        foreach ($t in $targets) { if (-not $netToPs[$s].Contains($t)) { $netToPs[$s].Add($t) } }
    }
}

# ---- the census ----
$census = Join-Path $here 'tools/NodeCensus'
& dotnet build $census -c Release -nologo -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'tools/NodeCensus did not build.' }
$censusDll = Join-Path $census 'bin/Release/net10.0/NodeCensus.dll'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-native-coverage-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $scratch | Out-Null
$programs = New-Object System.Collections.Generic.List[object]
foreach ($folder in @('tests/compat', 'tests/web', 'tests/network')) {
    foreach ($f in Get-ChildItem -LiteralPath (Join-Path $here $folder) -Recurse -Filter '*.ot' -File) {
        $programs.Add([pscustomobject]@{ Path = $f.FullName; Corpus = $folder; Name = (Get-Relative $f.FullName $here) })
    }
}
foreach ($folder in @('conformance', 'examples', 'benchmarks', 'experiments', 'otter-docs', 'OtterWorkspace', 'projects', 'selfhost')) {
    foreach ($f in Get-ChildItem -LiteralPath (Join-Path $OtterPs $folder) -Recurse -Filter '*.ot' -File) {
        $programs.Add([pscustomobject]@{ Path = $f.FullName; Corpus = 'otterPS'; Name = 'otterPS/' + (Get-Relative $f.FullName $OtterPs) })
    }
}
$cliCases = @{}
foreach ($caseFile in Get-ChildItem -LiteralPath (Join-Path $here 'tests/cli') -Filter '*.psd1' | Sort-Object Name) {
    $data = Import-PowerShellDataFile -LiteralPath $caseFile.FullName
    $n = 0
    foreach ($case in $data.Cases) {
        $n++
        $cliCases["$($caseFile.BaseName): $($case.Name)"] = @(@($case.Args) | ForEach-Object { [string]$_ })
        if (-not $case.Files) { continue }
        foreach ($name in $case.Files.Keys) {
            if ($name -notmatch '\.ot$') { continue }
            $target = Join-Path $scratch "$($caseFile.BaseName)-$n-$([IO.Path]::GetFileName($name))"
            [System.IO.File]::WriteAllText($target, ([string]$case.Files[$name] -replace "`r`n", "`n"), $utf8)
            $programs.Add([pscustomobject]@{ Path = $target; Corpus = 'tests/cli'; Name = "tests/cli/$($caseFile.Name) ""$($case.Name)""" })
        }
    }
}
$listFile = Join-Path $scratch 'programs.txt'
$byPath = @{}
foreach ($p in $programs) { $byPath[$p.Path] = $p }
$censusLines = New-Object System.Collections.Generic.List[string]
# Several batches: a command line has a length limit on Windows.
$batch = New-Object System.Collections.Generic.List[string]
$all = @($programs | ForEach-Object { $_.Path })
for ($i = 0; $i -lt $all.Count; $i++) {
    $batch.Add($all[$i])
    if ($batch.Count -ge 60 -or $i -eq $all.Count - 1) {
        $output = & dotnet $censusDll @($batch.ToArray())
        if ($LASTEXITCODE -ne 0) { throw 'NodeCensus failed.' }
        foreach ($l in $output) { $censusLines.Add($l) }
        $batch.Clear()
    }
}
Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue

$kindTests = @{}     # kind -> list of program records
$netClassKinds = @{} # otter-net class -> kinds seen
$netNodeClasses = New-Object System.Collections.Generic.List[string]
$parsedPrograms = 0; $refusedPrograms = 0
foreach ($line in $censusLines) {
    $parts = $line -split "`t"
    switch ($parts[0]) {
        'FILE' {
            $parsedPrograms++
            $program = $byPath[$parts[1]]
            foreach ($entry in ($parts[2] -split ',')) {
                $k = ($entry -split '=')[0]
                if (-not $kindTests.ContainsKey($k)) { $kindTests[$k] = New-Object System.Collections.Generic.List[object] }
                $kindTests[$k].Add($program)
            }
        }
        'ERROR' { $refusedPrograms++ }
        'CLASS' {
            if (-not $netClassKinds.ContainsKey($parts[1])) { $netClassKinds[$parts[1]] = New-Object System.Collections.Generic.List[string] }
            if (-not $netClassKinds[$parts[1]].Contains($parts[2])) { $netClassKinds[$parts[1]].Add($parts[2]) }
        }
        'NODE' { $netNodeClasses.Add($parts[1]) }
    }
}
# kind -> otter-net classes: the census, -DebugAst's mapping, and same-named classes.
$kindNet = @{}
function Add-KindNet([string]$Kind, [string]$Class) {
    if (-not $kindNet.ContainsKey($Kind)) { $kindNet[$Kind] = New-Object System.Collections.Generic.List[string] }
    if (-not $kindNet[$Kind].Contains($Class)) { $kindNet[$Kind].Add($Class) }
}
foreach ($cls in $netNodeClasses) {
    if ($netToPs.ContainsKey($cls)) { foreach ($ps in $netToPs[$cls]) { if ($classKind.ContainsKey($ps)) { Add-KindNet $classKind[$ps] $cls } } }
    elseif ($classKind.ContainsKey($cls)) { Add-KindNet $classKind[$cls] $cls }
}
foreach ($pair in $netClassKinds.GetEnumerator()) { foreach ($k in $pair.Value) { if ($kinds -contains $k) { Add-KindNet $k $pair.Key } } }

# Notes on kinds whose status needs words (wave 4 and what otterPS itself
# does not run).
$kindNotes = @{
    UiLayout = 'enum value only: otterPS 1.0.1 has no node class for it and nothing builds it'
    RouteParameter = 'unreachable in 1.0.1: `parameter` is a keyword only as the first word of a line (D33 mechanism 2), so `route parameter X` never lexes as this form, in otterPS or otter-net (SPEC-QUESTIONS V4-1)'
    QueryParameter = 'unreachable in 1.0.1: `query` and `parameter` are keywords only as the first word of a line (D33 mechanism 2), so `query parameter X` never lexes as this form, in otterPS or otter-net (SPEC-QUESTIONS V4-1)'
    RunRemoteCommand = 'native: on Windows through WinRM and the PowerShell Remoting Protocol, as Invoke-Command (`RemoteCommands.cs`, `Psrp.cs`; protocol tested locally, a real WinRM connection not yet observed); off Windows otterPS''s own errors'
    CreateUiResource = 'web pages and the run-time UI native; a WPF window in `otter run` on Windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
    When = 'web pages and the run-time UI native; WPF windows on Windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
    PutIn = 'web pages and the run-time UI native; WPF windows on Windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
    Show = 'web pages and the run-time UI native; WPF windows on Windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
}

$kindRows = New-Object System.Collections.Generic.List[string]
$stats = @{ Native = 0; Tested = 0; Unreachable = 0; Untested = New-Object System.Collections.Generic.List[string]; NotNode = 0; Missing = New-Object System.Collections.Generic.List[string]; RunReached = 0; RunMeasured = 0 }
foreach ($kind in $kinds) {
    $psClasses = @(); if ($kindClasses.ContainsKey($kind)) { $psClasses = @($kindClasses[$kind] | Sort-Object) }
    $psSide = New-Object System.Collections.Generic.List[string]
    foreach ($c in $psClasses) { $psSide.Add("class ``$c`` (Otter.Contract.psm1:$($classLine[$c]))") }
    if ($psBuilds.ContainsKey($kind)) { foreach ($fn in $psBuilds[$kind].Keys) { $psSide.Add("parser ``$fn`` (Otter.Parser.psm1:$($psBuilds[$kind][$fn]))") } }
    if ($psCases.ContainsKey($kind)) { $psSide.Add("interpreter ``'$kind'`` (Otter.Interpreter.psm1:$($psCases[$kind]))") }
    if ($psJsCases.ContainsKey($kind)) { $psSide.Add("JS compiler (Otter.Compiler.JavaScript.psm1:$($psJsCases[$kind]))") }
    if ($psNativeCases.ContainsKey($kind)) {
        $experimental = if ($psNativeExperimental -contains $kind) { ', only with OTTER_NATIVE_EXPERIMENTAL=1' } else { '' }
        $psSide.Add("compiled engine (Otter.Compiler.Native.psm1:$($psNativeCases[$kind])$experimental)")
    }

    $netSide = New-Object System.Collections.Generic.List[string]
    $netClasses = @(); if ($kindNet.ContainsKey($kind)) { $netClasses = @($kindNet[$kind] | Sort-Object) }
    if ($kind -eq 'Program') { $netClasses = @('ProgramNode') }
    $parsed = $false; $runSite = $false; $webSite = $false; $reached = $null
    foreach ($c in $netClasses) {
        $decl = @(Find-Cs '^src/Otter\.Language/Syntax' "\b(class|record) $c\b" 1)
        if ($decl.Count) { $netSide.Add("``$c`` ($($decl[0].File -replace '^src/', ''):$($decl[0].Line))") }
        foreach ($site in @(Find-Cs '^src/Otter\.Language/Parser' "new $c\(" 3)) {
            $parsed = $true
            $netSide.Add("parser ``$(Get-CsMethod $site.File $site.Line)`` ($($site.File -replace '^src/', ''):$($site.Line))")
        }
        foreach ($site in @(Find-Cs '^src/Otter\.Runtime/(?!CompiledEngine)' "(case $c\b|\b$c \w+ (=>|when)|is $c\b)" 2)) {
            $runSite = $true
            $r = Get-Reached $site.File $site.Line
            if ($r) { if ($r -like '*(run)*') { $reached = $true } elseif ($null -eq $reached) { $reached = $false } }
            $netSide.Add("run ($($site.File -replace '^src/', ''):$($site.Line))$r")
        }
        foreach ($site in @(Find-Cs '^src/Otter\.Runtime/CompiledEngine' "^\s+$c\b" 1)) {
            $netSide.Add("compiled-engine subset ($($site.File -replace '^src/', ''):$($site.Line))")
        }
        foreach ($site in @(Find-Cs '^src/Otter\.Web/(JsCompiler|DeclarativeRenderer|RuntimeUi|WebCompiler)' "(case $c\b|\b$c \w+ (=>|when)|is $c\b)" 2)) {
            $webSite = $true
            $netSide.Add("web ($($site.File -replace '^src/', ''):$($site.Line))$(Get-Reached $site.File $site.Line)")
        }
    }
    $tests = @(); if ($kindTests.ContainsKey($kind)) { $tests = @($kindTests[$kind] | Sort-Object Name -Unique) }
    $byCorpus = @($tests | Group-Object Corpus | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" })
    $examples = @($tests | Where-Object { $_.Corpus -ne 'otterPS' } | Select-Object -First 3 | ForEach-Object { "``$($_.Name)``" })
    if ($examples.Count -eq 0) { $examples = @($tests | Select-Object -First 3 | ForEach-Object { "``$($_.Name)``" }) }
    $testCell = if ($tests.Count) { (($byCorpus -join ', ') + '<br>' + ($examples -join '<br>')) } else { '**none**' }

    if ($psClasses.Count -eq 0) { $status = 'not a node'; $stats.NotNode++ }
    elseif ($netClasses.Count -eq 0 -or -not $parsed) { $status = '**MISSING**'; $stats.Missing.Add($kind) }
    else {
        $stats.Native++
        if ($tests.Count) { $stats.Tested++; $status = 'native, tested' }
        elseif ($kind -in @('RouteParameter', 'QueryParameter')) { $status = 'native (unreachable)'; $stats.Unreachable++ }
        else { $status = '**native, untested**'; $stats.Untested.Add($kind) }
        if ($null -ne $reached) { $stats.RunMeasured++; if ($reached) { $stats.RunReached++ } }
        if (-not $runSite -and -not $psCases.ContainsKey($kind) -and $kind -ne 'Program') { $status += '; web only (run: otterPS''s own not-runnable error)' }
    }
    if ($kindNotes.ContainsKey($kind)) { $status += '; ' + $kindNotes[$kind] }
    $kindRows.Add("| $kind | $(Join-Cell $psSide 8) | $(Join-Cell $netSide 8) | $testCell | $status |")
}

# ---- the command line ----
$otterPs1 = Read-Lines (Join-Path $OtterPs 'otter.ps1')
function Find-Ps1([string]$Pattern) {
    for ($i = 0; $i -lt $otterPs1.Count; $i++) { if ($otterPs1[$i] -match $Pattern) { return "otter.ps1:$($i + 1)" } }
    return '-'
}
# Every parameter of otter.ps1's param() block.
$paramText = [regex]::Match(([System.IO.File]::ReadAllText((Join-Path $OtterPs 'otter.ps1'))), '(?s)\nparam\((.*?)\n\)').Groups[1].Value
$parameters = @([regex]::Matches($paramText, '\[(?:string|int|switch|string\[\])\]\$(\w+)') | ForEach-Object { $_.Groups[1].Value })
# Commands: every `$Path -eq '...'` / `-in @(...)` of the dispatch.
$commands = New-Object System.Collections.Generic.List[string]
foreach ($m in [regex]::Matches(([System.IO.File]::ReadAllText((Join-Path $OtterPs 'otter.ps1'))), "\`$Path -(?:eq|in) (?:@\()?('[^)]*?')(?:\))?(?:\s|\))")) {
    foreach ($w in [regex]::Matches($m.Groups[1].Value, "'([^']+)'")) { if (-not $commands.Contains($w.Groups[1].Value)) { $commands.Add($w.Groups[1].Value) } }
}
# Each CLI form: where otterPS handles it, where otter-net does, which cases use it.
# Test = a regex over a case's arguments joined by spaces.
$cliForms = @(
    @{ Form = 'otter (REPL)'; Ps = 'Start-OtterRepl'; Net = @('src/Otter.Cli/Repl.cs', 'static int Start\('); Test = '^$' }
    @{ Form = 'REPL `exit`, `reset`, multi-line blocks'; Ps = "Trim\(\) -eq 'reset'"; Net = @('src/Otter.Cli/Repl.cs', 'reset'); Test = '^$'; Stdin = 'reset|exit' }
    @{ Form = 'otter <file.ot> (shortest form)'; Ps = 'Canonical shortest form'; Net = @('src/Otter.Cli/Cli.cs', 'static int RunPath\('); Test = '(?i)^[^ ]+\.ot( |$)' }
    @{ Form = 'otter <folder or otter.json> (project)'; Ps = 'D118B: Check if \$Path is a project'; Net = @('src/Otter.Cli/Cli.cs', 'static int RunPath\('); Test = '(?i)^((run|check|web|browse|serve|desktop|build|publish|test) )?(\.|~|proj\S*|\S*otter\.json|\S+/)( |$)' }
    @{ Form = 'otter <unknown command>'; Ps = 'I do not recognize the command'; Net = @('src/Otter.Cli/Cli.cs', 'I do not recognize the command'); Test = '(?i)^(?!(run|check|new|test|build|publish|profile|debug|web|browse|serve|desktop|studio|help|--help|--version|-)\b)[a-z]\w*( |$)' }
    @{ Form = 'otter run <file.ot|project> [args]'; Ps = "\`$Path -eq 'run' -or"; Net = @('src/Otter.Cli/Cli.cs', 'static int RunOrCheck\('); Test = '(?i)^run\b' }
    @{ Form = 'otter check <file.ot|project>'; Ps = "\`$Path -eq 'run' -or"; Net = @('src/Otter.Cli/Cli.cs', 'static int RunOrCheck\('); Test = '(?i)^check\b' }
    @{ Form = 'otter new <archetype> <name>'; Ps = "\`$Path -eq 'new'"; Net = @('src/Otter.Cli/ProjectCommands.cs', 'static int New\('); Test = '(?i)^new\b' }
    @{ Form = 'otter test [target]'; Ps = "\`$Path -eq 'test'"; Net = @('src/Otter.Cli/ProjectCommands.cs', 'static int Test\('); Test = '(?i)^test\b' }
    @{ Form = 'otter build [target]'; Ps = "\`$Path -eq 'build'"; Net = @('src/Otter.Cli/ProjectBuild.cs', 'static int Build\('); Test = '(?i)^build\b' }
    @{ Form = 'otter publish [target] [--output|-Output dir]'; Ps = "\`$Path -eq 'publish'"; Net = @('src/Otter.Cli/ProjectBuild.cs', 'static int Publish\('); Test = '(?i)^publish\b' }
    @{ Form = 'otter profile <file.ot>'; Ps = "\`$Path -eq 'profile'"; Net = @('src/Otter.Cli/Cli.cs', 'static int Profile\('); Test = '(?i)^profile\b' }
    @{ Form = 'otter debug <file.ot> [-Breakpoints]'; Ps = "\`$Path -eq 'debug'"; Net = @('src/Otter.Cli/Cli.cs', 'static int Debug\('); Test = '(?i)^debug\b' }
    @{ Form = 'otter web <file.ot> [-NoOpen]'; Ps = "\`$Path -eq 'web' -or"; Net = @('src/Otter.Cli/WebCommand.cs', 'static int\? Run\('); Test = '(?i)^web\b' }
    @{ Form = 'otter browse <file.ot>'; Ps = "\`$Path -eq 'web' -or"; Net = @('src/Otter.Cli/Cli.cs', 'Is\(command, "browse"\)'); Test = '(?i)^browse\b' }
    @{ Form = 'otter serve <file.ot> [-Port N]'; Ps = "\`$Path -eq 'serve'"; Net = @('src/Otter.Cli/ServeCommand.cs', 'static int Run\('); Test = '(?i)^serve\b' }
    @{ Form = 'otter desktop <file.ot>'; Ps = "\`$Path -eq 'desktop'"; Net = @('src/Otter.Cli/DesktopCommand.cs', 'static int Run\('); Test = '(?i)^desktop\b' }
    @{ Form = 'otter studio'; Ps = "\`$Path -eq 'studio'"; Net = @('src/Otter.Cli/Cli.cs', 'otter studio is not native'); Test = '(?i)^studio\b' }
    @{ Form = 'otter help / --help / -help'; Ps = "HelpFlag -or"; Net = @('src/Otter.Cli/Cli.cs', 'static void ShowHelp\('); Test = '(?i)(^| )(help|--help|-help|-he)( |$)' }
    @{ Form = 'otter --version / -version / -v'; Ps = "VersionFlag -or"; Net = @('src/Otter.Cli/Cli.cs', 'line\.Version \|\|'); Test = '(?i)(^| )(--version|-version|-v|-ve\w*)( |$)' }
    @{ Form = '-Open'; Ps = '\[switch\]\$Open'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("Open"'); Test = '(?i)(^| )-Open( |$)' }
    @{ Form = '-NoOpen'; Ps = '\[switch\]\$NoOpen'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("NoOpen"'); Test = '(?i)(^| )-NoOpen( |$)' }
    @{ Form = '-Port N'; Ps = '\[int\]\$Port'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("Port"'); Test = '(?i)(^| )-Port\b' }
    @{ Form = '-DebugTokens'; Ps = '\[switch\]\$DebugTokens'; Net = @('src/Otter.Cli/FileMode.cs', 'WriteTokens\(\)'); Test = '(?i)(^| )-DebugTokens( |$)' }
    @{ Form = '-DebugAst'; Ps = '\[switch\]\$DebugAst'; Net = @('src/Otter.Cli/AstDump.cs', 'public static List<string> Lines\('); Test = '(?i)(^| )-DebugAst( |$)' }
    @{ Form = '-ParseOnly'; Ps = '\[switch\]\$ParseOnly'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("ParseOnly"'); Test = '(?i)(^| )-ParseOnly( |$)' }
    @{ Form = '-DebugErrors'; Ps = '\[switch\]\$DebugErrors'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("DebugErrors"'); Test = '(?i)(^| )-DebugErrors( |$)' }
    @{ Form = '-Breakpoints "5,12"'; Ps = '\[string\]\$Breakpoints'; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("Breakpoints"'); Test = '(?i)(^| )-Breakpoints\b' }
    @{ Form = 'trailing program arguments (D94)'; Ps = 'function Get-OtterRawTrailingArguments'; Net = @('src/Otter.Cli/CommandLine.cs', 'OwnSwitchFlags'); Test = '(?i)\.ot \S' }
    @{ Form = 'PowerShell common parameters (-Verbose, -Debug, -ErrorAction, -OutVariable, ...)'; Ps = 'OtterOwnSwitchFlags = '; Net = @('src/Otter.Cli/CommandLine.cs', 'new\("Verbose"'); Test = '(?i)(^| )-(Verbose|vb|Debug|db|ErrorAction|ea|WarningAction|wa|InformationAction|infa|ErrorVariable|ev|WarningVariable|wv|InformationVariable|iv|OutVariable|ov|OutBuffer|ob|PipelineVariable|pv)\b' }
    @{ Form = 'parameter-binding errors (unknown or ambiguous flag, bad -Port)'; Ps = 'param\('; Net = @('src/Otter.Cli/CommandLine.cs', 'class CommandLine'); Test = '(?i)(^| )-(Deb|Port abc|Nope|Foo)\b|-Port \D' }
    @{ Form = 'OTTER_ENGINE=interpreter / compiled, OTTER_ENGINE_TRACE, OTTER_COMPILED_CACHE (otterPS''s compiled engine)'; Ps = 'OTTER_ENGINE=interpreter turns it off'; Net = @('src/Otter.Cli/Program.cs', 'OTTER_ENGINE'); Test = 'OTTER_ENGINE' }
)
$caseArgs = @{}
foreach ($pair in $cliCases.GetEnumerator()) { $caseArgs[$pair.Key] = ($pair.Value -join ' ') }
$cliEnv = @{}; $cliStdin = @{}
foreach ($caseFile in Get-ChildItem -LiteralPath (Join-Path $here 'tests/cli') -Filter '*.psd1') {
    $data = Import-PowerShellDataFile -LiteralPath $caseFile.FullName
    foreach ($case in $data.Cases) {
        $key = "$($caseFile.BaseName): $($case.Name)"
        if ($case.Env) { $cliEnv[$key] = (@($case.Env.Keys) -join ' ') }
        if ($case.Stdin) { $cliStdin[$key] = [string]$case.Stdin }
    }
}
$cliRows = New-Object System.Collections.Generic.List[string]
$cliStats = @{ Forms = 0; Native = 0; Tested = 0; Untested = New-Object System.Collections.Generic.List[string] }
foreach ($form in $cliForms) {
    $cliStats.Forms++
    $netFile = $form.Net[0]
    $netSite = @(Find-Cs ('^' + [regex]::Escape($netFile) + '$') $form.Net[1] 1)
    $netCell = if ($netSite.Count) { "$($netFile -replace '^src/', ''):$($netSite[0].Line)" } else { '-' }
    $matching = @($caseArgs.Keys | Where-Object {
        $a = $caseArgs[$_]
        if ($form.Form -like 'OTTER_ENGINE*') { $cliEnv.ContainsKey($_) -and $cliEnv[$_] -match 'OTTER_ENGINE' }
        elseif ($form.Stdin) { $a -eq '' -and $cliStdin.ContainsKey($_) -and $cliStdin[$_] -match $form.Stdin }
        else { $a -match $form.Test }
    } | Sort-Object)
    $status = if ($form.Form -eq 'otter studio') { 'out of scope (the IDE is developed separately)' }
        elseif ($form.Form -like 'OTTER_ENGINE*') { 'out of scope: otterPS''s run-time C# compiler (Add-Type) gives the interpreter''s output faster; Native AOT cannot generate code and otter-net does not need it (docs/port/cli.md). OTTER_ENGINE means native/powershell to otter-net' }
        else { 'native' }
    if ($status -eq 'native') { $cliStats.Native++ }
    if ($matching.Count) { $cliStats.Tested++ } elseif ($status -eq 'native') { $cliStats.Untested.Add($form.Form) }
    $shownCases = Join-Cell (@($matching | Select-Object -First 3 | ForEach-Object { Format-Code $_ })) 3
    $cliRows.Add("| $($form.Form) | $(Find-Ps1 $form.Ps) | $netCell | $($matching.Count)<br>$shownCases | $status |")
}

# ---- the library ----
$libraryRows = New-Object System.Collections.Generic.List[string]
$libStats = @{ Functions = 0; Reachable = 0; Mapped = 0; Reached = 0; Measured = 0; Unmapped = New-Object System.Collections.Generic.List[string] }
# C# counterparts of the functions otter-net's code does not cite by name.
$counterparts = @{
    'Add-OtterFileContent' = @('src/Otter.Runtime/Files.cs', 'static void Append\(')
    'ConvertFrom-OtterInput' = @('src/Otter.Runtime/Values.cs', 'static object FromInput\(')
    'ConvertTo-OtterNumber' = @('src/Otter.Runtime/Values.cs', 'static bool TryNumber\(')
    'Copy-OtterFile' = @('src/Otter.Runtime/Files.cs', 'static void CopyFile\(')
    'Copy-OtterFolder' = @('src/Otter.Runtime/Files.cs', 'static void CopyFolder\(')
    'Expand-OtterZipArchive' = @('src/Otter.Runtime/Files.cs', 'static void Unzip\(')
    'Get-OtterFilesIn' = @('src/Otter.Runtime/Files.cs', 'static OtterList Entries\(')
    'Get-OtterFoldersIn' = @('src/Otter.Runtime/Files.cs', 'static OtterList Entries\(')
    'Get-OtterPowerActionCommandLine' = @('src/Otter.Runtime/Interpreter.System.cs', 'void RunPowerAction\(')
    'Get-OtterPowerShellHost' = @('src/Otter.Runtime/Commands.cs', '"pwsh"')
    'New-OtterFolder' = @('src/Otter.Runtime/Files.cs', 'static void CreateFolder\(')
    'Invoke-OtterJobEvent' = @('src/Otter.Runtime/Jobs.cs', 'void RunJobHandlers\(')
    'Move-OtterFile' = @('src/Otter.Runtime/Files.cs', 'static void MoveFile\(')
    'Move-OtterFolder' = @('src/Otter.Runtime/Files.cs', 'static void MoveFolder\(')
    'New-OtterList' = @('src/Otter.Runtime/Values.cs', 'class OtterList\b')
    'New-OtterNow' = @('src/Otter.Runtime/Interpreter.cs', 'new OtterDate\(DateTime\.Now')
    'New-OtterToday' = @('src/Otter.Runtime/Interpreter.cs', 'new OtterDate\(DateTime\.Now')
    'New-OtterZipArchive' = @('src/Otter.Runtime/Files.cs', 'static void ZipFolder\(')
    'Read-OtterCsvFile' = @('src/Otter.Runtime/Interpreter.cs', 'case ReadCsvStmt')
    'Read-OtterFile' = @('src/Otter.Runtime/Files.cs', 'static string Read\(string path\)')
    'Read-OtterFileBytes' = @('src/Otter.Runtime/Files.cs', 'static byte\[\] ReadBytes\(')
    'Read-OtterJsonFile' = @('src/Otter.Runtime/Interpreter.cs', 'case ReadJsonStmt')
    'Remove-OtterFile' = @('src/Otter.Runtime/Files.cs', 'static void DeleteFile\(')
    'Remove-OtterFolder' = @('src/Otter.Runtime/Files.cs', 'static void DeleteFolder\(')
    'Stop-OtterHttpRequest' = @('src/Otter.Runtime/Interpreter.Network.cs', 'cancel requires an HTTP request')
    'Test-OtterBytes' = @('src/Otter.Runtime/Values.cs', 'class OtterBytes\b')
    'Test-OtterCommandJob' = @('src/Otter.Runtime/Jobs.cs', 'class OtterCommandJob\b')
    'Test-OtterDate' = @('src/Otter.Runtime/Values.cs', 'class OtterDate\b')
    'Test-OtterEqual' = @('src/Otter.Runtime/Values.cs', 'static bool AreEqual\(')
    'Test-OtterFileExists' = @('src/Otter.Runtime/Files.cs', 'static bool Exists\(')
    'Test-OtterFileLocked' = @('src/Otter.Runtime/Files.cs', 'static bool IsLocked\(')
    'Test-OtterFileWatcher' = @('src/Otter.Runtime/NetValues.cs', 'class OtterFileWatcher\b')
    'Test-OtterHttpRequest' = @('src/Otter.Runtime/NetValues.cs', 'class OtterHttpRequest\b')
    'Test-OtterList' = @('src/Otter.Runtime/Values.cs', 'class OtterList\b')
    'Test-OtterNumeric' = @('src/Otter.Runtime/Values.cs', 'static bool TryNumber\(')
    'Test-OtterObject' = @('src/Otter.Runtime/Values.cs', 'class OtterThing\b')
    'Test-OtterTcp' = @('src/Otter.Runtime/NetValues.cs', 'class OtterTcp\(')
    'Test-OtterTcpServer' = @('src/Otter.Runtime/NetValues.cs', 'class OtterTcpServer\b')
    'Test-OtterTruthy' = @('src/Otter.Runtime/Values.cs', 'static bool IsTruthy\(')
    'Test-OtterUdp' = @('src/Otter.Runtime/NetValues.cs', 'class OtterUdp\b')
    'Test-OtterWebSocket' = @('src/Otter.Runtime/NetValues.cs', 'class OtterWebSocket\b')
    'Test-OtterXml' = @('src/Otter.Runtime/Values.cs', 'class OtterXml\b')
    'Write-OtterCsvFile' = @('src/Otter.Runtime/Interpreter.cs', 'case WriteCsvStmt')
    'Write-OtterFile' = @('src/Otter.Runtime/Files.cs', 'static void Write\(string path')
    'Write-OtterFileBytes' = @('src/Otter.Runtime/Files.cs', 'static void WriteBytes\(')
}
$libraryNotes = @{
    'Invoke-OtterRemoteCommand' = 'native: on Windows through WinRM and the PowerShell Remoting Protocol, as Invoke-Command (`RemoteCommands.cs`, `Psrp.cs`; protocol tested locally, a real WinRM connection not yet observed); off Windows otterPS''s own errors'
    'Set-OtterUiDispatcher' = 'the WPF dispatcher of desktop windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
    'Get-OtterUiDispatcher' = 'the WPF dispatcher of desktop windows: native on Windows through otter-desktop.exe (WPF, `src/Otter.Desktop`, verified by tests/desktop on a Windows desktop)'
}
$psModules = @{}
foreach ($f in Get-ChildItem -LiteralPath (Join-Path $OtterPs 'src') -Filter '*.psm1') { $psModules[$f.Name] = Read-Lines $f.FullName }
foreach ($moduleName in @('Otter.Library.psm1', 'Otter.Runtime.psm1')) {
    $text = [System.IO.File]::ReadAllText((Join-Path $OtterPs "src/$moduleName"))
    $exportBlock = [regex]::Match($text, '(?s)Export-ModuleMember -Function(.*?)(\n\s*\n|\z)').Groups[1].Value
    $functions = @([regex]::Matches($exportBlock, '[A-Z][A-Za-z]+-Otter\w+') | ForEach-Object { $_.Value } | Select-Object -Unique)
    foreach ($fn in $functions) {
        $libStats.Functions++
        $defLine = 0
        $lines = $psModules[$moduleName]
        for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^function $([regex]::Escape($fn))\b") { $defLine = $i + 1; break } }
        # Where otterPS calls it (other modules first, then its own).
        $callers = New-Object System.Collections.Generic.List[string]
        foreach ($m in ($psModules.Keys | Sort-Object { if ($_ -eq $moduleName) { 1 } else { 0 } }, { $_ })) {
            $ml = $psModules[$m]
            for ($i = 0; $i -lt $ml.Count; $i++) {
                if ($ml[$i] -match "(?<![\w-])$([regex]::Escape($fn))\b" -and $ml[$i] -notmatch '^function ' -and $ml[$i] -notmatch '^\s*#' -and $ml[$i] -notmatch 'Export-ModuleMember' -and $ml[$i] -notmatch '^\s+[A-Z][\w-]+(, [A-Z][\w-]+)*,? *`?$') {
                    $callers.Add("$($m):$($i + 1)"); break
                }
            }
        }
        $reachable = $callers.Count -gt 0
        if ($reachable) { $libStats.Reachable++ }
        $net = @(Find-Cs '^src/Otter\.(Runtime|Language|Web|Cli)/' "(?<![\w-])$([regex]::Escape($fn))\b" 2 -Comments)
        $netCells = @($net | ForEach-Object { "$($_.File -replace '^src/', ''):$($_.Line) (cited)" })
        if ($net.Count -eq 0 -and $counterparts.ContainsKey($fn)) {
            $site = @(Find-Cs ('^' + [regex]::Escape($counterparts[$fn][0]) + '$') $counterparts[$fn][1] 1)
            if ($site.Count) { $netCells = @("$($site[0].File -replace '^src/', ''):$($site[0].Line)$(Get-Reached $site[0].File $site[0].Line)") }
        }
        elseif ($net.Count) {
            # The citation is a comment above the C# code; whether it ran is measured a few lines on.
            $r = ''
            for ($k = 1; $k -le 6 -and -not $r; $k++) { $r = Get-Reached $net[0].File ($net[0].Line + $k) }
            if ($r) { $netCells[0] += $r }
        }
        $note = if ($libraryNotes.ContainsKey($fn)) { $libraryNotes[$fn] } elseif ($netCells.Count) { 'native' } else { '**no counterpart found**' }
        if ($netCells.Count) { $libStats.Mapped++ } elseif (-not $libraryNotes.ContainsKey($fn)) { $libStats.Unmapped.Add($fn) }
        $joined = $netCells -join ' '
        if ($joined -match '\(run\)') { $libStats.Reached++; $libStats.Measured++ } elseif ($joined -match 'never run') { $libStats.Measured++ }
        $libraryRows.Add("| ``$fn`` | $($moduleName):$defLine | $(Join-Cell (@($callers | Select-Object -First 2)) 2) | $(Join-Cell $netCells 2) | $note |")
    }
}


# ---- every message otterPS can show ----
# The literal text of each OtterError, runtime error and thrown message in
# otterPS's modules (the longest piece between interpolations, 12 characters
# or more), looked for in otter-net's C#.
$messageNotes = [ordered]@{
    'I can only roll back a real database transaction.' = 'present: Database.cs writes "I can only {verb} a real database transaction." with verb "roll back"'
    'not the xml add-element form' = 'the parser''s internal back-tracking signal (otter-net: NotThisForm), never shown'
    'not the xml remove-element form' = 'the parser''s internal back-tracking signal, never shown'
    'not the xml remove-attribute form' = 'the parser''s internal back-tracking signal, never shown'
    'not implemented in provider' = 'the abstract database provider''s methods; the SQLite provider implements them all'
    'provider does not support schema introspection' = 'SQLite, the only provider, supports it'
    'I do not know a power action called' = 'unreachable: the parser only builds the four power actions'
    'I could not build a credential for' = 'native (`Interpreter.System.cs`: an empty stored password)'
    'Otter could not turn this into JSON.' = 'ConvertTo-Json failing after the shape check; otter-net''s writer cannot fail there'
    'cancel requires a command job.' = 'unreachable: the interpreter calls Stop-OtterCommandJob only for a command job'
    'Unknown job context field' = 'unreachable: the parser builds only the job fields it knows'
    'Only properties belong inside a thing.' = 'unreachable: the parser builds only `name is value` lines inside a thing'
    'A property name inside a thing must be a plain name.' = 'unreachable: as above'
    'Archive entry' = 'Expand-OtterDeterministicArchive is exported but nothing calls it'
    'is required for publish.' = 'unreachable for target and entryPoint: the manifest reader has already required or defaulted them'
    'No profile is running.' = 'Get-OtterProfileResult before Start-OtterProfile: otter.ps1 never does that'
    'Could not bind terminal bridge listener on port' = 'otter studio (out of scope)'
    'Node.js is required to run the full Otter Studio backend' = 'otter studio (out of scope)'
    'The compiled backend does not support' = 'otterPS''s compiled engine refusing a program: the interpreter then runs it and nothing is shown (OtterPsCompiledEngine.Accepts)'
    'values yet. Run this program with otter run.' = 'the compiled engine''s bridge meeting a value it cannot convert: no library function in its subset returns one'
    'The compiled backend has no library bridge for' = 'the compiled engine''s own guard'
}
$csAll = (($csFiles.Values | ForEach-Object { $_ -join "`n" }) -join "`n")
$csUnescaped = $csAll.Replace('\"', '"')
$messagePattern = '(?:OtterError\]::new\(|New-OtterRuntimeError\s+(?:-Message\s+)?|-Message\s+|throw\s+)("(?:[^"`]|`.)*"|''(?:[^'']|'''')*'')'
$messageRows = New-Object System.Collections.Generic.List[string]
$messageStats = @{ Total = 0; Found = 0; Explained = 0; Missing = 0 }
foreach ($module in ($psModules.Keys | Sort-Object)) {
    $lines = $psModules[$module]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in [regex]::Matches($lines[$i], $messagePattern)) {
            $literal = $m.Groups[1].Value
            $body = $literal.Substring(1, $literal.Length - 2)
            if ($literal[0] -eq '"') { $parts = [regex]::Split($body, '\$\([^)]*\)|\$\{[^}]*\}|\$[\w:.]+') }
            else { $parts = @($body.Replace("''", "'")) }
            $fragment = ([string]($parts | ForEach-Object { $_.Replace('`"', '"').Replace('`n', "`n").Replace('""', '"') } | Sort-Object Length -Descending | Select-Object -First 1)).Trim()
            if ($fragment.Length -lt 12) { continue }
            $messageStats.Total++
            if ($csAll.Contains($fragment) -or $csUnescaped.Contains($fragment)) { $messageStats.Found++; continue }
            # otter-net may interpolate a word otterPS writes out (or the other
            # way round): found when a run of whole words covering most of the
            # message is in the C#.
            $words = $fragment.Split(' ')
            $best = 0
            for ($a = 0; $a -lt $words.Count; $a++) {
                for ($b = $words.Count - 1; $b -ge $a; $b--) {
                    $run = ($words[$a..$b] -join ' ')
                    if ($run.Length -le $best) { break }
                    if ($csUnescaped.Contains($run)) { $best = $run.Length; break }
                }
            }
            if ($best -ge 20 -and $best -ge 0.6 * $fragment.Length) { $messageStats.Found++; continue }
            $note = $null
            foreach ($key in $messageNotes.Keys) { if ($fragment.Contains($key) -or $key.Contains($fragment)) { $note = $messageNotes[$key]; break } }
            if ($note) { $messageStats.Explained++ } else { $messageStats.Missing++; $note = '**not found**' }
            $shown = $fragment -replace '\|', '\|' -replace "`n", ' '
            $messageRows.Add("| $($module):$($i + 1) | ``$shown`` | $note |")
        }
    }
}

# ---- fallback sites ----
# Category, then why, for each site: matched by file and by the text of the line.
$siteRules = @(
    @{ File = 'Parser\.cs$'; Text = 'class NotSupportedYet|NotSupportedYet Unsupported'; Category = 'definition'; Why = 'the mechanism itself (the exception and its helper), not a site' }
    @{ File = 'JsCompiler\.(Statements|Expressions)\.cs$|NodeFields\.cs$'; Text = 'compiling'; Category = 'bug guard'; Why = 'a node class with no web compiler case; every node otter-net parses has one (the census above: every kind the parser builds is compiled or refused with otterPS''s own error)' }
    @{ File = 'WebCompiler\.cs$'; Text = 'nested more than'; Category = 'n/a'; Why = 'UI resources nested more than 1,500 deep without a cycle: on Windows PowerShell 5.1 otterPS overflows its stack at about 1,610 levels, but not at a fixed one - the same program rendered in one run and overflowed in the next (measured in CI; SPEC-QUESTIONS F5-6)' }
    @{ File = '(FileMode|ProjectBuild|DesktopCommand|WebCommand)\.cs$'; Text = 'is not native yet'; Category = 'bug guard'; Why = 'passes on the parser''s or web compiler''s NotSupportedYet: no form throws it any more (every form is native)' }
    @{ File = '(FileMode|ProjectBuild|DesktopCommand|WebCommand)\.cs$'; Text = 'stopped unexpectedly'; Category = 'bug guard'; Why = 'a crash of otter-net''s lexer, parser or web compiler is handed over instead of shown' }
    @{ File = 'AstDump\.cs$'; Text = '.'; Category = 'bug guard'; Why = '-DebugAst of a node class with no table entry; every class has one (tools/NodeCensus maps every node of the corpus)' }
    @{ File = 'Project\.cs$'; Text = 'refused by the JSON reader'; Category = 'bug guard'; Why = 'the two manifest readers disagree; they agree on 2.4 million fuzzed inputs' }
    @{ File = 'InformationAction\.cs$'; Text = 'position'; Category = 'bug guard'; Why = 'a Write-Host failing first under -InformationAction Stop/Ignore whose otterPS position otter-net lacks; every first Write-Host of a command carries one (tests/cli/ps-texts.psd1)' }
    @{ File = 'Cli\.cs$'; Text = 'webReason'; Category = 'bug guard'; Why = 'passes on WebCommand''s reason: the web compiler''s NotSupportedYet (no form throws it any more) or a crash' }
)
$siteRows = New-Object System.Collections.Generic.List[string]
$siteCounts = [ordered]@{}
foreach ($file in ($csFiles.Keys | Sort-Object)) {
    $lines = $csFiles[$file]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match '^\s*//') { continue }
        if ($l -notmatch 'Unsupported\(|NotSupportedYet\(|new OtterNetGap\(|\?\? throw new OtterNetGap') { continue }
        $rule = $siteRules | Where-Object { $file -match $_.File -and $l -match $_.Text } | Select-Object -First 1
        $category = if ($rule) { $rule.Category } else { '**unclassified**' }
        $why = if ($rule) { $rule.Why } else { '' }
        if ($category -eq 'definition') { continue }
        if (-not $siteCounts.Contains($category)) { $siteCounts[$category] = 0 }
        $siteCounts[$category]++
        $code = $l.Trim()
        if ($code.Length -gt 110) { $code = $code.Substring(0, 107) + '...' }
        $siteRows.Add("| $($file -replace '^src/', ''):$($i + 1) | ``$($code -replace '\|', '\|')`` | $category | $why |")
    }
}

# ---- the page ----
$out = New-Object System.Collections.Generic.List[string]
$out.Add('# Native coverage: otterPS 1.0.1 → otter-net, source by source')
$out.Add('')
$out.Add('Generated by `tools/native-coverage.ps1` - do not edit by hand. Every row is')
$out.Add('found in the sources: otterPS 1.0.1''s contract, parser, interpreter, JavaScript')
$out.Add('compiler, otter.ps1 and library modules; otter-net''s C# (file:line); and the')
$out.Add('test programs that contain each node, measured by parsing them with')
$out.Add('otter-net (`tools/NodeCensus`). The summary of what remains is')
$out.Add('`docs/FULL_PORT_MATRIX.md`; code coverage is `docs/coverage/`. "compiled engine" is')
$out.Add('otterPS''s other runtime: a plain `otter run` of a program whose statements are all')
$out.Add('in its subset runs there (SPEC-QUESTIONS V4-2); "compiled-engine subset" is where')
$out.Add('otter-net decides the same (`CompiledEngine.cs`).')
$out.Add('')
if ($Cobertura) {
    $out.Add('"(run)" after an otter-net location: a corpus run executed it (tools/coverage.ps1);')
    $out.Add('"(**never run**)": no corpus run did.')
    $out.Add('')
}
$out.Add('## Totals')
$out.Add('')
$out.Add('| What | Count |')
$out.Add('|---|---:|')
$out.Add("| otterPS NodeKinds | $($kinds.Count) |")
$out.Add("| ... with a node class in otterPS 1.0.1 | $($kinds.Count - $stats.NotNode) |")
$out.Add("| ... native in otter-net (a syntax class its parser builds) | **$($stats.Native)** |")
$out.Add("| ... in at least one test program | **$($stats.Tested)** |")
$out.Add("| ... unreachable in 1.0.1's grammar (no program can contain them) | $($stats.Unreachable) |")
if ($Cobertura) { $out.Add("| ... whose otter-net interpreter case a corpus run reached (of those with one) | $($stats.RunReached) of $($stats.RunMeasured) |") }
$out.Add("| Programs read by the census (parsed / refused with a syntax error) | $parsedPrograms / $refusedPrograms |")
$out.Add("| otter.ps1 parameters / commands found | $($parameters.Count) / $($commands.Count) |")
$out.Add("| CLI forms (commands, flags, environment) | $($cliStats.Forms) |")
$out.Add("| ... native / tested by tests/cli / out of scope | **$($cliStats.Native)** / **$($cliStats.Tested)** / $($cliStats.Forms - $cliStats.Native) |")
$out.Add("| Library functions exported (Otter.Library.psm1, Otter.Runtime.psm1) | $($libStats.Functions) |")
$out.Add("| ... called from otterPS | $($libStats.Reachable) |")
$out.Add("| ... with a C# counterpart found | **$($libStats.Mapped)** |")
if ($Cobertura) { $out.Add("| ... whose counterpart a corpus run reached (of those measured) | $($libStats.Reached) of $($libStats.Measured) |") }
$out.Add("| Messages in otterPS's modules (errors and refusals, by their literal text) | $($messageStats.Total) |")
$out.Add("| ... found in otter-net's C# / not reachable or out of scope (explained below) / missing | **$($messageStats.Found)** / $($messageStats.Explained) / **$($messageStats.Missing)** |")
$out.Add("| Places that can still hand a program to PowerShell | **0** (the hand-over was removed on 2026-10-20) |")
$out.Add("| Gaps in otter-net, reported as Otter's internal error (none reached by the corpus or the fuzzing) | $($siteRows.Count) |")
foreach ($c in $siteCounts.Keys) { $out.Add("| ... $c | $($siteCounts[$c]) |") }
$out.Add('')
$untested = @($stats.Untested) + @($cliStats.Untested | ForEach-Object { "CLI: $_" })
$out.Add('Untested: ' + $(if ($untested.Count) { ($untested | ForEach-Object { Format-Code $_ }) -join ', ' } else { 'nothing.' }))
if ($stats.Missing.Count) { $out.Add(''); $out.Add('**Missing in otter-net:** ' + (($stats.Missing | ForEach-Object { Format-Code $_ }) -join ', ')) }
if ($libStats.Unmapped.Count) { $out.Add(''); $out.Add('**Library functions without a counterpart:** ' + (($libStats.Unmapped | ForEach-Object { Format-Code $_ }) -join ', ')) }
$out.Add('')
$out.Add('## Gaps in otter-net, and why')
$out.Add('')
$out.Add('Every `NotSupportedYet(` and `OtterNetGap` in `src`. None hands anything to PowerShell any more: each is reported as Otter''s own internal error (')
$out.Add('parser refuses before anything runs; the web compiler before a page is written;')
$out.Add('the CLI hands the whole command line to otterPS).')
$out.Add('')
$out.Add('| Site | Code | Category | Why |')
$out.Add('|---|---|---|---|')
foreach ($r in $siteRows) { $out.Add($r) }
$out.Add('')
$out.Add("## Messages not found in otter-net's source")
$out.Add('')
$out.Add("Every other message otterPS's modules can show is in otter-net's C#, word for word")
$out.Add('(interpolated values aside). These are not, and why:')
$out.Add('')
$out.Add('| otterPS | Message | Why |')
$out.Add('|---|---|---|')
foreach ($r in $messageRows) { $out.Add($r) }
$out.Add('')
$out.Add('## Node kinds')
$out.Add('')
$out.Add('Tests: programs containing the kind, by corpus (tests/compat, tests/web, the')
$out.Add('.ot files of tests/cli''s cases, otterPS 1.0.1''s corpus), and up to three of')
$out.Add('them. A program refused with a syntax error contributes no nodes; the error')
$out.Add('programs are covered by the comparisons, not by this census.')
$out.Add('')
$out.Add('| Kind | otterPS 1.0.1 | otter-net | Tests | Status |')
$out.Add('|---|---|---|---|---|')
foreach ($r in $kindRows) { $out.Add($r) }
$out.Add('')
$out.Add('## The `otter` command line')
$out.Add('')
$parameterList = ($parameters | ForEach-Object { Format-Code $_ }) -join ', '
$commandList = ($commands | ForEach-Object { Format-Code $_ }) -join ', '
$out.Add("otter.ps1's parameters: $parameterList. Commands in its dispatch: $commandList.")
$out.Add('')
$out.Add('| Form | otterPS | otter-net | tests/cli cases | Status |')
$out.Add('|---|---|---|---|---|')
foreach ($r in $cliRows) { $out.Add($r) }
$out.Add('')
$out.Add('## Library functions')
$out.Add('')
$out.Add('Exported by otterPS''s src/Otter.Library.psm1 and src/Otter.Runtime.psm1. "Called')
$out.Add('from": the first call outside the function''s own module, else inside it. otter-net')
$out.Add('location: where its code cites the function by name ("cited"), or the C#')
$out.Add('member that does its work.')
$out.Add('')
$out.Add('| Function | Defined | Called from | otter-net | Status |')
$out.Add('|---|---|---|---|---|')
foreach ($r in $libraryRows) { $out.Add($r) }
[System.IO.File]::WriteAllText($OutFile, (($out -join "`n") + "`n"), $utf8)
Write-Host "Node kinds: $($stats.Native) native, $($stats.Tested) tested of $($kinds.Count) ($($stats.NotNode) not a node). CLI: $($cliStats.Native) native, $($cliStats.Tested) tested of $($cliStats.Forms). Library: $($libStats.Mapped) of $($libStats.Functions) mapped. Gaps (internal errors, no PowerShell): $($siteRows.Count). -> $OutFile"
