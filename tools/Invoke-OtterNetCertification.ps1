<#
.SYNOPSIS
Certifies one otter-net commit: builds it from a clean, detached checkout of
exactly that commit, runs every release gate, and writes a machine-readable
record and a report (docs/CERTIFICATION.md).

.DESCRIPTION
otter-net's counterpart of otterPS 1.0.1's tools/Invoke-OtterReleaseCertification.ps1.
Like that script it adds no test logic of its own beyond running the
repository's tools and reading their counts: each gate is one or more of
otter-net's tools, run against the frozen reference, otterPS 1.0.1 (v1.0.1,
cfb657c), with its output kept as evidence.

A run counts as certification only when it runs in a clean (no modified or
untracked files), detached checkout whose HEAD is the commit given. When the
checkout this script is in is not that, the script makes one - a detached
git worktree of -Commit in the work folder - runs that commit's own copy of
this script there, and removes the worktree afterwards. Records go to
-OutputDirectory (default: release/certification of the checkout the script
was started from).

Gates (each passes or fails, with counts and the paths of its evidence):

  build                 dotnet build otter-net.sln (Release) and the Native AOT
                        publish of otter (and otter-desktop on Windows), 0 warnings
  conformance           otterPS's conformance/manifest.json fixtures against
                        their expected outputs (as tools/Test-OtterReleaseConformance.ps1
                        runs them), and otterPS's conformance/ programs against otterPS
  programs              tools/compare.ps1 -NativeOnly (conformance, examples, tests/compat)
  network               tools/compare.ps1 -IncludeEventPrograms -NativeOnly: tests/compat/net_*,
                        and tests/network against tools/network-test-server.js (needs node)
  cli                   tools/compare-cli.ps1 (tests/cli)
  web                   tools/compare-web.ps1 (tests/web, otterPS's examples and conformance, tests/compat)
  fuzz                  otterPS's differential-fuzzer generators (tools/New-FuzzCorpus.ps1), fixed
                        seed: generated programs, grammar-aware mutations, and mutations and
                        case variants of otterPS's conformance and example programs
  projects              every project of otterPS's projects/ and OtterWorkspace/ (check, run,
                        test, build) and every example of examples/ (check), through
                        tools/compare-cli.ps1
  site                  the website: otter-docs built with otterPS and with otter-net,
                        compared file by file (tools/compare-site.ps1)
  no-powershell         the whole corpus again (programs with event programs, web, CLI cases,
                        projects, fuzz, site) with fake pwsh/powershell/powershell.exe first on
                        otter-net's PATH logging every launch; only a program that runs a .ps1
                        file itself may start one. On Windows, as administrator, also every
                        powershell.exe/pwsh.exe whose parent is otter (Windows' process-creation audit).
  native-coverage       tools/native-coverage.ps1: 0 places that hand anything to PowerShell,
                        and every process otter-net's source starts, accounted for
  desktop               (Windows) tests/desktop: real windows driven by UI Automation against otterPS
  machine-state         (Windows, -IncludeMachineState: it changes the machine) tools/windows-state.ps1,
                        then tests/windows-state and the system programs against otterPS, also
                        under German and Finnish formats
  repository-clean      git status of the checkout is the same after the gates as before

Where otterPS's certified host is present (Windows PowerShell 5.1 on win-x64:
-Reference auto) the program, CLI, web, fuzz, project and site gates compare
with otterPS directly. Elsewhere they run otter-net alone (a gate then fails
on a gap in otter-net or a crash) and capture what it showed; -Combine
compares every platform's capture with the Windows reference afterwards
(tools/compare-captures.ps1, the declared differences in
tests/cross-platform.psd1) and writes the combined record.

Output, under -OutputDirectory:
  <sha>/<platform>.json       this platform's record (machine-readable)
  <sha>/<platform>.md         the same for people
  <sha>/<platform>/           evidence: reports/, logs/, captures/
  <sha>.json, <sha>.md        with -Combine: every platform and the cross-platform check

Runs on Windows PowerShell 5.1 and PowerShell 7 (needs git and the .NET 10 SDK).

.EXAMPLE
pwsh -NoProfile -File tools/Invoke-OtterNetCertification.ps1 -Commit 1a2b3c4 -OtterPs ../otterPS-1.0.1

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File tools\Invoke-OtterNetCertification.ps1 -Commit 1a2b3c4 -OtterPs otterps -IncludeMachineState

.EXAMPLE
pwsh -NoProfile -File tools/Invoke-OtterNetCertification.ps1 -Commit 1a2b3c4 -OtterPs ../otterPS-1.0.1 -Combine
#>
[CmdletBinding()]
param(
    # The candidate commit (a SHA or anything git rev-parse takes).
    [string]$Commit = 'HEAD',
    # A checkout of otterPS 1.0.1, the reference.
    [string]$OtterPs = '',
    [string]$OutputDirectory = '',
    # A runtime identifier: win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64.
    [string]$Platform = '',
    # auto: compare with otterPS on Windows PowerShell 5.1 on win-x64, otter-net alone elsewhere.
    [ValidateSet('auto', 'otterps', 'none')][string]$Reference = 'auto',
    [int]$FuzzCount = 150,
    [int]$FuzzSeed = 20261001,
    # Mutations and case variants of each conformance and example program.
    [int]$FuzzVariants = 2,
    [string[]]$Only = @(),
    # Windows: add printers, tasks, services, event logs (CI runners only).
    [switch]$IncludeMachineState,
    [switch]$SkipDesktop,
    [string]$WorkDirectory = '',
    # Combine the platform records of -Commit under -OutputDirectory.
    [switch]$Combine,
    # Development only (a machine that cannot link Native AOT): publish otter
    # framework-dependent instead. The run is then a development run.
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
# This PowerShell's own executable: powershell.exe, or pwsh (which can be a
# .NET global tool, whose process is dotnet).
$hostExe = (Get-Process -Id $PID).Path
if ($PSVersionTable.PSEdition -eq 'Core' -and [System.IO.Path]::GetFileNameWithoutExtension($hostExe) -ne 'pwsh') {
    $pwshCommand = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pwshCommand) { $hostExe = $pwshCommand.Source }
}
$onWindows = [System.IO.Path]::DirectorySeparatorChar -eq '\'
$hostArgs = @('-NoProfile')
if ($onWindows) { $hostArgs += @('-ExecutionPolicy', 'Bypass') }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$referenceSha = 'cfb657c'
$gateNames = @('build', 'conformance', 'programs', 'network', 'cli', 'web', 'fuzz', 'projects', 'site', 'no-powershell', 'native-coverage', 'desktop', 'machine-state')

function Invoke-Git {
    param([string]$Directory, [string[]]$GitArgs)
    $previous = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & git -C $Directory @GitArgs 2>$null } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { return $null }
    return $out
}

# Windows' quoting convention, which .NET reads back on every platform.
function ConvertTo-ArgumentLine {
    param([string[]]$Arguments)
    $quoted = foreach ($a in $Arguments) {
        if ($a -ne '' -and $a -notmatch '[\s"]') { $a; continue }
        $sb = New-Object System.Text.StringBuilder('"')
        $backslashes = 0
        foreach ($c in $a.ToCharArray()) {
            if ($c -eq [char]92) { $backslashes++; continue }
            if ($c -eq '"') { [void]$sb.Append((New-Object string ([char]92), (2 * $backslashes + 1))).Append('"'); $backslashes = 0; continue }
            [void]$sb.Append((New-Object string ([char]92), $backslashes)).Append($c); $backslashes = 0
        }
        [void]$sb.Append((New-Object string ([char]92), (2 * $backslashes))).Append('"')
        $sb.ToString()
    }
    return ($quoted -join ' ')
}

# Runs a program, appending everything it prints to $Log; returns its exit
# code and output.
function Invoke-Logged {
    param([string]$FileName, [string[]]$Arguments, [string]$Log, [string]$WorkingDirectory = $repoRoot, [hashtable]$Environment = @{}, [int]$TimeoutSeconds = 7200)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = ConvertTo-ArgumentLine $Arguments
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardInput = $true
    foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = $Environment[$key] }
    Add-Content -LiteralPath $Log -Value "> $FileName $($psi.Arguments)" -Encoding UTF8
    $process = [System.Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try { $process.StandardInput.Close() } catch { }
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) { try { $process.Kill() } catch { } }
    [void]$stdout.Wait(30000); [void]$stderr.Wait(30000)
    $code = 'timeout'
    if ($finished) { $code = $process.ExitCode }
    Add-Content -LiteralPath $Log -Value $stdout.Result -Encoding UTF8
    if ($stderr.Result) { Add-Content -LiteralPath $Log -Value $stderr.Result -Encoding UTF8 }
    Add-Content -LiteralPath $Log -Value "(exit $code)" -Encoding UTF8
    return New-Object PSObject -Property @{ ExitCode = $code; Stdout = $stdout.Result; Stderr = $stderr.Result }
}

# Runs one of the repository's PowerShell tools in this PowerShell, with
# everything it writes in $Log, and returns the counts it wrote beside its
# report (<report>.json).
function Invoke-Tool {
    param([string]$Tool, [hashtable]$Parameters, [string]$Log)
    $toolPath = Join-Path $repoRoot "tools/$Tool"
    $shown = ($Parameters.Keys | Sort-Object | ForEach-Object {
        $value = $Parameters[$_]
        if ($value -is [switch] -or $value -is [bool]) { "-$_" } else { "-$_ $(@($value) -join ',')" }
    }) -join ' '
    Add-Content -LiteralPath $Log -Value "> tools/$Tool $shown" -Encoding UTF8
    $global:LASTEXITCODE = 0
    try {
        & $toolPath @Parameters *>&1 | ForEach-Object { "$_" } | Add-Content -LiteralPath $Log -Encoding UTF8
        $code = $LASTEXITCODE
    }
    catch {
        Add-Content -LiteralPath $Log -Value "tools/$Tool failed: $($_.Exception.Message)" -Encoding UTF8
        $code = 'exception'
    }
    $summary = $null
    if ($Parameters.ContainsKey('ReportPath')) {
        $json = [System.IO.Path]::ChangeExtension([string]$Parameters['ReportPath'], '.json')
        if (Test-Path -LiteralPath $json) { $summary = [System.IO.File]::ReadAllText($json) | ConvertFrom-Json }
    }
    Add-Content -LiteralPath $Log -Value "(exit $code)" -Encoding UTF8
    return New-Object PSObject -Property @{ ExitCode = $code; Summary = $summary }
}

function Get-RelativePath {
    param([string]$Path, [string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $base = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if ($full.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) { return ($full.Substring($base.Length).TrimStart('\', '/') -replace '\\', '/') }
    return ($full -replace '\\', '/')
}

function Get-PlatformName {
    if ($onWindows) {
        $arch = $env:PROCESSOR_ARCHITEW6432
        if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
        if ($arch -eq 'ARM64') { return 'win-arm64' }
        return 'win-x64'
    }
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $os = 'linux'
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) { $os = 'osx' }
    return "$os-$arch"
}

function Get-Porcelain {
    param([string]$Directory)
    # Records written into this checkout are not changes made by the gates.
    return @(@(Invoke-Git $Directory @('status', '--porcelain', '--untracked-files=all')) | Where-Object { $_ -and $_ -notmatch '^.. "?release/certification/' })
}

# --------------------------------------------------------------------------
# The commit, and a clean detached checkout of it
# --------------------------------------------------------------------------
$sha = [string](Invoke-Git $repoRoot @('rev-parse', '--verify', "$Commit^{commit}"))
$sha = $sha.Trim()
if (-not $sha) { throw "Not a commit here: $Commit" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'release/certification' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

# --------------------------------------------------------------------------
# -Combine: every platform's record, and each platform's capture against the
# Windows reference.
# --------------------------------------------------------------------------
if ($Combine) {
    $platforms = @('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64', 'linux-x64', 'linux-arm64')
    $shaDir = Join-Path $OutputDirectory $sha
    if (-not (Test-Path -LiteralPath $shaDir)) { throw "No platform records for $sha in $shaDir." }
    $crossDir = Join-Path $shaDir 'cross-platform'
    New-Item -ItemType Directory -Force -Path $crossDir | Out-Null
    $combined = [ordered]@{ kind = 'release-certification'; commit = $sha; subject = ([string](Invoke-Git $repoRoot @('log', '-1', '--format=%s', $sha))).Trim(); platforms = [ordered]@{}; crossPlatform = [ordered]@{}; missing = @(); certified = $false }
    $gateOrder = New-Object System.Collections.Generic.List[string]
    $allGood = $true
    foreach ($platform in $platforms) {
        $json = Join-Path $shaDir "$platform.json"
        if (-not (Test-Path -LiteralPath $json)) { $combined.missing += $platform; $allGood = $false; continue }
        $r = [System.IO.File]::ReadAllText($json) | ConvertFrom-Json
        $gatesOf = [ordered]@{}
        foreach ($g in @($r.gates)) {
            if (-not $gateOrder.Contains($g.gate)) { $gateOrder.Add($g.gate) }
            $gatesOf[$g.gate] = [ordered]@{ passed = $g.passed; counts = $g.counts; failures = @($g.failures | Select-Object -First 20); note = $g.note }
        }
        $combined.platforms[$platform] = [ordered]@{ kind = $r.kind; allGatesPassed = $r.allGatesPassed; referenceComparedDirectly = $r.reference.comparedDirectly; run = $r.host.githubRun; os = $r.host.os; gates = $gatesOf }
        if ($r.kind -ne 'release-certification' -or -not $r.allGatesPassed -or $r.commit -ne $sha) { $allGood = $false }
    }
    $referenceCaptures = Join-Path (Join-Path $shaDir 'win-x64') 'captures'
    foreach ($platform in $platforms) {
        if ($platform -eq 'win-x64') { continue }
        $candidateCaptures = Join-Path (Join-Path $shaDir $platform) 'captures'
        if (-not (Test-Path -LiteralPath $referenceCaptures) -or -not (Test-Path -LiteralPath $candidateCaptures)) {
            $combined.crossPlatform[$platform] = [ordered]@{ compared = $false; reason = 'no capture (the platform or win-x64 did not run)' }
            $allGood = $false
            continue
        }
        $report = Join-Path $crossDir "$platform.md"
        & (Join-Path $PSScriptRoot 'compare-captures.ps1') -ReferenceRoot $referenceCaptures -CandidateRoot $candidateCaptures -Platform $platform -ReportPath $report
        $summary = [System.IO.File]::ReadAllText([System.IO.Path]::ChangeExtension($report, '.json')) | ConvertFrom-Json
        $combined.crossPlatform[$platform] = [ordered]@{ compared = $true; unexpected = $summary.unexpected; declared = $summary.declared; gates = $summary.gates; report = (Get-RelativePath $report $OutputDirectory) }
        if ([int]$summary.unexpected -ne 0) { $allGood = $false }
    }
    $combined.certified = $allGood
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$sha.json"), ($combined | ConvertTo-Json -Depth 10), $utf8)
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add("# otter-net certification: $($sha.Substring(0, 7))")
    $md.Add('')
    $md.Add("Commit ``$sha`` - $($combined.subject)")
    $md.Add('')
    $md.Add("**$(if ($allGood) { 'Certified: every gate passed on all six platforms, and every platform matches the Windows reference.' } else { 'Not certified.' })**")
    if ($combined.missing.Count -gt 0) { $md.Add(''); $md.Add("Platforms without a record: $($combined.missing -join ', ')") }
    $md.Add('')
    $present = @($platforms | Where-Object { $combined.platforms.Contains($_) })
    $md.Add('| Gate | ' + ($present -join ' | ') + ' |')
    $md.Add('|---|' + (($present | ForEach-Object { '---' }) -join '|') + '|')
    foreach ($gate in $gateOrder) {
        $cells = foreach ($platform in $present) {
            $g = $combined.platforms[$platform].gates[$gate]
            if (-not $g) { '-' ; continue }
            $c = $g.counts
            $short = ''
            foreach ($key in @('programs.total', 'cases.total', 'pages.total', 'files.total', 'fixtures.total', 'runs', 'programs.identical', 'solution.warnings')) {
                $value = $c.$key
                if ($null -ne $value) { $short = "$key $value"; break }
            }
            "$(if ($g.passed) { 'pass' } else { '**FAIL**' })$(if ($short) { " ($short)" })"
        }
        $md.Add("| $gate | " + ($cells -join ' | ') + ' |')
    }
    $md.Add('')
    $md.Add('## Each platform against Windows (otterPS 1.0.1 on Windows PowerShell 5.1)')
    $md.Add('')
    foreach ($platform in $combined.crossPlatform.Keys) {
        $x = $combined.crossPlatform[$platform]
        if (-not $x.compared) { $md.Add("- ${platform}: not compared ($($x.reason))"); continue }
        $md.Add("- ${platform}: **$($x.unexpected) unexpected**, $($x.declared) declared differences (``$($x.report)``)")
    }
    $md.Add('')
    $md.Add('## Runs')
    $md.Add('')
    foreach ($platform in $present) { $md.Add("- ${platform}: $($combined.platforms[$platform].run) ($($combined.platforms[$platform].os))") }
    [System.IO.File]::WriteAllText((Join-Path $OutputDirectory "$sha.md"), ($md -join "`n") + "`n", $utf8)
    Write-Host "Combined record: $(Join-Path $OutputDirectory "$sha.json") - $(if ($allGood) { 'certified' } else { 'NOT certified' })"
    if ($allGood) { exit 0 }
    exit 1
}

if (-not $OtterPs) { throw '-OtterPs (a checkout of otterPS 1.0.1, the reference) is required.' }
$OtterPs = (Resolve-Path $OtterPs).Path
$head = ([string](Invoke-Git $repoRoot @('rev-parse', 'HEAD'))).Trim()
$detached = $null -eq (Invoke-Git $repoRoot @('symbolic-ref', '-q', 'HEAD'))
$porcelainBefore = Get-Porcelain $repoRoot
$clean = $porcelainBefore.Count -eq 0
if (-not ($head -eq $sha -and $detached -and $clean)) {
    # Not a clean detached checkout of the commit: make one, and run the
    # commit's own copy of this script there.
    if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-net-cert-' + $sha.Substring(0, 7)) }
    $checkout = Join-Path $WorkDirectory 'checkout'
    if (Test-Path -LiteralPath $checkout) { [void](Invoke-Git $repoRoot @('worktree', 'remove', '--force', $checkout)); Remove-Item -LiteralPath $checkout -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
    Write-Host "This checkout is not a clean detached checkout of $($sha.Substring(0, 7)) (HEAD $($head.Substring(0, 7)), detached: $detached, clean: $clean); certifying a fresh one: $checkout"
    $added = Invoke-Git $repoRoot @('worktree', 'add', '--detach', $checkout, $sha)
    if (-not (Test-Path -LiteralPath (Join-Path $checkout '.git'))) { throw "git worktree add --detach $checkout $sha failed." }
    try {
        $script = Join-Path $checkout 'tools/Invoke-OtterNetCertification.ps1'
        if (-not (Test-Path -LiteralPath $script)) { throw "Commit $sha has no tools/Invoke-OtterNetCertification.ps1: it predates the certification process and cannot be certified by it." }
        $forward = @('-Commit', $sha, '-OtterPs', $OtterPs, '-OutputDirectory', $OutputDirectory, '-Reference', $Reference,
            '-FuzzCount', "$FuzzCount", '-FuzzSeed', "$FuzzSeed", '-FuzzVariants', "$FuzzVariants", '-WorkDirectory', (Join-Path $WorkDirectory 'work'))
        if ($Platform) { $forward += @('-Platform', $Platform) }
        if ($Only.Count -gt 0) { $forward += @('-Only', (($Only | ForEach-Object { $_ -split ',' }) -join ',')) }
        if ($IncludeMachineState) { $forward += '-IncludeMachineState' }
        if ($SkipDesktop) { $forward += '-SkipDesktop' }
        if ($FrameworkDependent) { $forward += '-FrameworkDependent' }
        & $hostExe @hostArgs -File $script @forward
        $code = $LASTEXITCODE
    }
    finally {
        [void](Invoke-Git $repoRoot @('worktree', 'remove', '--force', $checkout))
        [void](Invoke-Git $repoRoot @('worktree', 'prune'))
    }
    exit $code
}

# --------------------------------------------------------------------------
# Certifying this checkout
# --------------------------------------------------------------------------
if (-not $Platform) { $Platform = Get-PlatformName }
$subject = [string](Invoke-Git $repoRoot @('log', '-1', '--format=%s'))
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$unknown = @($Only | Where-Object { $gateNames -notcontains $_ })
if ($unknown.Count -gt 0) { throw "Unknown gate(s): $($unknown -join ', '). Gates: $($gateNames -join ', ')" }
$referenceHostPresent = $onWindows -and (Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'))
$withReference = switch ($Reference) { 'otterps' { $true } 'none' { $false } default { $referenceHostPresent -and $Platform -eq 'win-x64' } }
$otterPsCommit = ([string](Invoke-Git $OtterPs @('rev-parse', 'HEAD'))).Trim()
$otterPsVersion = ''
if (Test-Path -LiteralPath (Join-Path $OtterPs 'VERSION')) { $otterPsVersion = ([System.IO.File]::ReadAllText((Join-Path $OtterPs 'VERSION'))).Trim() }
if ($otterPsCommit -and -not $otterPsCommit.StartsWith($referenceSha)) { throw "The reference must be otterPS 1.0.1 ($referenceSha); $OtterPs is at $otterPsCommit." }
if ($otterPsVersion -ne '1.0.1') { throw "The reference must be otterPS 1.0.1; $OtterPs has VERSION $otterPsVersion." }

$recordDir = Join-Path (Join-Path $OutputDirectory $sha) $Platform
if (Test-Path -LiteralPath $recordDir) { Remove-Item -LiteralPath $recordDir -Recurse -Force }
$reports = Join-Path $recordDir 'reports'; $logs = Join-Path $recordDir 'logs'; $captures = Join-Path $recordDir 'captures'
foreach ($d in $reports, $logs, $captures) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
if (-not $WorkDirectory) { $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-net-cert-' + $sha.Substring(0, 7) + '-work') }
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null
$WorkDirectory = (Resolve-Path $WorkDirectory).Path
$bin = Join-Path $WorkDirectory 'bin'
$exe = 'otter'; if ($onWindows) { $exe = 'otter.exe' }
$otter = Join-Path $bin $exe
# Corpora generated here live in compat-results/ (ignored by git), so their
# names - and the capture ids - are the same on every machine.
$generated = Join-Path $repoRoot 'compat-results/certification'
if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
New-Item -ItemType Directory -Force -Path $generated | Out-Null

Write-Host ''
Write-Host ('=' * 72)
Write-Host 'otter-net release certification'
Write-Host ('=' * 72)
Write-Host "Commit:     $sha  $subject"
Write-Host "Checkout:   $repoRoot (clean, detached)"
Write-Host "Platform:   $Platform; PowerShell $($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"
Write-Host "Reference:  otterPS $otterPsVersion ($otterPsCommit) at $OtterPs; $(if ($withReference) { 'compared directly (Windows PowerShell 5.1)' } else { 'otter-net alone here; compared with the Windows capture by -Combine' })"
Write-Host "Record:     $recordDir"
Write-Host ''

$records = New-Object System.Collections.Generic.List[object]
$state = @{ BuildPassed = $false; ShimDirectory = $null; ShimLog = $null; FuzzFolders = @(); ProjectCases = $null }

function New-GateResult {
    param([bool]$Passed, [System.Collections.IDictionary]$Counts, [string[]]$Evidence = @(), [string[]]$Failures = @(), [string]$Note = '')
    return [ordered]@{ passed = $Passed; counts = $Counts; evidence = @($Evidence | Where-Object { $_ } | ForEach-Object { Get-RelativePath $_ $OutputDirectory }); failures = @($Failures | Select-Object -First 100); note = $Note }
}

function Add-Counts {
    param([System.Collections.IDictionary]$Counts, [string]$Prefix, $Summary)
    if (-not $Summary) { $Counts["$Prefix.total"] = 0; $Counts["$Prefix.failed"] = 'no summary'; return }
    $Counts["$Prefix.total"] = $Summary.total
    $Counts["$Prefix.failed"] = $Summary.failed
    $Counts["$Prefix.passed"] = $Summary.passed
    if ($null -ne $Summary.native) { $Counts["$Prefix.native"] = $Summary.native }
    if ($null -ne $Summary.skipped -and $Summary.skipped) { $Counts["$Prefix.skipped"] = $Summary.skipped }
    if ($null -ne $Summary.known -and $Summary.known) { $Counts["$Prefix.known"] = $Summary.known }
}

function Test-ToolPassed {
    param($Run)
    return ($Run.ExitCode -eq 0) -and $Run.Summary -and ([int]$Run.Summary.failed -eq 0) -and ([int]$Run.Summary.total -gt 0)
}

# Parameters every comparing tool takes: the reference, or otter-net alone.
function Get-CompareParameters {
    param([string]$Gate, [string]$Report)
    $p = @{ OtterPs = $OtterPs; OtterNet = $otter; ReportPath = (Join-Path $reports $Report); CaptureDirectory = (Join-Path $captures $Gate) }
    if (-not $withReference) { $p['CandidateOnly'] = $true }
    return $p
}

# The skip pattern of tools/compare.ps1: programs that never end on their own.
$eventPattern = '\bwindow\b|\bshow app\b|\bis a page\b|\bcreate (button|window|text box)\b|\blisten (securely )?for\b|\breceives (GET|POST)\b|\bwatch (file|folder)\b|\bevery \d+ (second|minute)|\bopen udp\b|https?://'

# --------------------------------------------------------------------------
# The gates
# --------------------------------------------------------------------------
$gates = [ordered]@{}

$gates['build'] = {
    param($log)
    $counts = [ordered]@{}
    $failures = @()
    $build = Invoke-Logged -FileName 'dotnet' -Arguments @('build', 'otter-net.sln', '-c', 'Release', '--nologo', '-p:TreatWarningsAsErrors=false') -Log $log
    $warnings = @([regex]::Matches($build.Stdout, '(?m): warning [A-Z]+[0-9]+[^\r\n]*') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique)
    $counts['solution.warnings'] = $warnings.Count
    $counts['solution.exit'] = $build.ExitCode
    if ($build.ExitCode -ne 0) { $failures += "dotnet build otter-net.sln: exit $($build.ExitCode)" }
    if (Test-Path -LiteralPath $bin) { Remove-Item -LiteralPath $bin -Recurse -Force }
    $publishArguments = @('publish', 'src/Otter.Cli/Otter.Cli.csproj', '-c', 'Release', '-r', $Platform, '-p:UseAppHost=true', '-p:TreatWarningsAsErrors=false', '-o', $bin, '--nologo')
    if ($FrameworkDependent) { $publishArguments += @('-p:PublishAot=false', '--self-contained', 'false') }
    $publish = Invoke-Logged -FileName 'dotnet' -Arguments $publishArguments -Log $log
    $publishWarnings = @([regex]::Matches($publish.Stdout, '(?m): warning [A-Z]+[0-9]+[^\r\n]*') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique)
    $counts['aot.warnings'] = $publishWarnings.Count
    if ($FrameworkDependent) { $counts['aot'] = 'no: -FrameworkDependent (development run)' }
    $counts['aot.exit'] = $publish.ExitCode
    if ($publish.ExitCode -ne 0) { $failures += "dotnet publish (Native AOT, $Platform): exit $($publish.ExitCode)" }
    $warnings += $publishWarnings
    if ($onWindows) {
        $desktop = Invoke-Logged -FileName 'dotnet' -Arguments @('publish', 'src/Otter.Desktop/Otter.Desktop.csproj', '-c', 'Release', '-r', $Platform, '--self-contained', 'false', '-p:UseAppHost=true', '-p:TreatWarningsAsErrors=false', '-o', $bin, '--nologo') -Log $log
        $desktopWarnings = @([regex]::Matches($desktop.Stdout, '(?m): warning [A-Z]+[0-9]+[^\r\n]*') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique)
        $counts['desktop.warnings'] = $desktopWarnings.Count
        if ($desktop.ExitCode -ne 0) { $failures += "dotnet publish otter-desktop: exit $($desktop.ExitCode)" }
        $warnings += $desktopWarnings
    }
    foreach ($w in $warnings) { $failures += "warning: $w" }
    if (Test-Path -LiteralPath $otter) {
        $version = Invoke-Logged -FileName $otter -Arguments @('--version') -Log $log
        $counts['otter.version'] = $version.Stdout.Trim()
        $counts['otter.bytes'] = (Get-Item -LiteralPath $otter).Length
    }
    else { $failures += "no $exe was published" }
    $state.BuildPassed = ($failures.Count -eq 0)
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log) -Failures $failures
}

$gates['conformance'] = {
    param($log)
    $counts = [ordered]@{}
    $failures = @()
    # 1. The release conformance fixtures against their expected outputs, as
    #    otterPS's tools/Test-OtterReleaseConformance.ps1 runs them (through
    #    the production entry point, `otter run|check|web`).
    $manifest = [System.IO.File]::ReadAllText((Join-Path $OtterPs 'conformance/manifest.json')) | ConvertFrom-Json
    $copy = Join-Path $WorkDirectory 'conformance-root'
    if (Test-Path -LiteralPath $copy) { Remove-Item -LiteralPath $copy -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $copy | Out-Null
    Copy-Item -LiteralPath (Join-Path $OtterPs 'conformance') -Destination $copy -Recurse
    $passedFixtures = 0
    $browser = $null
    if ($onWindows) { $browser = @('C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe', 'C:\Program Files\Microsoft\Edge\Application\msedge.exe', 'C:\Program Files\Google\Chrome\Application\chrome.exe') | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
    $browserChecked = 0
    foreach ($case in @($manifest.fixtures)) {
        $source = Join-Path $copy ([string]$case.source)
        $workdir = $copy
        $temp = $null
        try {
            if ($case.mode -eq 'run-in-temp-directory' -or $case.mode -eq 'web') {
                $temp = Join-Path $WorkDirectory ('fixture-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
                New-Item -ItemType Directory -Path $temp | Out-Null
                Copy-Item -LiteralPath $source -Destination $temp
                $source = Join-Path $temp ([System.IO.Path]::GetFileName($source))
                $workdir = $temp
            }
            $mode = [string]$case.mode
            if ($mode -eq 'run-in-temp-directory') { $mode = 'run' }
            $arguments = @($mode, $source)
            if ($mode -eq 'web') { $arguments += '-NoOpen' }
            $run = Invoke-Logged -FileName $otter -Arguments $arguments -Log $log -WorkingDirectory $workdir -TimeoutSeconds 120
            $stdout = ($run.Stdout -replace "`r`n", "`n").TrimEnd("`r", "`n")
            $stderr = ($run.Stderr -replace "`r`n", "`n").TrimEnd("`r", "`n")
            if ("$($run.ExitCode)" -ne "$([int]$case.expectedExitCode)") { throw "exit code $($run.ExitCode), expected $($case.expectedExitCode). stderr: $stderr" }
            if ($null -ne $case.expectedStdout) {
                $expected = (@($case.expectedStdout) -join "`n")
                if ($stdout -cne $expected) { throw "stdout [$stdout], expected [$expected]" }
            }
            if ($case.expectedDiagnosticContains -and ($stdout + "`n" + $stderr) -notmatch [regex]::Escape([string]$case.expectedDiagnosticContains)) {
                throw "the diagnostic does not contain '$($case.expectedDiagnosticContains)': $stdout $stderr"
            }
            if ($case.expectedHtmlContains) {
                $html = [System.IO.Path]::ChangeExtension($source, '.html')
                if (-not (Test-Path -LiteralPath $html)) { throw "otter web wrote no $html" }
                $htmlText = [System.IO.File]::ReadAllText($html)
                foreach ($needle in @($case.expectedHtmlContains)) { if ($htmlText -notlike "*$needle*") { throw "the page does not contain '$needle'" } }
                if ($browser) {
                    # As otterPS's conformance: the page mounts in a real browser.
                    $dump = Join-Path $WorkDirectory ('dom-' + [Guid]::NewGuid().ToString('N') + '.txt')
                    $proc = Start-Process -FilePath $browser -ArgumentList @('--headless', '--disable-gpu', '--dump-dom', $html) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $dump
                    if ($proc.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $dump) -or -not ([System.IO.File]::ReadAllText($dump).Trim())) { throw "the page did not load in $browser" }
                    Remove-Item -LiteralPath $dump -Force
                    $browserChecked++
                }
            }
            $passedFixtures++
            Add-Content -LiteralPath $log -Value "  pass  $($case.name)" -Encoding UTF8
        }
        catch {
            $failures += "fixture $($case.name): $($_.Exception.Message)"
            Add-Content -LiteralPath $log -Value "  FAIL  $($case.name): $($_.Exception.Message)" -Encoding UTF8
        }
        finally { if ($temp) { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue } }
    }
    $counts['fixtures.total'] = @($manifest.fixtures).Count
    $counts['fixtures.passed'] = $passedFixtures
    $counts['fixtures.browser'] = $browserChecked
    # 2. Every program of conformance/ against otterPS (or captured).
    $p = Get-CompareParameters 'conformance' 'conformance.md'
    $p['Corpus'] = @((Join-Path $OtterPs 'conformance')); $p['NativeOnly'] = $true
    $run = Invoke-Tool 'compare.ps1' $p $log
    Add-Counts $counts 'programs' $run.Summary
    if (-not (Test-ToolPassed $run)) { $failures += "conformance programs: exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

$gates['programs'] = {
    param($log)
    $counts = [ordered]@{}
    $p = Get-CompareParameters 'programs' 'programs.md'
    $p['NativeOnly'] = $true
    $run = Invoke-Tool 'compare.ps1' $p $log
    Add-Counts $counts 'programs' $run.Summary
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

function Start-NetworkServer {
    param([string]$Log)
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { return $null }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $node.Source
    $psi.Arguments = ConvertTo-ArgumentLine @((Join-Path $repoRoot 'tools/network-test-server.js'))
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $server = [System.Diagnostics.Process]::Start($psi)
    # Ready when it accepts a connection.
    for ($i = 0; $i -lt 50; $i++) {
        try { $client = New-Object System.Net.Sockets.TcpClient; $client.Connect('127.0.0.1', 18765); $client.Close(); break } catch { Start-Sleep -Milliseconds 200 }
    }
    Add-Content -LiteralPath $Log -Value "network test server: node tools/network-test-server.js (pid $($server.Id))" -Encoding UTF8
    return $server
}

$gates['network'] = {
    param($log)
    $counts = [ordered]@{}
    $failures = @()
    $corpus = @(Get-ChildItem (Join-Path $repoRoot 'tests/compat') -Filter 'net_*.ot' | ForEach-Object FullName)
    $server = Start-NetworkServer $log
    if ($server) { $corpus += @(Get-ChildItem (Join-Path $repoRoot 'tests/network') -Filter '*.ot' | ForEach-Object FullName) }
    else { $failures += 'node is not installed: tests/network (against tools/network-test-server.js) did not run' }
    try {
        $p = Get-CompareParameters 'network' 'network.md'
        $p['Corpus'] = $corpus; $p['IncludeEventPrograms'] = $true; $p['NativeOnly'] = $true
        $run = Invoke-Tool 'compare.ps1' $p $log
    }
    finally { if ($server) { try { $server.Kill() } catch { } } }
    Add-Counts $counts 'programs' $run.Summary
    $counts['testServer'] = [bool]$server
    if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

$gates['cli'] = {
    param($log)
    $counts = [ordered]@{}
    $p = Get-CompareParameters 'cli' 'cli.md'
    $run = Invoke-Tool 'compare-cli.ps1' $p $log
    Add-Counts $counts 'cases' $run.Summary
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

$gates['web'] = {
    param($log)
    $counts = [ordered]@{}
    $p = Get-CompareParameters 'web' 'web.md'
    $run = Invoke-Tool 'compare-web.ps1' $p $log
    Add-Counts $counts 'pages' $run.Summary
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

# Each fuzz program in a folder of its own (compare.ps1 copies a program's
# whole folder for every run).
function Split-IntoFolders {
    param([string]$Folder)
    foreach ($file in @(Get-ChildItem -LiteralPath $Folder -Filter '*.ot' -File)) {
        $own = Join-Path $Folder $file.BaseName
        New-Item -ItemType Directory -Force -Path $own | Out-Null
        Move-Item -LiteralPath $file.FullName -Destination $own
    }
}

function New-FuzzFolders {
    param([string]$Log)
    if ($state.FuzzFolders.Count -gt 0) { return $state.FuzzFolders }
    $generatedFuzz = Join-Path $generated 'fuzz'
    $variants = Join-Path $generated 'fuzz-variants'
    [void](Invoke-Tool 'New-FuzzCorpus.ps1' @{ OtterPs = $OtterPs; OutputDirectory = $generatedFuzz; Count = $FuzzCount; Seed = $FuzzSeed; Mutated = $true } $Log)
    $sources = @(Get-ChildItem (Join-Path $OtterPs 'conformance') -Recurse -Filter '*.ot' | ForEach-Object FullName) + @(Get-ChildItem (Join-Path $OtterPs 'examples') -Filter '*.ot' | ForEach-Object FullName)
    [void](Invoke-Tool 'New-FuzzCorpus.ps1' @{ OtterPs = $OtterPs; OutputDirectory = $variants; Count = $FuzzVariants; Seed = $FuzzSeed; MutateFrom = $sources; CaseVariants = $true } $Log)
    Split-IntoFolders $generatedFuzz
    Split-IntoFolders $variants
    $state.FuzzFolders = @($generatedFuzz, $variants)
    return $state.FuzzFolders
}

$gates['fuzz'] = {
    param($log)
    $counts = [ordered]@{}
    $folders = New-FuzzFolders $log
    $counts['generated'] = @(Get-ChildItem -LiteralPath $folders[0] -Recurse -Filter 'program-*.ot').Count
    $counts['mutated'] = @(Get-ChildItem -LiteralPath $folders[0] -Recurse -Filter 'mutated-*.ot').Count
    $counts['variants.mutations'] = @(Get-ChildItem -LiteralPath $folders[1] -Recurse -Filter '*-m*.ot').Count
    $counts['variants.case'] = @(Get-ChildItem -LiteralPath $folders[1] -Recurse -Filter '*-c*.ot').Count
    $counts['seed'] = $FuzzSeed
    $p = Get-CompareParameters 'fuzz' 'fuzz.md'
    $p['Corpus'] = $folders; $p['TimeoutSeconds'] = 20; $p['NativeOnly'] = $true
    $run = Invoke-Tool 'compare.ps1' $p $log
    Add-Counts $counts 'programs' $run.Summary
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

# tests/cli-style cases for every project and example of otterPS (written to
# compat-results/, run by tools/compare-cli.ps1).
function New-ProjectCases {
    if ($state.ProjectCases) { return $state.ProjectCases }
    $q = { param($t) "'" + ([string]$t).Replace("'", "''") + "'" }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add('# Generated by tools/Invoke-OtterNetCertification.ps1: every project and example of otterPS 1.0.1.')
    $lines.Add('@{ Cases = @(')
    $projects = @(Get-ChildItem (Join-Path $OtterPs 'projects') -Directory | Sort-Object Name) + @(Get-Item (Join-Path $OtterPs 'OtterWorkspace'))
    foreach ($project in $projects) {
        $manifestPath = Join-Path $project.FullName 'otter.json'
        if (-not (Test-Path -LiteralPath $manifestPath)) { $manifestPath = Join-Path $project.FullName 'project.json' }
        $manifestData = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
        $entry = $manifestData.entryPoint; if (-not $entry) { $entry = $manifestData.main }
        $entrySource = ''
        if ($entry -and (Test-Path -LiteralPath (Join-Path $project.FullName $entry))) { $entrySource = [System.IO.File]::ReadAllText((Join-Path $project.FullName $entry)) }
        $name = $project.Name
        $copyFrom = & $q $project.FullName
        $lines.Add("    @{ Name = 'project ${name}: check'; Args = @('check', '.'); CopyFrom = $copyFrom }")
        $lines.Add("    @{ Name = 'project ${name}: check the entry program'; Args = @('check', $(& $q $entry)); CopyFrom = $copyFrom }")
        if ($entrySource -and $entrySource -notmatch $eventPattern) {
            $lines.Add("    @{ Name = 'project ${name}: run'; Args = @('run', '.'); CopyFrom = $copyFrom; TimeoutSeconds = 60 }")
        }
        $lines.Add("    @{ Name = 'project ${name}: test'; Args = @('test', '.'); CopyFrom = $copyFrom; TimeoutSeconds = 300 }")
        $lines.Add("    @{ Name = 'project ${name}: build'; Args = @('build', '.'); CopyFrom = $copyFrom; TimeoutSeconds = 300 }")
    }
    $examples = Join-Path $OtterPs 'examples'
    foreach ($example in @(Get-ChildItem -LiteralPath $examples -Recurse -Filter '*.ot' | Sort-Object FullName)) {
        $relative = Get-RelativePath $example.FullName $examples
        $lines.Add("    @{ Name = 'example ${relative}: check'; Args = @('check', $(& $q $example.Name)); CopyFrom = $(& $q $example.DirectoryName) }")
    }
    $lines.Add(') }')
    $file = Join-Path $generated 'projects.psd1'
    [System.IO.File]::WriteAllText($file, ($lines -join "`n") + "`n", $utf8)
    $state.ProjectCases = $file
    return $file
}

$gates['projects'] = {
    param($log)
    $counts = [ordered]@{}
    $cases = New-ProjectCases
    $p = Get-CompareParameters 'projects' 'projects.md'
    $p['Cases'] = @($cases)
    $run = Invoke-Tool 'compare-cli.ps1' $p $log
    Add-Counts $counts 'cases' $run.Summary
    $counts['projects'] = @(Get-ChildItem (Join-Path $OtterPs 'projects') -Directory).Count + 1
    $counts['examples'] = @(Get-ChildItem (Join-Path $OtterPs 'examples') -Recurse -Filter '*.ot').Count
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath'], $cases) -Failures $failures `
        -Note 'Every example also runs in the programs gate and compiles in the web gate.'
}

$gates['site'] = {
    param($log)
    $counts = [ordered]@{}
    $p = Get-CompareParameters 'site' 'site.md'
    $run = Invoke-Tool 'compare-site.ps1' $p $log
    Add-Counts $counts 'files' $run.Summary
    if ($run.Summary) { $counts['pages'] = $run.Summary.pages }
    $failures = @(); if (-not (Test-ToolPassed $run)) { $failures += "exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $p['ReportPath']) -Failures $failures
}

# Fake pwsh, powershell and powershell.exe that log every launch (with the
# case that caused it, OTTER_TEST_PROGRAM) and fail.
function New-PowerShellShims {
    param([string]$Log)
    $shims = Join-Path $WorkDirectory 'shims'
    if (Test-Path -LiteralPath $shims) { Remove-Item -LiteralPath $shims -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $shims | Out-Null
    $launches = Join-Path $WorkDirectory 'powershell-launches.log'
    [System.IO.File]::WriteAllText($launches, '')
    if (-not $onWindows) {
        foreach ($name in 'pwsh', 'powershell', 'powershell.exe') {
            $text = "#!/bin/sh`necho `"`$OTTER_TEST_PROGRAM :: $name `$*`" >> '$launches'`nexit 1`n"
            $path = Join-Path $shims $name
            [System.IO.File]::WriteAllText($path, $text)
            & chmod +x $path
        }
    }
    else {
        # A small .NET program, copied under each name.
        $project = Join-Path $WorkDirectory 'shim-src'
        New-Item -ItemType Directory -Force -Path $project | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $project 'shim.csproj'), '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyName>shim</AssemblyName><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>')
        $code = 'File.AppendAllText(Environment.GetEnvironmentVariable("OTTER_SHIM_LOG")!, Environment.GetEnvironmentVariable("OTTER_TEST_PROGRAM") + " :: " + Path.GetFileName(Environment.ProcessPath) + " " + string.Join(" ", args) + Environment.NewLine); return 1;'
        [System.IO.File]::WriteAllText((Join-Path $project 'Program.cs'), $code)
        $built = Invoke-Logged -FileName 'dotnet' -Arguments @('build', $project, '-c', 'Release', '-o', (Join-Path $WorkDirectory 'shim-bin'), '--nologo') -Log $Log
        if ($built.ExitCode -ne 0) { throw 'The PowerShell shim did not build.' }
        foreach ($name in 'pwsh', 'powershell') {
            Copy-Item -LiteralPath (Join-Path $WorkDirectory 'shim-bin/shim.exe') -Destination (Join-Path $shims "$name.exe")
        }
        foreach ($f in @(Get-ChildItem (Join-Path $WorkDirectory 'shim-bin') -File | Where-Object { $_.Name -ne 'shim.exe' })) { Copy-Item -LiteralPath $f.FullName -Destination $shims }
        [Environment]::SetEnvironmentVariable('OTTER_SHIM_LOG', $launches)
    }
    $state.ShimDirectory = $shims
    $state.ShimLog = $launches
}

$gates['no-powershell'] = {
    param($log)
    $counts = [ordered]@{}
    $failures = @()
    New-PowerShellShims $log
    $shims = $state.ShimDirectory
    # On Windows a full path (C:\Windows\System32\...\powershell.exe) passes
    # the shims by: as administrator, Windows' own process-creation audit
    # (event 4688, with the command line and the parent's name) records every
    # process started meanwhile, and each PowerShell whose parent is otter is
    # kept.
    $auditStart = $null
    $traced = @()
    if ($onWindows) {
        try {
            $auditLog = Join-Path $WorkDirectory 'audit.log'
            [System.IO.File]::WriteAllText($auditLog, '')
            [void](Invoke-Logged -FileName 'auditpol.exe' -Arguments @('/set', '/subcategory:{0CCE922B-69AE-11D9-BED3-505054503030}', '/success:enable') -Log $auditLog)
            New-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit' -Force -ErrorAction Stop | Out-Null
            Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\Audit' -Name 'ProcessCreationIncludeCmdLine_Enabled' -Value 1 -Type DWord -ErrorAction Stop
            [void](Invoke-Logged -FileName 'wevtutil.exe' -Arguments @('sl', 'Security', '/ms:1073741824') -Log $auditLog)
            $check = Invoke-Logged -FileName 'auditpol.exe' -Arguments @('/get', '/subcategory:{0CCE922B-69AE-11D9-BED3-505054503030}') -Log $auditLog
            if ($check.Stdout -notmatch 'Success') { throw "process-creation auditing did not turn on: $($check.Stdout.Trim())" }
            $auditStart = Get-Date
            $counts['processAudit'] = 'on'
        }
        catch { $counts['processAudit'] = "unavailable: $($_.Exception.Message)" }
    }
    $runs = @()
    try {
        $common = @{ OtterPs = $OtterPs; OtterNet = $otter; CandidateOnly = $true; ShimDirectory = $shims }
        $p = $common.Clone(); $p['IncludeEventPrograms'] = $true; $p['TimeoutSeconds'] = 20; $p['ReportPath'] = (Join-Path $reports 'no-powershell-programs.md')
        $p['Corpus'] = @((Join-Path $OtterPs 'conformance'), (Join-Path $OtterPs 'examples'), (Join-Path $repoRoot 'tests/compat'), (Join-Path $repoRoot 'tests/network'), (Join-Path $repoRoot 'tests/windows-state'), (Join-Path $repoRoot 'tests/desktop')) + @(New-FuzzFolders $log)
        $server = Start-NetworkServer $log
        try { $runs += Invoke-Tool 'compare.ps1' $p $log } finally { if ($server) { try { $server.Kill() } catch { } } }
        $p = $common.Clone(); $p['ReportPath'] = (Join-Path $reports 'no-powershell-web.md')
        $p['Corpus'] = @((Join-Path $repoRoot 'tests/web'), (Join-Path $OtterPs 'examples'), (Join-Path $OtterPs 'conformance'), (Join-Path $repoRoot 'tests/compat'), (Join-Path $OtterPs 'otter-docs/pages'))
        $runs += Invoke-Tool 'compare-web.ps1' $p $log
        $p = $common.Clone(); $p['ReportPath'] = (Join-Path $reports 'no-powershell-cli.md'); $p['Cases'] = @((Join-Path $repoRoot 'tests/cli'), (New-ProjectCases))
        $runs += Invoke-Tool 'compare-cli.ps1' $p $log
        $p = $common.Clone(); $p['ReportPath'] = (Join-Path $reports 'no-powershell-site.md')
        $runs += Invoke-Tool 'compare-site.ps1' $p $log
    }
    finally {
        if ($auditStart) {
            try {
                $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4688; StartTime = $auditStart } -ErrorAction Stop)
                $counts['processAudit.events'] = $events.Count
                foreach ($e in $events) {
                    $data = @{}
                    foreach ($d in ([xml]$e.ToXml()).Event.EventData.Data) { $data[$d.Name] = [string]$d.'#text' }
                    $newName = [System.IO.Path]::GetFileName($data['NewProcessName'])
                    if ($newName -notin @('powershell.exe', 'pwsh.exe')) { continue }
                    $traced += "$newName, parent $([System.IO.Path]::GetFileName($data['ParentProcessName'])): $($data['CommandLine'])"
                }
            }
            catch { $counts['processAudit'] = "unreadable: $($_.Exception.Message)" }
        }
    }
    $total = 0
    foreach ($run in $runs) { if ($run.Summary) { $total += [int]$run.Summary.total } }
    $counts['runs'] = $total
    $launchLines = @([System.IO.File]::ReadAllLines($state.ShimLog) | Where-Object { $_.Trim() })
    # Allowed: a program running a .ps1 file of its own (Commands.cs: the
    # PowerShell that runs otterPS, -NoProfile -File <file>.ps1), never otter.ps1.
    $allowed = @($launchLines | Where-Object { $_ -match ' -File "?[^"]*\.ps1\b' -and $_ -notmatch '[\\/ ]otter\.ps1\b' })
    $implicit = @($launchLines | Where-Object { $allowed -notcontains $_ })
    $tracedByOtter = @($traced | Where-Object { $_ -match ', parent otter(-desktop)?\.exe: ' })
    $tracedImplicit = @($tracedByOtter | Where-Object { -not ($_ -match ' -File "?[^"]*\.ps1\b' -and $_ -notmatch '[\\/ ]otter\.ps1\b') })
    $counts['launches'] = $launchLines.Count
    $counts['launches.allowed'] = $allowed.Count
    $counts['launches.implicit'] = $implicit.Count
    if ($onWindows) { $counts['traced.byOtter'] = $tracedByOtter.Count; $counts['traced.implicit'] = $tracedImplicit.Count }
    $launchReport = Join-Path $reports 'no-powershell-launches.md'
    $text = @('# PowerShell launches under the fake pwsh/powershell', '', "$total runs (programs with event programs, web pages, CLI cases, projects, fuzz, the site). Launches: $($launchLines.Count); a program running its own .ps1: $($allowed.Count); implicit: **$($implicit.Count)**.", '')
    $text += @($launchLines | ForEach-Object { "- $_" })
    if ($onWindows) { $text += @('', "## Every PowerShell started meanwhile (process-creation audit, event 4688): $($counts['processAudit'])", ''); $text += @($traced | ForEach-Object { "- $_" }) }
    [System.IO.File]::WriteAllText($launchReport, ($text -join "`n") + "`n", $utf8)
    foreach ($l in $implicit) { $failures += "implicit PowerShell launch: $l" }
    foreach ($l in $tracedImplicit) { $failures += "PowerShell started by otter (process trace): $l" }
    foreach ($run in $runs) { if (-not $run.Summary -or [int]$run.Summary.failed -gt 0) { $failures += @($run.Summary.failures | Where-Object { $_ }) } }
    if ($total -eq 0) { $failures += 'nothing ran' }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $launchReport) -Failures $failures `
        -Note 'Fake pwsh/powershell/powershell.exe first on otter-net''s PATH (and on Windows the process-creation audit, for a PowerShell started by its full path); a launch is allowed only for a program''s own .ps1 file (-File <file>.ps1, never otter.ps1).'
}

$gates['native-coverage'] = {
    param($log)
    $counts = [ordered]@{}
    $failures = @()
    $report = Join-Path $reports 'native-coverage.md'
    $run = Invoke-Tool 'native-coverage.ps1' @{ OtterPs = $OtterPs; OutFile = $report } $log
    if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)) { $failures += "tools/native-coverage.ps1: exit $($run.ExitCode)" }
    else {
        $text = [System.IO.File]::ReadAllText($report)
        $m = [regex]::Match($text, 'Places that can still hand a program to PowerShell \| \*\*(\d+)\*\*')
        if (-not $m.Success) { $failures += 'native coverage: no count of places that hand a program to PowerShell' }
        else { $counts['handsToPowerShell'] = [int]$m.Groups[1].Value; if ([int]$m.Groups[1].Value -ne 0) { $failures += "native coverage: $($m.Groups[1].Value) places hand a program to PowerShell" } }
        $nodes = [regex]::Match($text, 'native in otter-net \(a syntax class its parser builds\) \| \*\*(\d+)\*\*')
        $kinds = [regex]::Match($text, '\.\.\. with a node class in otterPS 1\.0\.1 \| (\d+)')
        if ($nodes.Success -and $kinds.Success) { $counts['nodeKinds'] = "$($nodes.Groups[1].Value) of $($kinds.Groups[1].Value) native"; if ($nodes.Groups[1].Value -ne $kinds.Groups[1].Value) { $failures += "node kinds: $($counts['nodeKinds'])" } }
        $messages = [regex]::Match($text, 'missing \| \*\*(\d+)\*\* / (\d+) / \*\*(\d+)\*\*')
        if ($messages.Success) { $counts['messages.missing'] = [int]$messages.Groups[3].Value; if ([int]$messages.Groups[3].Value -ne 0) { $failures += "messages missing from otter-net: $($messages.Groups[3].Value)" } }
        $gaps = [regex]::Match($text, 'reported as Otter''s internal error[^|]*\| (\d+)')
        if ($gaps.Success) { $counts['gaps.internalError'] = [int]$gaps.Groups[1].Value }
    }
    # Every process otter-net's source can start, with what it starts. One
    # that names PowerShell must be a program's own .ps1 (Commands.cs) or a
    # terminal a desktop program opens (DesktopCommand.cs).
    $sites = New-Object System.Collections.Generic.List[string]
    $suspicious = New-Object System.Collections.Generic.List[string]
    $allowedPowerShell = @{ 'src/Otter.Runtime/Commands.cs' = 'a program running a .ps1 file runs it with PowerShell, as otterPS does (New-OtterProcessStartInfo)'; 'src/Otter.Cli/DesktopCommand.cs' = 'the terminal of `otter desktop` offers the PowerShell installed, as a shell the user types into' }
    foreach ($file in @(Get-ChildItem (Join-Path $repoRoot 'src') -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })) {
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        $relative = Get-RelativePath $file.FullName $repoRoot
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch '\b[Pp]rocess\.Start\(|new ProcessStartInfo' -or $lines[$i].TrimStart().StartsWith('//')) { continue }
            $from = [Math]::Max(0, $i - 25); $to = [Math]::Min($lines.Count - 1, $i + 10)
            $context = ($lines[$from..$to] | Where-Object { -not $_.TrimStart().StartsWith('//') }) -join "`n"
            $mentions = $context -match '(?i)powershell|pwsh|OTTER_POWERSHELL|otter\.ps1'
            $sites.Add("$relative`:$($i + 1)$(if ($mentions) { ' (names PowerShell)' })")
            if ($mentions -and -not $allowedPowerShell.ContainsKey($relative)) { $suspicious.Add("$relative`:$($i + 1)") }
        }
    }
    $counts['processSites'] = $sites.Count
    $counts['processSites.namingPowerShell'] = @($sites | Where-Object { $_ -like '*(names PowerShell)' }).Count
    $counts['processSites.unexplained'] = $suspicious.Count
    $sitesReport = Join-Path $reports 'process-sites.md'
    $text = @('# Every process otter-net starts (src/**/*.cs)', '', 'A site that names PowerShell is allowed only where a program asks for it:', '')
    foreach ($key in $allowedPowerShell.Keys) { $text += "- ``$key``: $($allowedPowerShell[$key])" }
    $text += ''; $text += @($sites | ForEach-Object { "- $_" })
    [System.IO.File]::WriteAllText($sitesReport, ($text -join "`n") + "`n", $utf8)
    foreach ($s in $suspicious) { $failures += "a process start that names PowerShell: $s" }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, $report, $sitesReport) -Failures $failures
}

$gates['desktop'] = {
    param($log)
    $counts = [ordered]@{}
    if (-not $onWindows) { return New-GateResult -Passed $true -Counts ([ordered]@{ ran = $false }) -Note 'Windows only: desktop windows (WPF) exist only on Windows.' }
    if ($SkipDesktop) { return New-GateResult -Passed $false -Counts ([ordered]@{ ran = $false }) -Failures @('skipped with -SkipDesktop') }
    if (-not $referenceHostPresent) { return New-GateResult -Passed $false -Counts ([ordered]@{ ran = $false }) -Failures @('no Windows PowerShell 5.1 for the otterPS reference') }
    $harness = Join-Path $WorkDirectory 'harness'
    $built = Invoke-Logged -FileName 'dotnet' -Arguments @('build', 'tests/desktop/Harness/Harness.csproj', '-c', 'Release', '-o', $harness, '--nologo') -Log $log
    $failures = @()
    if ($built.ExitCode -ne 0) { return New-GateResult -Passed $false -Counts $counts -Evidence @($log) -Failures @('the desktop harness did not build') }
    $out = Join-Path $reports 'desktop'
    $run = Invoke-Logged -FileName (Join-Path $harness 'desktop-harness.exe') -Arguments @('--otterps', (Join-Path $OtterPs 'otter.ps1'), '--otter', $otter, '--tests', 'tests/desktop', '--out', $out) -Log $log
    $m = [regex]::Match($run.Stdout, '(\d+) of (\d+) test programs identical')
    if ($m.Success) { $counts['programs.total'] = [int]$m.Groups[2].Value; $counts['programs.identical'] = [int]$m.Groups[1].Value }
    if ($run.ExitCode -ne 0 -or -not $m.Success) { $failures += "desktop harness: exit $($run.ExitCode)"; $failures += @([regex]::Matches($run.Stdout, '(?m)^\| `([^`]+)` \| \*\*different\*\* \| (.*) \|$') | ForEach-Object { "$($_.Groups[1].Value): $($_.Groups[2].Value)" }) }
    # Without otter-desktop, a window program asks for a reinstall (no PowerShell).
    $folder = Join-Path $WorkDirectory 'no-desktop'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $folder 'made.ot'), "create window into w`nsay `"made`" w`n")
    $missing = Invoke-Logged -FileName $otter -Arguments @('run', 'made.ot') -Log $log -WorkingDirectory $folder -Environment @{ OTTER_DESKTOP = (Join-Path $folder 'missing\otter-desktop.exe'); OTTER_NET_TRACE = '1' } -TimeoutSeconds 60
    $counts['missingHost.exit'] = $missing.ExitCode
    if ("$($missing.ExitCode)" -ne '1' -or ($missing.Stderr -notmatch 'otter-desktop.exe, which is missing from this installation')) { $failures += "without otter-desktop: exit $($missing.ExitCode), $($missing.Stderr.Trim())" }
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence @($log, (Join-Path $out 'summary.md')) -Failures $failures
}

$gates['machine-state'] = {
    param($log)
    $counts = [ordered]@{}
    if (-not $onWindows) { return New-GateResult -Passed $true -Counts ([ordered]@{ ran = $false }) -Note 'Windows only.' }
    if (-not $IncludeMachineState) { return New-GateResult -Passed $false -Counts ([ordered]@{ ran = $false }) -Failures @('not run: it changes the machine (printers, tasks, services, event logs); pass -IncludeMachineState on a CI runner') }
    if (-not $referenceHostPresent) { return New-GateResult -Passed $false -Counts ([ordered]@{ ran = $false }) -Failures @('no Windows PowerShell 5.1 for the otterPS reference') }
    $failures = @()
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    [void](Invoke-Logged -FileName $powershell -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/windows-state.ps1') -Log $log)
    $corpus = @(Get-ChildItem (Join-Path $repoRoot 'tests/windows-state') -Filter '*.ot' | ForEach-Object FullName) + @(Get-ChildItem (Join-Path $repoRoot 'tests/compat') -Filter '*.ot' | Where-Object Name -match '^(sys|t3|c4)_' | ForEach-Object FullName)
    $run = Invoke-Tool 'compare.ps1' @{ OtterPs = $OtterPs; OtterNet = $otter; Corpus = $corpus; NativeOnly = $true; ReportPath = (Join-Path $reports 'machine-state.md') } $log
    Add-Counts $counts 'programs' $run.Summary
    if (-not (Test-ToolPassed $run)) { $failures += "machine state: exit $($run.ExitCode)"; $failures += @($run.Summary.failures) }
    # The user's regional formats reach a few outputs (SPEC-QUESTIONS W5-4).
    $cultureCorpus = @(Get-ChildItem (Join-Path $repoRoot 'tests/windows-state') -Filter '*.ot' | ForEach-Object FullName) + @(Get-ChildItem (Join-Path $repoRoot 'tests/compat') -Filter '*.ot' | Where-Object Name -in 'c4_interpreter_dotnet.ot', 'c4_bytes.ot', 'sys_desktop.ot', 't3_system_tasks_printers.ot', 'numbers.ot' | ForEach-Object FullName)
    $evidence = @($log, (Join-Path $reports 'machine-state.md'))
    foreach ($culture in 'de-DE', 'fi-FI') {
        [void](Invoke-Logged -FileName $powershell -Arguments @('-NoProfile', '-Command', "Set-Culture $culture") -Log $log)
        $report = Join-Path $reports "machine-state-$culture.md"
        $cultureRun = Invoke-Tool 'compare.ps1' @{ OtterPs = $OtterPs; OtterNet = $otter; Corpus = $cultureCorpus; NativeOnly = $true; ReportPath = $report } $log
        Add-Counts $counts $culture $cultureRun.Summary
        if (-not (Test-ToolPassed $cultureRun)) { $failures += "machine state under ${culture}: exit $($cultureRun.ExitCode)"; $failures += @($cultureRun.Summary.failures) }
        $evidence += $report
    }
    [void](Invoke-Logged -FileName $powershell -Arguments @('-NoProfile', '-Command', 'Set-Culture en-US') -Log $log)
    return New-GateResult -Passed ($failures.Count -eq 0) -Counts $counts -Evidence $evidence -Failures $failures
}

# --------------------------------------------------------------------------
# Run them
# --------------------------------------------------------------------------
$selected = @($gates.Keys | Where-Object { $Only.Count -eq 0 -or $Only -contains $_ -or $_ -eq 'build' })
foreach ($name in $selected) {
    $log = Join-Path $logs "$name.log"
    [System.IO.File]::WriteAllText($log, '')
    $start = (Get-Date).ToUniversalTime()
    Write-Host ("[{0}] {1} ..." -f $start.ToString('HH:mm:ss'), $name) -NoNewline
    if ($name -ne 'build' -and -not $state.BuildPassed -and -not (Test-Path -LiteralPath $otter)) {
        $result = New-GateResult -Passed $false -Counts ([ordered]@{}) -Failures @('not run: the build failed')
    }
    else {
        Push-Location -LiteralPath $repoRoot
        try { $result = & $gates[$name] $log }
        catch { $result = New-GateResult -Passed $false -Counts ([ordered]@{}) -Evidence @($log) -Failures @("the gate failed to run: $($_.Exception.Message) at $($_.InvocationInfo.PositionMessage)") }
        finally { Pop-Location }
    }
    $end = (Get-Date).ToUniversalTime()
    $record = [ordered]@{ gate = $name; startUtc = $start.ToString('o'); endUtc = $end.ToString('o'); seconds = [Math]::Round(($end - $start).TotalSeconds, 1) }
    foreach ($key in $result.Keys) { $record[$key] = $result[$key] }
    $records.Add($record)
    $shownCounts = ($result.counts.Keys | ForEach-Object { "$_=$($result.counts[$_])" }) -join ', '
    if ($result.passed) { Write-Host (" PASS in {0:N0}s ({1})" -f ($end - $start).TotalSeconds, $shownCounts) -ForegroundColor Green }
    else {
        Write-Host (" FAIL in {0:N0}s ({1})" -f ($end - $start).TotalSeconds, $shownCounts) -ForegroundColor Red
        foreach ($f in ($result.failures | Select-Object -First 15)) { Write-Host "    $f" -ForegroundColor Red }
    }
}

# repository-clean: the gates must not change the checkout.
$porcelainAfter = Get-Porcelain $repoRoot
$changed = @(@($porcelainAfter | Where-Object { $porcelainBefore -notcontains $_ }) + @($porcelainBefore | Where-Object { $porcelainAfter -notcontains $_ }))
$cleanLog = Join-Path $logs 'repository-clean.log'
[System.IO.File]::WriteAllLines($cleanLog, [string[]](@("git status --porcelain --untracked-files=all: $($porcelainBefore.Count) path(s) before the gates, $($porcelainAfter.Count) after.") + @($changed | ForEach-Object { "  changed: $_" })), $utf8)
$cleanResult = New-GateResult -Passed ($changed.Count -eq 0) -Counts ([ordered]@{ pathsChanged = $changed.Count }) -Evidence @($cleanLog) -Failures @($changed | ForEach-Object { "changed by the gates: $_" })
$now = (Get-Date).ToUniversalTime().ToString('o')
$record = [ordered]@{ gate = 'repository-clean'; startUtc = $now; endUtc = $now; seconds = 0 }
foreach ($key in $cleanResult.Keys) { $record[$key] = $cleanResult[$key] }
$records.Add($record)
Write-Host "[$(Get-Date -Format HH:mm:ss)] repository-clean ... $(if ($changed.Count -eq 0) { 'PASS' } else { "FAIL ($($changed.Count) paths)" })"

$allRun = ($Only.Count -eq 0)
$allPassed = (@($records | Where-Object { -not $_.passed }).Count -eq 0)
$kind = 'development-run'
if ($allRun -and -not $FrameworkDependent) { $kind = 'release-certification' }
$dotnetVersion = ''
try { $dotnetVersion = ((& dotnet --version) | Out-String).Trim() } catch { }
$platformRecord = [ordered]@{
    kind = $kind
    commit = $sha
    subject = $subject
    platform = $Platform
    cleanDetachedCheckout = $true
    checkout = $repoRoot
    reference = [ordered]@{ name = 'otterPS'; version = $otterPsVersion; commit = $otterPsCommit; comparedDirectly = $withReference; host = $(if ($withReference) { 'Windows PowerShell 5.1' } else { 'none here: compared with the Windows capture (-Combine)' }) }
    host = [ordered]@{ os = [System.Environment]::OSVersion.VersionString; powershell = "$($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))"; dotnetSdk = $dotnetVersion; machine = [System.Environment]::MachineName
        githubRun = $(if ($env:GITHUB_RUN_ID) { "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID (attempt $env:GITHUB_RUN_ATTEMPT)" } else { $null }); githubJob = $env:GITHUB_JOB }
    fuzz = [ordered]@{ count = $FuzzCount; seed = $FuzzSeed; variants = $FuzzVariants }
    only = @($Only)
    gates = $records.ToArray()
    allGatesPassed = $allPassed
    note = 'Only kind=release-certification with allGatesPassed=true on every platform, and the cross-platform comparison of -Combine, is release evidence.'
}
$recordJson = Join-Path (Join-Path $OutputDirectory $sha) "$Platform.json"
[System.IO.File]::WriteAllText($recordJson, ($platformRecord | ConvertTo-Json -Depth 8), $utf8)

$md = New-Object System.Collections.Generic.List[string]
$md.Add("# otter-net certification: $Platform")
$md.Add('')
$md.Add('| | |'); $md.Add('|---|---|')
$md.Add("| Kind | **$kind** |")
$md.Add("| Commit | ``$sha`` $subject |")
$md.Add("| Checkout | clean, detached, HEAD = commit |")
$md.Add("| Reference | otterPS $otterPsVersion (``$otterPsCommit``), $($platformRecord.reference.host) |")
$md.Add("| Host | $($platformRecord.host.os); PowerShell $($platformRecord.host.powershell); .NET SDK $dotnetVersion |")
if ($platformRecord.host.githubRun) { $md.Add("| Run | $($platformRecord.host.githubRun) |") }
$md.Add("| Fuzz | $FuzzCount generated + $FuzzCount mutated, $FuzzVariants mutations and case variants per conformance/example program, seed $FuzzSeed |")
$md.Add("| Result | **$(if ($allPassed) { 'all gates passed' } else { 'FAILED' })** |")
$md.Add('')
$md.Add('| Gate | Result | Counts | Seconds | Evidence |')
$md.Add('|---|---|---|---:|---|')
foreach ($r in $records) {
    $c = ($r.counts.Keys | ForEach-Object { "$_ $($r.counts[$_])" }) -join '; '
    $tick = [string][char]96
    $evidenceCell = (@($r.evidence) | ForEach-Object { $tick + $_ + $tick }) -join '<br>'
    $result = 'pass'; if (-not $r.passed) { $result = '**FAIL**' }
    $md.Add("| $($r.gate) | $result | $($c -replace '\|', '\|') | $($r.seconds) | $evidenceCell |")
}
$failed = @($records | Where-Object { -not $_.passed })
if ($failed.Count -gt 0) {
    $md.Add(''); $md.Add('## Failures'); $md.Add('')
    foreach ($r in $failed) { $md.Add("### $($r.gate)"); $md.Add(''); foreach ($f in $r.failures) { $md.Add("- $($f -replace '\|', '\|')") }; $md.Add('') }
}
$notes = @($records | Where-Object { $_.note })
if ($notes.Count -gt 0) { $md.Add(''); $md.Add('## Notes'); $md.Add(''); foreach ($r in $notes) { $md.Add("- **$($r.gate)**: $($r.note)") } }
[System.IO.File]::WriteAllText((Join-Path (Join-Path $OutputDirectory $sha) "$Platform.md"), ($md -join "`n") + "`n", $utf8)

Remove-Item -LiteralPath $generated -Recurse -Force -ErrorAction SilentlyContinue
Write-Host ''
Write-Host "Record: $recordJson"
if ($allPassed) { Write-Host "All $($records.Count) gates passed on $Platform ($kind)." -ForegroundColor Green; exit 0 }
Write-Host "Failing gates on ${Platform}: $(($failed | ForEach-Object { $_.gate }) -join ', ')" -ForegroundColor Red
exit 1
