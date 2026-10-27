# Otter Web runtime 0.1: the Phase 1a contract

**Status: preview, loopback by default, not for production.** Approved
scope: [../OTTER_WEB_PLAN.md](../OTTER_WEB_PLAN.md) Phase 1a (owner,
2026-10-23). This page defines the request and response types and the HTTP
behaviour before the code. The black-box tests
(`tests/web-runtime/`, `tools/test-web-runtime.ps1`) check every rule
marked **[T]**.

No grammar is added. Every form below is Otter 1.0.1 syntax (SPEC-DECISIONS
D51 and D41) and passes `otter check`.

## 1. Turning it on

The runtime is **opt-in per project**: `otter.json` declares it.

```json
{
  "name": "hello-web",
  "version": "0.1.0",
  "target": "server",
  "entryPoint": "main.ot",
  "web": { "runtime": "otter-web-0.1" }
}
```

- With `"web"`, `otter-net run <project>` runs the program and serves its
  routes **[T]**.
- Without `"web"`, everything is exactly Otter 1.0.1 **[T]**. For example
  `otter run` stops at a route with "I do not know how to run a WebRoute
  statement yet", and `otter serve` is the literal-reply stub.
- `otter check`, `build`, `publish` and `serve` do not change, with or
  without `"web"`.
- `web.runtime` must be `"otter-web-0.1"`. Another value, or a `web` that
  is not an object, stops with a manifest error (exit 2, as every otter.json error) **[T]**.
- `web.allowPublicBind` (boolean, default `false`): see §2.

## 2. The server

```otter
site is a web server
    port is 8080
    host is "127.0.0.1"
    maxbody is 1048576
.
```

| Property | Type | Default | Rule |
|---|---|---|---|
| `port` | number 1-65535 | 8080 | An address already in use stops the program with a runtime error **[T]** |
| `host` | text | `"127.0.0.1"` | `"127.0.0.1"`, `"localhost"` and `"::1"` are loopback. Any other host is refused unless `web.allowPublicBind` is `true`, which also prints a warning **[T]** |
| `maxbody` | number of bytes, 0 to 104857600 | 1048576 (1 MiB) | A larger body is answered 413 **[T]** |
| `static` | text: a folder in the project | none | Serves the folder's files (§2b). It must be a folder inside the project, not the project folder itself (which holds `otter.json` and the program), and is resolved once, links included **[T]** |
| `fallback` | text: a file in the `static` folder | **none: off** | The SPA fallback (§2b). Needs `static` **[T]** |
| anything else | - | - | A runtime error that names the property **[T]** |

- `listen on port N` (D51) declares a server named `server`, used by
  routes whose server name is not a defined web server.
- A server starts at `start X`, or when the program's last top-level
  statement has run. It prints
  `Otter Web: listening on http://HOST:PORT/ (Ctrl+C to stop)` to standard
  error.
- The program keeps running while a server is running (the event loop,
  D121).

## 2b. Static files (Phase 1b)

- **Order:** a request is answered by a route if any route matches its path;
  otherwise by a static file; otherwise by the fallback; otherwise **404**
  **[T]**. Static files are read and sent by the host, streamed, without
  reaching the program (a 100 MB file: no memory growth) **[T]**.
- **What is served:** GET and HEAD of a file inside the folder; a folder's
  `index.html` (also for `/`). There is no directory listing: a folder
  without `index.html` is 404 **[T]**. Other methods on a file: **405**,
  `Allow: GET, HEAD` **[T]**.
- **What is never served (404), in every encoding** **[T]**:
  - `..`, `.` and empty segments;
  - backslashes, `:` (drive letters, Windows streams), NUL and control
    characters;
  - **dotfiles and dot-folders** (`.env`, `.git/`, `.hidden/x`), also when a
    link points at one;
  - **anything whose real path, through symbolic links or junctions at any
    level, leaves the folder** (links that stay inside are served).

  `.well-known/` is a dot-folder too: leave it to the reverse proxy in this
  version.
- **MIME types** (fixed table; `X-Content-Type-Options: nosniff` always)
  **[T]**:

| Extensions | Content-Type |
|---|---|
| `.html` `.htm` | `text/html; charset=utf-8` |
| `.css` | `text/css; charset=utf-8` |
| `.js` `.mjs` | `text/javascript; charset=utf-8` |
| `.json` `.map` | `application/json; charset=utf-8` |
| `.txt` | `text/plain; charset=utf-8` |
| `.wasm` | `application/wasm` |
| `.glb` | `model/gltf-binary` |
| `.gltf` | `model/gltf+json` |
| `.bin` | `application/octet-stream` |
| `.ktx2` | `image/ktx2` |
| `.hdr` | `image/vnd.radiance` |
| images | `.png`, `.jpg`/`.jpeg`, `.gif`, `.webp`, `.avif`, `.svg` (`image/svg+xml`), `.ico` |
| fonts | `.woff`, `.woff2`, `.ttf`, `.otf` (`font/...`) |
| audio and video | `.mp3`, `.ogg`, `.wav`, `.mp4`, `.webm` |
| anything else | `application/octet-stream` |

- **Caching** **[T]**:
  - every file gets an `ETag` (size and modification time) and
    `Last-Modified`;
  - `If-None-Match` or `If-Modified-Since` give **304**;
  - a **Vite-style hashed name** (`name-[hash].ext` or `name.[hash].ext`,
    8-16 characters of `A-Za-z0-9_-` with a letter and a digit) gets
    `Cache-Control: public, max-age=31536000, immutable`;
  - anything else gets `no-cache`.

  A hashed name the rule misses only loses caching. Build-manifest
  integration comes in Phase 4.
- **Fallback** (only with `fallback`) **[T]**:
  - It is served for **GET/HEAD navigations**: requests whose `Accept`
    includes `text/html`, that match no route and no file, are not under
    `/api/` or `/ws/`, and have no file extension.
  - It is sent with `no-cache`.
  - Everything else stays 404.

## 3. Routes

`when X receives METHOD at "PATH" [into req]` (D51).

- **METHOD:** `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `OPTIONS` or `HEAD`;
  `a request` (any method); or nothing (GET).
- **PATH:** a text literal starting with `/`.
- **Matching:**
  - the path matches exactly, ignoring capitals and a trailing `/` (as the
    1.0.1 stub matches), and the query string is not part of it **[T]**;
  - the first route declared that matches both path and method handles the
    request **[T]**;
  - `HEAD` is answered by the `GET` route, without a body **[T]**.
- **No route:**
  - unknown path → **404**, `{"error":"Not Found"}`, `application/json` (as
    the stub) **[T]**;
  - known path, other method → **405**, with an `Allow` header **[T]**.
- **Path parameters (Phase 1b)** **[T]**:
  - `:name` in a route's path matches one non-empty segment;
  - the values are percent-decoded UTF-8, keep their capitals, and are read
    with `parameters of req`;
  - invalid UTF-8, or a decoded `/`, in a parameter gives **400**.
- **Precedence** **[T]**:
  - routes with more literal segments win (`/users/me` beats `/users/:id`);
  - then the first declared;
  - a route whose method does not fit gives way to the next matching route.
- **Duplicates:** two routes with the same method and the same path shape
  (`/users/:id` and `/users/:name`) stop the program when the server
  starts, with an error naming both lines **[T]**.
- (Unreleased preview 0.1 matched `:` literally; nothing was released with
  that behaviour.)

## 4. The request: `into req`

A thing of type `web request`. Its properties are D51's five plus one:

| Property | Otter type | Value |
|---|---|---|
| `method` | text | Upper case: `GET`, `POST`, ... |
| `path` | text | The decoded path, without the query: `/hello` |
| `query` | thing | Each query name with its **first** value (text). Names are case-sensitive |
| `queries` | thing | Each query name with a **list of every value, in order**: the explicit multi-value form **[T]** |
| `body` | text | The body as UTF-8 text. Empty when there is none. A body that is not valid UTF-8 is answered **400** before the handler runs **[T]** |
| `headers` | thing | Header names **ignore capitals** **[T]**. A repeated header gives its values joined with `", "` (RFC 9110) |
| `parameters` | thing | The route's `:name` values (Phase 1b) **[T]** |

- **Query decoding:** `+` is a space and `%XX` is decoded as UTF-8. A query
  or path that is not valid UTF-8 after decoding is answered **400**
  **[T]**.
- **Missing values follow Otter 1.0.1 exactly (D41), verified 2026-10-23:**
  - `name of query of req` with no `name` is a **runtime error** ("This
    thing has no property called "name"."), so the request gets a 500
    (§6) **[T]**;
  - `get "name" from query of req into name` gives **`gone`** without an
    error. **This is the way to read an optional value:**

```otter
when site receives GET at "/hello" into req
    get "name" from query of req into name
    if name is gone
        name is "stranger"
    .
    respond with "Hello, " plus name
.
```

- **Client identity:** `X-Forwarded-For`, `Forwarded` and similar headers
  are ordinary headers. The runtime trusts no proxy and exposes no client
  address in 0.1. A trusted-proxy setting comes with the deployment work
  (Phase 3).
- All values are text. `"5" is 5` follows Otter's existing comparison
  rules.

## 5. The response

| Statement | Status | Content-Type | Body |
|---|---|---|---|
| `respond with V` | 200 | `text/plain; charset=utf-8` | `V` formatted as `say` formats one value **[T]** |
| `respond with V as json` | 200 | `application/json; charset=utf-8` | `V` as `convert V to json` writes it (text becomes a JSON string) **[T]** |
| `respond with V as json and status N` | `N` | `application/json; charset=utf-8` | as above. `N` may be any expression and must be a whole number from 200 to 599; anything else makes the handler fail (§6) **[T]** |
| `respond with status N` | `N` | - | empty **[T]** |
| the handler ends without `respond` | 204 | - | empty **[T]** |

- **`respond` ends the handler:** statements after it do not run **[T]**.
  It also passes through `try ... otherwise` the way `return` does, so a
  `respond` inside `try` answers the request **[T]**.
- **Plain text with a status cannot be written in Otter 1.0.1:**
  `respond with "x" and status 404` is a syntax error, because without
  `as json` the `and` is read as a logical "and" (checked). Use `as json
  and status N`, or `respond with status N`, or the `web response`
  object below.

### 5b. The `web response` object (Phase 1b)

```otter
reply is a web response
    status is 404
    type is "text/plain"
    body is "Nothing here"
.
respond with reply
```

A `web response` given to `respond with` (without `as json`) is sent as
its properties say. Each rule below is tested **[T]**:

| Property | Meaning and rules |
|---|---|
| `status` | 200-599, default 200. A 3xx needs `location` |
| `type` | the Content-Type, default `text/plain; charset=utf-8`; `text/...` without a charset gets `charset=utf-8` |
| `body` | text, sent as UTF-8 (`Content-Length` counts bytes); a thing or list is refused (use `as json`) |
| `location` | the redirect target: a path on this site (`/new`, `page?x=1`), or an `http(s)` URL whose host is the request's own `Host`. `//other`, other sites, `javascript:` and anything with whitespace or a backslash are refused (open redirects) |
| `headers` | a thing of extra headers, named with D41 (`set "Content-Disposition" to "..." in extra`). Names must be header tokens, values may not contain CR, LF or NUL. The runtime's own headers cannot be set: `Content-Length`, `Transfer-Encoding`, `Connection`, `Server`, `Date`, `X-Request-Id`, `X-Content-Type-Options`, `Content-Type` (use `type`) |
| `cookie`, `Set-Cookie` | refused until Phase 3 (cookies with safe defaults) |
| anything else | refused |

- A refused response object fails the handler: 500 with the request id
  (§6), and nothing of the refused response is sent.
- `respond with reply as json` sends the object as JSON data, like any
  thing.
- **Headers on every response** **[T]**:
  - `X-Request-Id`, a random id for this request;
  - `X-Content-Type-Options: nosniff`;
  - `Cache-Control: no-store`;
  - no `Server` header.
- A `HEAD` request gets the headers without the body.

## 6. Errors

- **A handler error** (an Otter runtime error, `fail with`, a bad status) **[T]**:
  - the client gets **500**, with
    `{"error":"Internal Server Error","requestId":"<id>"}` and no details;
  - the error is printed to standard error as an Otter runtime error,
    preceded by `Otter Web: request <id> (<METHOD> <path>) failed:`;
  - the server keeps running.
- **HTTP-level answers** **[T]**:

| Code | When |
|---|---|
| 400 | Malformed request; invalid UTF-8 in the path, query or body |
| 408 | A client that sends its headers or body too slowly |
| 413 | Body over `maxbody` |
| 431 | Headers over 32 KiB |
| 503 | More than 1,000 requests waiting for the program, or the server is stopping |

## 7. The request lifecycle, limits and slow clients

- **Who answers a request is decided once, atomically.** The program takes
  it (its handler runs, and its answer is sent), or the host abandons it
  (the answer is 503, or nothing if the client has gone). Never both.
  - A request whose client disconnects while it waits is abandoned; its
    handler **never runs** **[T]**.
  - A request that waits longer than 60 s gets **503** and is abandoned;
    its handler never runs **[T]**.
  - More than 1,000 requests waiting: each new one gets **503** at once
    **[T]**.
- **A handler that has started runs to the end**, even if its client
  disconnects meanwhile. Otter has no way to stop a running handler, and
  its effects (a counter, a file, a database row) happen. Its answer is
  discarded. Each request has its own `into req` thing and its own answer
  slot, so a late or discarded answer can never reach another request
  **[T]**.
- **Handlers run strictly one at a time.** A `wait` inside a handler pauses
  the whole server: other requests are still read, but they wait for it
  **[T]**.
- The tests shorten the 60 s and the 1,000 through two environment
  variables, `OTTER_WEB_TEST_QUEUE_TIMEOUT_SECONDS` and
  `OTTER_WEB_TEST_MAX_WAITING`. They are for testing only and are not a
  supported setting.


- **How requests reach the program:** connections are accepted and read
  concurrently. A request reaches the program only when its headers and
  whole body have arrived. Handlers run one at a time on the program's
  thread, as all Otter handlers do (D121).
- **A slow or stalled client never blocks another client** **[T]**.
- **The limits:**

| Limit | Value |
|---|---|
| Header read timeout | 10 s |
| Minimum body rate | 240 bytes/s after a 5 s grace |
| Keep-alive idle | 30 s |
| Total header size | 32 KiB |
| Connections | 1,000 |
| Request line | 8 KiB |

## 8. Stopping

- **Ctrl+C (SIGINT), SIGTERM, or the end of the program's event sources**
  **[T]** (on Windows, a real console Ctrl+C is tested by
  `tools/test-web-windows-ctrlc.ps1`):
  - the server stops accepting connections;
  - requests already received are answered, for up to 5 s, then the rest
    get 503;
  - it prints `Otter Web: stopped.` to standard error;
  - the program exits with 0.
- A second Ctrl+C stops at once.
- **The stop starts from the signal itself**, so it also happens while a
  handler is busy. Kestrel stops accepting at once and closes the remaining
  connections after the 5 s grace. **No client waits indefinitely**, even if
  a handler never finishes (the process then needs the second Ctrl+C)
  **[T]**.
- Not tested: closing the console window, logging off or shutting down
  Windows (SIGTERM on Windows).

## 9. Compatibility

- **Changes only in opted-in projects:** route bodies run, `respond` sends
  any value, status codes come from expressions, and there are the 204,
  405 and limit answers.
- **Unchanged without the opt-in:** every 1.0.1 command and output. The
  certification suites and `tests/cli` check this, along with a dedicated
  test of the stub **[T]**.
- **One addition to the runtime's types:** a thing whose names ignore
  capitals (used only for `headers`). Every other thing keeps Otter's
  case-sensitive names.
