<#
.SYNOPSIS
Black-box tests of the Otter Web runtime 0.1 (docs/web/RUNTIME_CONTRACT.md).

.DESCRIPTION
Starts otter-net on the projects in tests/web-runtime (each copy gets a free
loopback port in place of 18080) and checks every rule of the contract over
real HTTP: routes, the request object, responses, errors, limits, slow
clients, concurrency, graceful stop, refusals, and that Otter 1.0.1's
behaviour is unchanged without the opt-in. PowerShell 7 on every platform;
otter-net itself never starts PowerShell.

.EXAMPLE
pwsh tools/test-web-runtime.ps1 -OtterNet artifacts/publish/osx-arm64/otter
.EXAMPLE
pwsh tools/test-web-runtime.ps1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll -ReportPath web-runtime.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OtterNet,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$OtterNet = (Resolve-Path $OtterNet).Path
$cases = Join-Path $root 'tests/web-runtime'
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('otter-web-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $scratch | Out-Null
$results = [System.Collections.Generic.List[object]]::new()
$skipped = [System.Collections.Generic.List[string]]::new()
$client = [System.Net.Http.HttpClient]::new([System.Net.Http.HttpClientHandler]@{ AllowAutoRedirect = $false; UseCookies = $false })
$client.Timeout = [TimeSpan]::FromSeconds(20)

function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $results.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    Write-Host ("{0}  {1}{2}" -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $(if (-not $Ok -and $Detail) { " - $Detail" } else { '' }))
}

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop(); $port
}

# A copy of a case project with its own port.
function New-Case([string]$Name, [int]$Port) {
    $target = Join-Path $scratch "$Name-$Port"
    Copy-Item -Recurse (Join-Path $cases $Name) $target
    foreach ($file in Get-ChildItem $target -Filter '*.ot') {
        [System.IO.File]::WriteAllText($file.FullName, ([System.IO.File]::ReadAllText($file.FullName) -replace '18080', "$Port"))
    }
    $target
}

# Output capture on .NET's own threads (PowerShell event actions run later,
# so a quick exit could lose the last lines).
Add-Type -TypeDefinition @"
using System.Diagnostics;
using System.Text;
public sealed class OtterCapture
{
    readonly StringBuilder text = new StringBuilder();
    public void Attach(Process process, bool error)
    {
        if (error) process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
        else process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (text) text.AppendLine(e.Data); };
    }
    public override string ToString() { lock (text) return text.ToString(); }
}
"@

# otter-net as a child process (not a shell job, so it receives SIGINT).
function Start-Otter([string[]]$Arguments, [string]$Directory) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    if ($OtterNet -like '*.dll') { $psi.FileName = 'dotnet'; $psi.ArgumentList.Add($OtterNet) } else { $psi.FileName = $OtterNet }
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $out = [OtterCapture]::new(); $err = [OtterCapture]::new()
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $out.Attach($process, $false); $err.Attach($process, $true)
    [void]$process.Start()
    $process.BeginOutputReadLine(); $process.BeginErrorReadLine()
    $process.StandardInput.Close()
    [pscustomobject]@{ Process = $process; Out = $out; Err = $err }
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 100 }
    return [bool](& $Condition)
}

function Stop-Otter($Run) {
    if (-not $Run.Process.HasExited) { try { $Run.Process.Kill($true) } catch { } }
    [void]$Run.Process.WaitForExit(10000)
}

function Send([string]$Method, [string]$Url, [string]$Body = $null, [hashtable]$Headers = @{}) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Url)
    if ($null -ne $Body) { $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, 'text/plain') }
    foreach ($key in $Headers.Keys) { [void]$request.Headers.TryAddWithoutValidation($key, $Headers[$key]) }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $all = @{}
    foreach ($h in $response.Headers) { $all[$h.Key] = ($h.Value -join ', ') }
    foreach ($h in $response.Content.Headers) { $all[$h.Key] = ($h.Value -join ', ') }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $text; Headers = $all }
}

# Raw bytes over TCP; returns what the server sent (until it closes or the timeout).
function Send-Raw([int]$Port, [byte[]]$Bytes, [int]$TimeoutMs = 15000) {
    $tcp = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
    try {
        $stream = $tcp.GetStream(); $stream.ReadTimeout = $TimeoutMs
        $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush()
        $buffer = [byte[]]::new(65536); $ms = [System.IO.MemoryStream]::new()
        try { while (($n = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) { $ms.Write($buffer, 0, $n); if ($ms.Length -gt 0 -and [System.Text.Encoding]::ASCII.GetString($ms.ToArray()).Contains("`r`n`r`n")) { Start-Sleep -Milliseconds 200; if (-not $stream.DataAvailable) { break } } } } catch [System.IO.IOException] { }
        [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
    } finally { $tcp.Dispose() }
}

function Ascii([string]$Text) { [System.Text.Encoding]::ASCII.GetBytes($Text) }

try {
    # ================= the main program =================
    $port = Get-FreePort
    $dir = New-Case 'basic' $port
    $run = Start-Otter @('run', $dir) $dir
    $up = Wait-Until { $run.Err.ToString().Contains('Otter Web: listening on') } 60
    Check 'starts and prints the listening line (§2)' $up $run.Err.ToString()
    if (-not $up) { throw "The server did not start: $($run.Err) $($run.Out)" }
    Check 'binds 127.0.0.1 by default (§2)' ($run.Err.ToString().Contains("http://127.0.0.1:$port/"))
    $base = "http://127.0.0.1:$port"

    # §4 request object
    $r = Send GET "$base/hello?name=Ada"
    Check 'GET with a query value (§4 query)' ($r.Status -eq 200 -and $r.Body -eq 'Hello, Ada') "$($r.Status) $($r.Body)"
    Check 'text responses are text/plain utf-8 (§5)' ($r.Headers['Content-Type'] -eq 'text/plain; charset=utf-8') $r.Headers['Content-Type']
    $r = Send GET "$base/hello"
    Check 'a missing query value is gone with get ... from (§4, D41)' ($r.Body -eq 'Hello, stranger') $r.Body
    $r = Send GET "$base/hello?name=J%C3%BCrgen+M"
    Check 'query decoding: %XX UTF-8 and + (§4)' ($r.Body -eq 'Hello, Jürgen M') $r.Body
    $r = Send GET "$base/hello?name=first&name=second"
    Check 'query gives the first of repeated values (§4)' ($r.Body -eq 'Hello, first') $r.Body
    $r = Send GET "$base/queries?tag=a&tag=b&tag=c"
    $tags = $r.Body | ConvertFrom-Json
    Check 'queries keeps every value in order (§4)' ($r.Status -eq 200 -and ($tags -join ',') -eq 'a,b,c') $r.Body
    $r = Send GET "$base/header" -Headers @{ 'X-Otter-Test' = 'Case Works' }
    Check 'header names ignore capitals (§4)' ($r.Body -eq 'Case Works') $r.Body
    $r = Send GET "$base/request?x=1"
    $info = $r.Body | ConvertFrom-Json
    Check 'method and path (§4, D51)' ($info.method -eq 'GET' -and $info.path -eq '/request') $r.Body
    $r = Send POST "$base/echo" 'plain body text'
    Check 'body as text (§4, D51)' ($r.Status -eq 200 -and $r.Body -eq 'plain body text') $r.Body
    $r = Send POST "$base/json" '{"a":1,"b":"two"}'
    $json = $r.Body | ConvertFrom-Json
    Check 'JSON in, JSON out with status 201 (§5)' ($r.Status -eq 201 -and $json.a -eq 1 -and $json.b -eq 'two' -and $r.Headers['Content-Type'] -eq 'application/json; charset=utf-8') "$($r.Status) $($r.Body)"

    # §5 responses
    $r = Send GET "$base/status"
    Check 'status from an expression (§5)' ($r.Status -eq 418 -and $r.Body -eq '"teapot"') "$($r.Status) $($r.Body)"
    $r = Send GET "$base/statusonly"
    Check 'respond with status N (§5)' ($r.Status -eq 202 -and $r.Body -eq '') "$($r.Status) $($r.Body)"
    $r = Send GET "$base/nothing"
    Check 'a handler without respond gives 204 (§5)' ($r.Status -eq 204) "$($r.Status)"
    $r = Send GET "$base/first"
    Check 'respond ends the handler (§5)' ($r.Body -eq 'first') $r.Body
    $r = Send GET "$base/trycatch"
    Check 'respond passes through try (§5)' ($r.Body -eq 'inside try') $r.Body
    $r = Send GET "$base/hello"
    Check 'X-Request-Id on every response (§5)' ($r.Headers['X-Request-Id'] -match '^[0-9a-f]{16}$') $r.Headers['X-Request-Id']
    Check 'nosniff and no-store (§5)' ($r.Headers['X-Content-Type-Options'] -eq 'nosniff' -and $r.Headers['Cache-Control'] -eq 'no-store')
    Check 'no Server header (§5)' (-not $r.Headers.ContainsKey('Server'))

    # §3 routes
    $r = Send GET "$base/HELLO/?name=x"
    Check 'paths ignore capitals and a trailing slash (§3)' ($r.Body -eq 'Hello, x') $r.Body
    $r = Send HEAD "$base/hello"
    Check 'HEAD is answered by GET, without a body (§3)' ($r.Status -eq 200 -and $r.Body -eq '') "$($r.Status) [$($r.Body)]"
    $r = Send DELETE "$base/hello"
    Check 'other method on a known path: 405 with Allow (§3)' ($r.Status -eq 405 -and $r.Headers['Allow'] -eq 'GET, HEAD') "$($r.Status) $($r.Headers['Allow'])"
    $r = Send GET "$base/nope"
    Check 'unknown path: 404 {"error":"Not Found"} (§3)' ($r.Status -eq 404 -and $r.Body -eq '{"error":"Not Found"}') "$($r.Status) $($r.Body)"
    $r = Send PATCH "$base/any"
    Check '"a request" takes any method (§3)' ($r.Status -eq 200 -and $r.Body -eq 'PATCH') "$($r.Status) $($r.Body)"

    # §6 errors
    $r = Send GET "$base/fail"
    $body = $r.Body | ConvertFrom-Json
    Check 'a failing handler gives 500 with the request id and no details (§6)' ($r.Status -eq 500 -and $body.error -eq 'Internal Server Error' -and $body.requestId -eq $r.Headers['X-Request-Id'] -and -not $r.Body.Contains('boom')) $r.Body
    $logged = Wait-Until { $run.Err.ToString().Contains("request $($r.Headers['X-Request-Id']) (GET /fail) failed:") -and $run.Err.ToString().Contains('boom') } 5
    Check 'the failure is printed to standard error with the request id (§6)' $logged
    $r = Send GET "$base/missing"
    Check 'name of query of req with no value: runtime error, 500 (§4, D41)' ($r.Status -eq 500) "$($r.Status)"
    $r = Send GET "$base/badstatus"
    Check 'a status outside 200-599 fails the handler: 500 (§5)' ($r.Status -eq 500) "$($r.Status)"
    $r = Send GET "$base/hello"
    Check 'the server keeps running after failures (§6)' ($r.Status -eq 200) "$($r.Status)"

    # §6 HTTP-level answers and §7 limits
    $big = 'x' * 2000
    $r = Send POST "$base/echo" $big
    Check 'a body over maxbody: 413 (§2, §6)' ($r.Status -eq 413) "$($r.Status)"
    $bad = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$base/echo")
    $bad.Content = [System.Net.Http.ByteArrayContent]::new([byte[]](0xFF, 0xFE, 0x41))
    $badStatus = [int]$client.SendAsync($bad).GetAwaiter().GetResult().StatusCode
    Check 'a body that is not UTF-8: 400 (§4)' ($badStatus -eq 400) "$badStatus"
    $r = Send GET "$base/hello?name=%FF"
    Check 'a query that is not UTF-8: 400 (§4)' ($r.Status -eq 400) "$($r.Status)"
    $huge = 'GET /hello HTTP/1.1' + "`r`nHost: x`r`nX-Big: " + ('a' * 40000) + "`r`n`r`n"
    $raw = Send-Raw $port (Ascii $huge)
    Check 'headers over 32 KiB: 431 (§6)' ($raw -match '^HTTP/1\.1 431') ($raw -split "`r`n")[0]

    # §7 slow clients never block others
    $stalled = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port)
    $s = $stalled.GetStream(); $partial = Ascii "POST /echo HTTP/1.1`r`nHost: x`r`nContent-Length: 500`r`n`r`nabc"; $s.Write($partial, 0, $partial.Length)
    $stalledHeaders = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port)
    $h = $stalledHeaders.GetStream(); $part = Ascii "GET /hello HTTP/1.1`r`nHost: x`r`n"; $h.Write($part, 0, $part.Length)
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $r = Send GET "$base/hello?name=fast"
    $clock.Stop()
    Check 'a stalled body and stalled headers do not block another client (§7)' ($r.Body -eq 'Hello, fast' -and $clock.ElapsedMilliseconds -lt 3000) "$($clock.ElapsedMilliseconds) ms"
    $timedOut = Wait-Until { try { $buf = [byte[]]::new(512); $h.ReadTimeout = 500; $n = $h.Read($buf, 0, 512); $n -ge 0 } catch { $false } } 20
    Check 'stalled headers are timed out (§7, 10 s)' $timedOut
    $stalled.Dispose(); $stalledHeaders.Dispose()

    # concurrency: many clients at once
    $tasks = 1..40 | ForEach-Object { $client.GetStringAsync("$base/hello?name=c$_") }
    [System.Threading.Tasks.Task]::WaitAll($tasks)
    $okAll = ($tasks | Where-Object { $_.Result -match '^Hello, c\d+$' }).Count
    Check '40 concurrent requests are all answered (§7)' ($okAll -eq 40) "$okAll of 40"

    # port in use (§2)
    $second = Start-Otter @('run', $dir) $dir
    if ($second.Process.WaitForExit(30000)) { $second.Process.WaitForExit() }
    Check 'a port in use stops a second server with exit 3 (§2)' ($second.Process.HasExited -and $second.Process.ExitCode -eq 3 -and $second.Out.ToString().Contains('already in use')) "exit $($second.Process.ExitCode): $($second.Out)"

    # §8 graceful stop (SIGTERM / Ctrl+C) - POSIX signals; not on Windows here
    if (-not $IsWindows) {
        & kill -TERM $run.Process.Id
        $stopped = $run.Process.WaitForExit(10000); if ($stopped) { $run.Process.WaitForExit() }
        Check 'SIGTERM stops gracefully: "stopped", exit 0 (§8)' ($stopped -and $run.Process.ExitCode -eq 0 -and $run.Err.ToString().Contains('Otter Web: stopped.')) "exited=$stopped code=$(if ($stopped) { $run.Process.ExitCode })"
        $port2 = Get-FreePort; $dir2 = New-Case 'basic' $port2
        $run2 = Start-Otter @('run', $dir2) $dir2
        if (Wait-Until { $run2.Err.ToString().Contains('listening') } 60) {
            & kill -INT $run2.Process.Id
            $stopped2 = $run2.Process.WaitForExit(10000); if ($stopped2) { $run2.Process.WaitForExit() }
            Check 'Ctrl+C (SIGINT) stops gracefully, exit 0 (§8)' ($stopped2 -and $run2.Process.ExitCode -eq 0 -and $run2.Err.ToString().Contains('Otter Web: stopped.')) "exited=$stopped2"
        } else { Check 'Ctrl+C (SIGINT) stops gracefully, exit 0 (§8)' $false 'did not start' }
        Stop-Otter $run2
    } else {
        # POSIX signals cannot be sent to a Windows console program from here:
        # graceful stop on Windows (Ctrl+C) is not automated.
        $skipped.Add('graceful stop on Ctrl+C / SIGTERM (§8): not automated on Windows')
        Write-Host 'SKIP  graceful stop on Ctrl+C / SIGTERM (§8): not automated on Windows'
    }
    Stop-Otter $run

    # ================= refusals =================
    foreach ($case in @(
        @{ Name = 'publicbind'; Code = 3; Text = 'only this computer'; Title = 'a public host is refused without allowPublicBind (§2)' },
        @{ Name = 'badprop'; Code = 3; Text = 'has no property called "static"'; Title = 'an unknown server property is refused (§2)' },
        @{ Name = 'badruntime'; Code = 2; Text = 'is not supported'; Title = 'an unknown web.runtime is a manifest error (§1)' }
    )) {
        $p = Get-FreePort; $d = New-Case $case.Name $p
        $x = Start-Otter @('run', $d) $d
        $done = $x.Process.WaitForExit(30000); if ($done) { $x.Process.WaitForExit() }
        $text = $x.Out.ToString() + $x.Err.ToString()
        Check $case.Title ($done -and $x.Process.ExitCode -eq $case.Code -and $text.Contains($case.Text)) "exit $(if ($done) { $x.Process.ExitCode }): $text"
        Stop-Otter $x
    }

    # ================= Otter 1.0.1 unchanged without the opt-in =================
    $p = Get-FreePort; $d = New-Case 'nooptin' $p
    $x = Start-Otter @('run', $d) $d
    $done = $x.Process.WaitForExit(30000); if ($done) { $x.Process.WaitForExit() }
    Check 'without "web": otter run refuses routes as Otter 1.0.1 (§1)' ($done -and $x.Process.ExitCode -eq 3 -and $x.Out.ToString().Contains('I do not know how to run a WebRoute statement yet.')) "exit $(if ($done) { $x.Process.ExitCode }): $($x.Out)"
    Stop-Otter $x
    $p = Get-FreePort
    $stubDir = Join-Path $scratch "stub-$p"; New-Item -ItemType Directory $stubDir | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $stubDir 'stub.ot'), ([System.IO.File]::ReadAllText((Join-Path $cases 'stub.ot')) -replace '18080', "$p"))
    $x = Start-Otter @('serve', 'stub.ot') $stubDir
    if (Wait-Until { $x.Out.ToString().Contains('Otter Web Server running') } 60) {
        $lit = $null; $var = $null
        foreach ($hostName in @('localhost', '127.0.0.1', '[::1]')) {
            try { $lit = Send GET "http://${hostName}:$p/literal"; $var = Send GET "http://${hostName}:$p/variable"; break } catch { }
        }
        Check 'otter serve is still the 1.0.1 stub: literal replies (§1)' ($lit.Body -eq 'literal reply') "$($lit.Body)"
        Check 'otter serve is still the 1.0.1 stub: non-literals as node text (§1)' ($var.Body -match '^Variable@L\d+$') "$($var.Body)"
    } else { Check 'otter serve is still the 1.0.1 stub (§1)' $false "did not start: $($x.Out) $($x.Err)" }
    Stop-Otter $x
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ''
Write-Host "$($results.Count - $failed.Count) passed, $($failed.Count) failed$(if ($skipped.Count) { ", $($skipped.Count) skipped" })"
if ($ReportPath) {
    $lines = @("# Otter Web runtime 0.1: black-box tests", '', "$($results.Count - $failed.Count) passed, $($failed.Count) failed$(if ($skipped.Count) { ", $($skipped.Count) skipped" }) ($([System.Runtime.InteropServices.RuntimeInformation]::OSDescription), $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture))", '', '| Result | Check |', '|---|---|')
    foreach ($s in $skipped) { $lines += "| skipped | $s |" }
    foreach ($r in $results) { $lines += "| $(if ($r.Ok) { 'pass' } else { '**FAIL**' }) | $($r.Name)$(if (-not $r.Ok -and $r.Detail) { ': ' + ($r.Detail -replace '\|', '/' -replace "`r?`n", ' ') }) |" }
    [System.IO.File]::WriteAllLines($ReportPath, $lines)
}
if ($failed.Count) { exit 1 }
