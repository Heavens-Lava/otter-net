<#
.SYNOPSIS
Compiles Otter programs to web pages with both implementations - otterPS
(the reference) and otter-net - and compares every file they write, byte
for byte.

.DESCRIPTION
For every .ot file in the corpus, `otter web <file> -NoOpen` runs once with
otterPS and once with otter-net, each in a fresh copy of the file's folder
at the same path. Compared: standard output, the exit code, and every file
left in the folder (the generated .html above all), byte for byte. otter-net
runs with OTTER_NET_TRACE=1, so the report says whether it compiled the
page itself or handed the command to the PowerShell Otter.

On Linux and macOS the reference is otterPS on PowerShell 7, which writes
the page differently from the certified Windows PowerShell 5.1 in three
known ways, all of them PowerShell's own (otter-net writes the 5.1 form):

  - Set-Content -Encoding UTF8: 5.1 writes a byte order mark and ends the
    file with CRLF; PowerShell 7 writes no byte order mark and ends with LF.
  - ConvertTo-Json (the runtime UI templates, runnable sample ids): 5.1
    writes < > & ' as < > & '; PowerShell 7 does not.
  - A number in the program written with more than 15 significant digits:
    5.1 writes 15 digits, PowerShell 7 the shortest exact form.

With a PowerShell 7 reference a file that differs only in the first two is
reported as "match (5.1 form)"; anything else is a difference.

Exit code 0 when every program matched, 1 otherwise. A Markdown report is
written to -ReportPath.

.EXAMPLE
powershell -NoProfile -File tools\compare-web.ps1 -OtterPs ..\otterPS-1.0.1 -OtterNet src\Otter.Cli\bin\Release\net10.0\otter.dll

.EXAMPLE
pwsh -NoProfile -File tools/compare-web.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
#>
[CmdletBinding()]
param(
    # The otterPS checkout to compare against (its otter.ps1 is the reference).
    [Parameter(Mandatory)][string]$OtterPs,
    # otter-net's built otter executable, or otter.dll from a plain .NET build.
    [Parameter(Mandatory)][string]$OtterNet,
    # Folders or files to compile; default: otter-net's tests/web, otterPS's
    # examples and conformance fixtures, and otter-net's tests/compat.
    [string[]]$Corpus = @(),
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
# otterPS runs on PowerShell 7 everywhere but Windows.
$pwsh7Reference = -not $onWindows
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/web-report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if ($Corpus.Count -eq 0) {
    $Corpus = @((Join-Path $here 'tests/web'), (Join-Path $OtterPs 'examples'), (Join-Path $OtterPs 'conformance'), (Join-Path $here 'tests/compat'))
}

$files = foreach ($item in $Corpus) {
    if (Test-Path -LiteralPath $item -PathType Leaf) { Get-Item -LiteralPath $item }
    elseif (Test-Path -LiteralPath $item) { Get-ChildItem -LiteralPath $item -Recurse -Filter '*.ot' }
}
$files = @($files | Sort-Object FullName -Unique)

function Invoke-OtterProcess {
    param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = New-Object System.IO.MemoryStream
    $copy = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Close() } catch { }
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        if ($onWindows) { & taskkill.exe /T /F /PID $process.Id 2>&1 | Out-Null }
        else { try { $process.Kill() } catch { } }
        [void]$process.WaitForExit(5000)
    }
    [void]$copy.Wait(5000)
    $stderrText = ''
    if ($stderr.Wait(5000)) { $stderrText = $stderr.Result }
    $exit = 'timeout'
    if ($finished) { $exit = $process.ExitCode }
    return New-Object PSObject -Property @{ ExitCode = $exit; Stdout = $stdout.ToArray(); Stderr = $stderrText }
}

function Get-Text { param([byte[]]$Bytes) return [System.Text.Encoding]::UTF8.GetString($Bytes) }

# Every file a run left in its folder, by relative name, with its bytes.
function Get-FolderFiles {
    param([string]$Folder)
    $result = @{}
    foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -Force -File -ErrorAction SilentlyContinue)) {
        $relative = $item.FullName.Substring($Folder.Length + 1)
        $result[$relative] = [System.IO.File]::ReadAllBytes($item.FullName)
    }
    return $result
}

function Get-FirstDifference {
    param([string]$A, [string]$B)
    $n = [Math]::Min($A.Length, $B.Length)
    for ($i = 0; $i -lt $n; $i++) { if ($A[$i] -ne $B[$i]) { return $i } }
    return $n
}

function Show-Around {
    param([string]$Text, [int]$At)
    $from = [Math]::Max(0, $At - 40)
    $length = [Math]::Min(100, [Math]::Max(0, $Text.Length - $from))
    return ($Text.Substring($from, $length) -replace "`r", '\r' -replace "`n", '\n')
}

# otter-net's page in the form PowerShell 7's Set-Content and ConvertTo-Json
# would have written it (see the description above).
function ConvertTo-Pwsh7Form {
    param([byte[]]$Bytes)
    $text = Get-Text $Bytes
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    if ($text.EndsWith("`r`n")) { $text = $text.Substring(0, $text.Length - 2) + "`n" }
    $text = $text.Replace('<', '<').Replace('>', '>').Replace('&', '&').Replace(''', "'")
    return $text
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-compare-web-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$results = New-Object System.Collections.Generic.List[object]
$index = 0
try {
    foreach ($file in $files) {
        $index++
        Write-Progress -Activity 'Comparing otter web: otterPS and otter-net' -Status $file.Name -PercentComplete (100 * $index / $files.Count)
        # One snapshot of the program's folder for both sides.
        $snapshot = Join-Path $work "source-$index"
        New-Item -ItemType Directory -Path $snapshot | Out-Null
        Get-ChildItem -LiteralPath $file.DirectoryName -File | Copy-Item -Destination $snapshot
        $runs = @{}
        foreach ($side in @('reference', 'candidate')) {
            # Both sides run in the same folder: the page's path is printed.
            $folder = Join-Path $work "$index"
            if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
            New-Item -ItemType Directory -Path $folder | Out-Null
            Get-ChildItem -LiteralPath $snapshot -File | Copy-Item -Destination $folder
            if ($side -eq 'reference') {
                $run = Invoke-OtterProcess -FileName $referenceHost -Arguments @($referencePrefix + @($referenceScript, 'web', $file.Name, '-NoOpen')) `
                    -WorkingDirectory $folder -Environment @{}
            }
            else {
                $candidateFile = $OtterNet; $candidateArguments = @('web', $file.Name, '-NoOpen')
                if ($OtterNet.EndsWith('.dll')) { $candidateFile = 'dotnet'; $candidateArguments = @($OtterNet, 'web', $file.Name, '-NoOpen') }
                $run = Invoke-OtterProcess -FileName $candidateFile -Arguments $candidateArguments -WorkingDirectory $folder `
                    -Environment @{ OTTER_NET_TRACE = '1'; OTTER_POWERSHELL = $referenceScript }
            }
            $run | Add-Member -NotePropertyName Files -NotePropertyValue (Get-FolderFiles -Folder $folder)
            $runs[$side] = $run
        }
        Remove-Item -LiteralPath $snapshot -Recurse -Force -ErrorAction SilentlyContinue
        $reference = $runs['reference']; $candidate = $runs['candidate']
        $traceLines = @(($candidate.Stderr -split "`r?`n") | Where-Object { $_ -like 'otter-net *' })
        $engine = '?'
        if ($traceLines | Where-Object { $_ -like 'otter-net native*' }) { $engine = 'native' }
        elseif ($traceLines.Count -gt 0) { $engine = ($traceLines[0] -replace '^otter-net fallback: ', 'fallback: ') }
        $candidateStderr = (($candidate.Stderr -split "`r?`n") | Where-Object { $_ -notlike 'otter-net *' }) -join "`n"

        $problems = New-Object System.Collections.Generic.List[string]
        $normalized = $false
        if ("$($reference.ExitCode)" -ne "$($candidate.ExitCode)") { $problems.Add("exit code $($reference.ExitCode) vs $($candidate.ExitCode)") }
        $refOut = Get-Text $reference.Stdout; $candOut = Get-Text $candidate.Stdout
        if ($refOut -ne $candOut) {
            $at = Get-FirstDifference $refOut $candOut
            $problems.Add("stdout differs at character $($at): reference [$(Show-Around $refOut $at)] otter-net [$(Show-Around $candOut $at)]")
        }
        $names = @(@($reference.Files.Keys) + @($candidate.Files.Keys) | Sort-Object -Unique)
        foreach ($name in $names) {
            if (-not $reference.Files.ContainsKey($name)) { $problems.Add("only otter-net wrote $name"); continue }
            if (-not $candidate.Files.ContainsKey($name)) { $problems.Add("only otterPS wrote $name"); continue }
            $a = $reference.Files[$name]; $b = $candidate.Files[$name]
            $same = ($a.Length -eq $b.Length)
            if ($same) { for ($i = 0; $i -lt $a.Length; $i++) { if ($a[$i] -ne $b[$i]) { $same = $false; break } } }
            if ($same) { continue }
            $refText = Get-Text $a
            $candText = Get-Text $b
            if ($pwsh7Reference -and $engine -eq 'native') {
                $candText = ConvertTo-Pwsh7Form $b
                if ($refText -ceq $candText) { $normalized = $true; continue }
            }
            $at = Get-FirstDifference $refText $candText
            $problems.Add("$name differs at character $($at): reference [$(Show-Around $refText $at)] otter-net [$(Show-Around $candText $at)]")
        }
        if ($reference.Stderr.Trim() -ne $candidateStderr.Trim()) { $problems.Add("stderr differs: reference [$($reference.Stderr.Trim())] otter-net [$($candidateStderr.Trim())]") }
        $outcome = 'match'
        if ($problems.Count -gt 0) { $outcome = 'DIFFERENT' }
        elseif ($normalized) { $outcome = 'match (5.1 form)' }
        $results.Add((New-Object PSObject -Property @{ File = $file.FullName; Outcome = $outcome; Engine = $engine; Detail = ($problems -join '; ') }))
    }
}
finally {
    Write-Progress -Activity 'Comparing otter web: otterPS and otter-net' -Completed
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$different = @($results | Where-Object { $_.Outcome -eq 'DIFFERENT' })
$matched = @($results | Where-Object { $_.Outcome -ne 'DIFFERENT' })
$native = @($matched | Where-Object { $_.Engine -eq 'native' })
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# otter web: otterPS vs otter-net')
$lines.Add('')
$lines.Add("$($results.Count) programs: **$($different.Count) different**, $($matched.Count) identical ($($native.Count) compiled natively by otter-net, $($matched.Count - $native.Count) handed to the PowerShell Otter).")
$lines.Add('')
$referenceHostName = 'Windows PowerShell 5.1'
if ($pwsh7Reference) { $referenceHostName = 'PowerShell 7 ("match (5.1 form)": identical once the known PowerShell 7 differences are applied)' }
$lines.Add("Reference: $referenceScript on $referenceHostName. Candidate: $OtterNet.")
$lines.Add('')
$lines.Add('| Program | Result | otter-net engine | Detail |')
$lines.Add('|---|---|---|---|')
foreach ($r in ($results | Sort-Object @{ Expression = { if ($_.Outcome -eq 'DIFFERENT') { 0 } else { 1 } } }, File)) {
    $relative = $r.File.Replace($OtterPs, 'otterPS').Replace($here, 'otter-net')
    $lines.Add("| $relative | $($r.Outcome) | $($r.Engine) | $(($r.Detail -replace '\|', '\|')) |")
}
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "$($results.Count) programs: $($different.Count) different, $($matched.Count) identical ($($native.Count) native). Report: $ReportPath"
foreach ($r in $different) { Write-Host "  DIFFERENT $($r.File): $($r.Detail)" -ForegroundColor Red }
if ($different.Count -gt 0) { exit 1 }
exit 0
