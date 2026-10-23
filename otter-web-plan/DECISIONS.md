# Owner Decisions and ADR Queue

## Recommended defaults
- Name: **Otter Web** (SDK/framework), separate from language and otter-net preview product version.
- Target: modern websites first, then 3D sites, then browser game/backend experiments.
- Backend: Otter language executed by otter-net; .NET 10 hosts services.
- Frontend: existing HTML/CSS/JS/TS and standard Vite/React/Three.js/R3F ecosystem.
- Database MVP: SQLite, with provider-neutral design for PostgreSQL and SQL Server.
- Hosting MVP: local development and documented reverse-proxy deployment, not a managed cloud platform.
- Compatibility: frozen Otter 1.0.1 behavior; additive SDK only until syntax approval.

## Owner approval required before implementation beyond Phase 0
1. Proposed public SDK API and any syntax changes (show real checked examples).
2. Which repository houses Otter Web and whether new packages are separately versioned.
3. Dependencies and licenses if incompatible with Otter Free Use License 1.1 or distribution terms.
4. Whether user accounts/authentication are in first public release or deferred.
5. Whether Node/npm is required only for React/3D templates (recommended).
6. Any paid accounts, code signing, domains, public releases or production deployments.

## ADRs to produce
- `WEB-001`: reuse versus extension of existing web server APIs.
- `WEB-002`: route/request/response API shape.
- `WEB-003`: template strategy and escaping model.
- `WEB-004`: SQLite and migration strategy.
- `WEB-005`: frontend tooling and asset manifest integration.
- `WEB-006`: sessions/auth and deployment threat model.
- `WEB-007`: WebSocket and future multiplayer boundary.

## Explicit non-goals for first release
PHP interpreter, WordPress plugin execution, JSX parsing in Otter, Three.js reimplementation, full game engine, production authoritative multiplayer, new Otter keywords without approval.
