<#
.SYNOPSIS
Writes otterPS's differential-fuzzer programs to a folder, so tools\compare.ps1
can run them through both implementations.

.DESCRIPTION
The generators are taken, unchanged, from otterPS's
tools\Invoke-OtterDifferentialFuzzer.ps1: the script is parsed (not run) and
its New-RandomInteractiveOtterProgram and Mutate-OtterGrammarAware functions
are defined here. The same seeds give the same programs that otterPS's own
release certification uses (default seed 20261001).

  <out>\program-<seed>.ot   generated programs (the differential fuzz)
  <out>\mutated-<seed>.ot   the same programs after a grammar-aware mutation
                            (the malformed-input fuzz: odd and broken programs)

With -MutateFrom, existing programs (files or folders of .ot files) are
mutated instead, -Count times each with different seeds:

  <out>\<name>-m<seed>.ot

With -CaseVariants (and -MutateFrom), each program is also rewritten -Count
times with the case of random words changed (UPPER, lower, Title, aLtErNaTe)
outside text literals and comments - keywords are case-insensitive, names
are not, so both implementations must agree on which changes matter:

  <out>\<name>-c<seed>.ot
#>
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [int]$Count = 200,
    [int]$Seed = 20261001,
    [switch]$Mutated,
    [string[]]$MutateFrom = @(),
    [switch]$CaseVariants
)
$ErrorActionPreference = 'Stop'
$fuzzer = Join-Path (Resolve-Path $OtterPs).Path 'tools\Invoke-OtterDifferentialFuzzer.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($fuzzer, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) { throw "Could not parse $fuzzer" }
$wanted = @('New-RandomInteractiveOtterProgram', 'Mutate-OtterGrammarAware')
$definitions = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $wanted }, $false)
if (@($definitions).Count -ne $wanted.Count) { throw "Expected the functions $($wanted -join ', ') in $fuzzer." }
foreach ($definition in $definitions) { . ([scriptblock]::Create($definition.Extent.Text)) }

# A case-variant mutation (otter-net's own, not otterPS's): words outside
# text literals and comments change case at random.
function Convert-OtterCaseVariant {
    param([string]$Source, [int]$MutationSeed)
    $r = [System.Random]::new($MutationSeed)
    $out = [System.Text.StringBuilder]::new()
    $first = $true
    foreach ($line in ($Source -split "`n")) {
        if (-not $first) { [void]$out.Append("`n") }
        $first = $false
        $inText = $false; $i = 0
        while ($i -lt $line.Length) {
            $c = $line[$i]
            if ($inText) {
                [void]$out.Append($c)
                if ($c -eq '\' -and $i + 1 -lt $line.Length) { [void]$out.Append($line[$i + 1]); $i += 2; continue }
                if ($c -eq '"') { $inText = $false }
                $i++; continue
            }
            if ($c -eq '"') { $inText = $true; [void]$out.Append($c); $i++; continue }
            if ($c -eq '#') { [void]$out.Append($line.Substring($i)); break }
            if ([char]::IsLetter($c)) {
                $start = $i
                while ($i -lt $line.Length -and [char]::IsLetterOrDigit($line[$i])) { $i++ }
                $word = $line.Substring($start, $i - $start)
                if ($r.Next(4) -eq 0) {
                    $word = switch ($r.Next(4)) {
                        0 { $word.ToUpperInvariant() }
                        1 { $word.ToLowerInvariant() }
                        2 { $word.Substring(0, 1).ToUpperInvariant() + $word.Substring(1).ToLowerInvariant() }
                        default { -join ($word.ToCharArray() | ForEach-Object -Begin { $k = 0 } -Process { if ($k++ % 2) { [char]::ToUpperInvariant($_) } else { [char]::ToLowerInvariant($_) } }) }
                    }
                }
                [void]$out.Append($word); continue
            }
            [void]$out.Append($c); $i++
        }
    }
    return $out.ToString()
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
if ($MutateFrom.Count -gt 0) {
    $sources = foreach ($item in $MutateFrom) {
        if (Test-Path -LiteralPath $item -PathType Leaf) { Get-Item -LiteralPath $item }
        else { Get-ChildItem -LiteralPath $item -Filter '*.ot' -File }
    }
    $written = 0
    foreach ($file in @($sources | Sort-Object FullName -Unique)) {
        $source = [System.IO.File]::ReadAllText($file.FullName)
        for ($i = 1; $i -le $Count; $i++) {
            $mutationSeed = $Seed + $i
            $mutatedSource = Mutate-OtterGrammarAware -Source $source -MutationSeed $mutationSeed
            [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$($file.BaseName)-m$mutationSeed.ot"), $mutatedSource, $utf8)
            $written++
            if ($CaseVariants) {
                $variant = Convert-OtterCaseVariant -Source $source -MutationSeed $mutationSeed
                [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$($file.BaseName)-c$mutationSeed.ot"), $variant, $utf8)
                $written++
            }
        }
    }
    "Wrote $written mutations of $(@($sources).Count) programs to $OutputDirectory (seed $Seed)."
    return
}
for ($i = 1; $i -le $Count; $i++) {
    $programSeed = $Seed + $i
    $source = New-RandomInteractiveOtterProgram -ProgramSeed $programSeed
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "program-$programSeed.ot"), $source, $utf8)
    if ($Mutated) {
        $mutatedSource = Mutate-OtterGrammarAware -Source $source -MutationSeed ($programSeed * 7 + 13)
        [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "mutated-$programSeed.ot"), $mutatedSource, $utf8)
    }
}
"Wrote $Count programs$(if ($Mutated) { " and $Count mutations" }) to $OutputDirectory (seed $Seed)."
