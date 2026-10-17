# Porting inventory: network and events

Area: HTTP, downloads, servers and routes, WebSockets, TCP, UDP, file
watching, the console event loop and its capture rules (D130, D133).

Reference: otterPS 1.0.1. `P` = `src/Otter.Parser.psm1`, `I` =
`src/Otter.Interpreter.psm1`, `L` = `src/Otter.Library.psm1`, `R` =
`src/Otter.Runtime.psm1`, `S` = `src/Otter.Server.psm1`, `LP` =
`src/Otter.LoopPasses.psm1`, `V` = `src/Otter.Validation.psm1`.

Status: **missing** = still handed to the PowerShell Otter (the parser
throws `NotSupportedYet` before anything runs). This first version is the
inventory; the status column is updated as forms are ported.

| Capability | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| `get URL [as json] into X` (+ options block) | P Read-OtterStatement `Get`; I `HttpGet`; L Invoke-OtterHttpRequest | | missing | yes | | |
| `get json from URL into X` | P `Get` (Json) | | missing | yes | | |
| `post DATA [as json] to URL [into X]` | P `Post`; I `HttpPost` | | missing | yes | | |
| `put DATA [as json] to URL [into X]` | P `Put`; I `HttpPut` | | missing | yes | | |
| `delete from URL [into X]` | P `Delete`; I `HttpDelete` | | missing | yes | | |
| HTTP options block (`with header`, `with/without cookies`, `following/without redirects`, `with timeout N seconds`) | P Read-OtterHttpOptions; I Get-OtterHttpOptionsArguments | | missing | yes | | |
| Soft continuation (`into` on an indented line) | P Test-OtterSoftContinuation | | missing | yes | | |
| `download file from URL to PATH` | P `Download`; L Receive-OtterFileDownload | | missing | yes | | |
| `start get/post/put/delete ... and call it R` | P `Start`; I `HttpStart`; L Start-OtterHttpRequest | | missing | yes | | |
| `cancel R` | P pre-switch `cancel`; I `HttpCancel` | | missing | yes | | |
| `R is pending/running/completed/failed/cancelled` (and `is not`) | P Read-OtterConditionPrimary; I `HttpRequestIsState` | | missing | yes | | |
| `received response` | P Read-OtterValue; I `ReceivedResponse` / `NetContext` | | missing | yes | | |
| `state/response/error/status of R` | I `PropertyAccess` | | missing | yes | | |
| `on complete/error/cancel of R` | P `On`; I `WebSocketEvent` | | missing | yes | | |
| `when S receives METHOD at PATH [into R]` | P `When`; I default error | | missing | yes | | |
| `respond with ...` | P `Respond`; I default error | | missing | yes | | |
| `start S`, `listen on port N`, `listen S` | P `Start`, `Listen`; I default error | | missing | yes | | |
| `connect to websocket URL [using protocol P] and call it W` | P `Connect`; I `WebSocketConnect` | | missing | yes | | |
| `send X through W` | P pre-switch `send`; I `WebSocketSend` | | missing | yes | | |
| `close websocket W [with code N] [and reason R]` | P pre-switch `close`; I `WebSocketClose` | | missing | yes | | |
| `on open/message/close/error of W` | P `On`; I `WebSocketEvent` | | missing | yes | | |
| `W is connecting/open/closing/closed/connected` | P Read-OtterConditionPrimary; I `WebSocketIsState` | | missing | yes | | |
| `received message`, `close code`, `close reason`, `close was clean`, `websocket error` | P Read-OtterValue; I | | missing | yes | | |
| `state/url/protocol of W` | I `PropertyAccess` | | missing | yes | | |
| `connect [securely] to tcp H on port N [for server S] [using protocol(s) P] and call it C` | P `Connect`, Read-OtterNetClauses; I `TcpConnect` | | missing | yes | | |
| `listen for tcp [on A] on port N and call it S`; `listen securely for tcp` (reserved error) | P pre-switch `listen`, Read-OtterListenClauses; I `TcpListen` | | missing | yes | | |
| `stop tcp S` | P pre-switch `stop`; I `TcpStop` | | missing | yes | | |
| `open udp [on port N] and call it U` | P pre-switch `open`; I `UdpOpen` | | missing | yes | | |
| `send B through U to H on port N` | P pre-switch `send`; I `UdpSend` | | missing | yes | | |
| `close tcp C` / `close udp U` | P pre-switch `close`; I `NetClose` | | missing | yes | | |
| `on connect of C`, `on data from C/U`, `on connection to S` | P `On`; I `WebSocketEvent` | | missing | yes | | |
| `received data`, `sender address`, `sender port`, `network error`, `incoming connection` | P Read-OtterValue; I `NetContext` | | missing | yes | | |
| `C is secure`, `S is listening/stopped` | P Read-OtterConditionPrimary; I | | missing | yes | | |
| `remote/local address/port of`, `tls version/protocol of`, `state of`, `port of`, `count of BYTES` | P Read-OtterValue; I `PropertyAccess` | | missing | yes | | |
| `watch file/folder P [recursively] and call it W` | P pre-switch `watch`; I `WatchDeclare` | | missing | yes | | |
| `stop watching W` | P `Return` case; I `StopWatching` | | missing | yes | | |
| `on change of W` / `on create/delete/rename in W` | P `On`; I `WatchEvent` | | missing | yes | | |
| `W is watching` | P; I `IsWatching` | | missing | yes | | |
| `changed path`, `changed file name`, `change kind`, `old path` | P; I | | missing | yes | | |
| `await ...` (statement and value) | P `Await`, Read-OtterValue; I `Await` error | | missing | yes | | |
| Console event loop (after the program, during `wait`) | I Invoke-OtterEventLoop, Invoke-OtterWaitServiceStep | | missing | yes | | |
| D130 loop passes | LP Get-OtterLoopPassNames; I New-OtterLoopPass | | missing | yes | | |
| D133 handler runs | LP; I Enter-OtterHandlerRun, New-OtterHandlerCaptureEnvironment | | missing | yes | | |
| Check: handler inside another handler's run (`HandlerRunName`) | LP Get-OtterHandlerRunCaptures; V | | missing | yes | | |
