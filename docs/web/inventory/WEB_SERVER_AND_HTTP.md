# Otter 1.0.1 web inventory: what already exists

Audit date: 2026-10-22. Scope: server-side web, the `otter web` compiler, the HTTP client, project scaffolding and security, as they exist today in
the frozen reference (otterPS-1.0.1) and in otter-net (byte-compatible C# port). Neither repository was modified.
Verified with the Native AOT `otter` 1.0.1 binary at `the otter-net 1.0.1 Native AOT binary (macOS arm64)` (macOS). Sample programs, logs and helper scripts are in
`docs/web/samples/web/`. Every server was bound to loopback (`localhost`, which was `[::1]`, or `127.0.0.1`), limited in time, and stopped.

Paths: `PS:` = `otterPS-1.0.1/`, `NET:` = `otter-net/`.

---

## 0. Headline findings

1. **`otter serve` is a static route table. It is not a program runtime.** The program is parsed and checked but **never executed**.
   For each top-level `when S receives ...` route, the server sends the **first `respond with`** in its body. The value is either
   a *literal* or the **AST node's debug text**: `respond with "Hi " plus name` sends `Math@L5`, and `respond with greeting` sends
   `Variable@L8`. Variables, functions, database calls, `say`, conditions and the `into req` request object never run.
   (NET:src/Otter.Cli/ServeCommand.cs:10-13, 238-262, 283-296; PS:src/Otter.Server.psm1:104-162. Verified: s02.)
2. The design in SPEC-DECISIONS D51 promises a request object (`method`, `path`, `query`, `body`, `headers`) and `with status`.
   **Neither is implemented.** `into req` is parsed and ignored. The request body is read up to 10 MB and thrown away.
   `with status` is a syntax error (PS:SPEC-DECISIONS.md:2883-2958 vs ServeCommand.cs:187).
3. `otter run` refuses route statements: "I do not know how to run a WebRoute statement yet." (exit 3). `otter web` refuses them:
   "Web server statements run with "otter serve", not inside a web page." (exit 2). (NET:src/Otter.Runtime/Interpreter.Network.cs:84-87;
   NET:src/Otter.Web/JsCompiler.Statements.cs:1078-1079.)
4. **The most capable server-side path today is the raw TCP server** (D113: `listen for tcp ... and call it server`,
   `on connection to`, `on data from`, `send ... through`). It runs as a real program under `otter run`, has full access to
   variables, SQLite, files and crypto, and **binds `127.0.0.1` by default**. I verified a dynamic HTML page, a SQLite-backed page and
   hand-written static file serving with it (s10, s11, s12). Everything above raw bytes is left to the program: HTTP parsing, framing,
   escaping and headers.
5. The **HTTP client** (`get` / `post` / `put` / `delete` / `download` / `start ... and call it`) is complete and certified on console
   and web. Missing: response status for synchronous calls, response headers, form/multipart encoders.
6. `otter web` is a **client-side** compiler: one standalone HTML page with inline JS, client-side routing with `route parameter`
   and `query parameter`, `fetch`, and WebSockets. It cannot include arbitrary JS or HTML. Text is HTML-escaped (textContent).
7. Absent everywhere: sessions, cookies (server side), templates with escaping, HTML/URL encoding functions, multipart parsing,
   form parsing, a WebSocket server, SSE, middleware, CORS/CSRF, TLS server ("listen securely for tcp" is reserved and refused),
   graceful shutdown, concurrency.

---

## 1. `otter serve <file.ot> [-Port N]` (D51)

**Status: PARTIAL. Static literal responses only.**

### 1.1 Syntax (checked with `otter check`, s01/s02/s04/s41/s43/s44)

```otter
api is a web server
    port is 18731
    host is "localhost"
.

when api receives GET at "/health"
    respond with "OK"
.
when api receives GET at "/api/users"
    respond with "Ada Lovelace" as json
.
when api receives POST at "/api/users" into req
    respond with "User created" as json and status 201
.
when api receives DELETE at "/api/users"
    respond with status 204
.
when api receives a request at "/any"        # any method
    respond with "any method"
.
when api receives at "/default-get"          # no method = GET
    respond with "default method"
.
when api receives PATCH at "/patch"          # any word is taken as the method
    respond with 42 as json
.
start api                                    # parsed, ignored by serve
listen on port 18745                         # only used when no `web server` object exists
```

Grammar: `NET:src/Otter.Language/Parser.Network.cs:404-433` (ReadWebRoute), `:435-465` (ReadRespond), `:467-495` (start), `:497-511`
(listen). Example: `PS:examples/api-server.ot`. Decision: `PS:SPEC-DECISIONS.md:2883` (D51).

**Parse traps (verified):**
- `respond with "go" and status 302` is a **syntax error**. Without `as json`, `and` is read as a logical *and*. Only
  `... as json and status N` and `respond with status N` work.
- `respond with "x" with status 201` is a syntax error. D51 documents it, but the parser only accepts `and status`
  (Parser.Network.cs:456-459 comment).
- `respond with ("x") ...`: parentheses are not Otter syntax.

### 1.2 Semantics (verified by running against loopback, s01/s02/s04/s44)

| Aspect | Behaviour | Source |
|---|---|---|
| Execution | Program never runs. Only top-level `web server` objects, routes and `listen on port` are collected. | ServeCommand.cs:60-92 |
| Response value | First `respond` in the body wins. Literal -> its text. Anything else -> node debug text (`Math@L5`, `Variable@L8`). Statements before it (`say`) do not run. A route with no `respond` sends 200 with an empty body. | ServeCommand.cs:244-262 |
| `as json` | `application/json; charset=utf-8`; the literal is JSON-encoded (`"Ada Lovelace"`, `42`) | :253-256 |
| Plain | `text/plain; charset=utf-8` | :243 |
| Status | Literal only. `and status code` (a variable) sends **200** (verified s04). `respond with status 302` sends 302 with **no Location header**, so no redirect is possible. | :250-252 |
| Methods | Any verb word. `a request` = ALL. HEAD is not mapped to GET (HEAD /health = 404). OPTIONS = 404. | :272 |
| Path matching | Exact and case-insensitive, with trailing `/` trimmed (`/HEALTH/` matches `/health`). Query string ignored (`/health?x=1` matches). No path parameters: `/users/:id` matches only the literal text `/users/:id`. A non-literal path (`at p`) becomes `/`. | :264-280 |
| Server name | Ignored. Every route is served by the first server's listener, including routes on undeclared or other servers (s44). | :101-103, 264 |
| Host/port | `port` and `host` must be literals. Default `localhost:8080`. `-Port N` overrides the port. Only the first `web server` object is used. | :66-103 |
| 404 | `{"error":"Not Found"}`, `application/json` (no charset) | :241 |
| Request body | Read and discarded. Over 10 MB (declared or streamed) gets `413 Request body too large.` (verified with `Content-Length: 20000000`). | :18, 176-200 |
| Errors | An exception while handling one request logs "Otter: a PUT request to /any could not be completed and was skipped. The server is still running." and sends 500 or aborts. A PUT with no body got `411 Length Required` from HttpListener plus that log line. | :212-221 |
| Request headers, query, form, JSON body, cookies | Not available | - |
| Response headers, cookies, redirects | Not available (no Location, no Set-Cookie, no custom headers) | - |
| Static files, MIME | None | - |
| Middleware / hooks | None | - |
| Concurrency | **Single-threaded synchronous loop**: `while (true) HandleNextRequest`. One client that stalls its body blocks everyone. Verified: a 1000-byte Content-Length with 3 bytes sent made a second request time out (4 s), until the stalled client went away. | :112, 173 |
| Shutdown | Ctrl+C or kill only. No graceful stop, no `stop` statement. | :110-118 |
| Imports | `use "file.ot"` is resolved (Modules.Resolve) | :30 |

### 1.3 Bind address and security

- The default host is `localhost`, giving an HttpListener prefix `http://localhost:PORT/`. **On macOS this bound `[::1]` only**
  (lsof: `[::1]:18741 IPv6`). Requests to `http://127.0.0.1:PORT` were **refused**. Requests with `Host: 127.0.0.1` sent to
  `[::1]` got the listener's 404 / 400 (Host-header check). Setting `host is "127.0.0.1"` bound IPv4 loopback (s43, verified).
- `host` is pasted verbatim into the prefix (ServeCommand.cs:128). `host is "*"`, `"+"` or `"0.0.0.0"` would request **all interfaces**.
  There is no warning and no allow-list. *Not run, by policy.* On Windows HTTP.sys, wildcard prefixes need a URL ACL or admin rights.
- No TLS, CORS, CSRF, auth, rate limit or timeouts (no request read timeout, see the concurrency row above).
- `Origin: http://evil.example` is answered normally. There are no CORS headers, so browsers cannot read cross-origin responses,
  but simple cross-site POSTs still reach the server.
- Path traversal: not applicable, because there is no file serving. `--path-as-is /../../etc/passwd` gives 404 JSON.
- Security review: PS:docs/OTTER_1_0_SECURITY_REVIEW.md:17-18 marks serve imports and "Desktop/server ... local listeners ... exposure
  and origin/token controls" as **FOLLOW-UP REQUIRED / not certified**. Capability matrix: "Desktop/server listener hosting ... BLOCKED BY
  ENVIRONMENT" (PS:docs/OTTER_1_0_CAPABILITY_MATRIX.md:16).

### 1.4 Tests
- PS:tests/Server.Tests.ps1 (78 lines): parses api-server.ot, dispatches routes in memory, and makes one live loopback request.
- NET:tests/cli/ps-texts.psd1:80-84, 130-139 (serve messages, port in use, -Verbose, empty program); NET:tests/cli/basics.psd1:23-26.
- NET:tests/compat/net_server.ot (`otter run` refusal messages); NET:tests/web/t3_err_server.ot, t3_err_server_route.ot (web refusal).

---

## 2. Raw TCP server as an HTTP server (D113): the real dynamic option today

**Status: SUPPORTED as TCP. HTTP is DIY.** Runs under `otter run` (the event loop keeps the program alive).

```otter
listen for tcp on "127.0.0.1" on port 18750 and call it server   # address optional; default 127.0.0.1
on connection to server
    conn is incoming connection
    on data from conn
        request is text from bytes received data
        ...
        send bytes from text reply through conn
        close tcp conn
    .
.
stop tcp server
```

- Default bind is `127.0.0.1` (NET:src/Otter.Runtime/Interpreter.Network.cs:782-815; verified s42 prints `127.0.0.1`). Any address
  can be given, including `0.0.0.0`.
- Properties: `local address of server`, `local port of server`, `server is listening/stopped`, `remote address of conn`, and others
  (Interpreter.Network.cs:349-400).
- `listen securely for tcp` is reserved and refused: "reserved for TLS servers, which Otter does not support yet" (Parser.Network.cs:62-67).
- Verified programs:
  - **s10_tcp_http.ot**: dynamic HTML with request path and a hit counter kept across requests. It returned
    `<h1>You asked for /hello?name=<script></h1>`. That is **reflected unescaped**: no HTML encoder exists (see section 6).
  - **s11_tcp_db.ot**: SQLite `:memory:` + parameterized `execute`/`query`, rendered as `<ul><li>first note</li></ul>`.
  - **s12_tcp_static.ot**: static files from `public/` via `bytes from file`, a hand-written `..` guard (400), and 404.
- Limits: no HTTP parser. One `on data` per TCP chunk, so a request can be split or merged, and there is no Content-Length framing.
  Handlers run one at a time on the event loop. No timeouts. String literals have **no `\r` escape**, so CRLF needs
  `text from bytes bytes from hex "0D0A"` (PS:docs/GRAMMAR.md:52-57). Text `plus` number is a runtime error: build lists and
  use `join`. Parenthesised expressions are not supported. Use intermediate variables.
- The web target refuses it: "TCP servers are not supported on the web target." (JsCompiler.Statements.cs:1059-1062).
- Tests: NET:tests/compat/net_tcp.ot, net_tcp_errors.ot, net_tls.ot.

---

## 3. WebSockets, SSE, timers

| Capability | Status | Syntax (checked: s40, s32, tests/web/t3_http_websocket.ot) | Notes |
|---|---|---|---|
| WebSocket **client** (console + web) | SUPPORTED | `connect to websocket "ws://localhost:18771/chat" [using protocol "chat"] and call it socket`; `on open of socket`; `on message from socket` + `received message`; `send "hi" through socket` (text or bytes); `close websocket socket [with code 1000] [and reason "done"]`; `on close of socket` + `close code`, `close reason`, `close was clean`; `on error of socket` + `websocket error`; `socket is open/connecting/closing/closed`; `state/url/protocol of socket` | Parser.Network.cs:72, 291-331, 838-886, 900-920; Interpreter.Network.cs:540-640. A close code the .NET enum does not name (e.g. 4000) marks the socket closed without sending a close frame (Interpreter.Network.cs:618-626). Tests: NET:tests/network/websocket_*.ot, tests/compat/net_websocket.ot |
| WebSocket **server** | ABSENT | - | NET:tools/network-test-server.js:1-4: "Otter has no WebSocket server of its own" |
| Server-sent events | ABSENT | - | No syntax. Not hand-writable over TCP without framing support (possible in principle over raw TCP) |
| Timers | PARTIAL | `start timer clock`; `elapsed time of clock`; `elapsed milliseconds of clock`; `wait 5 seconds` (console/desktop; services event sources while waiting, D121) | Stopwatch, not interval events. `wait` is refused on the web target (Parser.cs:667-672; SPEC-DECISIONS D101). Nothing interacts with `otter serve`, because serve runs no code |
| UDP, file watching | SUPPORTED (console) | `open udp ...`, `watch file ... and call it X` | Not web-related |

---

## 4. `otter web <file.ot> [-NoOpen]`: Otter to HTML/JS (D49/D50/D56/D60/D103)

**Status: SUPPORTED (client-side only).** NET:src/Otter.Cli/WebCommand.cs, NET:src/Otter.Web/*. Writes `<file>.html` (UTF-8 BOM) next to the source.
Without `-NoOpen` it opens the system browser (WebCommand.cs:73-82, 196).

- **Language subset**: the portable core (values, control flow, functions, lists/things, JSON, dates, random, try/fail, crypto
  hashes/random bytes), UI, the HTTP client, the WebSocket client and XML. Refused with a target diagnostic (JsCompiler.Statements.cs; grep
  `WebError`): databases, `start command`, TCP/UDP, file watching, credential vault, `wait`, `ask secretly`, `choose from`, coloured `say`,
  `show progress`, `set random seed`, UI animations, `memo`, `shared`, and server routes. File I/O compiles to calls to the **desktop bridge**.
  In a plain browser it throws "needs the Otter desktop application" (Templates.g.cs:1478-1484).
- **UI mapping**: resources `X is a page|window|button|text box|text|row|column|image|list|link|card|checkbox|dropdown|select|slider|
  text area|badge|canvas|table|scroll|progress|toggle|radio|dialog|panel ...` (WebCompiler.cs:20-28). Declarative D56 tags `page "T"`, `card`,
  `heading`, `text`, `[primary|secondary|danger] button`, `input`, `grid`, `section`, `sidebar`, `main`, `link`, `image`
  (Parser.Ui.cs:15-20). There are layout words (`layout form|row|column|grid N|centered|stack|sidebar|navbar|cards|split`), `gap`, `padding`,
  `background`, `round`, `width`, `height`, and events (`click`, `hover`, `press`, `enter`, `leave`). `layout form` is only CSS: **there is
  no `<form>` element or form submission**. Events: `when go is clicked`. Reads: `value of box`.
- **Escaping**: static text and titles are HTML-escaped at compile time (s30: `<script>alert(1)</script>` became
  `&lt;script&gt;...`, and the title `Sign up <b>&</b>` became `&lt;b&gt;&amp;...`). Runtime updates use `textContent` / `.value`
  (Templates.g.cs:640-654). No raw-HTML property was found. **No Content-Security-Policy** meta tag; all JS is inline.
- **Client-side routing (D103)**: `route "/" shows homePage`, `route "/users/:id" shows userPage`, `route otherwise shows notFoundPage`,
  `go to "/about"`, `go back`, `go forward`, `replace route with "/login"`, `on route change`, `current route`, `route parameter "id"`,
  `query parameter "tab"` (s31, Parser.Ui.cs:645-660; Templates.g.cs:1894-1965, using history.pushState and popstate). Deep links need a host that
  rewrites every path to the page. There is no such host in Otter.
- **fetch**: `get/post/put/delete/start ...` compile to `fetch()` (JsCompiler.Statements.cs:100-215). The options map as follows:
  headers -> `headers`, `with cookies` -> `credentials:'include'`, `without cookies` -> `'omit'`, `without redirects` -> `redirect:'manual'`,
  timeout -> AbortController. CORS is whatever the browser enforces.
- **CSS/JS inclusion**: a sidecar `<entry>.css` next to the program is inlined as `<style id="otter-sidecar-style">`. Symlinks
  outside the folder are refused (WebCommand.cs:138-160; tests/web/sidecar/styled.ot). There is **no way to include external JS, script
  tags, npm modules or arbitrary HTML**.
- **`otter desktop`** (Windows only; on macOS: "desktop windows are only available on Windows", exit 1, verified): compiles the same
  page, then starts an **authenticated loopback bridge** (HttpListener on `127.0.0.1:<random>`). It uses a 256-bit token in
  `X-Otter-Token` / Bearer / `?token=`, an Origin allow-list (loopback + `null`) and a heartbeat. The page opens in an Edge/Chrome `--app` window.
  Endpoints: `/api/fs/read|write|download|files|folders|operate`, `/api/terminal/exec|profiles|ping`, `/api/system/clipboard|notify|env`,
  `/api/dialog/*`, `/api/session/heartbeat` (NET:src/Otter.Cli/DesktopCommand.cs:212-560).
  Note: `InCwd` accepts absolute paths and `..` (DesktopCommand.cs:562). The token is the only boundary, and `/api/terminal/exec` runs shell
  commands. This is a local-app bridge, not a web server.
- Tests: NET:tests/web/*.ot (92 programs, compared with otterPS output via tools/compare-web*.ps1), PS:tests/Web.Tests.ps1,
  WebRuntimeUi.Tests.ps1, PS:conformance/http/http_web_fetch.ot.

---

## 5. Outbound HTTP client (D49, D96, D101, D116A/B)

**Status: SUPPORTED, console (.NET HttpClient) and web (fetch).** Certified by PS:tests/Http.Tests.ps1. Scope matrix:
PS:docs/OTTER_1_0_RELEASE_SCOPE_MATRIX.md:48. (PS:docs/OTTER_1_0_HTTP_TARGET_PARITY.md is marked **superseded**.)

```otter
get "http://127.0.0.1:18760/hello" into page
get json from "http://127.0.0.1:18760/json" into data
get "http://127.0.0.1:18760/json" as json into data2
post user to URL into answer               # a thing/list is sent as JSON automatically
post "a=1&b=2" to URL into answer2         # text/plain; charset=utf-8
post user as json to URL into answer3
put "x" to URL into answer4
delete from URL into answer5
get URL into hdr
    with header "X-A" is "1"
    with cookies | without cookies
    following redirects | without redirects
    with timeout 5 seconds
download file from URL to "copy.txt"
start get from URL [as json] and call it req     # also start post/put/delete
on complete of req / on error of req / on cancel of req
cancel req
state of req / status of req / response of req / error of req / received response
```
(Parser.Network.cs:525-835; Http.cs; verified against a local Python server on 127.0.0.1 in s20.)

Observed behaviour (s20):
- A synchronous `get` of a 404 or 500 **returns the body without an error**. The status is not exposed for synchronous calls. Only
  `status of req` on a started request gives it (404 seen).
- A shared cookie jar for the life of the program (`Set-Cookie` sent back on the next request). `without cookies` sends none.
- Redirects are followed by default (max 10). `without redirects` returns the empty 302 body.
- POST of a thing gives `application/json; charset=utf-8`, pretty-printed in PowerShell `ConvertTo-Json` style.
- Header injection is guarded: "HTTP header names and values cannot contain line breaks (CR or LF) or NUL characters."
  (Http.cs:91-96).
- Only http/https. `/relative` URLs are refused on console. Downloads never overwrite and use a temp file, then a move (Http.cs:207-333).
- Missing: response headers, a status on synchronous calls, form-urlencoded or multipart builders (you can override `Content-Type` by
  header with a hand-built body), URL encoding, a TLS client-cert option and proxies.

---

## 6. Templates, text substitution, encoding

| Need | Status | What exists |
|---|---|---|
| HTML templating | ABSENT | Idiom: `read "page.tpl" into page` + `replace "{{name}}" with name in page` (s50, verified). **No escaping**: `<b>Ann</b>` is inserted raw |
| HTML encode / attribute encode | ABSENT (console) | Only the web compiler escapes its own UI text |
| URL encode / decode | ABSENT | The web router decodes route params (decodeURIComponent) internally |
| Base64 / hex | SUPPORTED | `bytes from base64 "..."`, `base64 from bytes X`, `hex from bytes X`, `text from bytes X` (Parser.cs:1792-1830) |
| JSON | SUPPORTED | `convert X to json into t`, `convert t from json into X`; `respond ... as json` (literal only) |
| CSV, XML | SUPPORTED | `convert rows to csv`; XML D105 |
| Date formatting | SUPPORTED | `format d as "MM/dd/yyyy" into s` |
| String building | PARTIAL | `plus` joins text+text only. `join list with "" into s`. No interpolation, no `\r` escape |
| Crypto for sessions | SUPPORTED | `secure random bytes 32`, `sha256 of BYTES`, `hmac sha256 of BYTES using KEYBYTES`, `hash password P and call it h`, `password P matches hash h`, `encrypt/decrypt ... with key`, `generate encryption key and call it k` (s50, verified. HMAC/SHA need bytes, not text) |

---

## 7. Project scaffolding (D118B)

- `otter new web <name>` (verified): creates `main.ot` (a page + text), `main.css` (picked up as the sidecar), `otter.json`,
  `tests/app_test.ot` and an empty `assets/`. Archetypes are only `console, desktop, web, automation, game`. **`otter new server` gives
  "Unknown archetype "server""** (NET:src/Otter.Cli/ProjectCommands.cs:13, 108-198).
- `otter.json` fields: `name`, `version`, `archetype`, `target` (`console|desktop|web|game|server|automation`), `entryPoint`
  (or `main`), `assets[]`, `build.outputDir` (must stay inside the project), `build.clean`, `build.sourceMaps`/`minify` (type-checked only),
  `publish.outputDir`, `scripts{start,test,check}` (NET:src/Otter.Cli/Project.cs:200-300). No port, host or route config.
- `otter build` (web): `dist/index.html` (sidecar CSS inlined) + `dist/otter.build.json`. `otter publish`: `publish/<name>-<ver>.zip`
  + `.sha256` + `otter.publish.json` (verified).
- Target `server`: `otter check .` works and `otter serve .` serves the entry point (verified on port 18744). **`otter build` fails:
  "target "server" is not supported for build."** (ProjectBuild.cs:207; NET:tests/cli/build.psd1:28-29). `otter run .` stops at the first route.

---

## 8. Security properties: present vs absent

**Present**
- Web target: compile-time HTML escaping of UI text; runtime `textContent`; refusals with target diagnostics for capabilities
  the browser lacks; the sidecar CSS symlink guard.
- HTTP client: CR/LF/NUL header-injection guard; only http/https; download never overwrites; timeouts.
- Serve: 10 MB body cap (413); one bad request does not kill the server; `localhost` default (loopback).
- TCP server: `127.0.0.1` default bind.
- Desktop bridge: loopback only, a 256-bit per-session token, an Origin allow-list, heartbeat expiry.
- SQLite: parameterized queries (`parameter "title" is x`) avoid SQL injection.
- Crypto: CSPRNG, HMAC, password hashing (useful building blocks for sessions/CSRF tokens).

**Absent**
- Server: request access, cookies, sessions, Set-Cookie flags (HttpOnly/Secure/SameSite), CSRF tokens, CORS policy, auth,
  security headers (CSP, X-Content-Type-Options, frame options), HTTPS/TLS server, rate limiting, request read timeouts
  (one stalled client blocks the server), concurrency, graceful shutdown, an explicit warning on a non-loopback `host`.
- Templates/encoding: no HTML/attribute/URL encoder, so any hand-built HTML (TCP server, templates) is XSS-prone by default
  (verified reflection in s10).
- Web output: no CSP and inline-only scripts.
- Both the security review and the capability matrix list server/bridge listeners as **not certified** (PS:docs/OTTER_1_0_SECURITY_REVIEW.md:17-18).

---

## 9. PHP-style use cases: existing support and gaps

| Use case | `otter serve` today | Best existing route | Gap |
|---|---|---|---|
| Route a URL | Exact literal path + method, static reply | serve (static) / TCP server (DIY parse) | Path params, query access, dynamic handlers, HEAD/OPTIONS, 405 |
| Read a form (urlencoded) | No (body discarded) | TCP: raw bytes, then hand-split on `&` and `=` | Body access, form parser, URL decoding |
| JSON API | Literal JSON out only | TCP + `convert ... from json` / `to json` | Dynamic JSON responses, request JSON parsing, proper status from expressions |
| Sessions | No | TCP + `secure random bytes` + an in-memory thing (DIY) | Session store, cookie API |
| Cookies | No | TCP: hand-write `Set-Cookie` / parse `Cookie` | Cookie read/write API, flags |
| DB-backed page | No (code never runs) | TCP + SQLite (s11, verified) | Integration with serve; HTML escaping |
| File upload | No | TCP: raw bytes only. Multipart not parsed. Chunking makes it unreliable | Multipart parser, streaming, size limits per field |
| Template rendering | No | `read` + `replace` (s50), no escaping | Templating with auto-escaping, layouts/partials |
| Static site | No static files | `otter web` + `otter build` gives a static `index.html` (needs an external host); TCP static (s12) | Static file handler with MIME types and traversal protection; SPA fallback |
| Redirects | `respond with status 302` without Location (useless) | TCP: hand-write `Location:` | Redirect/response-header API |
| Auth | No | TCP + `hash password` / `password ... matches hash` + HMAC | Auth middleware, session binding, CSRF |
| Outbound API calls | n/a | HTTP client (fully supported) | Sync status/headers |
| Realtime | No | WebSocket **client** only | WebSocket server, SSE |

---

## 10. Verified sample programs (`docs/web/samples/web/`)

| File | `otter check` | Run result |
|---|---|---|
| s01_api_server.ot | valid | serve on [::1] verified: GET/POST(201)/DELETE(204)/PATCH/`a request`/default GET, case-insensitive and trailing-slash match, query ignored, 404 JSON, HEAD/OPTIONS 404, 413 at 20 MB, PUT without body gives 411 + skip log |
| s02_dynamic_respond.ot | valid (after removing `"go" and status 302`, which failed) | `/dyn` -> `Math@L5`, `/var` -> `Variable@L8`, first respond wins, empty route -> 200 "", `/users/42` -> 404, 302 without Location |
| s03_with_status.ot | **syntax error** (`with status`) | - |
| s04_status_expr.ot | valid | `as json and status code` -> 200 (variable ignored); `respond with status 302` -> 302 |
| s05_text_status.ot | **syntax error** (parentheses) | - |
| s10_tcp_http.ot | valid | dynamic HTML over raw TCP, unescaped reflection, counter state |
| s11_tcp_db.ot | valid | SQLite-backed page |
| s12_tcp_static.ot | valid | static file 200 / 404 / `..` 400 |
| s20_http_client.ot | valid | all client forms against local Python server (pyserver.py, 127.0.0.1:18760) |
| s30_web_form.ot | valid | `otter web -NoOpen` OK; escaping and fetch confirmed in HTML |
| s31_web_routes.ot | valid | `otter web -NoOpen` OK (routes, params, query params) |
| s32_web_ws.ot | valid | `otter web -NoOpen` OK |
| s40_ws_client.ot | valid | not run (no WS server) |
| s41_listen_forms.ot | valid | `listen on port 18745` used as the port with no `-Port` |
| s42_tcp_default.ot | valid | prints `127.0.0.1` |
| s43_host_v4.ot | valid | `host is "127.0.0.1"` binds IPv4 loopback |
| s44_edge.ot | valid | variable path -> `/`; server names ignored |
| s50_template.ot | valid (after fixing HMAC to bytes) | template replace (raw), random/HMAC/SHA/password hash |
| proj/site1 (`otter new web`) | valid | build + publish OK |
| proj/srv (target server) | valid | `serve .` OK, `build` fails, `run .` exit 3 |

Helper: `run_server.sh <file> <port> <requests.sh>`. It starts serve under `perl alarm 30`, prints the listening address, runs the requests
and kills the server. Logs: `serve-<port>.log`. After the run, no otter/python processes or 187xx listeners remained.

---

## 11. Notes for design work

- `otter-net/otter-web-plan/` already holds an **unapproved proposal** ("Otter Web SDK"). Its DECISIONS.md lists ADRs WEB-001..007:
  reuse vs extension of serve, route/request API, template escaping, SQLite, sessions/auth, and WebSocket. This inventory is its Phase 0 input.
- Any change to `otter serve` semantics conflicts with byte compatibility. The frozen reference sends `[string]$node` for
  non-literals (PS:src/Otter.Server.psm1:136-140), and otter-net reproduces this deliberately. New behaviour likely needs a new
  command/target or a version gate, not a fix in place.
- Existing reserved surface to reuse: `when S receives METHOD at PATH into req`, `respond with ... as json and status N`,
  `listen on port`, `start S`, `web server` objects, `route parameter`/`query parameter` (web), `listen securely for tcp` (reserved for TLS).
