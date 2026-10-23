# Otter Web runtime: Phase 1b plan

**Status: planning approved (2026-10-23). Not implemented.**
Implementation starts only after Phase 1a (PR #6) passes its review gate.
There is no grammar change. Every example is in
[samples/phase1b/phase1b.ot](samples/phase1b/phase1b.ot) and passes
`otter-net check` today. What the examples *do* is the plan below.

Scope (owner): path parameters, safe static-file hosting, MIME types for
JavaScript, WASM, GLB and KTX2, and the web response object.

## 1. Path parameters

```otter
when site receives GET at "/api/users/:id" into req
    id is id of parameters of req
    respond with id as json
.
```

**Matching:**

- A path segment `:name` matches one non-empty segment.
- Literal segments still match ignoring capitals.
- Parameter values keep their capitals and are percent-decoded as UTF-8.
  Invalid UTF-8, or a decoded `/`, gives **400**.

**Precedence, deterministic:**

1. More literal segments win: `/users/me` beats `/users/:id`.
2. Then declaration order.
3. Two routes with the same pattern and method: the program stops at
   start, with an error naming both lines.

**Reading parameters:** `parameters of req` is a thing (D41).
`id of parameters of req` is an error if the parameter is absent, and
`get "id" from parameters of req` gives `gone`. In 1b every parameter in
the pattern is always present.

**Compatibility:** a `:` in a path matched literally in 0.1. In 1b it
means a parameter. This applies only in opted-in projects.

## 2. Safe static files

```otter
site is a web server
    port is 8080
    static is "public"
    fallback is "index.html"
.
```

**The folder (`static`):**

- A folder inside the project; a path outside it is a start-up error.
- It is resolved **once at start** to a canonical absolute path.
- Every request path is percent-decoded, normalised, and joined to it.
- **Refused with 404, never served:**
  - `..` segments (also encoded: `%2e%2e`, `%2f`, `%5c`);
  - backslashes;
  - NUL bytes and drive letters;
  - names that start with `.` (dotfiles), unless a later setting allows
    them;
  - any path whose canonical form leaves the folder, **including through
    a symlink or junction** (checked on the real path of the final file);
  - directories without an `index.html`. There is no directory listing.
- Methods: GET and HEAD only; other methods on a static path get 405.

**Order:** routes are matched first, then static files, then the fallback,
then 404.

**SPA fallback (`fallback`):**

- A file inside `static`.
- Served only for `GET` requests whose `Accept` includes `text/html`, that
  match no route and no file.
- **Never** for paths under `/api/` or `/ws/`, or paths with a file
  extension. Those get 404.

**Responses:**

- `ETag` (size and modification time) and `Last-Modified`;
  `If-None-Match` and `If-Modified-Since` give 304.
- Names with a content hash (`app.3f9a1c2b.js`, Vite style
  `name-[hash].ext`): `Cache-Control: public, max-age=31536000, immutable`.
- Anything else: `Cache-Control: no-cache`.
- Every response: `X-Content-Type-Options: nosniff`.
- Files are streamed, not loaded into memory. The size limit is the file
  system's.
- Range requests are deferred to Phase 5 (large 3D assets).

**MIME types** (fixed table; anything else is
`application/octet-stream`):

| Extension | Type |
|---|---|
| `.html` `.htm` | `text/html; charset=utf-8` |
| `.css` | `text/css; charset=utf-8` |
| `.js` `.mjs` | `text/javascript; charset=utf-8` |
| `.json` `.map` | `application/json; charset=utf-8` |
| `.wasm` | `application/wasm` |
| `.glb` | `model/gltf-binary` |
| `.gltf` | `model/gltf+json` |
| `.bin` | `application/octet-stream` |
| `.ktx2` | `image/ktx2` |
| `.hdr` | `image/vnd.radiance` |
| `.png` `.jpg` `.jpeg` `.gif` `.webp` `.avif` `.svg` `.ico` | image types (`image/svg+xml` for `.svg`) |
| `.woff` `.woff2` `.ttf` `.otf` | `font/*` |
| `.mp3` `.ogg` `.wav` `.mp4` `.webm` | audio and video types |
| `.txt` | `text/plain; charset=utf-8` |

## 3. The web response object

```otter
when site receives GET at "/missing"
    reply is a web response
        status is 404
        type is "text/plain"
        body is "Nothing here"
    .
    respond with reply
.
```

A `web response` (a thing declared with `X is a web response`) passed to
`respond with` is sent as described by its properties:

| Property | Meaning | Rules |
|---|---|---|
| `status` | whole number 200-599 | Default 200. 3xx needs `location` |
| `type` | Content-Type | Default `text/plain; charset=utf-8`. `text/*` types get `charset=utf-8` when they have none |
| `body` | text (UTF-8) | Default empty. A thing or list here is an error: use `respond with V as json` |
| `location` | redirect target | A relative path (`/new`, `page?x=1`) by default. An absolute `http(s)://` URL only if its host is the request's own host. `//evil` and `javascript:` are refused (open redirects) |
| `headers` | a thing of extra headers, set with D41 (`set "Content-Disposition" to "..." in extra`) | Names must be header tokens; values may not contain CR, LF or NUL; the runtime's own headers cannot be overridden (see below) |

- **Headers the program cannot set:** `Content-Length`,
  `Transfer-Encoding`, `Connection`, `Server`, `Date`, `X-Request-Id`,
  `X-Content-Type-Options`.
- **`Set-Cookie` waits for Phase 3**, which brings cookies with safe
  defaults. In 1b, a `Set-Cookie` header or a `cookie` property is refused
  with a runtime error that says so.
- **It solves the 0.1 limit:** a text body with any status, since
  `respond with "x" and status N` is not valid Otter 1.0.1 syntax.
- **Compatibility:** the type name `web response` matters only inside an
  opted-in project's route. Elsewhere it stays an ordinary thing, as in
  1.0.1.

## 4. Tests

Every rule above becomes a **[T]** row in `docs/web/RUNTIME_CONTRACT.md`
and a check in `tools/test-web-runtime.ps1`, on all six platforms.

**Path parameters:**

- one, two and encoded values; invalid UTF-8;
- an encoded `/` in a segment;
- precedence;
- duplicate routes refused at start.

**Static files:**

- every MIME type in the table;
- HEAD;
- ETag and 304;
- hashed names marked immutable;
- `..` (raw, encoded, double-encoded), backslash, NUL, dotfiles;
- **a symlink pointing out of the folder** (POSIX symlink, Windows
  junction where the runner allows);
- a directory without `index.html`;
- a route beats a file;
- the fallback for HTML navigations only, never for `/api/` or assets;
- a large (100 MB) file streamed with flat memory use.

**The response object:**

- each property;
- 3xx without `location`;
- open-redirect cases (`//x`, `https://other`, `javascript:`);
- CR/LF in a header;
- forbidden headers;
- `Set-Cookie` refused;
- a body that is not text.

**Regression:** the Phase 1a suite, certification, ci, desktop and
package. 1.0.1 behaviour without the opt-in stays unchanged.

## 5. Open questions for the owner

1. `fallback` as a server property (proposed), or always `index.html`
   when `static` is set?
2. Dotfiles: never served (proposed), or a later allow-list?
3. `.well-known/` (for example ACME) is a dotfile path: serve it as an
   exception, or leave it to the reverse proxy (proposed: the proxy)?
4. Hashed-name detection: the Vite pattern only (proposed), or a manifest
   (Phase 4)?
