# Differential fuzzing: otterPS 1.0.1 vs otter-net

Random and mutated programs are run through both implementations and every
difference is triaged: a bug in otter-net (fixed, with a regression
program), a known Windows PowerShell 5.1 / PowerShell 7 difference of the
reference (`docs/baselines/`), or an unrelated property of the program
(it prints the time, it needs Windows).

## Tools

- `tools/New-FuzzCorpus.ps1` writes the programs. Its generators are
  otterPS's own (`tools/Invoke-OtterDifferentialFuzzer.ps1`, parsed and
  reused unchanged): `New-RandomInteractiveOtterProgram` (functions, things,
  lists, loops, text) and `Mutate-OtterGrammarAware` (14 kinds of damage:
  dropped or doubled words, operator and keyword swaps, missing or extra
  periods, odd indentation, unclosed text, malformed numbers and property
  chains, extra arguments, bare words in blocks, CRLF, comments).
  `-MutateFrom` mutates existing programs; `-CaseVariants` (added by the
  coverage audit) also rewrites each one with the case of random words
  changed outside text and comments (keywords ignore case, names do not).
- `tools/compare.ps1 -NativeOnly -TimeoutSeconds 20` runs each program with
  `otter run` on both sides and compares standard output, standard error,
  the exit code and the files left behind. Slow (otterPS takes seconds per
  program), so it is run in parallel shards.
- `tools/compare-web-batch.ps1` (added by the coverage audit) does the same
  for `otter web`: the page, or the refusal, of each program.
- `tools/Get-FuzzTriage.ps1` sorts a run's differences into the known
  reasons (below) and lists the rest.
- `tools/compare-parse.ps1` (added by the coverage audit) compares what
  `otter check` decides - the syntax errors in full, or the tree as
  -DebugAst shows it - for thousands of programs at once: otterPS's lexer,
  parser and language contract are loaded once in one PowerShell process,
  otter-net's are `tools/NodeCensus --dump`. It is how the parser is fuzzed.

## Runs

| Date | Corpus | Seeds | Programs | How | Result |
|---|---|---|---:|---|---|
| CI, every push | otterPS's generators, 150 + 150 mutated | 20261001 | 300 | `compare.ps1`, Windows PowerShell 5.1 | 0 different |
| 2026-10-18 (coverage audit, macOS, PowerShell 7) | 600 generated + 600 mutated | 20261101 | 1,200 | `compare.ps1 -NativeOnly -TimeoutSeconds 20` | see below |
| 2026-10-18 | 245 programs of tests/compat and otterPS's conformance, examples and experiments (no program that touches processes, the clipboard, dialogs, credentials, the registry, power or printing, or that serves or waits), each mutated 4 times and case-varied 4 times | 20261201 | 1,960 | same | see below |
| 2026-10-18 | 516 programs (tests/compat, tests/web, otterPS's conformance, examples, experiments, otter-docs, OtterWorkspace, projects, benchmarks), each mutated 15 times and case-varied 15 times | 20261301 | 15,480 | `compare-parse.ps1` | see below |
| 2026-10-18 | the 1,200 + 1,960 programs above | - | 3,160 | `compare-parse.ps1` | see below |
| 2026-10-18 | the 15,480's mutations of tests/web and of the documentation site's pages | 20261301 | 5,490 | `compare-web-batch.ps1` | see below |

### Results (2026-10-18, macOS, otterPS 1.0.1 on PowerShell 7)

**Programs run on both sides** (`compare.ps1`, 3,160: the 1,200 generated
and mutated, the 1,960 corpus mutations and case variants). The first pass
ran with 20 seconds per program on a machine loaded by other work, and
otterPS did not finish 277 of them; those were run again with 60 seconds.

| Outcome | Programs |
|---|---:|
| identical | **3,062** |
| different, explained (see below) | 97 |
| neither side finishes (a mutation that loops for ever; both print the same lines until stopped) | 1 |
| different because of otter-net | **0** |

The 97: databases (24: otterPS loads Windows' winsqlite3.dll and cannot
open a database on macOS, SPEC-QUESTIONS Q11), PowerShell 7's ConvertTo-Json
layout (19), .NET's error texts on PowerShell 7 (20), PowerShell 7's
negative zero (9), `a<b.txt` allowed on macOS (4), PowerShell 7's empty
environment variable (4), saying a timer (3), the folder listing order (2) -
all listed in `docs/baselines/` - and programs whose output changes from run
to run (the current time, unseeded random numbers: 8) or whose zip archive
is left behind with .NET's own compressed bytes (4). The triage script
matches each program to its source and the reason.

**Parsed on both sides** (`compare-parse.ps1`): 15,480 mutations and case
variants of 516 programs, and the 3,160 above with otterPS's own corpus
(3,397): **18,877 programs**, 35 different - every one a mutation of
`t3_ui_columns_text.ot`, whose message quotes .NET's FormatException text
("The input string 'abc' was not in a correct format." on PowerShell 7,
"Input string was not in a correct format." on 5.1 and in otter-net). Every
other program gave the same tree or the same syntax errors, word for word.

**Web pages** (`compare-web-batch.ps1`, added by the coverage audit: otterPS's
web compiler loaded once, otter-net's `otter web` per program): the 5,490
mutations and case variants of tests/web (2,400) and of the documentation
site's pages (3,090, compiled beside the site's `_shell.ot` and `_docs.ot`).
The first pass found 10 differences, all one bug (a resource property set
under a differently-capitalised name, fixed); after the fix **0 different**:
3,498 identical pages and 1,992 identical refusals.


## Bugs found by the coverage audit and fixed

Most came from reading the uncovered branches (docs/coverage/) and from
running otterPS's two engines side by side (SPEC-QUESTIONS V4-2); the web
compiler's came from case-variant fuzzing. Each is fixed with a regression
program in tests/compat, tests/web, tests/cli/c4.psd1 or tests/network:

| Found by | Bug | Regression |
|---|---|---|
| comparing otterPS's engines | `otter run` of a program in otterPS's compiled-engine subset did not run as that engine does: `length of` bytes, .NET's failures in `otherwise into` and uncaught, random bounds | `c4_compiled_engine.ot` |
| coverage | moving a date beyond DateTime's range: PowerShell's `Exception calling "AddYears" ...` with .NET Framework's text (otter-net gave .NET 10's bare message) | `c4_interpreter_dotnet.ot` |
| coverage | random bounds, seeds and xml child positions beyond Int32: PowerShell's own cast error (otter-net invented "is too large for a random number"); `[int]::MaxValue` as the top of a random range picks a double | `c4_interpreter_dotnet.ot`, `c4_core.ot`, `c4_xml.ot` |
| coverage | a repeat count or a wait beyond Int32 (a wait of a month): PowerShell's cast error; otter-net saturated and waited | `c4_interpreter_dotnet.ot` |
| coverage | process ids and wait timeouts: a wait for an id [int] cannot hold has finished, a priority change wraps the cast error in its own message, a timeout goes through Max(int, int) | `c4_processes.ot` |
| network test server | closing a websocket with an application close code (4000): otterPS only marks it closed | `tests/network/websocket_close_application_code.ot` |
| case-variant web fuzzing | `x has text v` (non-literal) after `create text into X`: the property is set on the name as written | `tests/web/c4_case_resource_names.ot` |

## Reproduce

    pwsh -NoProfile -File tools/New-FuzzCorpus.ps1 -OtterPs ../otterPS-1.0.1 -OutputDirectory fuzz/gen -Count 600 -Seed 20261101 -Mutated
    pwsh -NoProfile -File tools/New-FuzzCorpus.ps1 -OtterPs ../otterPS-1.0.1 -OutputDirectory fuzz/mut -MutateFrom <sources> -Count 4 -Seed 20261201 -CaseVariants
    pwsh -NoProfile -Command "& tools/compare.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll -NativeOnly -TimeoutSeconds 20 -Corpus @('fuzz/gen')"
    pwsh -NoProfile -Command "& tools/compare-parse.ps1 -OtterPs ../otterPS-1.0.1 -Corpus @('fuzz/mut')"
