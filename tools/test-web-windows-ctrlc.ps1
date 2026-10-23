<#
.SYNOPSIS
Windows only: graceful stop of the Otter Web runtime on a real console Ctrl+C
(docs/web/RUNTIME_CONTRACT.md §8).

.DESCRIPTION
otter-net runs in a console of its own (hidden window). A helper process
attaches to that console and sends CTRL_C_EVENT, as pressing Ctrl+C does.
Checked: a request in progress (a 3-second handler) is answered 200, the
process exits with 0, and the server no longer accepts connections.
Standard output and error are not captured (a new console cannot be
redirected), so the "Otter Web: stopped." line is not checked here.

If the environment cannot deliver console control events, the script says
so and exits 2 (not passed).

.EXAMPLE
pwsh tools/test-web-windows-ctrlc.ps1 -OtterNet artifacts/new/otter.exe
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OtterNet, [string]$ReportPath = '')

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { Write-Host 'Windows only.'; exit 2 }
$root = Split-Path -Parent $PSScriptRoot
$OtterNet = (Resolve-Path $OtterNet).Path

$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-ctrlc-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
Copy-Item -Recurse (Join-Path $root 'tests/web-runtime/basic') $work
$main = Join-Path $work 'main.ot'
[System.IO.File]::WriteAllText($main, ([System.IO.File]::ReadAllText($main) -replace '18080', "$port"))

$results = @()
function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $script:results += [pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail }
    Write-Host ("{0}  {1}{2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $(if (-not $Ok -and $Detail) { " - $Detail" } else { '' }))
}

# A new console for otter-net (hidden), so the Ctrl+C reaches it alone.
$otter = Start-Process -FilePath $OtterNet -ArgumentList @('run', "`"$work`"") -WorkingDirectory $work -WindowStyle Hidden -PassThru
$client = [System.Net.Http.HttpClient]::new(); $client.Timeout = [TimeSpan]::FromSeconds(30)
$up = $false
for ($i = 0; $i -lt 120 -and -not $up; $i++) {
    try { $up = ($client.GetStringAsync("http://127.0.0.1:$port/hello").GetAwaiter().GetResult() -eq 'Hello, stranger') } catch { Start-Sleep -Milliseconds 250 }
}
if (-not $up) { Write-Host 'The server did not start.'; try { $otter.Kill() } catch { }; exit 1 }

# A request in progress when Ctrl+C arrives.
$slow = $client.GetAsync("http://127.0.0.1:$port/slow")
Start-Sleep -Milliseconds 500

# The helper: leave its own console, attach to otter-net's, ignore Ctrl+C
# itself, send CTRL_C_EVENT (0) to every process on that console (group 0).
$helper = @"
Add-Type -Namespace W -Name K -MemberDefinition '[DllImport("kernel32.dll")] public static extern bool FreeConsole(); [DllImport("kernel32.dll")] public static extern bool AttachConsole(uint pid); [DllImport("kernel32.dll")] public static extern bool SetConsoleCtrlHandler(System.IntPtr h, bool add); [DllImport("kernel32.dll")] public static extern bool GenerateConsoleCtrlEvent(uint ev, uint group);'
[void][W.K]::FreeConsole()
if (-not [W.K]::AttachConsole($($otter.Id))) { exit 3 }
[void][W.K]::SetConsoleCtrlHandler([System.IntPtr]::Zero, `$true)
if (-not [W.K]::GenerateConsoleCtrlEvent(0, 0)) { exit 4 }
Start-Sleep -Milliseconds 500
exit 0
"@
$encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($helper))
$sender = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList @('-NoProfile', '-EncodedCommand', $encoded) -WindowStyle Hidden -PassThru -Wait
if ($sender.ExitCode -ne 0) {
    Write-Host "UNAVAILABLE: this environment could not deliver a console Ctrl+C (helper exit $($sender.ExitCode)); graceful stop on Windows is NOT verified here."
    try { $otter.Kill() } catch { }
    if ($ReportPath) { [System.IO.File]::WriteAllText($ReportPath, "Windows Ctrl+C: not verified (the environment could not deliver a console control event, helper exit $($sender.ExitCode)).`n") }
    exit 2
}

$answered = $slow.Wait(20000)
$status = if ($answered -and $slow.IsCompletedSuccessfully) { [int]$slow.Result.StatusCode } else { 0 }
$body = if ($status -eq 200) { $slow.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult() } else { '' }
Check 'Ctrl+C: the request in progress is answered (§8)' ($status -eq 200 -and $body -eq 'slow done') "status $status [$body]"
$exited = $otter.WaitForExit(20000)
Check 'Ctrl+C: otter-net exits with 0 (§8)' ($exited -and $otter.ExitCode -eq 0) "exited=$exited code=$(if ($exited) { $otter.ExitCode })"
$refused = $false
try { $tcp = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port); $tcp.Dispose() } catch { $refused = $true }
Check 'Ctrl+C: the server no longer accepts connections (§8)' $refused
if (-not $exited) { try { $otter.Kill() } catch { } }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host "$($results.Count - $failed.Count) passed, $($failed.Count) failed"
if ($ReportPath) {
    $lines = @("Windows console Ctrl+C ($([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)): $($results.Count - $failed.Count) passed, $($failed.Count) failed", '')
    foreach ($r in $results) { $lines += "- $(if ($r.Ok) { 'pass' } else { 'FAIL' }): $($r.Name)$(if (-not $r.Ok) { ' - ' + $r.Detail })" }
    [System.IO.File]::WriteAllLines($ReportPath, $lines)
}
if ($failed.Count) { exit 1 }
