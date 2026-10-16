<#
.SYNOPSIS
Runs Otter programs through both implementations - otterPS (the reference)
and otter-net - and compares what a user would see.

.DESCRIPTION
For every .ot file in the corpus, both real `otter` commands run it, each in
its own temporary copy of the file's folder, with the same standard input
(<name>.input next to the file, if there is one). Compared: standard output
byte for byte, standard error, and the exit code. otter-net is run with
OTTER_NET_TRACE=1 so the report says whether it ran the program natively
or handed it to the PowerShell Otter; those trace lines are removed before
standard error is compared.

Exit code 0 when every program matched, 1 otherwise. A Markdown report is
written to -ReportPath.

.EXAMPLE
powershell -NoProfile -File tools\compare.ps1 -OtterPs ..\otter-101 -OtterNet artifacts\otter.exe
#>
[CmdletBinding()]
param(
    # The otterPS checkout to compare against (its otter.ps1 is the reference).
    [Parameter(Mandatory)][string]$OtterPs,
    # otter-net's built otter executable, or otter.dll from a plain .NET build.
    [Parameter(Mandatory)][string]$OtterNet,
    # Folders or files to run; default: otterPS's conformance fixtures and
    # examples, and otter-net's own compatibility cases.
    [string[]]$Corpus = @(),
    [string]$ReportPath = '',
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
$referenceScript = Join-Path $OtterPs 'otter.ps1'
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results\report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if ($Corpus.Count -eq 0) {
    $Corpus = @((Join-Path $OtterPs 'conformance'), (Join-Path $OtterPs 'examples'), (Join-Path $here 'tests\compat'))
}

# Programs that open windows, serve the web or wait for events are left out:
# they never finish on their own. The reason is recorded.
$skipPattern = '\bwindow\b|\bshow app\b|\bis a page\b|\bcreate (button|window|text box)\b|\blisten (securely )?for\b|\breceives (GET|POST)\b|\bwatch (file|folder)\b|\bevery \d+ (second|minute)|\bopen udp\b|https?://'

# Programs whose output legitimately differs between two runs of the same
# implementation. Compared only once they can be given a fixed clock or seed.
$nondeterministic = @{
    'dates_and_time.ot' = 'prints the current time'
    'random.ot' = 'random numbers'
}

$files = foreach ($item in $Corpus) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { Get-Item -LiteralPath $item }
    elseif (Test-Path -LiteralPath $item) { Get-ChildItem -LiteralPath $item -Recurse -Filter '*.ot' }
}
$files = @($files | Sort-Object FullName -Unique)

function Invoke-OtterProcess {
    param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory, [string]$InputText, [hashtable]$Environment)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = [System.IO.MemoryStream]::new()
    $copy = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Write($InputText); $process.StandardInput.Close() } catch { }
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        # The whole tree: otter-net's PowerShell fallback is a child process
        # that would otherwise keep running and hold the output pipes open.
        & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null
        [void]$process.WaitForExit(5000)
    }
    [void]$copy.Wait(5000)
    $stderrText = if ($stderr.Wait(5000)) { $stderr.Result } else { '' }
    return [pscustomobject]@{
        ExitCode = $(if ($finished) { $process.ExitCode } else { 'timeout' })
        Stdout = $stdout.ToArray()
        Stderr = $stderrText
    }
}

function Show-Bytes { param([byte[]]$Bytes) return [System.Text.Encoding]::UTF8.GetString($Bytes) }

function Get-FirstDifference {
    param([string]$A, [string]$B)
    $n = [Math]::Min($A.Length, $B.Length)
    for ($i = 0; $i -lt $n; $i++) { if ($A[$i] -ne $B[$i]) { return $i } }
    return $n
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-compare-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$index = 0
try {
    foreach ($file in $files) {
        $index++
        $name = $file.FullName
        Write-Progress -Activity 'Comparing otterPS and otter-net' -Status $file.Name -PercentComplete (100 * $index / $files.Count)
        $source = [System.IO.File]::ReadAllText($file.FullName)
        if ($source -match $skipPattern) {
            $results.Add([pscustomobject]@{ File = $name; Outcome = 'skipped'; Engine = ''; Detail = 'opens a window, serves the web or waits for events' })
            continue
        }
        $inputPath = $file.FullName + '.input'
        $inputText = if (Test-Path -LiteralPath $inputPath) { [System.IO.File]::ReadAllText($inputPath) } else { '' }

        if ($nondeterministic.ContainsKey($file.Name)) {
            $results.Add([pscustomobject]@{ File = $name; Outcome = 'skipped'; Engine = ''; Detail = "output changes from run to run: $($nondeterministic[$file.Name])" })
            continue
        }
        $runs = @{}
        foreach ($side in @('reference', 'candidate')) {
            # Both sides run in the same path (a program may print its folder).
            $folder = Join-Path $work "$index"
            if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
            New-Item -ItemType Directory -Path $folder | Out-Null
            Get-ChildItem -LiteralPath $file.DirectoryName -File | Copy-Item -Destination $folder
            if ($side -eq 'reference') {
                $runs[$side] = Invoke-OtterProcess -FileName 'powershell.exe' -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $referenceScript, 'run', $file.Name) `
                    -WorkingDirectory $folder -InputText $inputText -Environment @{}
            }
            else {
                # A native build runs directly; a plain .NET build (otter.dll) runs through dotnet.
                $candidateFile = $OtterNet; $candidateArguments = @('run', $file.Name)
                if ($OtterNet.EndsWith('.dll')) { $candidateFile = 'dotnet'; $candidateArguments = @($OtterNet, 'run', $file.Name) }
                $runs[$side] = Invoke-OtterProcess -FileName $candidateFile -Arguments $candidateArguments -WorkingDirectory $folder -InputText $inputText `
                    -Environment @{ OTTER_NET_TRACE = '1'; OTTER_POWERSHELL = $referenceScript }
            }
        }
        $reference = $runs['reference']; $candidate = $runs['candidate']
        $traceLines = @(($candidate.Stderr -split "`r?`n") | Where-Object { $_ -like 'otter-net *' })
        $engine = if ($traceLines | Where-Object { $_ -like 'otter-net native*' }) { 'native' } elseif ($traceLines) { ($traceLines[0] -replace '^otter-net fallback: ', 'fallback: ') } else { '?' }
        $candidateStderr = (($candidate.Stderr -split "`r?`n") | Where-Object { $_ -notlike 'otter-net *' }) -join "`n"
        $referenceStderr = (($reference.Stderr -split "`r?`n")) -join "`n"

        $problems = [System.Collections.Generic.List[string]]::new()
        if ("$($reference.ExitCode)" -ne "$($candidate.ExitCode)") { $problems.Add("exit code $($reference.ExitCode) vs $($candidate.ExitCode)") }
        $refOut = Show-Bytes $reference.Stdout; $candOut = Show-Bytes $candidate.Stdout
        if ($refOut -ne $candOut) {
            if (($refOut -replace "`r`n", "`n") -eq ($candOut -replace "`r`n", "`n")) { $problems.Add('stdout differs in line endings only') }
            else {
                $at = Get-FirstDifference $refOut $candOut
                $from = [Math]::Max(0, $at - 30)
                $show = { param($t) ($t.Substring($from, [Math]::Min(80, [Math]::Max(0, $t.Length - $from))) -replace "`r", '\r' -replace "`n", '\n') }
                $problems.Add("stdout differs at character $($at): reference [$(& $show $refOut)] otter-net [$(& $show $candOut)]")
            }
        }
        if ($referenceStderr.Trim() -ne $candidateStderr.Trim()) { $problems.Add("stderr differs: reference [$($referenceStderr.Trim())] otter-net [$($candidateStderr.Trim())]") }
        $outcome = if ($problems.Count -eq 0) { 'match' } else { 'DIFFERENT' }
        $results.Add([pscustomobject]@{ File = $name; Outcome = $outcome; Engine = $engine; Detail = ($problems -join '; ') })
    }
}
finally {
    Write-Progress -Activity 'Comparing otterPS and otter-net' -Completed
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$matched = @($results | Where-Object { $_.Outcome -eq 'match' })
$different = @($results | Where-Object { $_.Outcome -eq 'DIFFERENT' })
$skipped = @($results | Where-Object { $_.Outcome -eq 'skipped' })
$native = @($matched | Where-Object { $_.Engine -eq 'native' })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# otterPS vs otter-net')
$lines.Add('')
$lines.Add("$($results.Count) programs: **$($different.Count) different**, $($matched.Count) identical ($($native.Count) run natively by otter-net, $($matched.Count - $native.Count) handed to the PowerShell Otter), $($skipped.Count) skipped.")
$lines.Add('')
$lines.Add("Reference: $referenceScript. Candidate: $OtterNet.")
$lines.Add('')
$lines.Add('| Program | Result | otter-net engine | Detail |')
$lines.Add('|---|---|---|---|')
foreach ($r in ($results | Sort-Object @{ Expression = { if ($_.Outcome -eq 'DIFFERENT') { 0 } else { 1 } } }, File)) {
    $relative = $r.File.Replace($OtterPs, 'otterPS').Replace($here, 'otter-net')
    $lines.Add("| $relative | $($r.Outcome) | $($r.Engine) | $(($r.Detail -replace '\|', '\|')) |")
}
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "$($results.Count) programs: $($different.Count) different, $($matched.Count) identical ($($native.Count) native), $($skipped.Count) skipped. Report: $ReportPath"
foreach ($r in $different) { Write-Host "  DIFFERENT $($r.File): $($r.Detail)" -ForegroundColor Red }
if ($different.Count -gt 0) { exit 1 }
exit 0
