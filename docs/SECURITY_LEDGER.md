# Security ledger

Every security finding is tracked here, with its severity, how to
reproduce it, and when it will be fixed. Opened 2026-10-23 from the Otter
Web Phase 0 audit ([OTTER_WEB_AUDIT.md](OTTER_WEB_AUDIT.md)).

**To report a vulnerability**, use the repository's private vulnerability
reporting: GitHub > Security > Report a vulnerability. Do not open a
public issue.

**Disclosure.** Findings that also affect the released otterPS 1.0.1 are
handled in coordination with an otterPS fix. Their technical details stay
in a private security advisory until fixes are available for both, so the
entry below says only what is affected and when it will be fixed.

| Severity | Meaning |
|---|---|
| High | data or code can be reached by an attacker in a realistic program |
| Medium | needs an unusual configuration or program pattern |
| Low | defence-in-depth, or wrong results without an attacker gain |

## Open

| ID | Severity | Affects | Finding | Reproduction | Fix milestone |
|---|---|---|---|---|---|
| **S-1** | High | otter-net up to 0.9.0-preview.1, **otterPS 1.0.1** | A vulnerability in the experimental query language (D99, `get … from TABLE in DB`) | Withheld until a coordinated fix is available (private advisory) | **Approved 2026-10-23** as a recorded security exception to otterPS 1.0.1 parity: identifiers are validated and quoted, and sort directions restricted, with regression tests. It must be fixed before any database-backed web feature reaches testers. otterPS: the owner decides the coordinated fix |
| S-2 | Low | otter-net, otterPS 1.0.1 | `contains`, `starts with` and `ends with` in the query language do not escape the LIKE wildcards `%` and `_` in their value | `pattern is "%"`, then `get all from users in db where name contains pattern into r`: every row matches | Phase 2 (before database-backed web features): escape with `ESCAPE '\'` |
| S-3 | Medium | `otter serve` (1.0.1 stub) | `host` is used verbatim, so `"0.0.0.0"` or `"*"` exposes every interface without a warning. One stalled client blocks all requests, and there are no read timeouts | `host is "0.0.0.0"`; or send `Content-Length: 1000` with 3 bytes, then a second request waits | Not changed (certified 1.0.1 stub). The Otter Web runtime (Phase 1a) binds loopback, refuses public binding unless the manifest opts in, and has Kestrel limits and timeouts |
| S-4 | Medium | hand-built HTML (TCP server, `replace` templates) | No HTML encoder in the language, so reflected input is not escaped | a TCP-server page that echoes the request path reflects `<script>` | Phase 2: SDK `escapeHtml` and escaping templates (WEB-003) |
| S-5 | Low | `otter web` pages | No Content-Security-Policy; the reactivity code uses `new Function` with `with` | read any generated page | Later: opt-in CSP-compatible output (changes 1.0.1 output, so it needs a decision) |
| S-6 | Medium | `otter desktop` (Windows) | The bridge's path check accepts absolute paths and `..`. The 256-bit token is the only boundary, and `/api/terminal/exec` runs commands | code reading (`src/Otter.Cli/DesktopCommand.cs`, `InCwd`) | Review before any public release; owner decision (the same design as otterPS) |
| S-7 | Low | D97 SQLite | `execute` runs only the first of several statements, silently. A bytes parameter is stored as the text "OtterBytes". BLOBs are read back as text, and integers above 2^53 lose precision | `execute db with "insert …; insert …"` inserts one row | Phase 2 |

## Fixed

None yet.
