<#
.SYNOPSIS
Measures the line and branch coverage of otter-net's Otter.Language,
Otter.Runtime, Otter.Web and Otter.Cli (otter.dll) while the whole test
corpus runs, and writes docs/coverage/.

.DESCRIPTION
otter-net is built in Debug, copied to a scratch folder and instrumented with
coverlet (`dotnet tool install --global coverlet.console`); then every program
of the corpus runs through the instrumented `otter`, one process each, as the
comparison tools run them (the reference is not run - this measures what the
corpus exercises, not whether it matches):

  - tests/compat, tests/web and otterPS 1.0.1's .ot programs (conformance,
    examples, benchmarks, experiments, otter-docs, OtterWorkspace, projects,
    selfhost): `otter check`, `otter web -NoOpen`, and `otter run` (with
    <name>.ot.input as standard input) for programs that finish on their own;
  - every case of tests/cli/*.psd1 (arguments, files, standard input and
    environment, as tools/compare-cli.ps1 prepares them);
  - any -Extra folders: `otter run` of every program, as it is (for example
    tests/network with tools/network-test-server.js running, or a fuzz
    corpus); the timeout stops what does not finish.

Instrumented assemblies record their hits when the process exits, so a run
that does not finish in -TimeoutSeconds is stopped with SIGTERM (Ctrl+Break
semantics are not available on Windows: there it is killed, and its hits
are lost - the report says how many).

Coverlet's cobertura report is turned into:

  docs/coverage/README.md              totals and a summary per source file
  docs/coverage/uncovered-branches.md  every uncovered line and partly
                                       covered branch of the parser, the
                                       interpreter and the web compiler
  docs/coverage/unshown-errors.md      every interpreter error no run showed

Runs on PowerShell 7 (macOS, Linux, Windows).

.EXAMPLE
pwsh -NoProfile -File tools/coverage.ps1 -OtterPs ../otterPS-1.0.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [string]$OutputDirectory = '',
    [string[]]$Extra = @(),
    [int]$TimeoutSeconds = 30,
    # otter processes at a time.
    [int]$Parallel = 4,
    [string]$Coverlet = '',
    # Re-use an existing cobertura file instead of running the corpus.
    [string]$FromCobertura = '',
    # Internal: the corpus pass, run by coverlet as its target.
    [switch]$RunCorpus,
    [string]$Otter = '',
    [string]$LogPath = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
$OtterPs = (Resolve-Path $OtterPs).Path
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $here 'docs/coverage' }

# Programs that open windows, serve the web or wait for events never finish
# on their own (tools/compare.ps1's list); tests/compat/net_* are written to
# finish and are run.
$skipPattern = '\bwindow\b|\bshow app\b|\bis a page\b|\bcreate (button|window|text box)\b|\blisten (securely )?for\b|\breceives (GET|POST)\b|\bwatch (file|folder)\b|\bevery \d+ (second|minute)|\bopen udp\b|https?://'

function ConvertTo-ArgumentString {
    param([string[]]$Arguments)
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

# One otter process: returns 'ok', 'timeout' (stopped with SIGTERM: hits
# kept) or 'lost' (killed: its hits are lost).
$invokeOtter = {
    param([string]$Otter, [string[]]$Arguments, [string]$WorkingDirectory, [string]$InputText, [hashtable]$Environment, [int]$TimeoutSeconds)
    $onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
    $quoted = foreach ($a in (@($Otter) + $Arguments)) {
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
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'dotnet'
    $psi.Arguments = ($quoted -join ' ')
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['OTTER_ENGINE'] = 'native'
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $out = $process.StandardOutput.ReadToEndAsync()
    $err = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Write($InputText); $process.StandardInput.Close() } catch { }
    $result = 'ok'
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        $result = 'timeout'
        # SIGTERM lets .NET run its exit handlers, which write the hits.
        if (-not $onWindows) { & kill -TERM $process.Id 2>$null; $finished = $process.WaitForExit(10000) }
        if (-not $finished) { try { $process.Kill($true) } catch { } ; [void]$process.WaitForExit(5000); $result = 'lost' }
    }
    [void]$out.Wait(5000); [void]$err.Wait(5000)
    return $result
}

# tools/compare-cli.ps1's case folder: its files, "{work}" replaced.
$newCaseFolder = {
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

if ($RunCorpus) {
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-coverage-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $work | Out-Null
    # Every run: the program's folder (copied) or a tests/cli case, its arguments,
    # standard input and environment.
    $jobs = [System.Collections.Generic.List[object]]::new()
    $programFolders = @((Join-Path $here 'tests/compat'), (Join-Path $here 'tests/web')) + @(
        'conformance', 'examples', 'benchmarks', 'experiments', 'otter-docs', 'OtterWorkspace', 'projects', 'selfhost' | ForEach-Object { Join-Path $OtterPs $_ })
    foreach ($program in @(foreach ($folder in $programFolders) { Get-ChildItem -LiteralPath $folder -Recurse -Filter '*.ot' -File })) {
        $source = [System.IO.File]::ReadAllText($program.FullName)
        $inputPath = $program.FullName + '.input'
        $inputText = if (Test-Path -LiteralPath $inputPath) { [System.IO.File]::ReadAllText($inputPath) } else { '' }
        # `run` checks before it runs; `check` alone only where the program is not run.
        $commands = @(, @('web', $program.Name, '-NoOpen'))
        if ($source -notmatch $skipPattern -or $program.Name -like 'net_*') { $commands += , @('run', $program.Name) }
        else { $commands += , @('check', $program.Name) }
        foreach ($command in $commands) {
            $jobs.Add(@{ Label = "$($command[0]) $($program.FullName)"; Source = $program.DirectoryName; File = $null; Args = $command; Input = $inputText; Env = @{}; Case = $null })
        }
    }
    foreach ($caseFile in @(Get-ChildItem -LiteralPath (Join-Path $here 'tests/cli') -Filter '*.psd1' | Sort-Object Name)) {
        $data = Import-PowerShellDataFile -LiteralPath $caseFile.FullName
        foreach ($case in $data.Cases) {
            $environment = @{ OTTER_POWERSHELL = (Join-Path $OtterPs 'otter.ps1') }
            if ($case.Env) { foreach ($key in $case.Env.Keys) { $environment[$key] = [string]$case.Env[$key] } }
            $jobs.Add(@{ Label = "cli $($caseFile.BaseName): $($case.Name)"; Source = $null; File = $null; Args = @($case.Args); Input = [string]$case.Stdin; Env = $environment; Case = $case })
        }
    }
    foreach ($folder in @($Extra | ForEach-Object { $_ -split [regex]::Escape([string][System.IO.Path]::PathSeparator) } | Where-Object { $_ })) {
        foreach ($program in @(Get-ChildItem -LiteralPath $folder -Recurse -Filter '*.ot' -File)) {
            $jobs.Add(@{ Label = "run $($program.FullName)"; Source = $null; File = $program.FullName; Args = @('run', $program.Name); Input = ''; Env = @{}; Case = $null })
        }
    }
    for ($i = 0; $i -lt $jobs.Count; $i++) { $jobs[$i].Folder = Join-Path $work "j$i" }
    $invokeText = $invokeOtter.ToString(); $caseText = $newCaseFolder.ToString()
    $results = $jobs | ForEach-Object -ThrottleLimit $Parallel -Parallel {
        $job = $_
        $invoke = [scriptblock]::Create($using:invokeText)
        $newCase = [scriptblock]::Create($using:caseText)
        $folder = $job.Folder
        if ($job.Case) {
            & $newCase $folder $job.Case
            $arguments = @(@($job.Args) | Where-Object { $null -ne $_ } | ForEach-Object { ([string]$_).Replace('{work}', $folder) })
            $stdin = ($job.Input -replace "`r`n", "`n").Replace('{work}', $folder)
        }
        else {
            New-Item -ItemType Directory -Path $folder | Out-Null
            if ($job.Source) { Get-ChildItem -LiteralPath $job.Source -File | Copy-Item -Destination $folder }
            if ($job.File) { Copy-Item -LiteralPath $job.File -Destination $folder }
            $arguments = @($job.Args); $stdin = $job.Input
        }
        $outcome = & $invoke $using:Otter $arguments $folder $stdin $job.Env $using:TimeoutSeconds
        Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
        "$outcome`t$($job.Label)"
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    $timedOut = @($results | Where-Object { $_ -notlike "ok`t*" }).Count
    $lost = @($results | Where-Object { $_ -like "lost`t*" }).Count
    $log = [System.Collections.Generic.List[string]]::new()
    $log.Add("$($results.Count) otter processes, $timedOut stopped after $TimeoutSeconds s, $lost of them killed (hits lost).")
    foreach ($group in ($results | Group-Object { (($_ -split "`t")[1] -split ' ')[0] } | Sort-Object Name)) { $log.Add("$($group.Name): $($group.Count)") }
    $log.Add('Stopped after the timeout:')
    foreach ($r in ($results | Where-Object { $_ -notlike "ok`t*" } | Sort-Object { ($_ -split "`t")[1] })) { $log.Add("  " + $r.Replace($here, 'otter-net').Replace($OtterPs, 'otterPS')) }
    if ($LogPath) { [System.IO.File]::WriteAllLines($LogPath, $log) }
    Write-Host $log[0]
    exit 0
}

# ---- the measurement ----
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-coverage-bin-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$cobertura = $FromCobertura
$runLog = Join-Path $OutputDirectory 'runs.txt'
if (-not $cobertura) {
    if (-not $Coverlet) {
        $Coverlet = (Get-Command coverlet -ErrorAction SilentlyContinue).Source
        if (-not $Coverlet) { $Coverlet = Join-Path $HOME '.dotnet/tools/coverlet' }
    }
    & dotnet build (Join-Path $here 'otter-net.sln') -c Debug -nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The Debug build failed.' }
    Copy-Item -Recurse -LiteralPath (Join-Path $here 'src/Otter.Cli/bin/Debug/net10.0') -Destination $scratch
    $dll = Join-Path $scratch 'otter.dll'
    $cobertura = Join-Path $scratch 'coverage.cobertura.xml'
    $pwshPath = (Get-Process -Id $PID).Path
    $targetArguments = ConvertTo-ArgumentString @('-NoProfile', '-File', $PSCommandPath, '-RunCorpus', '-OtterPs', $OtterPs, '-Otter', $dll,
        '-TimeoutSeconds', "$TimeoutSeconds", '-Parallel', "$Parallel", '-LogPath', $runLog)
    if ($Extra.Count) { $targetArguments += ' -Extra ' + (ConvertTo-ArgumentString @((@($Extra | ForEach-Object { (Resolve-Path $_).Path })) -join [System.IO.Path]::PathSeparator)) }
    & $Coverlet $dll --include-test-assembly --include '[otter]*' --include '[Otter.*]*' --target $pwshPath --targetargs $targetArguments `
        --format cobertura --output $cobertura | Out-Host
    if (-not (Test-Path -LiteralPath $cobertura)) { throw 'coverlet wrote no report.' }
}

[xml]$report = [System.IO.File]::ReadAllText($cobertura)
# Lines and branches per source file (a file's classes are merged; a line
# shows up in several classes for lambdas and nested types).
$files = [ordered]@{}
foreach ($class in $report.SelectNodes('//class')) {
    $name = $class.filename -replace '\\', '/'
    $at = $name.IndexOf('/src/')
    if ($at -ge 0) { $name = $name.Substring($at + 1) } elseif (-not [System.IO.Path]::IsPathRooted($name)) { $name = 'src/' + $name }
    if ($name -match '/obj/') { continue }
    if (-not $files.Contains($name)) { $files[$name] = @{} }
    foreach ($line in $class.SelectNodes('lines/line')) {
        $number = [int]$line.number
        $hits = [long]$line.hits
        $covered = 0; $total = 0
        if ($line.branch -eq 'True' -and $line.'condition-coverage' -match '\((\d+)/(\d+)\)') { $covered = [int]$Matches[1]; $total = [int]$Matches[2] }
        $entry = $files[$name][$number]
        if ($entry) {
            $entry.Hits += $hits
            if ($total -gt $entry.Total) { $entry.Total = $total; $entry.Covered = $covered } elseif ($total -eq $entry.Total -and $covered -gt $entry.Covered) { $entry.Covered = $covered }
        }
        else { $files[$name][$number] = [pscustomobject]@{ Hits = $hits; Covered = $covered; Total = $total } }
    }
}

function Get-Percent([long]$a, [long]$b) { if ($b -eq 0) { return '-' } else { return ('{0:0.0}%' -f (100.0 * $a / $b)) } }

$rows = foreach ($name in ($files.Keys | Sort-Object)) {
    $lines = @($files[$name].Values)
    [pscustomobject]@{
        File = $name
        Project = ($name -split '/')[1]
        Lines = $lines.Count
        LinesCovered = @($lines | Where-Object { $_.Hits -gt 0 }).Count
        Branches = ($lines | Measure-Object -Property Total -Sum).Sum
        BranchesCovered = ($lines | Measure-Object -Property Covered -Sum).Sum
    }
}
$out = [System.Collections.Generic.List[string]]::new()
$out.Add('# Code coverage of otter-net while the test corpus runs')
$out.Add('')
$out.Add('Generated by `tools/coverage.ps1` (coverlet, Debug build): every program of')
$out.Add('tests/compat, tests/web and otterPS 1.0.1''s .ot corpus through `otter check`,')
$out.Add('`otter web` and (when it finishes on its own) `otter run`, and every case of')
$out.Add('tests/cli. Uncovered lines and branches of the parser, the interpreter and')
$out.Add('the web compiler: `uncovered-branches.md`.')
$out.Add('')
if (Test-Path -LiteralPath $runLog) { $out.Add((Get-Content -LiteralPath $runLog -TotalCount 1)); $out.Add('') }
$out.Add('| Project | Lines | Covered | Line % | Branches | Covered | Branch % |')
$out.Add('|---|---:|---:|---:|---:|---:|---:|')
$all = @{ L = 0; LC = 0; B = 0; BC = 0 }
foreach ($group in ($rows | Group-Object Project | Sort-Object Name)) {
    $l = ($group.Group | Measure-Object Lines -Sum).Sum; $lc = ($group.Group | Measure-Object LinesCovered -Sum).Sum
    $b = ($group.Group | Measure-Object Branches -Sum).Sum; $bc = ($group.Group | Measure-Object BranchesCovered -Sum).Sum
    $all.L += $l; $all.LC += $lc; $all.B += $b; $all.BC += $bc
    $out.Add("| $($group.Name) | $l | $lc | $(Get-Percent $lc $l) | $b | $bc | $(Get-Percent $bc $b) |")
}
$out.Add("| **Total** | $($all.L) | $($all.LC) | **$(Get-Percent $all.LC $all.L)** | $($all.B) | $($all.BC) | **$(Get-Percent $all.BC $all.B)** |")
$out.Add('')
$out.Add('## Per file')
$out.Add('')
$out.Add('| File | Lines | Covered | Line % | Branches | Covered | Branch % |')
$out.Add('|---|---:|---:|---:|---:|---:|---:|')
foreach ($r in $rows) { $out.Add("| ``$($r.File)`` | $($r.Lines) | $($r.LinesCovered) | $(Get-Percent $r.LinesCovered $r.Lines) | $($r.Branches) | $($r.BranchesCovered) | $(Get-Percent $r.BranchesCovered $r.Branches) |") }
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'README.md'), (($out -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

# Uncovered lines and branches of the parser, interpreter and web compiler.
$focus = '^src/Otter\.Language/(Parser|Lexer)|^src/Otter\.Runtime/Interpreter|^src/Otter\.Web/(JsCompiler|WebCompiler|DeclarativeRenderer|RuntimeUi|Analysis)'
$detail = [System.Collections.Generic.List[string]]::new()
$detail.Add('# Uncovered lines and branches: parser, interpreter, web compiler')
$detail.Add('')
$detail.Add('Generated by `tools/coverage.ps1`. A line is listed when no corpus run reached')
$detail.Add('it (`never run`) or when it has a branch some run never took (`b/n`:')
$detail.Add('branches taken of those the compiler generated; for `switch`, `?.`, `??` and')
$detail.Add('pattern matching the generated branches include ones the C# source cannot')
$detail.Add('reach). Consecutive never-run lines are shown as a range.')
foreach ($name in ($files.Keys | Where-Object { $_ -match $focus } | Sort-Object)) {
    $path = Join-Path $here $name
    $text = if (Test-Path -LiteralPath $path) { [System.IO.File]::ReadAllLines($path) } else { @() }
    $entries = $files[$name]
    $numbers = @($entries.Keys | Sort-Object)
    $items = [System.Collections.Generic.List[string]]::new()
    $i = 0
    while ($i -lt $numbers.Count) {
        $n = $numbers[$i]; $e = $entries[$n]
        $show = { param($k) if ($k -le $text.Count) { ($text[$k - 1].Trim() -replace '\|', '\|') } else { '' } }
        if ($e.Hits -eq 0) {
            $j = $i
            while ($j + 1 -lt $numbers.Count -and $entries[$numbers[$j + 1]].Hits -eq 0) { $j++ }
            $last = $numbers[$j]
            $range = if ($last -ne $n) { "$n-$last" } else { "$n" }
            $items.Add("| $range | never run | ``$(& $show $n)`` |")
            $i = $j + 1; continue
        }
        if ($e.Total -gt 0 -and $e.Covered -lt $e.Total) { $items.Add("| $n | $($e.Covered)/$($e.Total) | ``$(& $show $n)`` |") }
        $i++
    }
    $detail.Add('')
    $detail.Add("## ``$name`` ($($items.Count))")
    if ($items.Count -eq 0) { continue }
    $detail.Add('')
    $detail.Add('| Line | Coverage | Source |')
    $detail.Add('|---:|---|---|')
    foreach ($item in $items) { $detail.Add($item) }
}
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'uncovered-branches.md'), (($detail -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

# Every place the interpreter raises an error a user can see: whether a
# corpus run reached it. A throw's own line has no sequence point when the
# message is built across lines, so the first line with hits data at or after
# it decides.
$errorRows = [System.Collections.Generic.List[string]]::new()
$errorStats = @{ Sites = 0; Reached = 0 }
foreach ($name in ($files.Keys | Where-Object { $_ -match '^src/Otter\.Runtime/Interpreter' } | Sort-Object)) {
    $path = Join-Path $here $name
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $text = [System.IO.File]::ReadAllLines($path)
    $entries = $files[$name]
    for ($i = 0; $i -lt $text.Count; $i++) {
        if ($text[$i] -notmatch 'throw (RuntimeError|NotOnWindows|NoVault|NotRunnable|NotEvaluable|WebError|new InternalProblem|new DotNetCallProblem)\(|=> RuntimeError\(') { continue }
        if ($text[$i] -match '^\s*//') { continue }
        $errorStats.Sites++
        $hit = $null
        for ($k = $i + 1; $k -le $i + 3; $k++) { if ($entries.ContainsKey($k)) { $hit = $entries[$k].Hits; break } }
        if ($null -ne $hit -and $hit -gt 0) { $errorStats.Reached++; continue }
        $message = [regex]::Match($text[$i], '\$?"(?:[^"\\]|\\.)*"').Value
        if (-not $message) { $message = $text[$i].Trim() }
        if ($message.Length -gt 120) { $message = $message.Substring(0, 117) + '...' }
        $errorRows.Add("| ``$($name -replace '^src/', '')``:$($i + 1) | ``$($message -replace '\|', '\|')`` |")
    }
}
$errorsOut = [System.Collections.Generic.List[string]]::new()
$errorsOut.Add('# Interpreter errors no corpus run showed')
$errorsOut.Add('')
$errorsOut.Add('Generated by `tools/coverage.ps1`: every `RuntimeError(`, `NotOnWindows(`, `NoVault(`,')
$errorsOut.Add('`NotRunnable(`, `NotEvaluable(`, `InternalProblem` and `DotNetCallProblem` throw in')
$errorsOut.Add('src/Otter.Runtime/Interpreter*.cs that no run of the corpus reached.')
$errorsOut.Add('')
$errorsOut.Add("$($errorStats.Reached) of $($errorStats.Sites) error sites reached.")
$errorsOut.Add('')
$errorsOut.Add('| Site | Message |')
$errorsOut.Add('|---|---|')
foreach ($r in $errorRows) { $errorsOut.Add($r) }
[System.IO.File]::WriteAllText((Join-Path $OutputDirectory 'unshown-errors.md'), (($errorsOut -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $cobertura -Destination (Join-Path ([System.IO.Path]::GetTempPath()) 'otter-coverage.cobertura.xml') -Force
Write-Host "Coverage: lines $(Get-Percent $all.LC $all.L), branches $(Get-Percent $all.BC $all.B). Report: $OutputDirectory"
if ($scratch -and (Test-Path -LiteralPath $scratch)) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
