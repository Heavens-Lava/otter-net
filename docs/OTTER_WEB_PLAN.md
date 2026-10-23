# Otter Web: architecture and implementation plan (proposal)

**Status: proposal for owner approval. Nothing is implemented.** Inputs:
- the brief, [otter-web-plan/](../otter-web-plan/AGENT_MASTER_PROMPT.md);
- the audit, [OTTER_WEB_AUDIT.md](OTTER_WEB_AUDIT.md);
- the API, [web/WEB_SYNTAX_PROPOSALS.md](web/WEB_SYNTAX_PROPOSALS.md);
- the ADRs in [adr/](adr/).

## Principles

1. **Otter 1.0.1 stays byte-for-byte as certified.** The web runtime is
   opt-in per project (WEB-001). `otter serve`, `otter run` and `otter
   web` behave as today without the opt-in, and the certification suites
   must stay green.
2. **No grammar change in Phases 1-4.** D51's certified grammar carries
   the server, and plain Otter libraries carry the rest. Anything that
   needs new syntax (a WebSocket server, interpolation, template syntax)
   goes through SPEC-DECISIONS and owner approval first.
3. **Browser code stays browser code.** HTML, CSS, JS, TS, React,
   Three.js and R3F are built by npm/Vite. Otter serves the result and
   provides the APIs.
4. **Secure by default:**
   - loopback binding;
   - body, header and time limits;
   - escaped output;
   - parameterized SQL;
   - `HttpOnly` / `SameSite` cookies;
   - CSRF defence;
   - no production claims without the Phase 3 review.
5. **No new third-party binaries.** .NET's own Kestrel / HttpListener,
   System.Text.Json source generation and the OS SQLite.

## Architecture

```text
browser: HTML/CSS/JS (Vite build) ── HTTP / WebSocket ──┐
                                                         ▼
otter-net run .  (project with "web": {"runtime": ...})
  ├─ Otter.Web.Hosting   (new): HTTP host (Kestrel or HttpListener, WEB-002),
  │                       limits, static files + MIME + SPA fallback, logging,
  │                       graceful shutdown; turns requests into events
  ├─ Otter.Runtime        (existing): interpreter + event loop; route bodies run
  │                       as event handlers; `into req` / `web response` objects
  ├─ Otter.Runtime SQLite (existing, D97) + fixes (S-1, S-7)
  └─ SDK Otter libraries  (new, plain Otter): html, templates, sessions, csrf
```

- **Concurrency.** The interpreter stays single-threaded, so Otter
  semantics are unchanged. The host accepts and reads requests
  concurrently with limits and timeouts, then queues complete requests to
  the event loop. One slow handler delays others, but a slow *client* no
  longer blocks anyone (today's stub problem). Parallel interpreters are
  a later measurement-driven decision.
- **Packaging.** It goes inside `otter-net`, so the installed command and
  packages are unchanged. Kestrel adds about 8 MB to the executable (WEB-002).
  Alternative: a separate host executable, like `otter-desktop`.

## Phases and order

Each phase is one or more small PRs, carrying:
- the tested SHA;
- the six-platform Native AOT build;
- the unchanged certification;
- a black-box HTTP suite;
- security tests;
- docs and samples;
- a progress-ledger entry (`docs/web/PROGRESS.md`, created by the first PR).

| Phase | Content | Demo | Gate |
|---|---|---|---|
| **1a** (first PR) | Opt-in runtime; host bound to loopback; dynamic route bodies; `respond with` any value; `as json`; `and status` expressions; D51 request (`method`, `path`, `query`, `body`, `headers`); 404/405/413/400; limits and timeouts; graceful shutdown; request ids | `/api/health`, `/api/hello?name=` | owner approves WEB-001, WEB-002, Q-W1/Q-W3/Q-W4 |
| 1b | Path parameters; `json of req`; `web response` (status, type, location, headers); static folder with MIME types, traversal and symlink guards, nosniff, SPA fallback, immutable caching | multipage site + `/api/hello` | - |
| 2 | `form of req`; SDK `html` + `templates` (escaping by default); SQLite fixes (S-1 by approval, S-2, S-7), busy timeout, migrations; validation helpers | **Otter Guestbook** | WEB-003, WEB-004 |
| 3 | Cookies, SDK sessions (rotation, expiry), CSRF, passwords (existing PBKDF2), authorization patterns; reverse-proxy and TLS recipe; rate limits; health/readiness; structured logs; **threat model and security review** | authenticated dashboard | WEB-006; review before any "production" wording |
| 4 | `otter-net new` templates `web-static`, `web-react` (Vite + TS); dev proxy; production `dist/` serving; explicit Node prerequisite | React dashboard | WEB-005 |
| 5 | `web-three`, `web-r3f` templates; GLB/KTX2/WASM assets; WebSocket server (needs a **grammar decision**) | Three.js showcase, R3F world | WEB-007 |
| 6 | Game platform feasibility: separate approval ([web/GAME_DEVELOPMENT_GAPS.md](web/GAME_DEVELOPMENT_GAPS.md)) | - | owner |

## The recommended first PR (Phase 1a)

**Branch** `web/phase1a-runtime`, about 1,500 lines with tests. It adds:

1. **Manifest.** `otter.json` `"web": { "runtime": "otter-web-0.1" }`
   (exact name per WEB-001). Validation errors use the existing manifest
   error style.
2. **Host.** `src/Otter.Web.Hosting/`, chosen by WEB-002:
   - binds `127.0.0.1` by default;
   - a non-loopback `host` is refused unless the manifest says
     `"allowPublicBind": true` (and then a warning is shown);
   - limits: a 1 MiB body limit (configurable via `maxbody`);
   - a header size limit;
   - a 30 s request-read timeout;
   - a keep-alive limit;
   - Ctrl+C or SIGTERM drains in-flight requests (5 s).
3. **Runtime.**
   - In an opted-in project, `otter-net run .` executes the program.
     `X is a web server` registers a server. `when X receives ...`
     registers routes. The event loop dispatches requests.
   - `respond with` evaluates its value:
     - text → `text/plain`;
     - `as json` → JSON via the existing serializer;
     - `and status N` takes any expression.
   - A handler that ends without `respond` gets 204. An exception gets 500
     with a request id, and no stack trace is sent to the client.
4. **Request object.** `method`, `path`, `query`, `body` and `headers`,
   exactly as D51. Header and query keys are case-insensitive.
5. **Tests.**
   - `tests/web-runtime/*.ot` programs plus a black-box client (C# xunit
     or a script) on all six platforms:

     | Area | Cases |
     |---|---|
     | Routes | methods, `a request`, default GET, HEAD, 404/405, exact and case-insensitive paths as today |
     | Request | query decoding (`%20`, `+`, invalid UTF-8 → 400), duplicate keys (per Q-W4), headers, body text |
     | Responses | status codes and content types |
     | Limits | 413 body, oversized headers, slow-client timeout |
     | Robustness | concurrent clients (a slow client does not block others), graceful shutdown, port in use |
     | Binding | loopback-only binding (connect from a non-loopback address is refused) |
     | Logging | no stack traces in responses |

   - Regression: `certify.yml` unchanged and green. A test asserts that
     `otter serve` without the opt-in is still byte-identical to 1.0.1
     (`Variable@L13` and all).
6. **Docs.** `docs/web/PROGRESS.md` (ledger); a `docs/web/RUNTIME.md`
   user page, marked preview and loopback-only.

**Acceptance:**
- all six platforms green;
- certification unchanged;
- binary size growth measured and reported;
- each demo route reproducible from a clean clone (`otter-net run .` and
  `curl` commands in the PR).

## What I need from you before writing code

1. **WEB-001**: approve the opt-in runtime (a manifest field) instead of
   changing `otter serve`.
2. **WEB-002**: Kestrel (recommended: robust limits, HTTP/2, TLS, a
   WebSocket server later, about 8 MB more) or HttpListener (smaller,
   legacy).
3. **API**: approve [WEB-SDK-API](adr/WEB-SDK-API.md) and Q-W1 to Q-W4.
4. **S-1**: fix the query-language vulnerability in otter-net, as a
   recorded security exception to byte parity with otterPS 1.0.1.
5. **Repository**: Otter Web lives in otter-net (recommended), with SDK
   libraries versioned separately (`otter-web-0.x`).
6. **Node/npm**: required only for the React/3D templates (Phase 4+).
