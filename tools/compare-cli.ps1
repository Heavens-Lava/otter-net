<#
.SYNOPSIS
Runs `otter` command lines through both implementations - otterPS (the
reference) and otter-net - and compares what a user would see.

.DESCRIPTION
tools/compare.ps1 compares programs (`otter run file.ot`). This compares the
command line itself: every case in tests/cli/*.psd1 is a list of arguments,
the files to create first, standard input and environment variables. Each
side runs in a fresh folder with the same path; compared are standard output
byte for byte, standard error, the exit code, and every file and folder left
behind (size, content hash, attributes).

otter-net runs with OTTER_ENGINE=native, so a case it would hand to the
PowerShell Otter fails visibly ("otter-net: OTTER_ENGINE=native, but ...")
instead of passing through the reference. With -AllowFallback it runs as
users run it (OTTER_POWERSHELL set to the reference), and the report says
which engine ran.

A case file is a PowerShell data file:

    @{ Cases = @(
        @{ Name = 'run with arguments'; Args = @('run', 'a.ot', 'x')
           Files = @{ 'a.ot' = 'say arguments' }          # text, "\n" line endings
           Stdin = ''                                      # optional
           Env = @{ NAME = 'value' }                       # optional, both sides
           Mask = @('\d+ ms')                              # optional: regexes replaced in stdout before comparing
           IgnoreFiles = @('publish/*.zip')                # optional: -like patterns left out of the file comparison
           KnownOnPwsh7 = 'why PowerShell 7 differs from 5.1' }  # optional
    ) }

`{work}` in Args, Files (names and text) and Stdin is replaced by the case's
folder. A file name ending in "/" makes a folder.

Exit code 0 when every case matched (or differed only as KnownOnPwsh7 says,
on a PowerShell 7 reference), 1 otherwise. Runs on Windows PowerShell 5.1
and PowerShell 7.

.EXAMPLE
pwsh -NoProfile -File tools/compare-cli.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll

.EXAMPLE
powershell -NoProfile -File tools\compare-cli.ps1 -OtterPs ..\otterPS-1.0.1 -OtterNet artifacts\otter.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OtterNet,
    # Case files or folders of them; default: tests/cli.
    [string[]]$Cases = @(),
    # Only cases whose name matches this -like pattern.
    [string]$Filter = '*',
    [switch]$AllowFallback,
    [string]$ReportPath = '',
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
$referenceScript = Join-Path $OtterPs 'otter.ps1'
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$referenceHost = if ($onWindows) { 'powershell.exe' } else { 'pwsh' }
$referencePrefix = if ($onWindows) { @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File') } else { @('-NoProfile', '-File') }
# The reference is the certified Windows PowerShell 5.1 only on Windows.
$referenceIsPwsh7 = -not $onWindows
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/cli-report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if ($Cases.Count -eq 0) { $Cases = @(Join-Path $here 'tests/cli') }

$caseFiles = foreach ($item in $Cases) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { Get-Item -LiteralPath $item }
    else { Get-ChildItem -LiteralPath $item -Filter '*.psd1' | Sort-Object Name }
}

function ConvertTo-ArgumentString {
    param([string[]]$Arguments)
    # The Windows command-line convention, which .NET parses back on every platform.
    $quoted = foreach ($a in $Arguments) {
        if ($a -ne '' -and $a -notmatch '[\s"]') { $a; continue }
        $sb = [System.Text.StringBuilder]::new('"')
        $backslashes = 0
        foreach ($c in $a.ToCharArray()) {
            if ($c -eq [char]92) { $backslashes++; continue }
            if ($c -eq '"') { [void]$sb.Append([string]::new([char]92, 2 * $backslashes + 1)).Append('"'); $backslashes = 0; continue }
            [void]$sb.Append([string]::new([char]92, $backslashes)).Append($c); $backslashes = 0
        }
        [void]$sb.Append([string]::new([char]92, 2 * $backslashes)).Append('"')
        $sb.ToString()
    }
    return ($quoted -join ' ')
}

function Invoke-OtterProcess {
    param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory, [string]$InputText, [hashtable]$Environment)
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.Arguments = ConvertTo-ArgumentString $Arguments
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
        if ($onWindows) { & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null }
        else { try { $process.Kill($true) } catch { } }
        [void]$process.WaitForExit(5000)
    }
    [void]$copy.Wait(5000)
    $stderrText = if ($stderr.Wait(5000)) { $stderr.Result } else { '' }
    return [pscustomobject]@{
        ExitCode = $(if ($finished) { $process.ExitCode } else { 'timeout' })
        Stdout = [System.Text.Encoding]::UTF8.GetString($stdout.ToArray())
        Stderr = $stderrText
    }
}

function Get-FolderSnapshot {
    param([string]$Folder, [string[]]$Ignore)
    $lines = foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -Force -ErrorAction SilentlyContinue)) {
        $relative = $item.FullName.Substring($Folder.Length + 1) -replace '\\', '/'
        $ignored = $false
        foreach ($pattern in $Ignore) { if ($relative -like $pattern) { $ignored = $true } }
        if ($ignored) { continue }
        if ($item.PSIsContainer) { "$relative/ [$($item.Attributes)]" }
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

function New-CaseFolder {
    param([string]$Folder, $Case)
    if (Test-Path -LiteralPath $Folder) { Remove-Item -LiteralPath $Folder -Recurse -Force }
    New-Item -ItemType Directory -Path $Folder | Out-Null
    if (-not $Case.Files) { return }
    foreach ($name in @($Case.Files.Keys | Sort-Object)) {
        $relative = $name.Replace('{work}', $Folder)
        $path = Join-Path $Folder $relative
        if ($relative.EndsWith('/')) { New-Item -ItemType Directory -Force -Path $path | Out-Null; continue }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        $text = ([string]$Case.Files[$name]) -replace "`r`n", "`n"
        $text = $text.Replace('{work}', $Folder)
        if (-not $text.EndsWith("`n") -and $text -ne '') { $text += "`n" }
        [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
    }
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-compare-cli-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$index = 0
try {
    foreach ($caseFile in $caseFiles) {
        $data = Import-PowerShellDataFile -LiteralPath $caseFile.FullName
        foreach ($case in $data.Cases) {
            $caseName = "$($caseFile.BaseName): $($case.Name)"
            if ($caseName -notlike $Filter) { continue }
            $index++
            Write-Progress -Activity 'Comparing otter command lines' -Status $caseName
            $folder = Join-Path $work "case-$index"
            $arguments = @(@($case.Args) | Where-Object { $null -ne $_ } | ForEach-Object { ([string]$_).Replace('{work}', $folder) })
            $stdin = if ($case.Stdin) { ([string]$case.Stdin -replace "`r`n", "`n").Replace('{work}', $folder) } else { '' }
            $environment = @{}
            if ($case.Env) { foreach ($key in $case.Env.Keys) { $environment[$key] = [string]$case.Env[$key] } }
            $ignore = @(); if ($case.IgnoreFiles) { $ignore = @($case.IgnoreFiles) }

            $runs = @{}
            foreach ($side in @('reference', 'candidate')) {
                New-CaseFolder -Folder $folder -Case $case
                if ($side -eq 'reference') {
                    $runs[$side] = Invoke-OtterProcess -FileName $referenceHost -Arguments @($referencePrefix + @($referenceScript) + $arguments) `
                        -WorkingDirectory $folder -InputText $stdin -Environment $environment
                }
                else {
                    $candidateEnvironment = $environment.Clone()
                    $candidateEnvironment['OTTER_NET_TRACE'] = '1'
                    if ($AllowFallback) { $candidateEnvironment['OTTER_POWERSHELL'] = $referenceScript }
                    else { $candidateEnvironment['OTTER_ENGINE'] = 'native' }
                    $candidateFile = $OtterNet; $candidateArguments = $arguments
                    if ($OtterNet.EndsWith('.dll')) { $candidateFile = 'dotnet'; $candidateArguments = @($OtterNet) + $arguments }
                    $runs[$side] = Invoke-OtterProcess -FileName $candidateFile -Arguments $candidateArguments -WorkingDirectory $folder `
                        -InputText $stdin -Environment $candidateEnvironment
                }
                $runs[$side] | Add-Member -NotePropertyName Files -NotePropertyValue (Get-FolderSnapshot -Folder $folder -Ignore $ignore)
            }
            $reference = $runs['reference']; $candidate = $runs['candidate']
            $traceLines = @(($candidate.Stderr -split "`r?`n") | Where-Object { $_ -like 'otter-net *' })
            $engine = if ($traceLines | Where-Object { $_ -like 'otter-net native*' }) { 'native' }
                elseif ($traceLines | Where-Object { $_ -like 'otter-net fallback*' }) { 'fallback' }
                elseif ($candidate.Stderr -like '*OTTER_ENGINE=native, but*') { 'refused' }
                else { '?' }
            $candidateStderr = (($candidate.Stderr -split "`r?`n") | Where-Object { $_ -notlike 'otter-net *' }) -join "`n"
            $referenceStderr = ($reference.Stderr -split "`r?`n") -join "`n"
            $refOut = $reference.Stdout; $candOut = $candidate.Stdout
            if ($case.Mask) {
                foreach ($pattern in @($case.Mask)) {
                    $refOut = [regex]::Replace($refOut, $pattern, '<masked>')
                    $candOut = [regex]::Replace($candOut, $pattern, '<masked>')
                }
            }

            $problems = [System.Collections.Generic.List[string]]::new()
            if ("$($reference.ExitCode)" -ne "$($candidate.ExitCode)") { $problems.Add("exit code $($reference.ExitCode) vs $($candidate.ExitCode)") }
            if ($refOut -ne $candOut) {
                if (($refOut -replace "`r`n", "`n") -eq ($candOut -replace "`r`n", "`n")) { $problems.Add('stdout differs in line endings only') }
                else {
                    $at = Get-FirstDifference $refOut $candOut
                    $from = [Math]::Max(0, $at - 30)
                    $show = { param($t) ($t.Substring([Math]::Min($from, $t.Length), [Math]::Min(80, [Math]::Max(0, $t.Length - $from))) -replace "`r", '\r' -replace "`n", '\n') }
                    $problems.Add("stdout differs at character $($at): reference [$(& $show $refOut)] otter-net [$(& $show $candOut)]")
                }
            }
            $fileDifferences = @(Compare-Object -ReferenceObject @($reference.Files | Where-Object { $null -ne $_ }) -DifferenceObject @($candidate.Files | Where-Object { $null -ne $_ }) -CaseSensitive)
            if ($fileDifferences.Count -gt 0) {
                $shown = ($fileDifferences | Select-Object -First 4 | ForEach-Object { "$(if ($_.SideIndicator -eq '<=') { 'reference' } else { 'otter-net' }) only: $($_.InputObject)" }) -join '; '
                $problems.Add("files left behind differ: $shown")
            }
            # otter-net names otter.ps1 next to its own executable where otterPS's
            # errors name the real script (SPEC-QUESTIONS C2-1): both become <otter.ps1>.
            $referenceStderr = $referenceStderr.Replace($referenceScript, '<otter.ps1>')
            $candidateStderr = $candidateStderr.Replace((Join-Path (Split-Path -Parent $OtterNet) 'otter.ps1'), '<otter.ps1>')
            if ($referenceStderr.Trim() -ne $candidateStderr.Trim()) {
                $refErr = $referenceStderr.Trim(); $candErr = $candidateStderr.Trim()
                $at = Get-FirstDifference $refErr $candErr
                $from = [Math]::Max(0, $at - 40)
                $showErr = { param($t) ($t.Substring([Math]::Min($from, $t.Length), [Math]::Min(160, [Math]::Max(0, $t.Length - $from))) -replace "`r", '\r' -replace "`n", '\n') }
                $problems.Add("stderr differs at character $($at): reference [$(& $showErr $refErr)] otter-net [$(& $showErr $candErr)]")
            }
            $outcome = if ($problems.Count -eq 0) { 'match' }
                elseif ($referenceIsPwsh7 -and $case.KnownOnPwsh7) { 'known' }
                else { 'DIFFERENT' }
            if ($outcome -eq 'known') { $problems.Insert(0, "known PowerShell 7 difference: $($case.KnownOnPwsh7)") }
            $results.Add([pscustomobject]@{ Case = $caseName; Arguments = ($arguments -join ' '); Outcome = $outcome; Engine = $engine; Detail = ($problems -join '; ') })
        }
    }
}
finally {
    Write-Progress -Activity 'Comparing otter command lines' -Completed
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$matched = @($results | Where-Object { $_.Outcome -eq 'match' })
$known = @($results | Where-Object { $_.Outcome -eq 'known' })
$different = @($results | Where-Object { $_.Outcome -eq 'DIFFERENT' })
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# otterPS vs otter-net: the otter command line')
$lines.Add('')
$lines.Add("$($results.Count) cases: **$($different.Count) different**, $($matched.Count) identical, $($known.Count) known PowerShell 7 differences.")
$lines.Add('')
$lines.Add("Reference: $referenceScript on $referenceHost. Candidate: $OtterNet$(if ($AllowFallback) { ' (fallback allowed)' } else { ' (OTTER_ENGINE=native)' }).")
$lines.Add('')
$lines.Add('| Case | otter ... | Result | otter-net engine | Detail |')
$lines.Add('|---|---|---|---|---|')
foreach ($r in ($results | Sort-Object @{ Expression = { if ($_.Outcome -eq 'DIFFERENT') { 0 } elseif ($_.Outcome -eq 'known') { 1 } else { 2 } } }, Case)) {
    $lines.Add("| $($r.Case) | ``$($r.Arguments)`` | $($r.Outcome) | $($r.Engine) | $(($r.Detail -replace '\|', '\|')) |")
}
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "$($results.Count) cases: $($different.Count) different, $($matched.Count) identical, $($known.Count) known. Report: $ReportPath"
foreach ($r in $different) { Write-Host "  DIFFERENT $($r.Case): $($r.Detail)" -ForegroundColor Red }
if ($different.Count -gt 0) { exit 1 }
exit 0
