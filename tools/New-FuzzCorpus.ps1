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
#>
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [int]$Count = 200,
    [int]$Seed = 20261001,
    [switch]$Mutated
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

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
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
