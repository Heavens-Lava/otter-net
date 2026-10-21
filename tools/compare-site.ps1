<#
.SYNOPSIS
Builds the Otter website (otterPS's otter-docs/) with otterPS and with
otter-net, and compares the two sites file by file, byte for byte.

.DESCRIPTION
otterPS builds its website with otter-docs/scripts/build-otter-pages.ps1:
every page in otter-docs/pages (not the `_` modules) is compiled with
Export-OtterWebApplication - what `otter web` runs - after the release tokens
({{RELEASE_VERSION}} and the others, from release-data.json) are expanded
into a temporary copy beside the page; the result goes to <slug>/index.html
and gets the site's icon, stylesheet and script added; static/ is copied and
static/docs-index.json (the search index) is written. otter-docs/site.ot,
the site shell, is compiled on its own with `otter web site.ot -NoOpen`.

Reference: that script, unchanged except that each compiled page is also
copied to a raw/ folder before it is post-processed, run by Windows
PowerShell 5.1 (PowerShell 7 elsewhere) in a copy of otterPS (src/, otter.ps1,
VERSION, otter-docs/).

Candidate: the same script with its one Export-OtterWebApplication call
replaced by `otter web <page> -NoOpen` of otter-net (the page it writes beside
the source is moved to <slug>/index.html), run by the same PowerShell in a
copy without otterPS's src/. Everything else - the token expansion, the
post-processing, the search index - is the same PowerShell code, so on
Windows both sites must be identical byte for byte.

Compared: the output of both builds, every file of both sites (and the raw
pages), and `otter web site.ot -NoOpen` (output, exit code, page).

With -CandidateOnly (no Windows PowerShell 5.1 reference here) only otter-net
builds; its raw pages, site shell and static files are captured for the
comparison with the Windows capture (tools/compare-captures.ps1). The
post-processed pages and the search index are written by the PowerShell
running the build script (Set-Content and ConvertTo-Json differ between 5.1
and 7), so off Windows the raw pages are what is compared.

Exit code 0 when everything matched (or, candidate only, the site built), 1
otherwise. A Markdown report is written to -ReportPath, the counts beside it
(.json).

.EXAMPLE
powershell -NoProfile -File tools\compare-site.ps1 -OtterPs otterps -OtterNet artifacts\win-x64\otter.exe -ReportPath results\site.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterPs,
    [Parameter(Mandatory)][string]$OtterNet,
    [string]$ReportPath = '',
    [switch]$CandidateOnly,
    [string]$CaptureDirectory = '',
    [string]$ShimDirectory = '',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CaptureFormat.ps1')
$OtterPs = (Resolve-Path $OtterPs).Path
$OtterNet = (Resolve-Path $OtterNet).Path
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$hostExe = (Get-Process -Id $PID).Path
$hostArgs = @('-NoProfile')
if ($onWindows) { $hostArgs += @('-ExecutionPolicy', 'Bypass') }
if (-not $ReportPath) { $ReportPath = Join-Path $here 'compat-results/site-report.md' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)

$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-site-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null

function Copy-Tree {
    param([string]$From, [string]$To)
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $From -Force)) {
        if ($item.Name -in @('node_modules', 'generated', 'dist', '.git')) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $To -Recurse -Force
    }
}

function Invoke-Build {
    param([string]$Script, [string[]]$Arguments, [string]$WorkingDirectory, [hashtable]$Environment)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
    $all = @($hostArgs) + @('-File', $Script) + @($Arguments)
    $psi.Arguments = ($all | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = $Environment[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) { try { $process.Kill() } catch { } }
    [void]$stdout.Wait(10000); [void]$stderr.Wait(10000)
    $exit = 'timeout'
    if ($finished) { $exit = $process.ExitCode }
    return New-Object PSObject -Property @{ ExitCode = $exit; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

function Invoke-Otter {
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
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Close() } catch { }
    $finished = $process.WaitForExit(300000)
    if (-not $finished) { try { $process.Kill() } catch { } }
    [void]$stdout.Wait(10000); [void]$stderr.Wait(10000)
    $exit = 'timeout'
    if ($finished) { $exit = $process.ExitCode }
    return New-Object PSObject -Property @{ ExitCode = $exit; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

function Get-Files {
    param([string]$Folder)
    $result = @{}
    if (-not (Test-Path -LiteralPath $Folder)) { return $result }
    foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Force)) {
        $result[($item.FullName.Substring($Folder.Length + 1) -replace '\\', '/')] = $item.FullName
    }
    return $result
}

# The build script, as otterPS has it, with the raw page kept (both sides),
# and for the candidate the compiler replaced by otter-net.
$original = [System.IO.File]::ReadAllText((Join-Path $OtterPs 'otter-docs/scripts/build-otter-pages.ps1'))
# The script names its folders with \ ('..\pages'), which only Windows reads
# as a separator: elsewhere both sides get /.
if (-not $onWindows) { $original = [regex]::Replace($original, "'(\.\.|src|static)\\[^'\r\n]*'", { param($m) $m.Value.Replace('\', '/') }) }
$exportLine = 'Export-OtterWebApplication -SourcePath $sourcePath -OutputPath $destination | Out-Null'
$importLine = 'Import-Module $webModule -Force'
if (-not $original.Contains($exportLine) -or -not $original.Contains($importLine)) { throw "build-otter-pages.ps1 is not the one this tool knows (otterPS 1.0.1)." }
$keepRaw = $exportLine + "`r`n        if (`$env:OTTER_SITE_RAW) { New-Item -ItemType Directory -Force -Path (Join-Path `$env:OTTER_SITE_RAW `$slug) | Out-Null; Copy-Item -LiteralPath `$destination -Destination (Join-Path (Join-Path `$env:OTTER_SITE_RAW `$slug) 'index.html') -Force }"
$referenceScriptText = $original.Replace($exportLine, $keepRaw)
$candidatePrelude = @'
# otter-net's `otter web <page> -NoOpen` in place of Export-OtterWebApplication
# (tools/compare-site.ps1).
function Invoke-OtterNetWeb {
    param([string]$SourcePath, [string]$OutputPath)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $env:OTTER_SITE_OTTER
    $arguments = @('web', [System.IO.Path]::GetFileName($SourcePath), '-NoOpen')
    if ($env:OTTER_SITE_OTTER.EndsWith('.dll')) { $psi.FileName = 'dotnet'; $arguments = @($env:OTTER_SITE_OTTER) + $arguments }
    $psi.Arguments = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $psi.WorkingDirectory = [System.IO.Path]::GetDirectoryName($SourcePath)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($env:OTTER_SITE_SHIM) {
        $psi.EnvironmentVariables['PATH'] = $env:OTTER_SITE_SHIM + [System.IO.Path]::PathSeparator + $env:PATH
        $psi.EnvironmentVariables['OTTER_TEST_PROGRAM'] = 'site ' + [System.IO.Path]::GetFileName($SourcePath)
    }
    $process = [System.Diagnostics.Process]::Start($psi)
    $out = $process.StandardOutput.ReadToEndAsync(); $err = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "otter web $SourcePath failed (exit $($process.ExitCode)): $($out.Result) $($err.Result)" }
    $written = [System.IO.Path]::ChangeExtension($SourcePath, '.html')
    Move-Item -LiteralPath $written -Destination $OutputPath -Force
}
'@
# After the script's param() block, which must come first.
$repoRootLine = '$repoRoot = (Resolve-Path'
if (-not $original.Contains($repoRootLine)) { throw "build-otter-pages.ps1 is not the one this tool knows (otterPS 1.0.1)." }
$candidateScriptText = $original.Replace($repoRootLine, $candidatePrelude + "`r`n" + $repoRootLine).Replace($importLine, '# (otter-net: no module to import)').Replace($exportLine, $keepRaw.Replace($exportLine, 'Invoke-OtterNetWeb -SourcePath $sourcePath -OutputPath $destination'))

$problems = New-Object System.Collections.Generic.List[string]
$compared = 0; $identical = 0
$sides = @('reference', 'candidate')
if ($CandidateOnly) { $sides = @('candidate') }
$builds = @{}
try {
    foreach ($side in $sides) {
        $root = Join-Path $work $side
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        Copy-Item -LiteralPath (Join-Path $OtterPs 'VERSION') -Destination $root
        Copy-Tree -From (Join-Path $OtterPs 'otter-docs') -To (Join-Path $root 'otter-docs')
        if ($side -eq 'reference') {
            Copy-Tree -From (Join-Path $OtterPs 'src') -To (Join-Path $root 'src')
            Copy-Item -LiteralPath (Join-Path $OtterPs 'otter.ps1') -Destination $root
        }
        $script = Join-Path $root 'otter-docs/scripts/build-site-certification.ps1'
        $text = $referenceScriptText
        if ($side -eq 'candidate') { $text = $candidateScriptText }
        [System.IO.File]::WriteAllText($script, $text, (New-Object System.Text.UTF8Encoding($true)))
        $out = Join-Path $root 'site'
        $raw = Join-Path $root 'raw'
        $environment = @{ OTTER_SITE_RAW = $raw; OTTER_SITE_OTTER = $OtterNet; OTTER_SITE_SHIM = $ShimDirectory; NO_COLOR = '1' }
        Write-Host "Building the site with $(if ($side -eq 'reference') { 'otterPS' } else { 'otter-net' }) ..."
        $build = Invoke-Build -Script $script -Arguments @('-OutputRoot', $out) -WorkingDirectory $root -Environment $environment
        # The site shell, compiled on its own.
        $shellFolder = Join-Path $root 'shell'
        New-Item -ItemType Directory -Force -Path $shellFolder | Out-Null
        Copy-Item -LiteralPath (Join-Path $OtterPs 'otter-docs/site.ot') -Destination $shellFolder
        if ($side -eq 'reference') {
            $referenceHost = 'pwsh'; $prefix = @('-NoProfile', '-File')
            if ($onWindows) { $referenceHost = 'powershell.exe'; $prefix = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File') }
            $shell = Invoke-Otter -FileName $referenceHost -Arguments ($prefix + @((Join-Path $root 'otter.ps1'), 'web', 'site.ot', '-NoOpen')) -WorkingDirectory $shellFolder -Environment @{}
        }
        else {
            $file = $OtterNet; $arguments = @('web', 'site.ot', '-NoOpen')
            if ($OtterNet.EndsWith('.dll')) { $file = 'dotnet'; $arguments = @($OtterNet) + $arguments }
            $environment = Add-ShimEnvironment -Environment @{} -ShimDirectory $ShimDirectory -CaseName 'site site.ot'
            $shell = Invoke-Otter -FileName $file -Arguments $arguments -WorkingDirectory $shellFolder -Environment $environment
        }
        $builds[$side] = New-Object PSObject -Property @{ Build = $build; Shell = $shell; Site = $out; Raw = $raw; ShellFolder = $shellFolder; Root = $root }
        $pageCount = @(Get-ChildItem -LiteralPath $out -Recurse -Filter 'index.html' -ErrorAction SilentlyContinue).Count
        Write-Host "  exit $($build.ExitCode), $pageCount pages; site.ot: exit $($shell.ExitCode)"
        if ("$($build.ExitCode)" -ne '0') { $problems.Add("$side build failed (exit $($build.ExitCode)): $($build.Stderr.Trim())") }
        if ($side -eq 'candidate') {
            $failure = Get-CandidateFailure ($build.Stderr + "`n" + $build.Stdout + "`n" + $shell.Stderr)
            if ($failure) { $problems.Add("otter-net: $failure") }
        }

        if ($CaptureDirectory) {
            # The raw pages (what `otter web` wrote), the static files without
            # the search index (ConvertTo-Json's), and the site shell.
            foreach ($page in @(Get-ChildItem -LiteralPath $raw -Directory -ErrorAction SilentlyContinue | Sort-Object Name)) {
                Write-Capture -Directory $CaptureDirectory -Side $side -Id "site__raw__$($page.Name)" -ExitCode 0 -Stdout '' -Stderr '' -Folder $page.FullName -WorkPaths @($root, $work) -KeepFiles
            }
            $static = Join-Path $out 'static'
            Write-Capture -Directory $CaptureDirectory -Side $side -Id 'site__static' -ExitCode $build.ExitCode `
                -Stdout ((($build.Stdout -split "`r?`n") | Where-Object { $_ }) -join "`n") -Stderr '' -Folder $static -WorkPaths @($root, $work) -Ignore @('docs-index.json')
            Write-Capture -Directory $CaptureDirectory -Side $side -Id 'site__site.ot' -ExitCode $shell.ExitCode -Stdout $shell.Stdout `
                -Stderr ((($shell.Stderr -split "`r?`n") | Where-Object { $_ -notlike 'otter-net *' }) -join "`n") -Folder $shellFolder -WorkPaths @($shellFolder, $root, $work) -KeepFiles -InputFolder (Join-Path $OtterPs 'otter-docs')
        }
    }

    if (-not $CandidateOnly) {
        $reference = $builds['reference']; $candidate = $builds['candidate']
        $refOut = ($reference.Build.Stdout -replace "`r`n", "`n").Trim(); $candOut = ($candidate.Build.Stdout -replace "`r`n", "`n").Trim()
        if ($refOut -ne $candOut) { $problems.Add("the builds printed different text: reference [$($refOut.Substring(0, [Math]::Min(300, $refOut.Length)))] otter-net [$($candOut.Substring(0, [Math]::Min(300, $candOut.Length)))]") }
        foreach ($pair in @(@('site', $reference.Site, $candidate.Site), @('raw', $reference.Raw, $candidate.Raw), @('shell', $reference.ShellFolder, $candidate.ShellFolder))) {
            $refFiles = Get-Files $pair[1]; $candFiles = Get-Files $pair[2]
            foreach ($name in @(@($refFiles.Keys) + @($candFiles.Keys) | Sort-Object -Unique)) {
                $compared++
                if (-not $refFiles.ContainsKey($name)) { $problems.Add("$($pair[0])/${name}: only otter-net wrote it"); continue }
                if (-not $candFiles.ContainsKey($name)) { $problems.Add("$($pair[0])/${name}: only otterPS wrote it"); continue }
                $a = [System.IO.File]::ReadAllBytes($refFiles[$name]); $b = [System.IO.File]::ReadAllBytes($candFiles[$name])
                if ([System.Convert]::ToBase64String($a) -ceq [System.Convert]::ToBase64String($b)) { $identical++; continue }
                $ta = [System.Text.Encoding]::UTF8.GetString($a); $tb = [System.Text.Encoding]::UTF8.GetString($b)
                $n = [Math]::Min($ta.Length, $tb.Length); $at = $n
                for ($i = 0; $i -lt $n; $i++) { if ($ta[$i] -ne $tb[$i]) { $at = $i; break } }
                $from = [Math]::Max(0, $at - 40)
                $show = { param($t) ($t.Substring([Math]::Min($from, $t.Length), [Math]::Min(120, [Math]::Max(0, $t.Length - $from))) -replace "`r", '\r' -replace "`n", '\n') }
                $problems.Add("$($pair[0])/$name differs at character ${at}: reference [$(& $show $ta)] otter-net [$(& $show $tb)]")
            }
        }
        $shellRef = $reference.Shell; $shellCand = $candidate.Shell
        $shellRefOut = $shellRef.Stdout.Replace($reference.ShellFolder, '<work>'); $shellCandOut = $shellCand.Stdout.Replace($candidate.ShellFolder, '<work>')
        if ("$($shellRef.ExitCode)" -ne "$($shellCand.ExitCode)" -or $shellRefOut -ne $shellCandOut) {
            $problems.Add("otter web site.ot: reference exit $($shellRef.ExitCode) [$($shellRefOut.Trim())], otter-net exit $($shellCand.ExitCode) [$($shellCandOut.Trim())]")
        }
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$candidateBuild = $builds['candidate']
$pages = 0
if ($candidateBuild) { $pages = ([regex]::Matches($candidateBuild.Build.Stdout, '(?m)^Built /')).Count }
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# The Otter website (otter-docs): otterPS vs otter-net')
$lines.Add('')
if ($CandidateOnly) {
    $lines.Add("otter-net built $pages pages and the site shell; **$($problems.Count) problems**. Compared with the Windows capture afterwards (raw pages, static files, site.ot).")
}
else {
    $lines.Add("$pages pages and the site shell built by both; $compared files compared (site, raw pages, site.ot): **$($problems.Count) problems**, $identical identical byte for byte.")
}
$lines.Add('')
$lines.Add("Build: otterPS's otter-docs/scripts/build-otter-pages.ps1, run by $hostExe; otter-net: $OtterNet.")
$lines.Add('')
foreach ($problem in $problems) { $lines.Add("- $problem") }
[System.IO.File]::WriteAllText($ReportPath, ($lines -join "`n") + "`n", $utf8)
$mode = 'reference'; if ($CandidateOnly) { $mode = 'candidate-only' }
$summary = [ordered]@{ tool = 'compare-site'; mode = $mode; total = $(if ($CandidateOnly) { $pages + 1 } else { $compared }); failed = $problems.Count; passed = $(if ($CandidateOnly) { $pages + 1 - [Math]::Min($problems.Count, $pages + 1) } else { $identical }); pages = $pages; skipped = 0
    failures = @($problems | Select-Object -First 200) }
[System.IO.File]::WriteAllText([System.IO.Path]::ChangeExtension($ReportPath, '.json'), ($summary | ConvertTo-Json -Depth 4), $utf8)
Write-Host "Site: $pages pages, $($problems.Count) problems. Report: $ReportPath"
foreach ($problem in ($problems | Select-Object -First 30)) { Write-Host "  $problem" -ForegroundColor Red }
if ($problems.Count -gt 0) { exit 1 }
exit 0
