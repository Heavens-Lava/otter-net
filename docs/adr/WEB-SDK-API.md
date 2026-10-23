# WEB-SDK-API (WEB-002/003): Route, request and response API

**Status:** proposed (2026-10-23). Full text, examples and open questions:
[../web/WEB_SYNTAX_PROPOSALS.md](../web/WEB_SYNTAX_PROPOSALS.md).

## Existing supported syntax (Otter 1.0.1, certified)
D51: `X is a web server` (properties `port`, `host`), `when X receives
METHOD at "PATH" [into req]`, `respond with V [as json] [and status N]`,
`respond with status N`, `start X`, `listen on port N`. D41 dynamic keys,
D97 SQLite with parameters, crypto statements (`hash password`, `secure random bytes`, HMAC), `use` modules.

## Proposed minimal API (no grammar change)
- Server properties: `port`, `host` (default `127.0.0.1`), `static`,
  `maxbody`.
- Routes: D51 methods + `a request`; `:name` path parameters; 404/405.
- Request (`into req`): `method`, `path`, `query`, `body`, `headers` (D51);
  proposed `parameters`, `json`, `form`, `cookies`, `remote`.
- Response: `respond with` any value; `as json`; `and status` expression;
  `reply is a web response` with `status`, `type`, `body`, `location`,
  `headers`, `cookie`.
- SDK libraries in plain Otter: `html` (escapeHtml), templates (escape by
  default), sessions, CSRF.

## Three end-to-end examples (all pass `otter-net check`)
`../web/samples/proposals/server_api.ot`, `guestbook.ot`,
`sessions_demo.ot` (the last also **runs** today), plus `html_demo.ot`
(runs today).

## Server stack (WEB-002)
| | Kestrel (ASP.NET Core, MIT, part of .NET) | HttpListener (BCL) |
|---|---|---|
| AOT build (measured, osx-arm64) | 9.00 MB minimal API | 4.14 MB with JSON + WebSocket + PBKDF2 |
| Limits/timeouts, slowloris | built-in | weak |
| HTTP/2, TLS, WebSocket server | yes | HTTP/1.1; TLS awkward off Windows |
| Status | supported | legacy; Microsoft advises against it for new development |
Proposed: **Kestrel**, measured again in the first PR.

## Rejected alternatives
Changing `otter serve` in place (breaks parity); new route syntax
(duplicates D51); a third-party web framework; executing JSX/TS in Otter.

## Compatibility risks
Behaviour depends on the manifest opt-in; `web response` becomes a
meaningful type name inside the runtime; `respond` non-literal semantics
differ from the 1.0.1 stub (only when opted in).

## Requested grammar changes
**None** for Phases 1-4. Later candidates, each needing its own decision:
WebSocket server, `with status N` (documented in D51, rejected by the
parser), interpolation/template syntax.
