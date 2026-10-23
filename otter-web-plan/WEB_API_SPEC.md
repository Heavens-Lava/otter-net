# Otter Web SDK — Provisional API Contract

**This is a design brief, not executable Otter code.** The agent must inspect the real grammar and APIs and propose concrete syntax in a separate ADR before implementation. Do not insert pseudo-Otter snippets into user docs as if supported.

## Required concepts
- App/host lifecycle: start, stop, bind address/port, graceful shutdown.
- Route registration: HTTP method, path, route parameters, handler, precedence, 404/405.
- Request: method, path, query, headers, content type, body stream, JSON/form parse, cookies, remote address (with proxy trust rules).
- Response: status, headers, content type, JSON/text/HTML, redirects, file/stream; prevent response splitting.
- Middleware: composable ordered pipeline for logging, errors, security, authentication, rate limiting.
- Validation: required fields, types, lengths, safe error reporting.
- Templates: context-aware HTML escaping by default, layouts, partials, cache strategy; prohibit arbitrary template execution by default.
- Database: connection settings from secrets, prepared/parameterized SQL, transactions, cancellation/timeouts, error sanitization.
- Realtime: WebSocket handshake, origin/auth checks, ping/pong, bounded message sizes and queues, disconnect cleanup.

## Contract tests
- Methods/paths, encoded segments, trailing slash behavior, invalid UTF-8, duplicate query keys.
- JSON and form input, malformed bodies, oversized payloads, missing content types.
- Cookie flags, CSRF and CORS, error responses, escaping and injection prevention.
- Connection concurrency, cancellation, client disconnect, server shutdown.

## Illustrative HTTP API (not Otter grammar)
```http
GET /api/health -> 200 {"status":"ok"}
GET /api/guestbook -> 200 [{"id":1,"name":"A"}]
POST /api/guestbook Content-Type: application/json
{"name":"A","message":"Hello"}
-> 201 {"id":1}
GET /api/scenes/forest -> 200 {"camera":{},"objects":[]}
```

## Language decision gate
Agent must deliver `docs/adr/WEB-SDK-API.md` with: existing supported syntax, proposed minimal API, 3 end-to-end examples checked with `otter-net check`, rejected alternatives, compatibility risks, and a list of any requested grammar changes. No grammar change before approval.
