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
| 1 | Every Otter 1.0.1 language feature implemented in C# | yes, except desktop windows (WPF) and remote commands on Windows - decisions pending |
| 2 | Every conformance fixture passes | 19 of 19 checked natively; all identical on Windows |
| 3 | No supported program needs the PowerShell Otter | 0 of 512 for `check`; at run time only desktop windows and remote commands on Windows (decisions pending) |
| 4 | Syntax and runtime diagnostics independent of PowerShell | for native programs; strict mode: `tools/compare.ps1 -NativeOnly` |
| 5 | All six platform configurations pass | yes (PR #3 CI) |
| 6 | No unexplained compatibility differences | yes: Windows 0; elsewhere every difference declared with its reason |
| 7 | Differential fuzzing passes, coverage documented | 300 programs, 0 differences, all native (CI); `docs/port/fuzz.md` |
| 8 | Native AOT executables run without PowerShell | for native programs |
| 9 | Packaging and clean-machine installation tests | not started |
| 10 | Release candidate reviewed and approved | not started |

## After wave 3 - 2026-10-18 (Windows tasks and printers, the last UI fallbacks, the web compiler for system/network/data programs, JSON reader fixes, PowerShell's own error layout)

| Comparison (Windows, against otterPS 1.0.1 on Windows PowerShell 5.1, CI run 39158442548) | Result |
|---|---|
| Programs | 257: **0 different**; 218 native, 4 handed over (desktop windows, remote commands) |
| `otter web` pages, byte for byte | 337: **0 different**; 335 native |
| Differential fuzz | 300: **0 different**, all native |
| Network programs, native only | 16: 0 different |
| Six platforms and the cross-platform check | all pass |
| `otter check`, native only, every program in otterPS's repository and tests/ | **512 of 512** |

Speed (Windows, median): `say "Hello"` 16 ms against otterPS's 2,332 ms;
22x-146x faster across the benchmarks (2,990x on lists); peak memory 10-27 MB
against 233-327 MB.

## After wave 2 - 2026-10-18 (`port/native`: UI in run/check, serve, desktop, project builds; remaining parser forms; the CLI's PowerShell binding, -DebugAst, manifests)

**All 237 of otterPS 1.0.1's programs - and all 483 programs in the corpus -
are taken on natively** (`otter check` with OTTER_ENGINE=native; it was 96
at the start of the campaign and 105 after wave 1).

| Comparison | Result |
|---|---|
| Programs on Windows, against otterPS 1.0.1 on Windows PowerShell 5.1 (CI) | 252 programs: **0 different**, 217 identical (211 native), 35 skipped |
| Network programs on Windows, native only (CI) | 16 of 16 identical |
| `otter web` pages on Windows, byte for byte, no conversion (CI) | 308 programs: **0 different**, 268 compiled natively |
| Differential fuzz on Windows (CI) | 300 programs: 0 different, all native |
| Programs on macOS, native only | 252: 197 identical (all native), 35 skipped, 20 different - all known PowerShell 7 or macOS-SQLite cases |
| CLI cases on macOS | 243: 0 different (58 known PowerShell 7 differences) |
| CLI cases on Windows (CI) | 243: 210 identical, 33 different - all PowerShell's own parameter-binding error texts (`otter -Deb`, `-Port abc`, ...; SPEC-QUESTIONS C2-1) |
| Six platforms (CI) | all build and pass tests/compat; the cross-platform check's remaining differences are declared with reasons |

Still needing the PowerShell Otter, decided before anything runs:

- **Desktop windows (WPF) in `otter run` on Windows** - needs a decision (`docs/port/desktop-options.md`).
- **Remote commands on Windows** - PowerShell remoting; needs a decision.
- PowerShell's own parameter-binding texts (C2-1), `-InformationAction Stop/Inquire/Ignore` (C2-5), and a few `otter serve`/`desktop` failure paths that otterPS reports with PowerShell's own rendering of an escaping error (see `docs/FULL_PORT_MATRIX.md`).

## Tail batch - 2026-10-18 (`port/tail`)

System information `tasks` and `printers` on Windows (Task Scheduler COM,
print spooler), the last UI parser corners, every `otter web` page of the
corpus (the system, network and data nodes now compile: 44 programs that
went to PowerShell before), `-DebugAst` of every node kind, `otter serve`'s
port casts and the web compiler's internal problems natively. Source sites
left: 1 parser, 4 web compiler (bug guards), 21 CLI (of which 12 are bug
guards or only reached by remote commands) - see `docs/FULL_PORT_MATRIX.md`.

| Comparison (macOS, otterPS 1.0.1 on PowerShell 7) | Result |
|---|---|
| `tests/compat`, native only | 195 programs: 162 identical (all native), 15 skipped, 18 different - the 11 baseline programs of this corpus, 4 database programs otterPS cannot run on macOS, `data_query_internal.ot`, `p2_case_json.ot`, and `t3_ui_columns_text.ot` (.NET's "The input string 'abc' was not in a correct format." on PowerShell 7 where 5.1, and otter-net, say "Input string was not in a correct format.") |
| `otter web` pages | 337 programs: 336 identical, **all native**; 1 different (`t3_ui_columns_text.ot`, the same wording) |
| CLI cases | 249: 0 different (59 known PowerShell 7 differences) |

## After wave 1 - 2026-10-17 (`port/native`: System, Network, Data, CLI and UI areas merged)

| Corpus (otterPS 1.0.1 unless noted) | Programs | Native | Need PowerShell |
|---|---:|---:|---:|
| benchmarks | 17 | 15 | 2 |
| conformance | 19 | 18 | 1 |
| examples | 43 | 25 | 18 |
| experiments | 32 | 31 | 1 |
| otter-docs (the website) | 103 | 0 | 103 |
| OtterWorkspace | 14 | 12 | 2 |
| projects | 8 | 3 | 5 |
| selfhost | 1 | 1 | 0 |
| **otterPS 1.0.1 total** | **237** | **105 (44%)** | **132** |
| otter-net's own tests | 200 | 157 | 43 |

Measured with `otter check` (the run path). UI programs (the website,
`tests/web`) compile natively with `otter web` but still need PowerShell for
`check`/`run`; that is the ui-run batch. Almost all of the 132 are UI.

Compatibility (macOS, against otterPS 1.0.1 on PowerShell 7):

| Comparison | Result |
|---|---|
| Programs, native only (`-NativeOnly`: a fallback counts as a failure), all five areas | 207 programs: 150 identical (all native), 35 skipped (windows/servers/events), 22 different: the 12 known baseline differences, 6 database programs otterPS cannot run on macOS, 1 .NET 5.1/7 wording difference, and 3 UI programs (`state`, `show`, `column` things) that are the ui-run batch |
| Programs on Windows against otterPS 1.0.1 on Windows PowerShell 5.1 (CI) | 207 programs: 171 identical, 35 skipped, 1 different - a real bug (`\N`/`\T` escapes), fixed in d01fe93 |
| `otter web` pages, byte for byte (`tools/compare-web.ps1`) | 262 programs: 0 different; 221 compiled natively (91 identical as written, 171 identical in 5.1 form), 41 handed to PowerShell |
| Network programs (servers, HTTP, TCP, TLS, UDP, WebSockets, watching, jobs, D130) | 16 of 16 identical, all native |
| Data programs | match except 6 that otterPS cannot run on macOS (it loads Windows' winsqlite3.dll; compared against a SQLite-patched scratch copy instead: identical) and 1 .NET 5.1/7 wording difference |
| CLI cases (`tools/compare-cli.ps1`) | 145: 125 identical, 11 known PowerShell 7 differences, 9 different, all explained and being fixed (REPL echo, usage exit code) |

Fallback sites left in the source: 22 in the parser, 22 in the CLI - all
listed with their batch in `docs/FULL_PORT_MATRIX.md`.

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
