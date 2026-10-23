# Otter Web: proposed APIs (Phase 0, for owner review)

**Status: proposal. Nothing here is implemented, and nothing changes the
certified Otter 1.0.1 grammar.** Every example below is a real file in
[samples/proposals/](samples/proposals/) that passes `otter-net check`
(otter-net 0.9.0-preview.1, baseline `f4e5134`). Examples are labelled by
what they need:

| Tier | Meaning | Approval needed |
|---|---|---|
| **A: existing grammar, new runtime meaning** | parses today; the proposed runtime gives it behaviour (today `otter serve` only sends literals and `otter run` refuses routes) | runtime semantics (ADR), no grammar change |
| **B: plain Otter library** | written in today's Otter; **runs today** with no runtime change | none for the code; packaging as an SDK module needs the WEB-001 decision |
| **C: needs new grammar** | not Otter syntax; shown only as `text` pseudocode, never as `otter` | grammar change through SPEC-DECISIONS + owner approval |

The inventory this builds on: [SYNTAX_COVERAGE.md](SYNTAX_COVERAGE.md),
[inventory/WEB_SERVER_AND_HTTP.md](inventory/WEB_SERVER_AND_HTTP.md),
[inventory/DATA_SECURITY_AOT.md](inventory/DATA_SECURITY_AOT.md).

## 0. How it stays compatible: an opt-in runtime

Otter 1.0.1 already reserves a web-server surface (SPEC-DECISIONS **D51**):
`X is a web server`, `when X receives METHOD at PATH [into req]`, `respond
with V [as json] [and status N]`, `start X`, `listen on port N`. D51 even
decides the request object (`method`, `path`, `query`, `body`, `headers`).
But the certified behaviour is a stub, in otterPS and otter-net alike:
`otter serve` answers each route with its first `respond`, literal values
only (anything else is sent as node text such as `Variable@L13`), and
`otter run` refuses routes ("I do not know how to run a WebRoute
statement yet").

Changing that in place would change certified 1.0.1 output. **Proposal
(ADR WEB-001):** the real runtime is **opt-in per project**, so every
existing program and command behaves byte-for-byte as today:

- a project declares it in `otter.json` (proposed field, e.g.
  `"web": { "runtime": "otter-web-0.1" }` with `"target": "server"`);
- in such a project, `otter-net run .` starts the declared web server and
  runs route bodies for real (the event loop already keeps programs with
  event sources alive);
- without the field: unchanged (`otter serve` = 1.0.1 stub, `run` refuses).

No new keyword, no parser change. A manifest field is new project
metadata, not language syntax (it is still a contract decision).

## 1. Server, routes, request, response (tier A)

[samples/proposals/server_api.ot](samples/proposals/server_api.ot), valid:

```otter
use "html.ot"

site is a web server
    port is 8080
    host is "127.0.0.1"
    static is "public"
    maxbody is 1048576
.

when site receives GET at "/hello" into req
    name is name of query of req
    if name is ""
        name is "stranger"
    .
    safe is escapeHtml name
    page is "<h1>Hello, " plus safe plus "</h1>"
    reply is a web response
        status is 200
        type is "text/html"
        body is page
    .
    respond with reply
.

when site receives GET at "/api/users/:id" into req
    id is id of parameters of req
    user has
        id is id
        name is "Ada"
    .
    respond with user as json
.

when site receives POST at "/api/notes" into req
    note is json of req
    title is title of note
    if title is ""
        respond with "title is required" as json and status 400
    .
    respond with note as json and status 201
.

when site receives GET at "/old"
    reply is a web response
        status is 302
        location is "/hello"
    .
    respond with reply
.
```

### 1.1 The server object

| Property | Proposed meaning | Default | Security requirement |
|---|---|---|---|
| `port` | TCP port | 8080 (as D51) | port in use: the existing 1.0.1 message |
| `host` | bind address | `"127.0.0.1"` (loopback; today's `localhost` binds only `[::1]` on macOS) | a non-loopback host (`"0.0.0.0"`, `"*"`) refused unless the manifest also opts in explicitly, with a warning on start |
| `static` | folder served as static files | none | canonical path inside the project, symlink escapes refused, no directory listing, MIME allow-list, `X-Content-Type-Options: nosniff` |
| `maxbody` | request body limit in bytes | 1 MiB (today's stub: 10 MB) | 413 above it; also header-count/size and read timeouts |

Property names must be single words: `public folder is "x"` is a syntax
error in 1.0.1 (checked), so multi-word names are not available.

### 1.2 Routes

`when S receives METHOD at "PATH" [into req]` (D51). Proposed runtime rules:

- methods `GET POST PUT PATCH DELETE OPTIONS HEAD` (D51's list) and `a
  request` (any); `HEAD` answered by the `GET` route without a body;
- **path parameters** `:name` in the literal path (`/api/users/:id`):
  matched by segment, URL-decoded, exposed as `parameters of req`; a
  literal path without `:` stays an exact match (today's behaviour);
- precedence: literal segments before parameters, then declaration order;
- unknown path: 404 (today's body `{"error":"Not Found"}` kept); known path,
  wrong method: **405** with `Allow` (today: 404).

### 1.3 The request: `into req`

| Expression (all check today) | Proposed value | Decided by |
|---|---|---|
| `method of req`, `path of req` | text | **D51** |
| `query of req` | a thing of query values (duplicates: the first; open question) | **D51** |
| `body of req` | the raw body as text | **D51** |
| `headers of req` | a thing; names case-insensitive; read one with `get "User-Agent" from headers of req into agent` (D41 dynamic thing access) | **D51** |
| `parameters of req` | path parameters | proposal |
| `json of req` | the body parsed as JSON (as `convert ... from json`); a malformed body answers **400** before the handler runs | proposal |
| `form of req` | `application/x-www-form-urlencoded` fields as a thing | proposal |
| `cookies of req` | request cookies as a thing | proposal |
| `remote of req` | the client address (proxy headers trusted only when configured) | proposal |

A missing property (`name of query of req` with no `?name=`) should give
`""` so the `if name is ""` idiom works; Otter 1.0.1's rule for a missing
thing property must be confirmed for this object (open question Q-W3).

### 1.4 The response

- `respond with V` - text/number: `text/plain; charset=utf-8` (as today);
  `respond with V as json` - JSON (as today, but for any value, not only
  literals); `and status N` - any expression (today: literals only; D51
  says `codeExpr`). D51 also documents `with status N`, which the 1.0.1
  parser rejects (checked): fixing that is a grammar change (tier C), not
  proposed.
- `reply is a web response` + properties, then `respond with reply`: the
  same `X is a TYPE` object form as `site is a web server`. Properties:
  `status`, `type` (Content-Type), `body`, `location` (redirect),
  `cookie` (a thing: `name`, `value`, `path`, `maxage`, `httponly`,
  `secure`, `samesite`), `headers` (a thing).
- Security: header names/values with CR/LF/NUL refused (the HTTP client
  already does this, `Http.cs:91-96`); `location` must be a relative path
  or an allow-listed origin (open redirects); cookies default to
  `HttpOnly`, `SameSite=Lax`, and `Secure` behind HTTPS; every response
  gets `X-Content-Type-Options: nosniff`.

Runtime needed: an HTTP host that runs Otter handlers concurrently with
the existing single-threaded interpreter (requests queued to the event
loop; one slow handler must not block reading other requests - today's
stub serves one request at a time and a stalled client blocks everyone),
graceful shutdown on Ctrl+C, and access logs with request ids.

## 2. HTML output and templates

### 2.1 Escaping (tier B, runs today)

[samples/proposals/html.ot](samples/proposals/html.ot):

```otter
to escapeHtml raw
    replace "&" with "&amp;" in raw into s1
    replace "<" with "&lt;" in s1 into s2
    replace ">" with "&gt;" in s2 into s3
    replace "\"" with "&quot;" in s3 into s4
    replace "'" with "&#39;" in s4 into s5
    return s5
.
```

`samples/proposals/html_demo.ot` runs today and prints
`&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;friends&quot;`.
Note: in `"<b>" plus escapeHtml name of row plus "</b>"` the call takes
`name of row` only (checked by running), so markup around it stays
markup.

### 2.2 Templates

- **Today (tier B):** `read "page.html" into page`, then `replace
  "{{name}}" with safe in page into page2` with values escaped first (as
  above). Workable for small pages; nothing enforces escaping.
- **Proposed (tier A runtime + tier B library):** an SDK `template`
  library function `render "page.html" with values` where every `{{x}}` is
  **escaped by default** and raw output needs an explicit, greppable marker
  (`{{{x}}}`); layouts/partials by file include; no code execution inside
  templates. Expressed as an Otter function so no grammar changes.
- **Tier C (not proposed now):** template syntax inside Otter source, or
  string interpolation (SPEC-DECISIONS D8 says Otter has "no interpolation
  syntax").

## 3. Databases (exists: D97 raw SQL; tier A fixes)

Supported today and verified ([samples/data/db1.ot](samples/data/db1.ot)):

```otter
connect "app.db" into db
execute db with
    "insert into users (name, email, age) values (@name, @email, @age)"
    parameter "name" is evil
    parameter "email" is "a@b.c"
    parameter "age" is 42
into result
query db with
    "select id, name, age from users where name = @name"
    parameter "name" is evil
into rows
begin transaction on db into tx
commit tx
```

Requirements before web use (defects found by the audit, inherited from
otterPS 1.0.1):

1. **Security (S-1):** a high-severity vulnerability in the experimental
   query language (D99), inherited from otterPS 1.0.1; details withheld
   until a coordinated fix is available (`../SECURITY_LEDGER.md`). The fix
   is approved as a security exception to 1.0.1 parity.
2. `contains`/`starts with` do not escape `%`/`_` (wildcard injection).
3. `execute` runs only the first statement of a multi-statement string,
   silently; bytes parameters are stored as the text "OtterBytes"; BLOBs
   read back as text; integers above 2^53 lose precision.
4. Web defaults: `busy_timeout`, WAL, one connection per request or a
   pool, migrations as numbered `.sql` files applied in a transaction
   (WEB-004).

## 4. Sessions, cookies, authentication

### 4.1 Session store (tier B, runs today)

[samples/proposals/sessions.ot](samples/proposals/sessions.ot) - the browser
holds only a random 256-bit id; data stays in SQLite:

```otter
to newSession db and user
    token is hex from bytes secure random bytes 32
    execute db with
        "insert into sessions (id, user, created) values (@id, @user, datetime('now'))"
        parameter "id" is token
        parameter "user" is user
    into ignored
    return token
.
```

`sessions_demo.ot` runs today: token length 64, lookup `ada`, empty after
logout.

### 4.2 Passwords (exists, verified)

```otter
hash password "correct horse" and call it stored
if password "correct horse" matches hash stored
    say "password ok"
.
```

PBKDF2-SHA256, 600,000 iterations, constant-time comparison (`Crypto.cs`).
Argon2id is not in the .NET BCL; adding it means a third-party
dependency (license and AOT review) - not proposed for Phase 3.

### 4.3 What the runtime must add (tier A)

`cookies of req` and the response `cookie` property (section 1.4);
session rotation on login (new id, old deleted); logout deletes the row;
**CSRF** tokens for cookie-authenticated POST/PUT/PATCH/DELETE (a
per-session token checked by the runtime, or `SameSite=Lax` plus an
`Origin` check as the first line); per-route authorization stays in the
handler (`if sessionUser db and token is ""` -> 401/303).

## 5. Realtime: WebSockets

- **Exists:** the WebSocket **client** on console and web
  (`connect to websocket "ws://..." and call it socket`, `on message from
  socket`, `send "hi" through socket`; D106).
- **Missing:** a WebSocket **server**. There is no grammar for one.
  Mirroring the existing TCP server (D113) would look like this
  **pseudocode (tier C, not Otter syntax)**:

```text
when site receives websocket at "/ws/room" into conn        -- NOT OTTER SYNTAX
    on message from conn ... send ... through conn            -- client forms exist; server side does not
```

  Runtime requirements: origin and session checks at the handshake,
  bounded frame size and send queues, ping/pong, cleanup on disconnect.
  Recommended for Phase 5, after a grammar decision (WEB-007).

## 6. Frontend serving (tier A)

`static is "dist"` serves a Vite build; proposed rules: `/api/*` and
`/ws/*` never fall back to the SPA; other `GET`s that accept `text/html`
and match no file get `index.html` (SPA fallback); hashed assets
(`assets/*-[hash].*`) get `Cache-Control: immutable`; MIME types include
`.js .mjs .css .wasm .glb .gltf .bin .ktx2 .hdr` (none exist in Otter
today). Details: [FRONTEND_COMPATIBILITY.md](FRONTEND_COMPATIBILITY.md).

## 7. The reference app: Otter Guestbook (tier A + B)

[samples/proposals/guestbook.ot](samples/proposals/guestbook.ot) (valid
today; needs the opt-in runtime to serve): a `GET /` page built from SQLite
rows with every value escaped, and a `POST /sign` form handler that
validates, inserts with parameters, and redirects with 303.

## 8. Compatibility implications

| Proposal | Changes 1.0.1 behaviour? | Notes |
|---|---|---|
| Opt-in runtime (WEB-001) | **No** without the manifest field | the one new contract is the manifest field |
| Route/request/response semantics | only in opted-in projects | `respond` with non-literals, `and status` expressions, 405, `HEAD` |
| `X is a web response` | parses today as an ordinary object; meaning only in the runtime | type name `web response` becomes significant there |
| SDK libraries (`html.ot`, `sessions.ot`, templates) | **No** | plain Otter; versioned with the SDK |
| D99 identifier escaping (S-1) | **Yes**, for crafted identifiers | a security fix that diverges from otterPS 1.0.1 output |
| `with status N` (D51 text) | grammar change | not proposed; `and status N` works |
| WebSocket server, interpolation, template syntax | grammar changes | not proposed before their ADRs |

## 9. Open questions for the owner

- **Q-W1** Approve the opt-in mechanism (manifest field) instead of
  changing `otter serve`.
- **Q-W2** Approve `X is a web response` as the response object, and the
  request properties beyond D51 (`parameters`, `json`, `form`, `cookies`,
  `remote`).
- **Q-W3** Missing request properties: `""`, or Otter's existing
  missing-property behaviour?
- **Q-W4** Duplicate query keys: first value, or a list?
- **S-1** Approved (2026-10-23): fix as a recorded security exception to
  byte parity with otterPS 1.0.1.
