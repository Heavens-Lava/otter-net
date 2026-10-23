# Otter Web — Agent Implementation Pack

**Status:** Proposal for review; no implementation or syntax change authorized by this document alone.  
**Base:** otter-net native C#/.NET 10 implementation; preserve certified Otter 1.0.1 behavior.  
**Goal:** Build secure, practical web applications with Otter backends and standard HTML/CSS/JavaScript/TypeScript/React frontends, including Three.js and React Three Fiber (R3F).

## Read first
1. `AGENT_MASTER_PROMPT.md` — copy/paste into your coding agent.
2. `ARCHITECTURE.md` — boundaries, design, and integration contracts.
3. `ROADMAP.md` — phased milestones and acceptance criteria.
4. `WEB_API_SPEC.md` — proposed SDK surface, not approved grammar.
5. `FRONTEND_3D_INTEGRATION.md` — Three.js/R3F and future game considerations.
6. `SECURITY_AND_TESTING.md` — mandatory gates.
7. `EXAMPLE_APPS.md` — demonstrators and success criteria.
8. `DECISIONS.md` — decisions and assumptions needing owner review.

## Non-negotiable
- Do not modify otterPS 1.0.1, published artifacts, or existing Otter language semantics.
- Do not assume the existing web/database APIs are missing: inventory them first.
- Avoid adding syntax until a separate specification decision is approved.
- React, R3F, and Three.js remain browser-side JavaScript/TypeScript libraries; Otter supplies hosting and backend APIs. Do not promise native execution of React JSX inside the Otter interpreter.
- No public deployment, signing purchases, package publication, or production credential use without explicit approval.
- Work on an isolated branch, with small reviewed PRs and green regression checks.
