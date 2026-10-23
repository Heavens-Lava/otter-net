# Otter Web — Phased Roadmap

## Phase 0 — Inventory and design gate
- Map current Otter 1.0.1 HTTP, web, DB, JSON, events, CLI, and project features to requested web scenarios.
- Run baseline tests, capture SHA, enumerate existing syntax and gaps.
- Check Native AOT constraints and license/security compatibility of candidate .NET dependencies.
- Deliver a feature matrix, architecture decision records, and an implementation plan. **No new syntax or broad refactor.**

**Gate:** owner reviews inventory and confirms MVP API design.

## Phase 1 — Secure HTTP MVP
- Serve static HTML/CSS/JS/assets, MIME types, cache policy, SPA fallback.
- Routes: GET/POST, route params, query params, request body and JSON response; extensible methods later.
- Form URL encoding and JSON with size limits; structured errors; request IDs.
- Development workflow with deterministic start/stop, port conflict errors, health check.
- Add unit, integration, and HTTP black-box tests.

**Demo:** simple multipage site + `/api/hello`; clean install; no PowerShell fallback.

## Phase 2 — Data and forms
- SQLite provider with parameterized queries, transactions, migrations strategy.
- Form validation, escaping, safe file uploads (allowlist, size/path rules).
- Server-rendered templates with autoescape and layout/partial support; avoid inventing syntax if existing features suffice.

**Demo:** contact form and persistent guestbook with safe output and negative security tests.

## Phase 3 — Identity and production hardening
- Secure cookies and sessions; CSRF defense for cookie-authenticated mutations.
- Password hashing through maintained proven libraries, secure reset flows and authorization checks.
- TLS/reverse proxy configuration, rate limits, logs, metrics, health, secrets, deployment docs.
- Security review and threat model before labeling production capable.

**Demo:** authenticated dashboard with role restrictions, secure session rotation and logout.

## Phase 4 — Modern frontend tooling
- Vite integration for vanilla JS/TS and React; dev proxy, production asset manifest.
- `otter-net` scaffolds templates without installing global npm packages silently.
- Ensure frontend package lockfiles and reproducible builds; explicit Node requirement.

**Demo:** React dashboard consuming Otter JSON API.

## Phase 5 — Three.js / React Three Fiber
- Vanilla Three.js and React+R3F templates with working asset pipeline for `.glb`, `.gltf`, textures, audio, shaders.
- WebSocket example for live presence/state, not authoritative production multiplayer.
- Compression, cache headers, CORS policy, range requests if needed, large asset safeguards.
- Cross-browser/performance smoke tests and graceful WebGL unsupported fallback.

**Demo:** interactive 3D scene, saved settings via Otter API, optional realtime lobby.

## Phase 6 — Future game platform (separate approval)
- Explore authoritative game servers, lobby/matchmaking, room lifecycle, state sync, persistence, replay/anti-cheat, deterministic tick where needed.
- Distinguish browser game engines from Otter scripting/gameplay logic. Do not claim 3D rendering or native game-engine support in Otter itself.
- Produce feasibility spike and performance measurements before choosing transport/engine.

## Delivery discipline
One phase per PR or small PR series. Every PR: baseline and changed tests, AOT builds on supported platforms, docs, samples, rollback notes, threat-model delta. Never break Otter 1.0.1 conformance. Stop for explicit approval at syntax, dependency/license, public distribution, and spending gates.
