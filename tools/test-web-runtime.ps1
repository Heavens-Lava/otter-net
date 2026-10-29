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
function Start-Otter([string[]]$Arguments, [string]$Directory, [hashtable]$Environment = @{}) {
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    if ($OtterNet -like '*.dll') { $psi.FileName = 'dotnet'; $psi.ArgumentList.Add($OtterNet) } else { $psi.FileName = $OtterNet }
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.WorkingDirectory = $Directory
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    foreach ($key in $Environment.Keys) { $psi.Environment[$key] = [string]$Environment[$key] }
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

# Form requests use the actual application/x-www-form-urlencoded MIME type.
function Send-Form([string]$Url, [string]$Encoded, [string]$ContentType = 'application/x-www-form-urlencoded; charset=utf-8') {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, $Url)
    $request.Content = [System.Net.Http.StringContent]::new($Encoded, [System.Text.Encoding]::UTF8)
    $request.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
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

# The static folder of a Phase 1b case: every MIME type, a hashed asset,
# hidden files, a folder without an index, a 100 MB file, a secret file
# OUTSIDE the folder, and links out of it. Returns how many links out were
# made (symbolic links; junctions too on Windows).
function New-StaticFiles([string]$Project, [string]$Marker) {
    $public = Join-Path $Project 'public'
    New-Item -ItemType Directory -Force (Join-Path $public 'assets'), (Join-Path $public 'docs'), (Join-Path $public 'sub'), (Join-Path $public '.hidden') | Out-Null
    $files = @{
        'index.html' = '<html>INDEX-PAGE</html>'; 'docs/index.html' = 'DOCS-PAGE'; 'app.js' = 'file app.js'; 'styles.css' = 'body{}'; 'main.mjs' = 'export {}'
        'data.json' = '{}'; 'mod.wasm' = 'wasm'; 'model.glb' = 'glb'; 'scene.gltf' = '{}'; 'buffer.bin' = 'bin'; 'tex.ktx2' = 'ktx2'; 'sky.hdr' = 'hdr'
        'img.png' = 'png'; 'pic.svg' = '<svg/>'; 'font.woff2' = 'woff2'; 'note.txt' = 'note'; 'sound.ogg' = 'ogg'; 'unknown.xyz' = 'xyz'
        'assets/index-B3x9k2Lq.js' = 'hashed'; 'assets/component.js' = 'plain'; 'sub/file.txt' = 'in sub'; 'inside.txt' = 'inside'
        '.env' = $Marker; '.hidden/secret.txt' = $Marker
    }
    foreach ($name in $files.Keys) { [System.IO.File]::WriteAllText((Join-Path $public $name), $files[$name]) }
    [System.IO.File]::WriteAllText((Join-Path $Project 'secret.txt'), $Marker)
    # A name with no dot and longer than "public": a containment check that
    # only looked at part of the path could not refuse it by accident.
    [System.IO.File]::WriteAllText((Join-Path $Project 'topsecretfile'), $Marker)
    $big = [System.IO.File]::Create((Join-Path $public 'big.bin')); $big.SetLength(100MB); $big.Dispose()
    $made = 0
    $links = @(
        @{ Path = 'link-out.txt'; Target = (Join-Path $Project 'secret.txt'); Type = 'SymbolicLink'; Out = $true },
        @{ Path = 'linkoutplain'; Target = (Join-Path $Project 'topsecretfile'); Type = 'SymbolicLink'; Out = $true },
        @{ Path = 'linkdir'; Target = $Project; Type = 'SymbolicLink'; Out = $true },
        @{ Path = 'link-in.txt'; Target = (Join-Path $public 'inside.txt'); Type = 'SymbolicLink'; Out = $false },
        @{ Path = 'link-hidden.txt'; Target = (Join-Path $public '.env'); Type = 'SymbolicLink'; Out = $false }
    )
    if ($IsWindows) { $links += @{ Path = 'junction-out'; Target = $Project; Type = 'Junction'; Out = $true } }
    foreach ($l in $links) {
        try { New-Item -ItemType $l.Type -Path (Join-Path $public $l.Path) -Target $l.Target -ErrorAction Stop | Out-Null; if ($l.Out) { $made++ } }
        catch { Write-Host "(could not create $($l.Type) $($l.Path): $($_.Exception.Message))" }
    }
    $made
}

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
    $r = Send GET "$base/hello?name=%F0%9F%98%80"
    Check 'query decoder keeps supplementary UTF-8 characters' ($r.Status -eq 200 -and $r.Body -eq 'Hello, 😀') $r.Body
    $r = Send-Form "$base/form" 'name=Ada+Lovelace'
    Check 'urlencoded form values and plus-as-space' ($r.Status -eq 200 -and $r.Body -eq 'Ada Lovelace') $r.Body
    $r = Send-Form "$base/form" 'name=%F0%9F%98%80'
    Check 'urlencoded form UTF-8 emoji' ($r.Status -eq 200 -and $r.Body -eq '😀') $r.Body
    $r = Send-Form "$base/form" 'name=first&name=second'
    Check 'form keeps the first duplicate' ($r.Status -eq 200 -and $r.Body -eq 'first') $r.Body
    $r = Send-Form "$base/forms" 'tag=a&tag=b&tag=c'
    Check 'forms keeps all duplicate values in order' ($r.Status -eq 200 -and (($r.Body | ConvertFrom-Json) -join ',') -eq 'a,b,c') $r.Body
    $r = Send-Form "$base/form-empty" 'other=value'
    Check 'missing form key gives gone via get from' ($r.Status -eq 200 -and $r.Body -eq 'missing') $r.Body
    $r = Send-Form "$base/form" 'name=%ZZ'
    Check 'malformed form percent escape rejected before handler' ($r.Status -eq 400) "$($r.Status) $($r.Body)"
    $r = Send-Form "$base/form" 'name=%FF'
    Check 'invalid UTF-8 form rejected before handler' ($r.Status -eq 400) "$($r.Status) $($r.Body)"
    $r = Send-Form "$base/form" 'name=Ada' 'application/x-www-form-urlencoded; charset=iso-8859-1'
    Check 'unsupported form charset is refused' ($r.Status -eq 415) "$($r.Status) $($r.Body)"
    $r = Send POST "$base/form" 'name=Ada'
    Check 'non-form body does not become form fields' ($r.Status -eq 200 -and $r.Body -eq 'missing') "$($r.Status) $($r.Body)"
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

    # ---- the request lifecycle (§6-§8) ----
    $hits = { [int](Send GET "$base/countvalue").Body }
    # A slow handler runs alone: the next request is read at once but runs after it.
    $slow = $client.GetStringAsync("$base/slow"); Start-Sleep -Milliseconds 400
    $clock = [System.Diagnostics.Stopwatch]::StartNew(); $r = Send GET "$base/hello?name=after"; $clock.Stop()
    Check 'a slow handler runs alone; the next request is answered after it (§7)' ($r.Body -eq 'Hello, after' -and $clock.ElapsedMilliseconds -ge 1500 -and $slow.Result -eq 'slow done') "$($clock.ElapsedMilliseconds) ms, slow: $($slow.Result)"
    # A request whose client leaves while it waits is never run.
    $before = & $hits
    $slow = $client.GetStringAsync("$base/slow"); Start-Sleep -Milliseconds 400
    $gone = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port); $bytes = Ascii "GET /count HTTP/1.1`r`nHost: x`r`n`r`n"; $gone.GetStream().Write($bytes, 0, $bytes.Length); Start-Sleep -Milliseconds 300; $gone.Dispose()
    [void]$slow.Wait(15000); Start-Sleep -Milliseconds 500
    $after = & $hits
    Check 'a request whose client left while it waited never runs its handler (§7)' ($after -eq $before) "hits $before -> $after"
    # A client that leaves during its handler: the handler finishes; the server carries on.
    $errBefore = $run.Err.ToString()
    $left = [System.Net.Sockets.TcpClient]::new('127.0.0.1', $port); $bytes = Ascii "GET /slow HTTP/1.1`r`nHost: x`r`n`r`n"; $left.GetStream().Write($bytes, 0, $bytes.Length); Start-Sleep -Milliseconds 500; $left.Dispose()
    $r = Send GET "$base/hello?name=still"
    $newErr = $run.Err.ToString().Substring($errBefore.Length)
    Check 'a client that leaves during its handler: the handler finishes and the server carries on (§7)' ($r.Body -eq 'Hello, still' -and -not $newErr.Contains('failed')) "$($r.Body) $newErr"
    # Concurrent requests: every handler runs exactly once.
    $before = & $hits
    $many = 1..20 | ForEach-Object { $client.GetAsync("$base/count") }
    [void][System.Threading.Tasks.Task]::WaitAll($many, 30000)
    $ok = ($many | Where-Object { $_.IsCompletedSuccessfully -and [int]$_.Result.StatusCode -eq 200 }).Count
    $after = & $hits
    Check '20 concurrent handlers each run exactly once (§7)' ($ok -eq 20 -and $after -eq $before + 20) "$ok answered, hits $before -> $after"

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

    # ================= time-outs, overload and shutdown with requests waiting =================
    # A request that waits too long gets 503 and its handler never runs.
    $p = Get-FreePort; $d = New-Case 'basic' $p
    $x = Start-Otter @('run', $d) $d @{ OTTER_WEB_TEST_QUEUE_TIMEOUT_SECONDS = 1 }
    if (Wait-Until { $x.Err.ToString().Contains('listening') } 60) {
        $b = "http://127.0.0.1:$p"
        $slow = $client.GetStringAsync("$b/slow"); Start-Sleep -Milliseconds 400
        $r = Send GET "$b/count"
        [void]$slow.Wait(15000); Start-Sleep -Milliseconds 500
        $value = [int](Send GET "$b/countvalue").Body
        Check 'a request that times out while waiting gets 503 and never runs (§6, §7)' ($r.Status -eq 503 -and $value -eq 0) "status $($r.Status), hits $value"
    } else { Check 'a request that times out while waiting gets 503 and never runs (§6, §7)' $false 'did not start' }
    Stop-Otter $x
    # Overload: beyond the waiting limit, 503 at once; the others run once each.
    $p = Get-FreePort; $d = New-Case 'basic' $p
    $x = Start-Otter @('run', $d) $d @{ OTTER_WEB_TEST_MAX_WAITING = 3 }
    if (Wait-Until { $x.Err.ToString().Contains('listening') } 60) {
        $b = "http://127.0.0.1:$p"
        $slow = $client.GetStringAsync("$b/slow"); Start-Sleep -Milliseconds 400
        $burst = 1..8 | ForEach-Object { $client.GetAsync("$b/count") }
        $all = [System.Threading.Tasks.Task]::WaitAll($burst, 30000)
        $codes = @($burst | Where-Object IsCompletedSuccessfully | ForEach-Object { [int]$_.Result.StatusCode })
        [void]$slow.Wait(15000); Start-Sleep -Milliseconds 300
        $value = [int](Send GET "$b/countvalue").Body
        $ran = @($codes | Where-Object { $_ -eq 200 }).Count; $refused = @($codes | Where-Object { $_ -eq 503 }).Count
        Check 'queue overload: 503 beyond the limit, every request answered, each accepted one runs once (§6)' ($all -and $codes.Count -eq 8 -and $refused -ge 4 -and $ran + $refused -eq 8 -and $value -eq $ran) "answered $($codes.Count), 200: $ran, 503: $refused, hits $value"
    } else { Check 'queue overload: 503 beyond the limit, every request answered, each accepted one runs once (§6)' $false 'did not start' }
    Stop-Otter $x
    # Shutdown with requests waiting (POSIX signal; see the Windows test for Ctrl+C there).
    if (-not $IsWindows) {
        $p = Get-FreePort; $d = New-Case 'basic' $p
        $x = Start-Otter @('run', $d) $d
        if (Wait-Until { $x.Err.ToString().Contains('listening') } 60) {
            $b = "http://127.0.0.1:$p"
            $slow = $client.GetAsync("$b/slow"); Start-Sleep -Milliseconds 400
            $pending = 1..3 | ForEach-Object { $client.GetAsync("$b/count") }
            Start-Sleep -Milliseconds 300
            & kill -TERM $x.Process.Id
            $answered = [System.Threading.Tasks.Task]::WaitAll(@($slow) + $pending, 20000)
            $exited = $x.Process.WaitForExit(20000); if ($exited) { $x.Process.WaitForExit() }
            $slowOk = $slow.IsCompletedSuccessfully -and [int]$slow.Result.StatusCode -eq 200
            $statuses = @($pending | ForEach-Object { if ($_.IsCompletedSuccessfully) { [int]$_.Result.StatusCode } else { 'error' } })
            Check 'shutdown with requests waiting: the running handler finishes, the waiting ones are answered, nothing hangs, exit 0 (§8)' ($answered -and $slowOk -and $exited -and $x.Process.ExitCode -eq 0 -and -not ($statuses -contains 'error')) "answered=$answered slow=$slowOk waiting=$($statuses -join ',') exited=$exited"
        } else { Check 'shutdown with requests waiting (§8)' $false 'did not start' }
        Stop-Otter $x
    } else {
        $skipped.Add('shutdown with requests waiting (§8): POSIX signal, see the Windows Ctrl+C test')
        Write-Host 'SKIP  shutdown with requests waiting (§8): POSIX signal, see the Windows Ctrl+C test'
    }

    # ================= Phase 1b: path parameters, static files, the web response object =================
    $p = Get-FreePort; $d = New-Case 'static' $p
    $marker = 'TOPSECRET-' + [Guid]::NewGuid().ToString('N')
    $linksMade = New-StaticFiles $d $marker
    $x = Start-Otter @('run', $d) $d
    if (Wait-Until { $x.Err.ToString().Contains('listening') } 60) {
        $b = "http://127.0.0.1:$p"
        # §3 path parameters and precedence
        Check 'a path parameter (§3)' ((Send GET "$b/api/users/42").Body -eq '42')
        Check 'a literal route beats a parameter, whatever the order (§3)' ((Send GET "$b/api/users/me").Body -eq 'me')
        Check 'a route whose method does not fit gives way to the next matching route (§3)' ((Send POST "$b/api/users/me" 'x').Body -eq 'posted me')
        Check 'two parameters (§3)' ((Send GET "$b/api/files/docs/a.txt").Body -eq 'docs|a.txt')
        Check 'parameters are percent-decoded UTF-8 (§3)' ((Send GET "$b/api/users/caf%C3%A9%20au%20lait").Body -eq 'café au lait')
        Check 'parameters keep their capitals (§3)' ((Send GET "$b/api/users/AdaL").Body -eq 'AdaL')
        Check 'equal precedence: the first declared route wins (§3)' ((Send GET "$b/order/fixed/fixed").Body -eq 'first')
        $r = Send-Raw $p (Ascii "GET /api/users/a%2Fb HTTP/1.1`r`nHost: x`r`nConnection: close`r`n`r`n")
        Check 'an encoded slash in a parameter: 400 (§3)' ($r -match '^HTTP/1\.1 400') ($r -split "`r`n")[0]
        $r = Send-Raw $p (Ascii "GET /api/users/%FF HTTP/1.1`r`nHost: x`r`nConnection: close`r`n`r`n")
        Check 'invalid UTF-8 in a parameter: 400 (§3)' ($r -match '^HTTP/1\.1 400') ($r -split "`r`n")[0]
        Check 'an empty parameter matches nothing: 404 (§3)' ((Send GET "$b/api/users//").Status -in 404, 400)

        # §2b static files
        $r = Send GET "$b/app.js"
        Check 'a route wins over a static file with the same path (§2b)' ($r.Body -eq 'route wins') $r.Body
        $html = @{ Accept = 'text/html' }
        $r = Send GET "$b/"
        Check '"/" serves index.html (§2b)' ($r.Status -eq 200 -and $r.Body.Contains('INDEX-PAGE') -and $r.Headers['Content-Type'] -eq 'text/html; charset=utf-8') "$($r.Status) $($r.Headers['Content-Type'])"
        $r = Send GET "$b/docs/"
        Check 'a folder serves its index.html (§2b)' ($r.Status -eq 200 -and $r.Body.Contains('DOCS-PAGE')) "$($r.Status)"
        Check 'a folder without index.html: 404, no listing (§2b)' ((Send GET "$b/sub/").Status -eq 404)
        $types = [ordered]@{
            'styles.css' = 'text/css; charset=utf-8'; 'main.mjs' = 'text/javascript; charset=utf-8'; 'data.json' = 'application/json; charset=utf-8'
            'mod.wasm' = 'application/wasm'; 'model.glb' = 'model/gltf-binary'; 'scene.gltf' = 'model/gltf+json'; 'buffer.bin' = 'application/octet-stream'
            'tex.ktx2' = 'image/ktx2'; 'sky.hdr' = 'image/vnd.radiance'; 'img.png' = 'image/png'; 'pic.svg' = 'image/svg+xml'; 'font.woff2' = 'font/woff2'
            'note.txt' = 'text/plain; charset=utf-8'; 'sound.ogg' = 'audio/ogg'; 'unknown.xyz' = 'application/octet-stream'
        }
        $wrong = @()
        foreach ($name in $types.Keys) { $r = Send GET "$b/$name"; if ($r.Status -ne 200 -or $r.Headers['Content-Type'] -ne $types[$name]) { $wrong += "$name=$($r.Status) $($r.Headers['Content-Type'])" } }
        Check 'MIME types: JavaScript, WASM, GLB, glTF, KTX2, HDR and the rest of the table (§2b)' ($wrong.Count -eq 0) ($wrong -join '; ')
        $r = Send GET "$b/styles.css"
        Check 'nosniff on static files (§2b)' ($r.Headers['X-Content-Type-Options'] -eq 'nosniff')
        Check 'ETag, Last-Modified and no-cache on an ordinary file (§2b)' ($r.Headers['ETag'] -match '^"[0-9a-f]+-[0-9a-f]+"$' -and $r.Headers['Last-Modified'] -and $r.Headers['Cache-Control'] -eq 'no-cache') "$($r.Headers['ETag']) $($r.Headers['Cache-Control'])"
        $again = Send GET "$b/styles.css" -Headers @{ 'If-None-Match' = $r.Headers['ETag'] }
        Check 'If-None-Match with the ETag: 304 (§2b)' ($again.Status -eq 304 -and $again.Body -eq '') "$($again.Status)"
        $since = Send GET "$b/styles.css" -Headers @{ 'If-Modified-Since' = $r.Headers['Last-Modified'] }
        Check 'If-Modified-Since: 304 (§2b)' ($since.Status -eq 304) "$($since.Status)"
        $r = Send GET "$b/assets/index-B3x9k2Lq.js"
        Check 'a Vite-hashed file is cached as immutable (§2b)' ($r.Status -eq 200 -and $r.Headers['Cache-Control'] -eq 'public, max-age=31536000, immutable') $r.Headers['Cache-Control']
        $r = Send GET "$b/assets/component.js"
        Check 'a file without a hash is not cached as immutable (§2b)' ($r.Headers['Cache-Control'] -eq 'no-cache') $r.Headers['Cache-Control']
        $r = Send HEAD "$b/styles.css"
        Check 'HEAD on a static file: headers, no body (§2b)' ($r.Status -eq 200 -and $r.Body -eq '' -and [int]$r.Headers['Content-Length'] -gt 0) "$($r.Status)"
        $r = Send POST "$b/styles.css" 'x'
        Check 'other methods on a static file: 405 with Allow (§2b)' ($r.Status -eq 405 -and $r.Headers['Allow'] -eq 'GET, HEAD') "$($r.Status) $($r.Headers['Allow'])"
        # the SPA fallback
        $r = Send GET "$b/dashboard/users" -Headers $html
        Check 'fallback: a page navigation gets index.html (§2b)' ($r.Status -eq 200 -and $r.Body.Contains('INDEX-PAGE') -and $r.Headers['Cache-Control'] -eq 'no-cache') "$($r.Status)"
        Check 'fallback: never under /api/ (§2b)' ((Send GET "$b/api/nothing/here" -Headers $html).Status -eq 404)
        Check 'fallback: never under /ws/ (§2b)' ((Send GET "$b/ws/room" -Headers $html).Status -eq 404)
        Check 'fallback: never for a missing asset (an extension) (§2b)' ((Send GET "$b/missing.js" -Headers $html).Status -eq 404)
        Check 'fallback: only for requests that accept HTML (§2b)' ((Send GET "$b/dashboard" -Headers @{ Accept = 'application/json' }).Status -eq 404)
        # §2b traversal, hidden files and links: never 200, never the secret
        $attacks = @('/../secret.txt', '/%2e%2e/secret.txt', '/%2E%2E%2Fsecret.txt', '/%252e%252e/secret.txt', '/..%5csecret.txt', '/%5c..%5csecret.txt',
                     '/sub/..%2f..%2fsecret.txt', '/docs/../../secret.txt', '/./../secret.txt', '/.env', '/%2eenv', '/.hidden/secret.txt', '/.hidden%2fsecret.txt',
                     '/index.html%00.js', '/C:/Windows/win.ini', '/c%3a/secret.txt', '/secret.txt', '/link-out.txt', '/linkdir/secret.txt', '/linkdir/', '/link-hidden.txt',
                     '/linkoutplain', '/linkdir/topsecretfile', '/topsecretfile')
        if ($IsWindows) { $attacks += @('/junction-out/secret.txt', '/junction-out/topsecretfile', '/..\secret.txt', '/sub/..\..\secret.txt') }
        $leaks = @()
        foreach ($a in $attacks) {
            $raw = Send-Raw $p (Ascii "GET $a HTTP/1.1`r`nHost: x`r`nConnection: close`r`n`r`n")
            if ($raw -match '^HTTP/1\.1 200' -or $raw.Contains($marker)) { $leaks += "$a -> $(($raw -split "`r`n")[0])" }
        }
        Check "traversal, encoded traversal, backslashes, NUL, drive letters, dotfiles and links out: never served ($($attacks.Count) cases) (§2b)" ($leaks.Count -eq 0) ($leaks -join '; ')
        Check 'symbolic links out of the folder were really created for the test' ($linksMade -ge 3) "$linksMade made"
        $r = Send GET "$b/link-in.txt"
        Check 'a symbolic link that stays inside the folder is served (§2b)' ($r.Status -eq 200 -and $r.Body -eq 'inside') "$($r.Status) $($r.Body)"
        # streaming: a 100 MB file without memory growth
        $x.Process.Refresh(); $before = $x.Process.WorkingSet64
        $stream = $client.GetStreamAsync("$b/big.bin").GetAwaiter().GetResult()
        $buffer = [byte[]]::new(1MB); $total = 0L
        while (($n = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) { $total += $n }
        $stream.Dispose(); $x.Process.Refresh(); $after = $x.Process.WorkingSet64
        $growth = ($after - $before) / 1MB
        Check 'a 100 MB file is streamed whole without memory growth (§2b)' ($total -eq 100MB -and $growth -lt 64) ("{0:N0} bytes, working set {1:N1} MB" -f $total, $growth)

        # §5 the web response object
        $r = Send GET "$b/resp/text404"
        Check 'web response: status, type, body (§5)' ($r.Status -eq 404 -and $r.Body -eq 'Nothing here' -and $r.Headers['Content-Type'] -eq 'text/plain; charset=utf-8') "$($r.Status) $($r.Headers['Content-Type'])"
        $r = Send GET "$b/resp/html"
        $expected = '<p>héllo ✓ 日本</p>'
        Check 'web response: UTF-8 body, charset added, Content-Length in bytes (§5)' ($r.Body -eq $expected -and $r.Headers['Content-Type'] -eq 'text/html; charset=utf-8' -and [int]$r.Headers['Content-Length'] -eq [System.Text.Encoding]::UTF8.GetByteCount($expected)) "$($r.Headers['Content-Length'])"
        $r = Send GET "$b/resp/redirect"
        Check 'web response: a redirect with a path (§5)' ($r.Status -eq 302 -and $r.Headers['Location'] -eq '/new') "$($r.Status) $($r.Headers['Location'])"
        $r = Send GET "$b/resp/redirect-same"
        Check 'web response: a redirect to this same host (§5)' ($r.Status -eq 303 -and $r.Headers['Location'] -eq "http://127.0.0.1:$p/ok") "$($r.Status) $($r.Headers['Location'])"
        foreach ($case in @(@('/resp/redirect-noloc', 'a redirect without a location'), @('/resp/redirect-evil1', 'an open redirect (//host)'), @('/resp/redirect-evil2', 'an open redirect (another site)'),
                            @('/resp/redirect-js', 'a javascript: location'), @('/resp/header-crlf', 'a line break in a header (injection)'), @('/resp/header-forbidden', 'a runtime header (Content-Length)'),
                            @('/resp/header-requestid', 'a runtime header (X-Request-Id)'), @('/resp/header-badname', 'an invalid header name'), @('/resp/setcookie', 'Set-Cookie (Phase 3)'),
                            @('/resp/cookie', 'a cookie (Phase 3)'), @('/resp/bodything', 'a thing as the body'), @('/resp/unknown', 'an unknown property'))) {
            $r = Send GET "$b$($case[0])"
            Check "web response refused: $($case[1]) - 500, nothing leaked (§5)" ($r.Status -eq 500 -and -not $r.Headers.ContainsKey('Location') -and -not $r.Headers.ContainsKey('Injected')) "$($r.Status)"
        }
        $r = Send GET "$b/resp/header"
        Check 'web response: extra headers (§5)' ($r.Headers['Content-Disposition'] -eq 'attachment; filename=data.csv' -and $r.Headers['X-Custom'] -eq 'yes' -and $r.Headers['Content-Type'] -eq 'text/csv; charset=utf-8') "$($r.Headers['Content-Disposition'])"
        $r = Send GET "$b/resp/json"
        Check 'respond with a web response as json sends it as JSON data (§5)' ($r.Status -eq 200 -and ($r.Body | ConvertFrom-Json).status -eq 202) "$($r.Status) $($r.Body)"
    } else { Check 'Phase 1b server starts' $false "$($x.Out) $($x.Err)" }
    Stop-Otter $x
    # No fallback unless declared
    $p = Get-FreePort; $d = New-Case 'nofallback' $p
    [void](New-StaticFiles $d 'unused')
    $x = Start-Otter @('run', $d) $d
    if (Wait-Until { $x.Err.ToString().Contains('listening') } 60) {
        $r = Send GET "http://127.0.0.1:$p/dashboard" -Headers @{ Accept = 'text/html' }
        Check 'the fallback is off unless declared: 404 (§2b)' ($r.Status -eq 404) "$($r.Status)"
        Check 'static files without a fallback still work (§2b)' ((Send GET "http://127.0.0.1:$p/styles.css").Status -eq 200)
    } else { Check 'the fallback is off unless declared (§2b)' $false 'did not start' }
    Stop-Otter $x

    # ================= refusals =================
    foreach ($case in @(
        @{ Name = 'publicbind'; Code = 3; Text = 'only this computer'; Title = 'a public host is refused without allowPublicBind (§2)' },
        @{ Name = 'badprop'; Code = 3; Text = 'has no property called "timeout"'; Title = 'an unknown server property is refused (§2)' },
        @{ Name = 'duproutes'; Code = 3; Text = 'answer the same requests'; Title = 'two routes for the same requests stop the program at start (§3)' },
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
