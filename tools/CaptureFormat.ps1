<#
Dot-sourced by tools/compare.ps1, compare-web.ps1, compare-cli.ps1 and
compare-site.ps1: the -CaptureDirectory format, so what one machine saw can
be compared with what another saw (tools/compare-captures.ps1), and the
-ShimDirectory hook of the no-implicit-PowerShell check.

A capture is one text file per case, <CaptureDirectory>/<side>/<id>.txt,
side "reference" (otterPS) or "candidate" (otter-net):

    exit: <exit code, or timeout>
    --- stdout
    <standard output>
    --- stderr
    <standard error, otter-net's trace lines removed>
    --- files
    <relative/path> <size> <SHA-256 of the bytes> <SHA-256 with CRLF made LF>
    <relative/folder>/

Line endings of the output are made "\n", and the case's folder (which
differs from machine to machine) becomes <work>; nothing else is changed.
With -KeepFiles, the text files a case left behind (generated web pages)
are saved too, under <side>/<id>.files/, so a difference can be read.

Works in Windows PowerShell 5.1 and PowerShell 7.
#>

function ConvertTo-CaptureId {
    param([string]$Text)
    $id = ($Text -replace '[\\/:]+', '__' -replace '[^A-Za-z0-9_.\-]+', '_')
    # Two cases whose names differ only in capitals ("run ..." and "RUN ...")
    # must not share a file on a case-insensitive file system (macOS, Windows):
    # a name with capitals gets a hash of itself (the same on every platform).
    if ($id -cne $id.ToLowerInvariant()) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $hash = [System.BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($id))).Replace('-', '').Substring(0, 8).ToLowerInvariant() }
        finally { $sha.Dispose() }
        $id = "$id~$hash"
    }
    return $id
}

function ConvertTo-CaptureText {
    param([string]$Text, [string[]]$WorkPaths)
    if ($null -eq $Text) { return '' }
    $result = $Text -replace "`r`n", "`n"
    foreach ($path in @($WorkPaths | Where-Object { $_ } | Sort-Object Length -Descending)) {
        # On macOS the temporary folder (/var/...) is a link to /private/var/...,
        # and messages quote the resolved path.
        $result = $result.Replace('/private' + $path, '<work>').Replace($path, '<work>')
        # Windows paths are sometimes written with / (Otter's own suggestions).
        if ($path.Contains('\')) { $result = $result.Replace($path.Replace('\', '/'), '<work>') }
    }
    return $result
}

function Get-CaptureFileLines {
    param([string]$Folder, [string[]]$Ignore = @())
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $lines = foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -Force -ErrorAction SilentlyContinue)) {
            $relative = $item.FullName.Substring($Folder.Length + 1) -replace '\\', '/'
            $ignored = $false
            foreach ($pattern in $Ignore) { if ($relative -like $pattern) { $ignored = $true } }
            if ($ignored) { continue }
            if ($item.PSIsContainer) { "$relative/"; continue }
            try {
                $bytes = [System.IO.File]::ReadAllBytes($item.FullName)
                $raw = [System.BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '')
                $lf = [System.Collections.Generic.List[byte]]::new($bytes.Length)
                for ($i = 0; $i -lt $bytes.Length; $i++) {
                    if ($bytes[$i] -eq 13 -and $i + 1 -lt $bytes.Length -and $bytes[$i + 1] -eq 10) { continue }
                    $lf.Add($bytes[$i])
                }
                $normalized = [System.BitConverter]::ToString($sha.ComputeHash($lf.ToArray())).Replace('-', '')
                "$relative $($bytes.Length) $raw $normalized"
            }
            catch { "$relative unreadable" }
        }
        # Ordinal: Sort-Object orders by culture, and differently in 5.1 and 7.
        $sorted = [string[]]@($lines)
        [System.Array]::Sort($sorted, [System.StringComparer]::Ordinal)
        return $sorted
    }
    finally { $sha.Dispose() }
}

function Write-Capture {
    param(
        [string]$Directory, [string]$Side, [string]$Id,
        $ExitCode, [string]$Stdout, [string]$Stderr,
        [string]$Folder, [string[]]$WorkPaths, [string[]]$Ignore = @(),
        [switch]$KeepFiles,
        # With -KeepFiles: files identical to the same file here (the case's
        # input) are not kept.
        [string]$InputFolder = ''
    )
    if (-not $Directory) { return }
    $sideFolder = Join-Path $Directory $Side
    if (-not (Test-Path -LiteralPath $sideFolder)) { New-Item -ItemType Directory -Force -Path $sideFolder | Out-Null }
    $id = ConvertTo-CaptureId $Id
    $files = @()
    if ($Folder -and (Test-Path -LiteralPath $Folder)) { $files = Get-CaptureFileLines -Folder $Folder -Ignore $Ignore }
    $text = "exit: $ExitCode`n--- stdout`n" + (ConvertTo-CaptureText $Stdout $WorkPaths) +
        "`n--- stderr`n" + (ConvertTo-CaptureText $Stderr $WorkPaths).Trim() +
        "`n--- files`n" + (@($files) -join "`n") + "`n"
    [System.IO.File]::WriteAllText((Join-Path $sideFolder "$id.txt"), $text, (New-Object System.Text.UTF8Encoding($false)))
    if ($KeepFiles -and $Folder -and (Test-Path -LiteralPath $Folder)) {
        $keep = Join-Path $sideFolder "$id.files"
        foreach ($item in @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Force -ErrorAction SilentlyContinue)) {
            if ($item.Length -gt 2MB -or $item.Extension -notin @('.html', '.js', '.css', '.json', '.txt', '.md', '.csv', '.xml')) { continue }
            $relative = $item.FullName.Substring($Folder.Length + 1)
            if ($InputFolder) {
                $original = Join-Path $InputFolder $relative
                if ((Test-Path -LiteralPath $original -PathType Leaf) -and ((Get-Item -LiteralPath $original).Length -eq $item.Length) -and
                    ([System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($original)) -eq [System.Convert]::ToBase64String([System.IO.File]::ReadAllBytes($item.FullName)))) { continue }
            }
            $target = Join-Path $keep $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $item.FullName -Destination $target -Force
        }
    }
}

# The environment for a candidate run under the no-implicit-PowerShell check:
# the fake pwsh/powershell first on PATH, and the case's name for their log.
function Add-ShimEnvironment {
    param([hashtable]$Environment, [string]$ShimDirectory, [string]$CaseName)
    if (-not $ShimDirectory) { return $Environment }
    $copy = @{}
    foreach ($key in $Environment.Keys) { $copy[$key] = $Environment[$key] }
    $copy['PATH'] = $ShimDirectory + [System.IO.Path]::PathSeparator + [System.Environment]::GetEnvironmentVariable('PATH')
    $copy['OTTER_TEST_PROGRAM'] = $CaseName
    return $copy
}

# Did otter-net report a gap in itself or crash? (A candidate-only run has no
# reference to compare with; these are failures on their own.)
function Get-CandidateFailure {
    param([string]$Stderr)
    if ($Stderr -match 'otter-net native: a gap in otter-net: (.*)') { return "a gap in otter-net: $($Matches[1].Trim())" }
    if ($Stderr -match 'otter-net: internal error,? (.*)') { return "otter-net internal error: $($Matches[1].Trim())" }
    if ($Stderr -match 'Unhandled exception\.? ?(.*)') { return "otter-net crashed: $($Matches[1].Trim())" }
    if ($Stderr -match 'otter-net fallback') { return 'handed to the PowerShell Otter' }
    return $null
}
