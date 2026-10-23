# AGENT DIRECTIVE — OTTER WEB + THREE.JS / REACT THREE FIBER

You are implementing the next web-development capability of Otter. Treat the files in this pack as the initial product brief. Begin with a repository audit and a scoped proposal; do not immediately invent syntax or replace existing web features.

## Goal
Enable developers to build complete websites with Otter as the server-side language, standard HTML/CSS/JavaScript/TypeScript for frontend work, and optional React, Three.js and React Three Fiber for interactive 3D experiences. Build a maintainable SDK and working examples, not a parallel browser engine.

## Required reading
- This pack's `README.md`, `ARCHITECTURE.md`, `ROADMAP.md`, `WEB_API_SPEC.md`, `FRONTEND_3D_INTEGRATION.md`, `SECURITY_AND_TESTING.md`, `EXAMPLE_APPS.md`, `DECISIONS.md`.
- otter-net current `README.md`, architecture, web/database implementation, tests and packaging.
- otterPS authoritative `SPEC-DECISIONS.md` and grammar, read-only.

## Guardrails
1. Preserve all certified Otter 1.0.1 behavior and `otter-net` command semantics. Do not change otterPS.
2. Reuse existing capabilities. Inventory first; do not claim a missing feature without checking.
3. No new language syntax, keyword, parser change, or semantics without owner-approved ADR.
4. Browser rendering stays with JS/TS libraries. R3F is React + Three.js, not a .NET package or native Otter rendering engine.
5. Follow Native AOT constraints, cross-platform portability and the six-platform test matrix.
6. Never use real production credentials or deploy publicly; no purchases, tags, public release or package submission.
7. Work on a new branch, keep PRs small, and provide evidence for each phase.

## Phase 0 — Start now
A. Inspect the repository and report exact existing support for static files, HTTP server/routes, JSON, cookies, forms, sessions, databases, templates, WebSockets, CLI scaffolding and frontend build tools. Cite file paths and tests.
B. Map PHP-style web use cases to existing Otter features and identified gaps. Separate MVP, security-critical, and future features.
C. Evaluate modular .NET 10 architecture, dependency licenses, Native AOT compatibility, and existing test harnesses.
D. Produce `docs/OTTER_WEB_AUDIT.md`, `docs/OTTER_WEB_PLAN.md`, and ADR proposals for SDK API, routing, template security, and frontend integration.
E. Propose a small Phase 1 implementation PR and acceptance tests. Show three REAL Otter examples verified by `otter-net check`, or clearly label proposed syntax as pseudocode.
F. Stop and request owner approval for API design and scope before making language changes or large architectural changes.

## After Phase 0 approval
Implement Roadmap Phases 1–5 in order. Each phase must include working examples, security tests, regression tests, cross-platform Native AOT checks, documentation and measured results. Maintain a visible progress ledger. Do not begin Phase 6 game platform design until separately approved.

## Completion reporting
For each PR report: branch/PR link, tested SHA, added features, exact tested platforms and pass counts, changed API contracts, security findings, known limitations, and next owner decision. If an unexpected security issue is found, stop deployment/release actions and report it.

## First response expected
Return the current-state inventory and recommended Phase 1 design, not an unsupported claim that Otter already has PHP parity.
