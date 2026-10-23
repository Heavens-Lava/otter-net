# Otter Web: Phase 0 audit

**What Otter 1.0.1 (otterPS) and otter-net support today for web
development, what is missing, and what was found on the way.** Read-only
audit, 2026-10-22/23, branch `web/phase0-audit` from `main` (`1635929`;
certified code `f4e5134`). No code, grammar or otterPS file was changed.
Brief: [otter-web-plan/](../otter-web-plan/AGENT_MASTER_PROMPT.md).

Evidence is in:
- [web/SYNTAX_COVERAGE.md](web/SYNTAX_COVERAGE.md): 338 constructs in 17
  categories.
- [web/inventory/WEB_SERVER_AND_HTTP.md](web/inventory/WEB_SERVER_AND_HTTP.md):
  server, HTTP and the web compiler.
- [web/inventory/DATA_SECURITY_AOT.md](web/inventory/DATA_SECURITY_AOT.md):
  data, security, .NET and Native AOT.
- [web/FRONTEND_COMPATIBILITY.md](web/FRONTEND_COMPATIBILITY.md).
- [web/GAME_DEVELOPMENT_GAPS.md](web/GAME_DEVELOPMENT_GAPS.md).

Every syntax claim was checked with the otter-net Native AOT binary. 145
sample programs live in [web/samples/](web/samples/): 91 must pass
`check` (6 of them the proposal examples), and 54 are documented forms
that 1.0.1 rejects.
`sh docs/web/samples/check.sh <otter-net>` re-verifies all of them.

## 1. Headline findings

1. **Otter has no server-side web runtime today.** `otter serve` is a
   static route table: the program is checked but never run, and each
   route sends its first `respond with`. Only literal values are real. Any
   other value is sent as parser node text: `respond with scores as json`
   returns `"Variable@L13"`. `otter run` refuses routes. otterPS 1.0.1
   behaves the same way (`src/Otter.Server.psm1:136-140`), so this is
   certified 1.0.1 behaviour, not a port bug.
2. **The grammar for a real server already exists and is certified** (D51):
   - `X is a web server`;
   - `when X receives METHOD at PATH into req`;
   - `respond with V as json and status N`.

   D51 also decides the request object: `method`, `path`, `query`, `body`
   and `headers`. A complete API (routes, path parameters, query, headers,
   JSON and form bodies, status, redirects, cookies) can be written in
   today's grammar and **passes `otter-net check`**
   ([web/samples/proposals/](web/samples/proposals/)). **Phase 1 needs no
   grammar change**, only runtime meaning behind an opt-in.
3. **The only real server today is the raw TCP server** (D113,
   `listen for tcp ... and call it server`). It runs under `otter run` and
   binds `127.0.0.1` by default. HTTP parsing, framing and escaping are
   left to the program, and a test page reflected `<script>` unescaped.
4. **Building blocks exist:**
   - the HTTP client (console and browser);
   - JSON and CSV;
   - SQLite with named parameters and transactions;
   - password hashing (PBKDF2-SHA256, 600k iterations);
   - secure random bytes, HMAC and encryption;
   - `use` modules, `otter.json` and `otter test`.

   An HTML-escaping library and a SQLite session store written in plain
   Otter **run today** (`html_demo.ot`, `sessions_demo.ot`).
5. **Missing for the web:**
   - request access, cookies, static files and a MIME table;
   - templates with escaping;
   - HTML and URL encoders;
   - a WebSocket server, SSE, CSRF, CORS, TLS server, concurrency and
     graceful shutdown;
   - frontend tooling (no npm, Vite, TypeScript, React, Three.js or R3F
     anywhere, which is correct: they stay external);
   - any game runtime (frame loop, input events with payloads, drawing,
     audio, 3D).

## 2. Security findings (reported first)

| # | Finding | Where | Status |
|---|---|---|---|
| **S-1** | **High severity: a vulnerability in the experimental query language (D99)**, inherited from otterPS 1.0.1. Technical details are withheld until a coordinated fix is available | query language runtime | reproduced on the preview binary; fix approved (security exception to 1.0.1 parity); see `SECURITY_LEDGER.md` |
| S-2 | `contains` / `starts with` / `ends with` do not escape `%` and `_` (`contains "%"` matched every row) | `Queries.cs:152-164` | wildcard injection, low severity |
| S-3 | `otter serve` binds `host` verbatim (`"0.0.0.0"` and `"*"` expose all interfaces with no warning); one stalled client blocks every request; no read timeouts | `src/Otter.Cli/ServeCommand.cs:112-128` | serve is a stub, and the security review already lists listeners as "not certified" |
| S-4 | No HTML encoder, so hand-built HTML (TCP server, `replace` templates) is XSS-prone by default | - | the `escapeHtml` library proposal (tier B) |
| S-5 | Generated web pages have no CSP, and their reactivity uses `new Function` with `with` | `src/Otter.Web/Templates.g.cs:781,1980` | a strict CSP needs `unsafe-eval` |
| S-6 | The desktop bridge's `InCwd` accepts absolute paths and `..`; the 256-bit token is the only boundary, and `/api/terminal/exec` runs commands | `src/Otter.Cli/DesktopCommand.cs:562` | local-app design (Windows, loopback), worth a review |
| S-7 | D97 `execute` silently runs only the first of several statements; bytes parameters are stored as the text "OtterBytes"; BLOBs read back as text; integers above 2^53 lose precision | `Sqlite.cs`, `Database.cs` | correctness, data-loss risk |

No deployment or release action is in progress. S-1 is also in the
internal preview; it needs a specific program pattern in the experimental
query language. The fix is planned before any web feature exposes data
queries.

## 3. Feature matrix (pack Phase 0 item A)

| Capability | Status | Existing syntax / mechanism | Evidence |
|---|---|---|---|
| Static files | **absent** (copy-only: `otter.json` `assets` → `dist/` by `otter build`) | - | inventory §7; FRONTEND §2.7 |
| HTTP server / routes | **stub** (literal replies, exact paths, one request at a time) | D51 grammar | inventory §1 |
| Request access (query, body, headers) | **absent** (decided in D51, never implemented) | `into req` parses | inventory §1.2 |
| JSON | supported | `convert X to json into t`, `convert t from json into X`, `get json from URL` | SYNTAX §14 |
| Forms | **absent** server side; web pages have inputs but no `<form>` | - | inventory §4, §9 |
| Cookies | client side only (HTTP client cookie jar) | `with cookies` | inventory §5 |
| Sessions | **absent** (building blocks exist: secure random, SQLite) | tier B library runs today | proposals §4 |
| Databases | SQLite raw SQL with parameters and transactions (D97; listed DEFERRED in the 1.0 scope matrix, but works); OQL experimental (S-1) | `connect`, `execute db with`, `query db with`, `parameter "x" is v` | DATA §1 |
| Templates | **absent** (`replace` idiom, no escaping) | - | inventory §6 |
| WebSockets | client only (console and web) | `connect to websocket ... and call it s` | inventory §3 |
| CLI scaffolding | `otter new web` (static page), `build`, `publish`; no `server` archetype; `build` fails for the `server` target | - | inventory §7 |
| Frontend build tools | none (correctly external) | - | FRONTEND §0 |
| TLS server | absent (`listen securely for tcp` is reserved and refused) | - | inventory §2 |
| Concurrency | one interpreter thread with a polling event loop | - | DATA §2 |

## 4. PHP-style use cases → existing support → gap (item B)

| Use case | Today | MVP gap | Security-critical | Future |
|---|---|---|---|---|
| Route a URL, read query/path | stub | dynamic handlers, path parameters, query | bind address, limits | - |
| JSON API | literal JSON only | dynamic JSON, `json of req`, status from expressions | body limits, malformed JSON → 400 | OpenAPI |
| Form post | no | `form of req`, 303 redirect | CSRF, `location` validation | multipart uploads |
| Server-rendered page | TCP DIY | `web response` with `text/html`; `escapeHtml` | XSS (escape by default) | template engine with layouts |
| DB-backed page | TCP + SQLite | runtime + D97 | parameters only; S-1 | migrations |
| Sessions / login | no | cookies, session store, password hash | cookie flags, rotation, CSRF | roles |
| Static site / SPA | no | `static`, MIME types, SPA fallback | traversal, symlinks, nosniff | range requests, compression |
| Realtime | client only | - | origin/auth checks | WebSocket server (grammar) |
| File upload | no | - | size/type limits | multipart |

## 5. .NET architecture, licences, Native AOT (item C)

- otter-net has five projects and **no package references**. Only the
  `otter` CLI is published as Native AOT. SQLite comes from the operating
  system (`winsqlite3.dll`, `libsqlite3.dylib`, `libsqlite3.so.0`) through
  `LibraryImport`.
- Native AOT scratch builds (SDK 10.0.402, osx-arm64, warnings as errors,
  no warnings):

  | Build | Size |
  |---|---|
  | Hello | 1.10 MB |
  | `HttpListener` + System.Text.Json source generation + server-side WebSocket + PBKDF2 | 4.14 MB |
  | ASP.NET Core `CreateSlimBuilder` (Kestrel) minimal API | 9.00 MB |

- Both server options are part of .NET (MIT): no third-party binary and no
  licence issue.
  - `HttpListener` is legacy, HTTP/1.1 only, has weak timeout and
    slowloris control, and uses http.sys with URL ACLs on Windows.
  - Kestrel is the supported server: limits, timeouts, HTTP/2, TLS and
    WebSockets.
- Not usable offline or not wanted:
  - `Microsoft.Data.Sqlite` would bundle third-party native binaries,
    against the project's rule.
  - Argon2 needs a third-party package (licence and AOT review).
- Test harnesses to reuse:
  - `tools/compare*.ps1` (differential against otterPS);
  - `tests/network` with `tools/network-test-server.js`;
  - the six-platform `certify.yml`.

  A web runtime needs a new black-box HTTP suite. otterPS has no reference
  behaviour for it, so its expected results are specified rather than
  differential.

## 6. Documentation discrepancies found (otterPS, not changed)

- `docs/SPECIFICATION.md` is marked "Frozen & Authoritative", but shows
  forms the grammar and both implementations reject: `to f with a, b`,
  `make` as return, `player's name`, `read file … into`, parentheses,
  `modulo`, and others (SYNTAX_COVERAGE gaps). GRAMMAR.md,
  SPEC-DECISIONS, the tests and the implementations agree with each other.
- D51 documents `with status N`, which the parser rejects. TUTORIALS §6
  (REST backend) and §8 (game) use syntax that does not parse. The
  platform checklist marks a game loop, keyboard input and WebGL as done
  with no syntax behind them.
- D97–D99 are marked deferred, yet parse and run in 1.0.1.

## 7. Recommendation

Proceed with the plan in [OTTER_WEB_PLAN.md](OTTER_WEB_PLAN.md):

- an **opt-in web runtime** behind the existing D51 grammar (ADR
  [WEB-001](adr/WEB-001-reuse-vs-extension.md));
- the API in [web/WEB_SYNTAX_PROPOSALS.md](web/WEB_SYNTAX_PROPOSALS.md)
  (ADR [WEB-SDK-API](adr/WEB-SDK-API.md));
- a first PR limited to the secure HTTP core.

Owner decisions are needed before implementation: WEB-001, the API (Q-W1
to Q-W4), S-1, and the server stack (Kestrel or HttpListener).
