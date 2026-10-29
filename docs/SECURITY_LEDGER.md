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
| S-3 | Medium | `otter serve` (1.0.1 stub) | `host` is used verbatim, so `"0.0.0.0"` or `"*"` exposes every interface without a warning. One stalled client blocks all requests, and there are no read timeouts | `host is "0.0.0.0"`; or send `Content-Length: 1000` with 3 bytes, then a second request waits | Not changed (certified 1.0.1 stub). The Otter Web runtime (Phase 1a) binds loopback, refuses public binding unless the manifest opts in, and has Kestrel limits and timeouts |
| S-4 | Medium | hand-built HTML (TCP server, `replace` templates) | No HTML encoder in the language, so reflected input is not escaped | a TCP-server page that echoes the request path reflects `<script>` | Phase 2: SDK `escapeHtml` and escaping templates (WEB-003) |
| S-5 | Low | `otter web` pages | No Content-Security-Policy; the reactivity code uses `new Function` with `with` | read any generated page | Later: opt-in CSP-compatible output (changes 1.0.1 output, so it needs a decision) |
| S-6 | Medium | `otter desktop` (Windows) | The bridge's path check accepts absolute paths and `..`. The 256-bit token is the only boundary, and `/api/terminal/exec` runs commands | code reading (`src/Otter.Cli/DesktopCommand.cs`, `InCwd`) | Review before any public release; owner decision (the same design as otterPS) |
| S-8 | Medium (only if untrusted parties can write in the static folder) | Otter Web runtime static files (Phase 1b) | Time-of-check to time-of-use: the host resolves a requested file's real path (links included) and checks it is inside the static folder, then opens the file by path. A process that can write in the static folder could replace a checked file or folder with a link to outside it between the check and the open | A writer racing requests while swapping `public/x` between a file and a link to outside | Requirement now: the static folder must be **trusted** (written only by whoever deploys the site; never user uploads), documented in the contract (§2b). Hardening before internet-facing production (Phase 3): open the file first, verify the **opened handle's** final path against the folder (Windows `GetFinalPathNameByHandle`, macOS `F_GETPATH`, Linux `/proc/self/fd`), and stream from that handle; refuse to start when the folder is writable by other users |

## Fixed

| ID | Fixed in | How | Tests |
|---|---|---|---|
| S-7 | otter-net, Phase 2a (PR #10; security and correctness exception to otterPS 1.0.1 parity, owner decisions 2, 3 and 4 of 2026-10-27) | (1) `execute` and `query` refuse text that holds a second statement, **before anything runs**; trailing spaces, comments and semicolons are allowed. Before, the first statement ran and the rest was silently dropped. (2) Bytes parameters are bound as BLOBs (before, the text "OtterBytes"). (3) BLOB columns are read as bytes (before, as text). (4) A whole number an Otter number cannot hold exactly is refused with an error naming the column; every whole number up to 2^53 and larger exact ones (2^60) are still read. Before, it was silently rounded. The same applies to `last inserted id`, after its statement ran. (5) A connection waits up to 5 s for another connection's lock (`sqlite3_busy_timeout`); before, it failed at once | `tests/security/s7_several_statements.ot`, `s7_blobs.ot`, `s7_integers.ot`, `s7_busy_timeout.ot`; all four fail with the previous build. In `tests/compat`, only `data_db.ot` used the old behaviour (a two-statement `execute` and a BLOB literal); those checks moved to the tests above, and the 202 programs give identical output before and after. otterPS 1.0.1 still has all five |
| S-2 | otter-net, PR "security: S-2" (security exception to otterPS 1.0.1 parity) | `contains`, `starts with` and `ends with` escape `%`, `_` and `\` in their value and use `LIKE ... ESCAPE '\'`: the value is matched exactly as written. Before, `contains "%"` matched every row | `tests/security/s2_like_wildcards.ot` (`tools/test-security.ps1`); the `tests/compat` programs give identical output before and after. otterPS 1.0.1 still has it (low; candidate for the otterPS 1.0.2 release) |
