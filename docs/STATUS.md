# otter-net status - 2026-10-18

Measured by CI run 39158442548 (`port/native`, after the third porting
batch): native AOT builds on GitHub-hosted runners, compared with otterPS
1.0.1 (tag `v1.0.1`) on Windows PowerShell 5.1. Native executables are built
only in CI until they can be signed. The running record is
`docs/PORT_DASHBOARD.md`; what remains, site by site, is
`docs/FULL_PORT_MATRIX.md`.

## Compatibility (Windows, against otterPS 1.0.1 on Windows PowerShell 5.1)

| Comparison | Compared | Different | Native |
|---|---:|---:|---:|
| Programs: otterPS's conformance fixtures and examples, `tests/compat` (35 more skipped: windows, servers that never end) | 257 | **0** | 218 (4 desktop-window and remote-command programs go to otterPS on Windows) |
| Network and event programs (servers, HTTP, TCP, TLS, UDP, WebSockets, watching, jobs), native only | 16 | **0** | 16 |
| `otter web` pages, byte for byte | 337 | **0** | 335 |
| Differential fuzz, otterPS's generators (150 generated + 150 mutated) | 300 | **0** | 300 |
| `otter` command lines (`tools/compare-cli.ps1`) | 249 | 27 before a comparison-tool fix (otter.ps1's path); 1 real: a desktop window | - |

On the other five platforms (Windows on Arm, macOS Apple silicon and Intel,
Linux x64 and Arm on Debian 12 - Chromebooks) every program in
`tests/compat` gives otterPS's Windows output, except differences declared
with their reasons in `.github/workflows/ci.yml` (path separators,
Windows-only features' "only available on Windows" messages, the operating
system's own words).

Without a program run: `tools/port-progress.ps1` finds every one of the 512
programs in otterPS's repository and `tests/` taken on natively by `otter
check`.

## Still handed to the PowerShell Otter

Decided before anything runs, so a program never starts natively and falls
back halfway:

- Desktop windows (WPF) in `otter run` on Windows: Native AOT cannot host
  WPF. Needs a decision: `docs/port/desktop-options.md`.
- Remote commands on Windows (PowerShell remoting). Needs a decision.
- A few failures that PowerShell itself reports on Windows (an `otter serve`
  that cannot start, `otter desktop`'s bridge, `-InformationAction`).
- `otter studio` (the IDE is developed separately).

## Speed and memory (Windows, CI; median of 7 runs, ms)

| Program | otterPS 1.0.1 | otter-net | Speed-up | Peak memory, otterPS / otter-net (MB) |
|---|---:|---:|---:|---:|
| hello (cold start) | 2,332 | 16 | 146x | 233 / 10 |
| text | 3,254 | 31 | 105x | 312 / 21 |
| data (files + JSON) | 5,082 | 95 | 54x | 327 / 13 |
| functions | 3,374 | 142 | 24x | 310 / 27 |
| loops | 3,312 | 152 | 22x | 310 / 27 |
| lists | 137,544 | 46 | 2,990x | 310 / 20 |

Startup on the other platforms is reported by each CI job
(`tools/bench-native.ps1`).

## Open specification questions

`docs/SPEC-QUESTIONS.md` (Q1-Q16, UI, UI2, C2, P2 and T3 questions): places
where otterPS 1.0.1's code and its documents disagree, or where Windows
PowerShell 5.1 and PowerShell 7 differ. otter-net keeps otterPS 1.0.1's
behaviour until each is decided. The cross-platform proposal is PR #2.
