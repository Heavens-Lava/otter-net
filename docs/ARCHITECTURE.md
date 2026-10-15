# otter-net architecture

otter-net is a second implementation of the Otter programming language, in C#
on .NET 10, compiled with Native AOT into one self-contained `otter`
executable per platform.

**The objective:** the same Otter language, powered by a faster and more
maintainable native engine. No new syntax and no new language features come
from this project. Language questions are decided once, in otterPS's
`SPEC-DECISIONS.md`, which stays the authoritative specification for both
implementations; `rules.md` and `docs/GRAMMAR.md` there describe the design.

otterPS (the PowerShell implementation, certified as Otter 1.0) is the
reference behaviour. Its release work is independent of this project: nothing
here changes otterPS, delays a release, or runs on the machine while a
certification is running.

---

## 1. Compatibility contract

For every program, otter-net must produce what otterPS produces:

| Observable | Rule |
|---|---|
| Standard output | Byte-identical, including `say` formatting (D8) |
| Errors | Same text, in the same place: otterPS prints `FormatDetailed()` to **standard output** (Write-Host), not stderr - the heading, `Line N:`, the source line, the caret where there is one, the message, `Try:` and the suggestion |
| Standard error | Same (otterPS writes nothing there for program errors) |
| Exit codes | 0 success, 2 check/syntax error, 3 runtime error, and the others otterPS uses |
| Several syntax errors | Same count, order and "Found N errors in source." line |
| Event and closure semantics | D130 (a handler set up in a loop pass keeps that pass) and D133 (a handler set up during a handler run keeps that run, and the run reads names live) |
| Input | `ask` reads lines the same way, with the same coercion (D6) |

Known places where the two runtimes could differ without care, each with a
dedicated test from the start:

- **Number display.** otterPS formats a double with `'0'` for whole numbers
  below 1e15 and `'0.##########'` otherwise, on .NET Framework 4.x. .NET 10's
  custom numeric formatting must give the same digits for every value the
  fuzzer can produce; any difference is fixed in otter-net's formatter, never
  accepted.
- **Text comparison and case.** Otter's `is`, `contains`, `starts with` and
  sorting follow otterPS's (PowerShell's) rules - case-insensitive where
  otterPS is - not .NET's defaults.
- **Dates** (`today` has no time of day; D32 display) and **culture**: always
  invariant culture.
- **Line endings** in output (`\r\n` on Windows, as otterPS writes them).

## 2. Pipeline

    source text
      -> Lexer            tokens, with Indent/Dedent and line/column
      -> Signature pass   every function name and its parameter count
      -> Parser           syntax tree (a node per otterPS NodeKind)
      -> Binder           semantic analysis: names, scopes, checks
      -> Bound tree (IR)  resolved, typed where known, slot-numbered
      -> Evaluator        runs the bound tree

    `otter check` stops after the Binder; `otter run` runs all of it.

### Lexer
Hand-written, one pass, no regular expressions on the hot path. Emits the
token kinds otterPS's lexer emits, with the same rules: indentation of four
spaces or one tab per level, `.` as a block end, the escapes `\n \t \\ \"`
(any other backslash sequence kept as written), numbers without sign or
exponent, `#` comments. Line and column on every token, because every
diagnostic shows them.

### Signature pass
Otter's grammar depends on knowing the program's functions: `calculateTax
100 and 0.08` is a call only because `calculateTax` is a function taking two
arguments, and each argument is one value. A cheap scan of `to name params`
lines (and `use` imports, expanded first, in source order, once each) builds
that table before parsing, as otterPS does.

### Parser
Recursive descent, one method per grammar rule, mirroring otterPS's parser
statement by statement so that error recovery - which errors are reported,
at which column, and how many - matches. Arithmetic has no precedence and no
parentheses: strictly left to right (`2 plus 3 times 4` is 20). Conditions
have `not` > `and` > `or`, short-circuiting, and are not values (D123).

The syntax tree uses one C# class per otterPS node kind (226 kinds in the
contract), immutable, each carrying its line. The prototype covers the core
kinds; the rest arrive with the features that need them.

### Binder (semantic analysis)
Turns the syntax tree into the bound tree and produces `otter check`'s
diagnostics:

- **Scopes.** The program's top level and each function body are scopes;
  parameters never overwrite globals (the brief's `greet` test). An
  undefined variable is a runtime error, not false (D9) - the binder records
  where a name may be undefined; it does not invent new compile errors that
  otterPS would not report.
- **Slots.** Every variable in a scope gets a slot number, so the evaluator
  reads and writes an array, not a dictionary.
- **Loop-pass and handler-run names** (D130, D133): the same static rule as
  otterPS's `Get-OtterLoopPassNames` - names assigned inside the loop body or
  handler and nowhere else in the enclosing scope, whichever branches run.
- **The checks otterPS's `otter check` makes**, including the ones that
  refuse a form (for example a handler inside a watcher that uses its run's
  names) - same codes, same messages.

### Bound tree (the intermediate representation)
A tree of bound nodes - resolved slots, known function targets, constant
literals pre-converted to values, the D130/D133 capture lists attached to
loops and handlers. It is the one input to the evaluator, and later to a
compiler (section 6), so analysis is never repeated per statement.

### Evaluator
A tree-walking evaluator over the bound tree. Each bound node evaluates
through a virtual method (no big `switch` on kinds, no reflection). Values:

| Otter | C# |
|---|---|
| number | `double` |
| text | `string` |
| true / false | `bool` |
| gone | a single `Gone` value, never `null` |
| list | `OtterList` (a `List<OtterValue>` with Otter's add/remove rules, D12) |
| thing | `OtterThing` (ordered properties, identity equality) |
| date | `OtterDate` (date only, or date and time) |

`OtterValue` is a small struct (kind + double + object reference), so
numbers in loops do not allocate.

Errors are one `OtterError` type with stage, code, line, message and
suggestion, and one `FormatDetailed()` that reproduces otterPS's layout
exactly. Nothing else may reach the user: the entry point catches everything
else and reports an internal error politely (D14), with a stack trace only
under `--debug`.

## 3. The `otter` command and the PowerShell fallback

otter-net replaces otterPS feature by feature, never all at once:

1. Parse and bind the program. If the program uses anything otter-net does
   not implement yet (desktop windows, a library area not ported yet), the
   binder says so **before anything runs**.
2. Then `otter` hands the whole program, unchanged, to the PowerShell Otter
   (`otter.ps1` from the installed release) - the user gets exactly today's
   behaviour. A program never starts natively and falls back halfway.
3. Commands not implemented yet (`web`, `desktop`, `serve`, `build`,
   `publish`, ...) are passed through the same way.

`OTTER_ENGINE=powershell` forces the old path; `OTTER_ENGINE=native` refuses
to fall back (for tests). Every release moves more programs onto the native
path and loses nothing.

Native AOT cannot host WPF, so desktop windows stay on the PowerShell Otter;
a separate non-AOT desktop host is a later decision, not part of this plan.

## 4. Compatibility harness

`tools/compare` runs a corpus through both implementations and compares
standard output, standard error and exit code, byte for byte, with the same
standard input:

- otterPS's conformance fixtures (`conformance/`, with their expected
  output) and its console examples;
- the programs of otterPS's differential fuzzer, generated with a fixed seed
  (the same generator, so failures reproduce);
- otter-net's own regression cases, one per bug found.

Each mismatch is reported with the program, both outputs and a first-
difference marker. A phase is done only when its corpus shows zero
differences - not when unit tests pass. The harness runs the real `otter`
executables of both implementations, never internal functions.

## 5. Benchmarks

`tools/bench` measures both implementations on the same machine, several
runs each, reporting median and spread:

| Benchmark | What it shows |
|---|---|
| Cold start: `say "Hello"` | Process start to exit - the 1-4 s problem |
| Warm run | The same program run again immediately (caches warm) |
| Loops | `count from 1 to 1000000` with arithmetic |
| Functions | Recursive calls (Fibonacci) and many small calls |
| Lists and text | Building, searching and joining |
| Memory | Peak working set of each benchmark |
| Application | A realistic console program (the task tracker's console part) |

Run on Windows locally and on Windows, macOS and Linux in GitHub Actions.
Results are recorded with the commit, machine and .NET version.

## 6. Later: compiled execution

The bound tree is designed so a second back end can sit beside the
evaluator: `otter build` could compile a program ahead of time (bound tree to
C# source, then the native compiler), as otterPS's compiled engine does
today. Native AOT forbids generating code at run time, so `otter run` keeps
the evaluator; compiled execution is for built apps. This is a later
decision; nothing in the first phases depends on it.

## 7. Layout

    src/Otter.Language    Lexer, signature pass, syntax tree, parser, binder,
                          bound tree, diagnostics
    src/Otter.Runtime     Values, scopes, evaluator, formatting, OtterError
    src/Otter.Cli         The `otter` command (Native AOT), fallback
    tests/Otter.Tests     Unit tests (lexer, parser, binder, evaluator)
    tools/compare         Compatibility harness (both implementations)
    tools/bench           Benchmarks
    docs/                 This document, phase records, benchmark results

## 8. Phases

1. **Prototype (now):** this document; a minimal executable (`say`,
   variables, numbers and text, arithmetic, `if`/`otherwise`, `count` and
   `each` loops, lists, functions, runtime errors in otterPS's layout); the
   compatibility harness running the conformance fixtures it covers; first
   cold-start numbers.
2. **Core language complete:** every core grammar rule (section 2 of
   GRAMMAR.md), things and types, `try`/`otherwise`, `ask`, `use`, all
   syntax errors with otterPS's recovery; zero differences on the core
   conformance fixtures and 1,000 of 1,000 fuzz programs; the fallback in
   place, so `otter` can already be used for everything.
3. **Standard library:** text, dates, files and folders, JSON, CSV, XML,
   random, environment, processes, HTTP - each ported with otterPS's tests
   for it.
4. **Projects:** `otter new`, `check`, `test`, `build`, `publish` for
   console projects.
5. **Shipping:** the native `otter` in the release ZIP, the Store package and
   the installer, with a both-implementations certification gate.
6. **Later:** `otter web`, servers, databases, compiled `otter build`.
