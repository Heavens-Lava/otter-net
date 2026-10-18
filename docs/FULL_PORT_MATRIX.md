# Full port matrix: otterPS 1.0.1 → otter-net

Every capability of Otter 1.0.1, where it lives in otterPS 1.0.1 (`v1.0.1`,
`cfb657c`), where it lives in otter-net, and whether otter-net runs it
without PowerShell. Updated after each integrated batch. The row-level
detail is in the area inventories; this page is the summary and the list of
what remains.

Progress numbers (programs that still need PowerShell, compatibility
differences) are in `docs/PORT_DASHBOARD.md`.

## By area

Counts are rows in each area's inventory: one row per statement,
expression, command or option form, with its otterPS source location, its
otter-net location, its tests and its remaining work.

| Area | Inventory | Native | Partial | Still needs PowerShell | Out of scope |
|---|---|---:|---:|---:|---:|
| Core language: values, variables, arithmetic, conditions, loops, lists, functions, things and types, `try`/`fail`, `ask`, text, lists, dates, keys, `use`, logging, files and folders, JSON, CSV, XML, bytes, crypto, random, timers, console | `docs/STATUS.md`, `docs/port/data.md` | all | - | - | - |
| System: processes, commands, system information, clipboard, notifications, dialogs, links, owners, read-only, registry, credentials, secrets, power, printing, zip | `docs/port/system.md` | 34 | 2 | 3 | - |
| Network and events: HTTP, downloads, servers and routes, WebSockets, TCP, TLS, UDP, file watching, command jobs, the event loop (D130, D133), `await` | `docs/port/network.md` | 63 | - | 1 | - |
| Data: databases (D97, D98), queries (D99), `the PROPERTY of X` | `docs/port/data.md` | 18 | - | 1 | - |
| CLI: every `otter` command, flags, projects, `new`, `test`, `build`, `publish`, REPL, debugger, profiler | `docs/port/cli.md` | 33 | 3 | 2 | - |
| UI: the web compiler (`otter web`), UI grammar, routing, state, animation, drag and drop | `docs/port/ui.md` | 51 | - | 7 | 1 (`otter studio`) |

## Everything that can still hand a program to PowerShell

These are the only two mechanisms: the parser throws `NotSupportedYet`
(`Unsupported(...)`) before anything runs, or the CLI throws
`NeedsPowerShell`. Measured at `cb666ae`; the batch working on each is in
the last column.

### Parser (`src/Otter.Language`)

| Site | Form | Batch |
|---|---|---|
| `Parser.cs` ~1348, ~1354, ~1588 | UI elements, `'X' things` built from them, UI properties, in `run`/`check` (they parse only for `otter web` today) | ui-run |
| `Parser.cs` ~618 | `create` (UI resources) in `run`/`check` | ui-run |
| `Parser.cs` ~1775 | `await` in `run`/`check` | ui-run |
| `Parser.cs` ~1918, ~530 | `query parameter`, `replace route` in `run`/`check` | ui-run |
| `Parser.cs` ~360, ~711 | the special statement words (`window`, `page`, `card`, ... and the non-UI ones) | ui-run, parser-rest |
| `Parser.Ui.cs` ~533 | a responsive column count that is not a number | ui-run |
| `Parser.cs` ~466 | `stop watching` (form not reached by the Network area) | parser-rest |
| `Parser.cs` ~829, ~918 | HTTP delete / get variants not reached by the Network area | parser-rest |
| `Parser.cs` ~905, ~922 | a discovery or `get` clause continued on an indented line | parser-rest |
| `Parser.cs` ~914, ~1165 | `get X` / `set X` for words no area reads | parser-rest |
| `Parser.cs` ~1993 | a value word no area reads | parser-rest |
| `Parser.Network.cs` ~937 | `the` after a property in network forms | parser-rest |
| `Parser.System.cs` ~196 | Windows only: system information `tasks`, `printers`, a kind that is not literal text | parser-rest |
| `Parser.System.cs` ~252 | Windows only: the event log | parser-rest |
| `Parser.System.cs` ~512 | remote commands (PowerShell remoting over WinRM) | parser-rest - **needs a decision** |

### CLI (`src/Otter.Cli`)

| Site | Case | Batch |
|---|---|---|
| `CommandLine.cs` ~76, ~86, ~93 | arguments PowerShell's parameter binder handles (`-x`, `--name`, prefixes, `-Port:5`, `-Port` without a number, `-Breakpoints` without a value, common parameters) | cli-rest |
| `Cli.cs` ~167 | a breakpoint that is not a whole number | cli-rest |
| `Cli.cs` ~85, ~125, ~141, ~148, ~156; `Repl.cs` ~46 | `-DebugAst` (otterPS dumps its own syntax tree) | cli-rest |
| `Project.cs` ~39, ~139, ~150 | a path starting with `~`; a manifest that cannot be read; a manifest that is not valid JSON | cli-rest |
| `ProjectCommands.cs` ~74 | a project name with a control character | cli-rest |
| `ProjectBuild.cs` ~57 | `build`/`publish` of web, desktop and game projects | ui-run |
| `Cli.cs` ~209, `WebCommand.cs` | `otter web` options it does not accept, a project folder, import problems | ui-run |
| `Cli.cs` ~211 | `otter serve`, `otter desktop`, `otter browse` of a project | ui-run |
| `Cli.cs` ~183 | `otter studio` | out of scope (the IDE is developed separately) |
| `FileMode.cs` ~163, ~172, ~180, ~190 | the parser's `NotSupportedYet` above; a lexer or parser crash (a bug in otter-net is handed over instead of shown) | - |
| `Program.cs` ~45, ~67, ~123 | `OTTER_ENGINE=powershell`; the hand-over itself | removed last (phase D) |

### Not a fallback, but not done

| What | Why | Status |
|---|---|---|
| Desktop windows (WPF) in `otter run` on Windows | Native AOT cannot host WPF | **needs a decision**: `docs/port/desktop-options.md` |
| A REPL line using an unported feature | a session cannot be handed to PowerShell halfway; the line is refused and the session goes on | disappears when everything is ported |

## Decisions needed from the project owner

1. Desktop windows on Windows (WPF): `docs/port/desktop-options.md`.
2. Remote commands (`RunRemoteCommand`): PowerShell remoting has no
   equivalent outside PowerShell.
3. The open specification questions in `docs/SPEC-QUESTIONS.md` (Q1-Q16,
   UI-1 to UI-4): otter-net keeps otterPS 1.0.1's behaviour until each is
   decided.
