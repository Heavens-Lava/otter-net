# Otter Web: progress ledger

| Date | Phase | Branch / PR | Tested commit | Result |
|---|---|---|---|---|
| 2026-10-23 | 0: audit and proposals | `web/phase0-audit` (merged) | `bfd2bec` | approved by the owner (WEB-001 opt-in, Kestrel, API in principle, S-1 fix) |
| 2026-10-23 | security: S-1 fix | private advisory fork (GHSA-w4wm-qr4q-9qvr) | `093e325` | regression test passes; 202 compat programs identical before/after; awaiting coordinated publication |
| 2026-10-23 | 1a: secure HTTP core | `web/phase1a-runtime` | see the PR | local macOS arm64: 46/46 black-box checks (Native AOT and framework builds); six-platform CI in the PR |

## Phase 1a checklist (owner's authorisation, 2026-10-23)

- [x] Opt-in manifest setting (`web.runtime`, `web.allowPublicBind`)
- [x] Kestrel host, loopback by default, public binding refused unless allowed
- [x] Dynamic route handlers on the event loop (D121)
- [x] D51 request fields (`method`, `path`, `query`, `body`, `headers`) + `queries` (all values, in order); missing values follow D41
- [x] JSON and text responses; status expressions; 204 without `respond`
- [x] HTTP errors (400, 404, 405, 408, 413, 431, 500 with request id, 503) and request limits
- [x] Slow-client protection (Kestrel timeouts and minimum data rate; whole requests only reach the program)
- [x] Graceful shutdown (SIGINT/SIGTERM; Windows console Ctrl+C checked by hand)
- [x] Black-box HTTP tests (`tools/test-web-runtime.ps1`) on all six platforms (`web-runtime.yml`)
- [x] Certified 1.0.1 behaviour without the opt-in (stub and refusal tests; certification run)
- [x] No grammar change
