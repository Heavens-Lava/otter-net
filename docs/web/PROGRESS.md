# Otter Web: progress ledger

| Date | Phase | Branch / PR | Tested commit | Result |
|---|---|---|---|---|
| 2026-10-23 | 0: audit and proposals | `web/phase0-audit` (merged) | `bfd2bec` | approved by the owner (WEB-001 opt-in, Kestrel, API in principle, S-1 fix) |
| 2026-10-23 | security: S-1 fix | private advisory fork (GHSA-w4wm-qr4q-9qvr) | `093e325` | regression test passes; 202 compat programs identical before/after; awaiting coordinated publication |
| 2026-10-23 | 1a: secure HTTP core | `web/phase1a-runtime`, PR #6 | `c877125` (product code = `309dfe0`) | web runtime 6/6 platforms (46/46; Windows 44 + 1 skipped); certify 309dfe0 every gate on 6 platforms, same totals as f4e5134 (`release/certification/309dfe0...md`); ci, desktop, package green; 202 compat programs identical to the preview binary; size +4.4 to +5.4 MB |

| 2026-10-24 | 1b: path parameters, static files, web response | PR #7, **merged** 2026-10-25 as `d3a7796` | `351251b` | **complete** (internal development milestone): web runtime 6/6 platforms (106/106; Windows 103 + 2 skipped + Ctrl+C 3/3), certified on 6 platforms, ci/desktop/package green; negative controls for the dot-name and symlink checks |
| 2026-10-27 | security: S-2 (LIKE wildcards) | PR #8, **merged** 2026-10-27 as `9caa3af` | `4e6cbe4` | fixes the last open injection finding before database-backed web features (Phase 2) |
| 2026-10-29 | 2a: forms, SQLite corrections (S-7), busy timeout | PR #10, **merged** 2026-10-29 as `fcba67c` | `98b0c5b` | **complete**: security 5/5 and HTTP suite on 6 platforms (116/116; Windows 113 + 2 skipped + Ctrl+C 3/3), certified, ci/desktop/package green. Two nonblocking limitations tracked (RUNTIME_CONTRACT, Phase 2a) |
| 2026-10-29 | 2b: `web template` (ADR WEB-003) | `web/phase2b` (draft PR) | see the PR | in review; not merged |

## Phase 1b checklist (owner's authorisation, 2026-10-24)

- [x] Path parameters (`:name`, decoded UTF-8, 400 on invalid or encoded `/`), precedence, duplicate routes refused at start
- [x] Static files: inside the project only, canonical root, `..`/encoded/backslash/NUL/drive/dot names refused, symlinks and junctions resolved at every level, no listing, streaming
- [x] Explicit `fallback`, off by default; navigations only (Accept text/html, no extension, not /api/ or /ws/)
- [x] Dotfiles and dot-folders never served; `.well-known/` left to the reverse proxy
- [x] MIME table (JavaScript, WASM, GLB, glTF, KTX2, HDR, images, fonts, audio/video)
- [x] HTTP caching: ETag, Last-Modified, 304; Vite-hashed names immutable; others no-cache
- [x] The `web response` object: status, type, body (UTF-8), location (no open redirects), headers (validated, runtime headers protected), cookies refused until Phase 3
- [x] Regression tests for traversal, symlinks, redirects, headers, response encoding, streaming, route precedence; Phase 1a tests kept
- [x] 1.0.1 behaviour without the opt-in unchanged; no grammar change

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
