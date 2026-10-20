# Full port matrix: otterPS 1.0.1 → otter-net

Every capability of Otter 1.0.1, where it lives in otterPS 1.0.1 (`v1.0.1`,
`cfb657c`), where it lives in otter-net, and whether otter-net runs it
without PowerShell. Updated after each integrated batch. The row-level
detail is in the area inventories; this page is the summary and the list of
what remains.

Progress numbers (programs that still need PowerShell, compatibility
differences) are in `docs/PORT_DASHBOARD.md`.

The source-level proof - every otterPS NodeKind, `otter` form, exported
library function and message mapped to otter-net's C# (file:line) and to the
programs that contain it, found mechanically - is `docs/NATIVE_COVERAGE.md`
(`tools/native-coverage.ps1`); what the corpus executes, line by line, is
`docs/coverage/` (`tools/coverage.ps1`).

## By area

Counts are rows in each area's inventory: one row per statement,
expression, command or option form, with its otterPS source location, its
otter-net location, its tests and its remaining work.

| Area | Inventory | Native | Partial | Still needs PowerShell | Out of scope |
|---|---|---:|---:|---:|---:|
| Core language: values, variables, arithmetic, conditions, loops, lists, functions, things and types, `try`/`fail`, `ask`, text, lists, dates, keys, `use`, logging, files and folders, JSON, CSV, XML, bytes, crypto, random, timers, console | `docs/STATUS.md`, `docs/port/data.md` | all | - | - | - |
| System: processes, commands, system information, clipboard, notifications, dialogs, links, owners, read-only, registry, credentials, secrets, power, printing, zip | `docs/port/system.md` | 36 | 1 (remote commands) | - | - |
| Network and events: HTTP, downloads, servers and routes, WebSockets, TCP, TLS, UDP, file watching, command jobs, the event loop (D130, D133), `await` | `docs/port/network.md` | 63 | - | 1 | - |
| Data: databases (D97, D98), queries (D99), `the PROPERTY of X` | `docs/port/data.md` | 18 | - | 1 | - |
| CLI: every `otter` command, flags, projects, `new`, `test`, `build`, `publish`, REPL, debugger, profiler | `docs/port/cli.md` | 33 | 3 | 2 | - |
| UI: the web compiler (`otter web`), UI grammar, routing, state, animation, drag and drop | `docs/port/ui.md` | 63 | - | - | 1 (`otter studio`) |
| Desktop windows (WPF) in `otter run` and the REPL on Windows, through `otter-desktop.exe` | `docs/port/desktop.md` | 25 | - | - | - |

## Everything that can still hand a program to PowerShell

Three mechanisms: the parser throws `NotSupportedYet` (`Unsupported(...)`)
before anything runs, the web compiler throws `NotSupportedYet` before the
page is written, or the CLI throws `NeedsPowerShell`. Measured on
`port/tail` after the tail batch (`grep -rn 'Unsupported(\|new
NeedsPowerShell(\|new NotSupportedYet(' src`).

### Parser (`src/Otter.Language`)

| Site | Form | Why it stays |
|---|---|---|
| `Parser.System.cs` ~502 | remote commands on Windows (PowerShell remoting over WinRM) | **needs a decision** |

Every other parser site is gone: system information `tasks`, `printers` and
kinds that are not literal text run natively on Windows (Task Scheduler
COM, the print spooler; SPEC-QUESTIONS T3-1, T3-2); a responsive column
count is cast as PowerShell's `[int]` (`PowerShellInt32`); `replace route
with P` followed by more words is otterPS's syntax error; `set drag data`,
`create`, `await`, `query parameter` and the statement words could not be
reached once the UI area parses for every command (proved by a parser probe
over every statement word and three following words, and by reading each
area's null paths), and were removed. A statement word no reader takes is now
an internal error (`UnreachableException`), which `FileMode` reports as a
parser bug.

### Web compiler (`src/Otter.Web`)

| Site | Case | Why it stays |
|---|---|---|
| `JsCompiler.Statements.cs` ~1081, `JsCompiler.Expressions.cs` ~377, `NodeFields.cs` ~249 | a node class with no case | bug guards: every node otter-net parses has a case (the system, network and data nodes were ported in this batch; the corpus has no web fallback left) |
| `WebCompiler.cs` ~390 | UI resources nested more than 200 deep without a cycle | whether otterPS overflows its call depth depends on how many PowerShell calls each level takes; a cycle is native ("The script failed due to call depth overflow.") |

### CLI (`src/Otter.Cli`)

| Site | Case | Why it stays |
|---|---|---|
| `FileMode.cs` ~190, ~207; `ProjectBuild.cs` ~75; `DesktopCommand.cs` ~44; `Cli.cs` ~236 (`WebCommand`'s reason) | the parser's or web compiler's `NotSupportedYet` above | only remote commands on Windows reach them |
| `FileMode.cs` ~199, ~217; `ProjectBuild.cs` ~84; `DesktopCommand.cs` ~61; `Cli.cs` ~236 | a lexer, parser or web compiler crash | bug guards (a bug in otter-net is handed over instead of shown) |
| `AstDump.cs` ~68, ~108, ~157 | `-DebugAst` of a node class with no table entry | bug guards: every node class otter-net parses has an entry; the dumps of all 475 corpus programs match otterPS's |
| `Project.cs` ~171 | a manifest the JSON reader refuses but the syntax-error reader accepts | bug guard: the two readers agree on 2.4 million fuzzed inputs (after fixing `-NaN` and `\/Date(...)\/` overflow in this batch) |
| `Cli.cs` ~24 | `-InformationAction Inquire` | PowerShell asks on the console before every Write-Host (the question is not on standard output, so it cannot be measured redirected) and its Suspend choice opens a nested prompt that runs PowerShell commands (SPEC-QUESTIONS F5-1). Stop and Ignore are native |
| `InformationAction.cs` ~45 | a Write-Host failing first under `-InformationAction` Stop or Ignore whose otterPS position otter-net lacks | bug guard: every Write-Host that can fail first carries its position (`tests/cli/ps-texts.psd1`) |
| `Cli.cs` ~206 | `otter studio` | out of scope (the IDE is developed separately) |
| `Program.cs` | `OTTER_ENGINE=powershell`; the hand-over itself | removed last (phase D) |
| `Program.cs` `Desktop` | a desktop window program on Windows when `otter-desktop.exe` is missing beside otter | packaging guard: with the host installed (as published) desktop windows are native (`docs/port/desktop.md`; decided 2026-10-18, WPF host) |

Native in the ps-texts batch (measured on Windows PowerShell 5.1 by
`.github/workflows/cli-windows.yml`, SPEC-QUESTIONS F5): `-Verbose` (the
VERBOSE lines of otterPS's Import-Module calls), `-InformationAction Stop`
and `Ignore`, `otter serve` of an empty program and of a server that cannot
start, and `otter desktop`'s import problems, pages that cannot be written and
bridge that cannot start.

Native in an earlier batch (CLI): `otter serve`'s port casts (`ServeCommand.CastPort`,
SPEC-QUESTIONS T3-3), the web compiler's internal problems in `otter web`,
`desktop`, `build` and `publish` (`WebInternalProblem`), every `otter web`
page of the corpus, `-DebugAst` of every node kind, and the unreachable
`otter X is not native` branch removed.

### Not a fallback, but not done

| What | Why | Status |
|---|---|---|
| A REPL line using an unported feature | a session cannot be handed to PowerShell halfway; the line is refused and the session goes on | disappears when everything is ported |
| otterPS's compiled engine (`otter run` of a program in its subset) | otter-net has no run-time C# compiler (Native AOT); it decides as otterPS does whether a run is compiled and reproduces the engine's observable differences | native (coverage audit; SPEC-QUESTIONS V4-2) |

## Decisions

1. **Desktop windows (WPF) on Windows** - decided 2026-10-18: implemented
   natively with WPF in a companion `otter-desktop.exe` (Native AOT cannot
   host WPF): 26 desktop programs identical to otterPS on a real Windows
   desktop (window tree, pixels, output, exit code); `docs/port/desktop.md`.
2. **Remote commands (`run command ... on remote ... using credential`, D84)
   on Windows** - for the release candidate, the one statement still handed
   to the PowerShell Otter, which runs it with Windows' built-in PowerShell
   remoting (`Invoke-Command`), exactly as Otter 1.0.1 does. A native WinRM
   client was approved but not implemented: the attempt was stopped by an
   automated safety review, and the owner has not yet chosen between keeping
   this hand-over, implementing it under direct supervision, or deciding the
   statement's future as a specification question. Off Windows otter-net
   reproduces otterPS's behaviour natively (SPEC-QUESTIONS P2-1).
3. The open specification questions in `docs/SPEC-QUESTIONS.md`: otter-net
   keeps otterPS 1.0.1's behaviour until each is decided.
