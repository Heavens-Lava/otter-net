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

.EXAMPLE
pwsh -NoProfile -File tools/compare.ps1 -OtterPs ../otterPS -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll

On Linux and macOS the reference is otterPS on PowerShell 7, which is not
the certified Windows PowerShell 5.1 reference: CI on Windows decides.
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
    [int]$TimeoutSeconds = 60,
    # Also run programs that listen, watch or use URLs - for programs written
    # to finish on their own (tests/compat/net_*); the timeout still applies.
    [switch]$IncludeEventPrograms,
    # Native-only: otter-net may not hand any program to the PowerShell Otter
    # (OTTER_ENGINE=native). A program it cannot run itself fails instead of
    # matching through the fallback.
    [switch]$NativeOnly,
    # Run otter-net only (no otterPS here, or not the certified one): a
    # program fails only when otter-net reports a gap in itself or crashes;
    # what it printed is compared with another machine's capture afterwards
    # (tools/compare-captures.ps1).
    [switch]$CandidateOnly,
    # Save what each side showed, normalised (tools/CaptureFormat.ps1).
    [string]$CaptureDirectory = '',
    # Put this folder's fake pwsh/powershell first on otter-net's PATH (the
    # no-implicit-PowerShell check); OTTER_TEST_PROGRAM names the program.
    [string]$ShimDirectory = ''
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CaptureFormat.ps1')
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
$referenceScript = Join-Path $OtterPs 'otter.ps1'
# otterPS runs on Windows PowerShell 5.1 on Windows and on PowerShell 7
# (pwsh) on Linux and macOS, as its own `otter` launchers do.
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$referenceHost = if ($onWindows) { 'powershell.exe' } else { 'pwsh' }
$referencePrefix = if ($onWindows) { @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File') } else { @('-NoProfile', '-File') }
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if ($Corpus.Count -eq 0) {
    $Corpus = @((Join-Path $OtterPs 'conformance'), (Join-Path $OtterPs 'examples'), (Join-Path $here 'tests/compat'))
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
        if ($onWindows) { & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null }
        else { try { $process.Kill($true) } catch { } }
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

# What a run left behind in its folder: every file and folder, with sizes,
# contents and attributes, so file side effects are compared too.
function Get-FolderSnapshot {
    param([string]$Folder)
    $lines = foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = $item.FullName.Substring($Folder.Length + 1)
        if ($item.PSIsContainer) { "$relative\ [$($item.Attributes)]" }
        else {
            $hash = try { (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256 -ErrorAction Stop).Hash } catch { 'unreadable' }
            "$relative $($item.Length) $hash [$($item.Attributes)]"
        }
    }
    return @(@($lines) | Sort-Object)
}

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
        if (-not $IncludeEventPrograms -and $source -match $skipPattern) {
            $results.Add([pscustomobject]@{ File = $name; Outcome = 'skipped'; Engine = ''; Detail = 'opens a window, serves the web or waits for events' })
            continue
        }
        $inputPath = $file.FullName + '.input'
        $inputText = if (Test-Path -LiteralPath $inputPath) { [System.IO.File]::ReadAllText($inputPath) } else { '' }

        # A fuzz mutation or case variant of one of them (name-m<seed>.ot, name-c<seed>.ot) too.
        $baseName = $file.Name -replace '-[mc]\d+\.ot$', '.ot'
        if ($nondeterministic.ContainsKey($baseName)) {
            $results.Add([pscustomobject]@{ File = $name; Outcome = 'skipped'; Engine = ''; Detail = "output changes from run to run: $($nondeterministic[$baseName])" })
            continue
        }
        # One snapshot of the program's folder for both sides, so a file
        # changing while the comparison runs cannot make the sides differ.
        $snapshot = Join-Path $work "source-$index"
        New-Item -ItemType Directory -Path $snapshot | Out-Null
        Get-ChildItem -LiteralPath $file.DirectoryName -File | Copy-Item -Destination $snapshot
        $runs = @{}
        $relativeName = $name.Replace($OtterPs, 'otterPS').Replace($here, 'otter-net')
        $sides = if ($CandidateOnly) { @('candidate') } else { @('reference', 'candidate') }
        foreach ($side in $sides) {
            # Both sides run in the same path (a program may print its folder).
            $folder = Join-Path $work "$index"
            if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
            New-Item -ItemType Directory -Path $folder | Out-Null
            Get-ChildItem -LiteralPath $snapshot -File | Copy-Item -Destination $folder
            if ($side -eq 'reference') {
                $runs[$side] = Invoke-OtterProcess -FileName $referenceHost -Arguments @($referencePrefix + @($referenceScript, 'run', $file.Name)) `
                    -WorkingDirectory $folder -InputText $inputText -Environment @{}
            }
            else {
                # A native build runs directly; a plain .NET build (otter.dll) runs through dotnet.
                $candidateFile = $OtterNet; $candidateArguments = @('run', $file.Name)
                if ($OtterNet.EndsWith('.dll')) { $candidateFile = 'dotnet'; $candidateArguments = @($OtterNet, 'run', $file.Name) }
                $candidateEnvironment = if ($NativeOnly) { @{ OTTER_NET_TRACE = '1'; OTTER_ENGINE = 'native' } } else { @{ OTTER_NET_TRACE = '1'; OTTER_POWERSHELL = $referenceScript } }
                $candidateEnvironment = Add-ShimEnvironment -Environment $candidateEnvironment -ShimDirectory $ShimDirectory -CaseName "run $relativeName"
                $runs[$side] = Invoke-OtterProcess -FileName $candidateFile -Arguments $candidateArguments -WorkingDirectory $folder -InputText $inputText `
                    -Environment $candidateEnvironment
            }
            $runs[$side] | Add-Member -NotePropertyName Files -NotePropertyValue (Get-FolderSnapshot -Folder $folder)
            $sideStderr = if ($side -eq 'candidate') { (($runs[$side].Stderr -split "`r?`n") | Where-Object { $_ -notlike 'otter-net *' }) -join "`n" } else { $runs[$side].Stderr }
            Write-Capture -Directory $CaptureDirectory -Side $side -Id $relativeName -ExitCode $runs[$side].ExitCode -Stdout (Show-Bytes $runs[$side].Stdout) `
                -Stderr $sideStderr -Folder $folder -WorkPaths @($folder, $work)
        }
        Remove-Item -LiteralPath $snapshot -Recurse -Force -ErrorAction SilentlyContinue
        $reference = $runs['reference']; $candidate = $runs['candidate']
        if ($CandidateOnly) {
            $traceLines = @(($candidate.Stderr -split "`r?`n") | Where-Object { $_ -like 'otter-net *' })
            $engine = if ($traceLines | Where-Object { $_ -like 'otter-net native*' }) { 'native' } elseif ($traceLines) { $traceLines[0] } else { '?' }
            $failure = Get-CandidateFailure $candidate.Stderr
            $results.Add([pscustomobject]@{ File = $name; Outcome = $(if ($failure) { 'DIFFERENT' } else { 'ran' }); Engine = $engine; Detail = $(if ($failure) { $failure } else { "exit $($candidate.ExitCode)" }) })
            continue
        }
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
        $fileDifferences = @(Compare-Object -ReferenceObject $reference.Files -DifferenceObject $candidate.Files -CaseSensitive)
        if ($fileDifferences.Count -gt 0) {
            $shown = ($fileDifferences | Select-Object -First 4 | ForEach-Object { "$(if ($_.SideIndicator -eq '<=') { 'reference' } else { 'otter-net' }) only: $($_.InputObject)" }) -join '; '
            $problems.Add("files left behind differ: $shown")
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

$matched = @($results | Where-Object { $_.Outcome -eq 'match' -or $_.Outcome -eq 'ran' })
$different = @($results | Where-Object { $_.Outcome -eq 'DIFFERENT' })
$skipped = @($results | Where-Object { $_.Outcome -eq 'skipped' })
$native = @($matched | Where-Object { $_.Engine -eq 'native' })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add($(if ($CandidateOnly) { '# otter-net alone (no reference on this machine: compared with the Windows capture afterwards)' } elseif ($NativeOnly) { '# otterPS vs otter-net (native only: no PowerShell fallback)' } else { '# otterPS vs otter-net' }))
$lines.Add('')
if ($CandidateOnly) { $lines.Add("$($results.Count) programs: **$($different.Count) failed** (a gap in otter-net or a crash), $($matched.Count) ran ($($native.Count) natively), $($skipped.Count) skipped.") }
else { $lines.Add("$($results.Count) programs: **$($different.Count) different**, $($matched.Count) identical ($($native.Count) run natively by otter-net, $($matched.Count - $native.Count) handed to the PowerShell Otter), $($skipped.Count) skipped.") }
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
# The counts, for tools/Invoke-OtterNetCertification.ps1.
$summary = [ordered]@{ tool = 'compare'; mode = $(if ($CandidateOnly) { 'candidate-only' } else { 'reference' }); total = $results.Count; failed = $different.Count; passed = $matched.Count; native = $native.Count; skipped = $skipped.Count
    failures = @($different | ForEach-Object { "$($_.File.Replace($OtterPs, 'otterPS').Replace($here, 'otter-net')): $($_.Detail)" }) }
[System.IO.File]::WriteAllText([System.IO.Path]::ChangeExtension($ReportPath, '.json'), ($summary | ConvertTo-Json -Depth 4), [System.Text.UTF8Encoding]::new($false))
Write-Host "$($results.Count) programs: $($different.Count) different, $($matched.Count) identical ($($native.Count) native), $($skipped.Count) skipped. Report: $ReportPath"
foreach ($r in $different) { Write-Host "  DIFFERENT $($r.File): $($r.Detail)" -ForegroundColor Red }
if ($different.Count -gt 0) { exit 1 }
exit 0
