# Architecture: Otter Web

## Objective
Make Otter a capable server-side web language, with a first-class developer workflow for modern browser applications. PHP is **not required** for new Otter web projects. PHP interoperability (e.g., WordPress) is a separate future integration and is not part of this scope.

## Runtime boundaries
```text
Browser: HTML/CSS/JS/TS, React, Three.js, R3F, WebGL/WebGPU
      HTTPS / WebSocket / static assets
Otter Web: routing, request/response, middleware, validation,
           sessions/auth, API, file uploads, static hosting
Otter-net: parser/binder/evaluator, native standard libraries
.NET 10 services: HTTP server, DB providers, cryptography, logging
Storage: SQLite initially; PostgreSQL/SQL Server adapters later
```

## Package strategy
Prefer a modular SDK/project template and adapter layers over core parser changes. Reuse existing Otter web, HTTP, JSON, database, event and CLI capabilities where they meet requirements. Record gaps with precise references before building anything. Native AOT compatibility must be tested for dependencies (reflection, dynamic code, JSON serialization, database drivers).

## Suggested components (names provisional)
- `Otter.Web.Hosting`: app lifecycle, host configuration, static file service.
- `Otter.Web.Http`: routing, typed request/response objects, body limits, middleware.
- `Otter.Web.Data`: parameterized database access and transaction interface.
- `Otter.Web.Security`: cookie/session/authentication primitives.
- `Otter.Web.Realtime`: WebSocket connection lifecycle, heartbeats, backpressure.
- `Otter.Web.Tooling`: project scaffolding, frontend integration, build/serve/publish.
- `Otter.Web.Tests`: conformance, security, integration and end-to-end fixtures.

Names are suggestions, not requirements; inspect repository conventions first.

## Frontend ownership
Vite/npm builds JS/TS/React/R3F. Otter does not transpile TypeScript, parse JSX, or implement WebGL. Development mode may proxy frontend requests to Vite. Production mode serves Vite's versioned assets and exposes API endpoints. Maintain explicit boundaries: `/api/*` and `/ws/*` for backend; frontend SPA fallback only for safe navigation requests.

## Deployment
Support loopback development first. Production requires reverse-proxy configuration, HTTPS, trusted-proxy policy, health/readiness endpoints, graceful shutdown, structured logs, secrets via environment/secret store, rate limiting, and a deployment recipe. Never expose a development server as production-ready.

## Compatibility and versioning
Otter language 1.0.1 is frozen. Web SDK evolves independently using semantic versioning. Any new keywords, parse forms, or runtime-visible behavior must go through `SPEC-DECISIONS.md` and an explicit owner approval. Keep otterPS and otter-net installation identities separate. Maintain the `otter-net` CLI contract.
