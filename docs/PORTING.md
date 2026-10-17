# Porting otterPS 1.0.1 to otter-net

The goal: every Otter 1.0.1 program runs natively in C#, with no PowerShell
installed, and prints exactly what otterPS 1.0.1 prints. This file is the
working agreement for everyone porting, people and agents alike.

## The reference

- **otterPS 1.0.1** (tag `v1.0.1`, commit `cfb657c`) is the frozen
  reference. Keep a read-only checkout next to otter-net:

      git -C ../otterPS worktree add --detach ../otterPS-1.0.1 v1.0.1

  Never edit it. Never edit otterPS's `SPEC-DECISIONS.md`. Never touch
  `../otter` (a separate personal project).
- **What to match:** standard output byte for byte, exit codes, error text
  and positions (`FormatDetailed()`), files left behind, and standard input
  handling. otterPS on **Windows PowerShell 5.1** is the certified
  behaviour. Where PowerShell 5.1 and 7 differ, 5.1 wins (see
  `docs/baselines/` for the known places).
- **The specification** is otterPS's `SPEC-DECISIONS.md` (D-numbers),
  `rules.md` and `docs/GRAMMAR.md`. When code and documents disagree, copy
  the code and record the question in `docs/SPEC-QUESTIONS.md`. No new
  syntax, no new features, no changed semantics.

## How to port

Read the otterPS parser rule and interpreter case for a feature, then
reimplement the behaviour in clean C# with .NET APIs. Do not translate
PowerShell line by line: PowerShell's behaviour (its conversions, its
formatting, its exception messages that reach the user) is what must be
reproduced, not its code.

Each area of the language has its own files:

| Area | Parser | Runtime | Syntax nodes |
|---|---|---|---|
| System (processes, OS integration) | `Parser.System.cs` | `Interpreter.System.cs` | `Syntax.System.cs` |
| Network (HTTP, sockets, servers, watching, events) | `Parser.Network.cs` | `Interpreter.Network.cs` | `Syntax.Network.cs` |
| Data (databases, queries, CSV, console) | `Parser.Data.cs` | `Interpreter.Data.cs` | `Syntax.Data.cs` |
| UI (web and desktop) | `Parser.Ui.cs` | `Interpreter.Ui.cs` | `Syntax.Ui.cs` |

`Parser.ReadStatement`, `Parser.ReadValue` and
`Parser.ReadConditionPrimary` ask each area first (`TryRead<Area>Statement`,
`TryRead<Area>Value`, `TryRead<Area>Condition`; return null for "not
mine"). `Interpreter.Execute` and `Interpreter.Evaluate` fall through to
`TryExecute<Area>` and `TryEvaluate<Area>`. Put an area's code in its
files and in new files of its own (`src/Otter.Runtime/Http.cs`, ...).
Change shared files (`Parser.cs`, `Interpreter.cs`, `Lexer.cs`,
`TokenKind.cs`, `Syntax.cs`, `Values.cs`) only where unavoidable, in
small, separate edits: other areas are being ported at the same time.

When a feature is ported, remove the `throw Unsupported(...)` that sent it
to the PowerShell Otter. A form that is still not ported must keep
throwing `Unsupported` *before anything runs*: never start natively and
fail halfway.

## Rules the build enforces

- .NET 10, Native AOT: no runtime code generation, no reflection-based
  serialisation. `TreatWarningsAsErrors` is on, including the AOT and trim
  analysers.
- No NuGet package without a good reason. Prefer what .NET includes. Native
  libraries come from the operating system (for example SQLite: Windows'
  `winsqlite3.dll`, macOS's `libsqlite3.dylib`, Linux's `libsqlite3.so.0`),
  through `LibraryImport`.
- Windows-only features (registry, event log, credentials, WPF) must compile
  on every platform and, off Windows, give otterPS's own message for that
  case.

## Testing

Every ported feature gets programs in `tests/compat/` (with
`<name>.ot.input` for standard input). Then:

    pwsh -NoProfile -File tools/compare.ps1 -OtterPs ../otterPS-1.0.1 \
        -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll \
        -Corpus @('tests/compat')

(through `pwsh -Command` to pass the array). On macOS and Linux the
reference runs on PowerShell 7, so the differences listed in
`docs/baselines/` are expected there; Windows CI is authoritative. Each
difference that is a real bug becomes a regression program.

A feature counts as ported when its programs run natively (`OTTER_NET_TRACE=1`
shows `otter-net native: run`) and match the reference.

## Commits

Small logical commits in the repository's style
(`runtime: HTTP requests (D52)`), naming the D-numbers ported.
