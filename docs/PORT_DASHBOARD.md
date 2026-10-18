# otter-net port dashboard

The full port of otterPS 1.0.1 to C# (see `docs/PORTING.md`). Updated after
every integrated batch. Measured with `tools/port-progress.ps1` (does each
program need the PowerShell Otter?) and `tools/compare.ps1` (does it print
exactly what otterPS prints?).

Passing CI on six platforms shows that what is ported works everywhere; it
is not a measure of how much is ported. These numbers are.

## Completion gate

otter-net is complete only when all of these hold:

| # | Gate | Now |
|---|---|---|
| 1 | Every Otter 1.0.1 language feature implemented in C# | no - see below |
| 2 | Every conformance fixture passes | 15 of 19 run natively |
| 3 | No supported program needs the PowerShell Otter | 141 programs still do |
| 4 | Syntax and runtime diagnostics independent of PowerShell | for native programs |
| 5 | All six platform configurations pass | yes (PR #1) |
| 6 | No unexplained compatibility differences | yes, for native programs |
| 7 | Differential fuzzing passes, coverage documented | 300 programs, 0 differences (CI) |
| 8 | Native AOT executables run without PowerShell | for native programs |
| 9 | Packaging and clean-machine installation tests | not started |
| 10 | Release candidate reviewed and approved | not started |

## Baseline - 2026-10-17, before the porting campaign (`port/native` at b24d0aa)

Programs otter-net takes on natively (`otter check`, OTTER_ENGINE=native):

| Corpus (otterPS 1.0.1 unless noted) | Programs | Native | Need PowerShell |
|---|---:|---:|---:|
| benchmarks | 17 | 13 | 4 |
| conformance | 19 | 15 | 4 |
| examples | 43 | 22 | 21 |
| experiments | 32 | 31 | 1 |
| otter-docs (the Otter website, written in Otter) | 103 | 0 | 103 |
| OtterWorkspace | 14 | 11 | 3 |
| projects | 8 | 3 | 5 |
| selfhost | 1 | 1 | 0 |
| **otterPS 1.0.1 total** | **237** | **96 (41%)** | **141** |
| otter-net's own tests | 107 | 107 | 0 |

Why they need PowerShell: web/desktop UI elements (`image`, `page`,
`window`, `column`, `link`, `state`, `show`, custom things built from them)
115; `create` (UI resources) 12; HTTP, downloads and web servers 6;
databases 2; clipboard 1; other statements 5.

Language coverage by otterPS node kind: about 64 of 226 when the campaign
started (`docs/STATUS.md`). The exhaustive per-feature inventory is
`docs/FULL_PORT_MATRIX.md`, assembled from `docs/port/*.md`.

Not yet measured by these numbers: `otter` commands other than `run` and
`check` (`new`, `test`, `build`, `web`, `serve`, REPL, projects), which all
need the PowerShell Otter today.

## Workstreams

| Area | Scope | Inventory |
|---|---|---|
| System | processes, commands, OS integration, registry, credentials, secrets, zip | `docs/port/system.md` |
| Network | HTTP, sockets, servers, file watching, the event loop (D130, D133) | `docs/port/network.md` |
| Data | databases (SQLite), queries, CSV, console, remaining library forms | `docs/port/data.md` |
| CLI | every `otter` command, REPL, projects, test runner, debugger, profiler | `docs/port/cli.md` |
| UI | the web compiler (`otter web`, `serve`); desktop options | `docs/port/ui.md` |

## Benchmarks

Native AOT, median ms (CI, `tools/bench-native.ps1`): hello 4 (Linux) to 35
(macOS); otterPS 1.0.1 on Windows: 2,318. See `docs/STATUS.md`.
