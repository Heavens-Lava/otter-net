# Porting inventory: network and events

Area: HTTP, downloads, servers and routes, WebSockets, TCP, UDP, file
watching, the console event loop and its capture rules (D130, D133).

Reference: otterPS 1.0.1. `P` = `src/Otter.Parser.psm1`, `I` =
`src/Otter.Interpreter.psm1`, `L` = `src/Otter.Library.psm1`, `R` =
`src/Otter.Runtime.psm1`, `S` = `src/Otter.Server.psm1`, `LP` =
`src/Otter.LoopPasses.psm1`, `V` = `src/Otter.Validation.psm1`.

otter-net files: `PN` = `src/Otter.Language/Parser.Network.cs`, `SN` =
`Syntax.Network.cs`, `IN` = `src/Otter.Runtime/Interpreter.Network.cs`, `EL` =
`EventLoop.cs`, `H` = `Http.cs`, `NV` = `NetValues.cs`, `LPC` = `src/Otter.Language/LoopPasses.cs` (LoopPassAnalysis).

Status: **native** = parsed and run by otter-net; **missing** = still handed
to the PowerShell Otter before anything runs. Tests are in `tests/compat/`.

## Statements and expressions

| Capability | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| `get URL [as json] into X` | P `Get`; I `HttpGet`; L Invoke-OtterHttpRequest | PN TryReadHttpGet; IN; H Send | native | no | net_http_errors (validation, refused, timeout) | success path not testable offline (a synchronous request blocks the program's own server) |
| `get json from URL into X` | P `Get` (Json) | PN TryReadHttpGet | native | no | net_http_errors | as above |
| `post DATA [as json] to URL [into X]` | P `Post`; I `HttpPost` | PN ReadPost; IN | native | no | net_http_errors | as above |
| `put DATA [as json] to URL [into X]` (vs `put x in y`, the UI's) | P `Put`; I `HttpPut` | PN TryReadHttpPut | native | no | net_http_errors | as above |
| `delete from URL [into X]` | P `Delete`; I `HttpDelete` | PN ReadHttpDelete | native | no | net_http_errors | as above |
| Body: JSON for `as json`, things and lists; bytes as octet-stream; text otherwise | L Invoke-OtterHttpRequest step 5 | H BuildRequest | native | no | net_http_requests (put) | |
| Options block: `with header N is V`, `with cookies`, `without cookies`, `following redirects`, `without redirects`, `with timeout N seconds`, and their errors | P Read-OtterHttpOptions; I Get-OtterHttpOptionsArguments | PN ReadHttpOptions; IN HttpOptionsArguments | native | no | net_http_errors, net_http_requests, net_syn_forms | |
| Header checks (empty name, CR/LF/NUL, colon or space) | L Assert-OtterHttpHeader | H CheckHeader | native | no | net_http_errors | |
| Timeout checks and "timed out after N seconds" | L | H CheckTimeout | native | no | net_http_errors, net_http_requests | |
| Soft continuation (`into` on an indented line) | P Test-OtterSoftContinuation | PN SoftContinuation | native | no | | |
| `download file from URL to PATH` and its checks | P `Download`; L Receive-OtterFileDownload | PN ReadDownload; H Download | native | no | net_http_errors (scheme, URL, existing file, missing folder, folder) | success not testable offline |
| `start get/post/put/delete ... and call it R` (+ options) | P `Start`; I `HttpStart`; L Start-OtterHttpRequest | PN ReadHttpStart; H Start | native | no | net_http_async, net_http_requests | |
| `cancel R` | P pre-switch `cancel`; I `HttpCancel` | PN; IN | native | no | net_http_requests, net_jobs | |
| `R is [not] pending/running/completed/failed/cancelled` | P Read-OtterConditionPrimary; I `HttpRequestIsState` | PN ReadNetworkStatePredicate; IN | native | no | net_http_async, net_http_requests, net_jobs | |
| `received response` | P Read-OtterValue; I `ReceivedResponse` | PN; IN | native | no | net_http_async, net_http_requests | |
| `state/response/error/status of R` | I `PropertyAccess` | IN TryNetworkProperty | native | no | net_http_async, net_http_requests | |
| `on complete/error/cancel of R`, kept outcome for a later handler | P `On`; I `WebSocketEvent` | PN ReadOnHandler; IN AddNetworkHandler | native | no | net_http_requests | |
| `when S receives METHOD at PATH [into R]` / `a request at` | P `When`; I default ("I do not know how to run a WebRoute statement yet.") | PN ReadWebRoute; IN | native (parse, refusal) | no | net_server | the server itself is `otter serve` (S), a command otter-net hands to otterPS |
| `respond with V [as json] [and status N]` / `respond with status N` | P `Respond`; I default | PN ReadRespond; IN | native (parse, refusal) | no | net_server | |
| `start S`, `start the S`, `listen S` | P `Start`, `Listen`; I default | PN ReadStart, ReadListenServer | native (parse, refusal) | no | net_server | |
| `listen on port N`, `listen port N` | P `Listen`; I default | PN ReadListenServer | native (parse, refusal) | no | net_server | |
| `S is a web server` (D51 thing) | P object definition | Parser.cs (core) | missing | yes | | an object of an unknown type is refused by the core parser; belongs with things/UI resources |
| `connect to websocket URL [using protocol P] and call it W` | P `Connect`; I `WebSocketConnect` | PN ReadConnect; IN ConnectWebSocket | native | no | net_websocket | no offline WebSocket server (the handshake needs SHA-1) |
| `send X through W` | P pre-switch `send`; I `WebSocketSend` | PN ReadSend; IN SendThrough | native | no | net_websocket | |
| `close websocket W [with code N] [and reason R]` | P pre-switch `close`; I `WebSocketClose` | PN; IN CloseWebSocket | native | no | net_websocket | |
| `on open/message/close/error of W` | P `On`; I `WebSocketEvent`; I Invoke-OtterWebSocketEventLoopStep | PN; EL WebSocketStep | native | no | net_websocket (error, close) | open/message need a server |
| `W is connecting/open/closing/closed/connected` | P; I `WebSocketIsState` | PN; IN | native | no | net_websocket, net_tcp, net_udp | |
| `received message`, `close code`, `close reason`, `close was clean`, `websocket error` | P Read-OtterValue; I | PN; IN Ambient | native | no | net_websocket, net_http_requests | |
| `state/url/protocol of W` | I `PropertyAccess` | IN | native | no | net_websocket | |
| `connect to tcp H on port N and call it C` | P `Connect`, Read-OtterNetClauses; I `TcpConnect` | PN ReadConnect, ReadNetClauses; IN ConnectTcp | native | no | net_tcp, net_tcp_errors | |
| `connect securely to tcp ... [for server S] [using protocol(s) P]` (D112) | as above | as above; EL TcpStep (SslStream) | native | no | net_tls, net_tcp_errors | `using protocol` gives 5.1's refusal (SPEC-QUESTIONS Q13) |
| `listen for tcp [on A] on port N and call it S`; `listen securely for tcp` (reserved) | P pre-switch `listen`, Read-OtterListenClauses; I `TcpListen` | PN ReadTcpListen; IN ListenTcp | native | no | net_tcp, net_tcp_errors, net_syn_forms | |
| `stop tcp S` | P pre-switch `stop`; I `TcpStop` | PN; IN | native | no | net_tcp, net_tcp_errors | |
| `open udp [on port N] and call it U` | P pre-switch `open`; I `UdpOpen` | PN ReadUdpOpen; IN | native | no | net_udp, net_tcp_errors | |
| `send B through U to H on port N` (on the line or indented) | P pre-switch `send`; I `UdpSend` | PN ReadSend; IN SendDatagram | native | no | net_udp, net_tcp_errors | |
| `close tcp C` / `close udp U` | P pre-switch `close`; I `NetClose` | PN; IN | native | no | net_tcp, net_udp, net_tcp_errors | |
| `on connect of C`, `on data from C/U`, `on connection to S`, `on close/error of ...` | P `On`; I `WebSocketEvent`; I Step-OtterTcp/Udp/TcpServer | PN; EL NetStep | native | no | net_tcp, net_udp, net_tls, net_loop_passes | |
| `received data`, `sender address`, `sender port`, `network error`, `incoming connection` | P Read-OtterValue; I `NetContext` | PN; IN | native | no | net_tcp, net_udp, net_tcp_errors | |
| `C is secure`, `S is listening/stopped` | P; I `ConnectionIsSecure`, `TcpServerIsState` | PN; IN | native | no | net_tcp, net_tls, net_tcp_errors | |
| `remote/local address/port of`, `tls version/protocol of`, `state of`, `port of`, `count of BYTES` | P Read-OtterValue; I `PropertyAccess` | PN; IN TryNetworkProperty | native | no | net_tcp, net_udp, net_tcp_errors | |
| Saying a socket, server, watcher or request; their type names in messages | R Format-OtterValue; I Get-OtterTypeName | NV ToString, IOtterTypeNamed | native | no | net_websocket, net_tcp_errors, net_watch, net_http_requests | |
| `watch file/folder P [recursively] and call it W` | P pre-switch `watch`; I `WatchDeclare`, New-OtterFileWatcher | PN ReadFileWatch; EL NewFileWatcher | native | no | net_watch, net_loop_passes | |
| `stop watching W` | P `Return` case; I `StopWatching` | PN; IN | native | no | net_watch | |
| `on change of W` / `on create/delete/rename in W` | P `On`; I `WatchEvent`, Invoke-OtterWatchEventLoopStep | PN; EL WatchStep | native | no | net_watch, net_loop_passes | rename and change events are implemented, not in a test (their notifications differ between file systems) |
| `W is watching` | P; I `IsWatching` | PN; IN | native | no | net_watch | |
| `changed path`, `changed file name`, `change kind`, `old path` | P; I | PN; IN | native | no | net_watch | |
| `await X` as a statement, `await X make/into Y`, `await` as a value | P `Await`, Read-OtterValue; I default / `Await` | PN; IN | native (refusal) | no | net_server, net_syn_forms | |
| Syntax errors of all the forms above, with otterPS's recovery | P | PN | native | no | net_syn_forms, net_syn_start | |
| `on X` that is none of the forms ("I expected 'start' or 'close' after 'on'") | P `On` | PN ReadOnHandler | native | no | net_syn_forms | `on start`/`on close`, `on route change`, drag and drop are the UI's (web target) |
| `start command CMD and call it J` (D119-R2) | P `Start`; I `StartCommand`; L Start-OtterCommandJob; R OtterProcessTracker | PN ReadStart; `Jobs.cs` StartJob, JobTracker | native | no | net_jobs | |
| `on output from J`, `on error output from J`, `on exit of J` (WhenStmt), kept exit for a later handler | P `On`; I `When` | PN ReadJobHandler; Jobs.cs TryExecuteJob | native | no | net_jobs | a `when` on a UI resource is the desktop's |
| `on complete/cancel of J` (an exit kept for later counts as completing) | I `WebSocketEvent` | Jobs.cs AddJobNetworkHandler | native | no | net_jobs | |
| `received output`, `received error output` | P Read-OtterValue; I `JobContext` | PN; Jobs.cs | native | no | net_jobs | |
| `state/exit code/output/error output/id of J` (`command of J` cannot be written: `command` is a keyword) | I `PropertyAccess` | Jobs.cs TryJobProperty | native | no | net_jobs | |
| Job events in the loop (at most 256 per job per turn), cancel kills the process tree, running jobs stopped at the end | I Invoke-OtterJobEventLoopStep, Stop-OtterCommandJob | Jobs.cs JobStep, CancelJob, StopJobs | native | no | net_jobs | |
| `every N seconds` / timer events | none: otterPS 1.0.1 has no timer event (`docs/OTTER_1_0_EVENT_LOOP_REVIEW.md` section 6) | | not a language form | | | |

## The console event loop and capture rules

| Capability | otterPS | otter-net | Status | Tests | Notes |
|---|---|---|---|---|---|
| Handlers run after the last statement until no source can produce an event | I Invoke-OtterEventLoop, called by Invoke-OtterProgram | EL RunEventLoop, called by Interpreter.Run | native | every `net_` program | watchers keep the program alive until stopped; sockets, servers and requests only while they have handlers |
| `wait` services every source (EV3) | I `WaitDelay`, Invoke-OtterWaitServiceStep | Interpreter.cs WaitDelay, EL ServiceEventsDuringWait | native | net_watch, net_loop_passes, net_http_requests | |
| One event per socket per pass, TCP/UDP armed in one pass and read in the next, every finished request per pass | I steps | EL steps | native | | same observable order as otterPS |
| Watch notifications: one per pass, 150 ms coalescing, the watcher error diagnostic | I Invoke-OtterWatchEventLoopStep | EL WatchStep, WatchQueue | native | net_watch | |
| Unhandled network error stops the program (line 0, "Add \"on error of <name>\" ...") | I Send-OtterNetError | EL SendNetError | native | | |
| Everything is closed when the program ends, however it ends | I Invoke-OtterProgram `finally` | EL CloseEventSources | native | | |
| Other areas' sources | | NV IOtterEventSource, IOtterHandlerTarget; EL AddEventSource | hook | | command jobs are in Jobs.cs; these hooks are for any later source |
| D130 loop passes (`for each`, `count`, `repeat`, `while`) | LP Get-OtterLoopPassNames; I New-OtterLoopPass, Set-OtterLoopVariable, Complete-OtterLoopPasses | LPC Compute; EL Run*WithPasses; Interpreter.cs loops | native | net_loop_passes | network handlers do not count (SPEC-QUESTIONS Q10) |
| D133 handler runs | LP; I Enter-OtterHandlerRun, Exit-OtterHandlerRun, New-OtterHandlerCaptureEnvironment | EL EnterHandlerRun, ExitHandlerRun, NewHandlerCaptureScope (public, for the UI area's `when`) | ready for the UI area | | only `when` handlers on UI resources use it in otterPS |
| Check: a handler inside another handler's run that uses its names (`HandlerRunName`) | LP Get-OtterHandlerRunCaptures; V | LPC HandlerRunCaptures, Diagnostics; src/Otter.Cli/FileMode.cs | native | net_check_runs | |
| Reserved names (D-2) in the area's statements | V walk | ReservedWords.cs | native | | |

## Notes

- `tools/compare.ps1` skips programs that mention `listen for`, `watch
  folder`, `open udp` or a URL, which is most of the `net_` programs; they are
  written to finish on their own and compared by passing them explicitly (see
  the final report of the port for how).
- Known 5.1/7 and platform differences in this area: SPEC-QUESTIONS Q12, Q13.
