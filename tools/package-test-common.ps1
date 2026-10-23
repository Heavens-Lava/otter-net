# Shared by tools\test-package.ps1, tools\test-coexistence.ps1 and
# tools\test-desktop-nodotnet.ps1 (dot-sourced). Windows PowerShell 5.1 or
# PowerShell 7, on a disposable Windows runner.

# The PATH a new terminal gets, as Windows builds it from the registry: the
# machine's, then the user's (each expanded).
function Get-NewTerminalPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    @(("$machine;$user" -split ';') | Where-Object { $_ } | ForEach-Object { [Environment]::ExpandEnvironmentVariables($_) }) -join ';'
}

# Runs a .cmd script in a brand-new process that Task Scheduler starts in the
# user's interactive session, with an environment Windows builds afresh from
# the registry - what a terminal opened now from the Start menu gets, and
# nothing inherited from this script's process. Returns its output lines, or
# $null when Task Scheduler cannot run it (then callers use
# Get-NewTerminalPath alone).
function Invoke-InNewSession {
    param([string]$Script)
    $id = 'otter-net-test-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $folder = Join-Path ([System.IO.Path]::GetTempPath()) $id
    New-Item -ItemType Directory -Path $folder | Out-Null
    $cmd = Join-Path $folder 'run.cmd'
    $out = Join-Path $folder 'out.txt'
    $done = Join-Path $folder 'done.txt'
    [System.IO.File]::WriteAllText($cmd, "@echo off`r`n$Script`r`necho done> `"$done`"`r`n")
    try {
        $action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\cmd.exe" -Argument "/d /c `"`"$cmd`" > `"$out`" 2>&1`"" -WorkingDirectory $folder
        $principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $id -Action $action -Principal $principal -Force -ErrorAction Stop | Out-Null
        Start-ScheduledTask -TaskName $id -ErrorAction Stop
        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        while (-not (Test-Path -LiteralPath $done) -and $clock.Elapsed.TotalSeconds -lt 60) { Start-Sleep -Milliseconds 250 }
        if (-not (Test-Path -LiteralPath $done)) { return $null }
        Start-Sleep -Milliseconds 200
        @(Get-Content -LiteralPath $out)
    } catch {
        Write-Host "(Task Scheduler could not start a new session process: $($_.Exception.Message))"
        $null
    } finally {
        Unregister-ScheduledTask -TaskName $id -Confirm:$false -ErrorAction SilentlyContinue
        Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
    }
}

# Only what a terminal can run: where.exe also lists files without an
# extension (otterPS's folder has `otter`, its launcher for macOS and Linux,
# beside otter.cmd), which cmd and PowerShell never start for `otter`.
function Select-Runnable {
    param([string[]]$Paths)
    $extensions = @(($env:PATHEXT, '.COM;.EXE;.BAT;.CMD' | Where-Object { $_ } | Select-Object -First 1) -split ';' | Where-Object { $_ } | ForEach-Object { $_.ToLowerInvariant() })
    @($Paths | Where-Object { $_ -and $extensions -contains [System.IO.Path]::GetExtension($_).ToLowerInvariant() })
}

# Every `otter` (or -Name, e.g. otter-net) a new terminal finds, in order
# (where.exe), checked two ways:
# where.exe with the PATH composed from the registry, and where.exe in a new
# process started by Task Scheduler. Returns an object with Found (the list),
# and Session (the new process's list, or $null when it could not run).
function Get-NewTerminalOtter {
    param([string]$Name = 'otter')
    $path = Get-NewTerminalPath
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "$env:SystemRoot\System32\where.exe"
    $psi.Arguments = $Name
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['PATH'] = $path
    $process = [System.Diagnostics.Process]::Start($psi)
    $text = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    $found = @(Select-Runnable @($text -split "`r?`n" | Where-Object { $_ }))
    $session = Invoke-InNewSession "where $Name"
    if ($null -ne $session) { $session = @(Select-Runnable @($session | Where-Object { $_ -and $_ -notmatch '^INFO:' })) }
    [pscustomobject]@{ Found = $found; Session = $session }
}

# Which Otter a path is.
function Get-OtterKind {
    param([string]$Path)
    if (-not $Path) { return 'none' }
    if ($Path -like '*\Programs\otter-net\*' -or $Path -like '*\otter-net-*\otter-net.exe') { return 'otter-net' }
    if ($Path -like '*\Otter\1.0.1\otter.cmd' -or $Path -like '*\Programs\Otter\otter.cmd') { return 'otterPS' }
    "other ($Path)"
}
