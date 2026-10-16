# otter-net status - 2026-10-16

Measured by CI run 38939573947 (commit "cli: every line reaches the console at
once"), native AOT builds on GitHub-hosted runners, against otterPS 1.0.1
(tag `v1.0.1`). Native executables are built only in CI until they can be
signed.

## Compatibility

| Corpus | Programs | Different | Run natively |
|---|---:|---:|---:|
| otterPS conformance + examples + `tests/compat` (Windows) | 140 (20 skipped: windows, servers, events) | **0** | 106 of 120 |
| Differential fuzz, otterPS's generators (150 generated + 150 mutated) | 300 | **0** | 299 of 300 |
| `tests/compat` on Linux and macOS vs otterPS's output on Windows | 78 | 1 (path separator in one error message) | 78 of 78 |

Every difference found along the way became a regression test in
`tests/compat`: `err_depth_math.ot` and `deep_nesting.ot` (a runaway
recursion crashed with a .NET stack overflow), `output_before_hang.ot`
(output was buffered, so a program that never ends showed nothing),
`xml_words.ot` and `syn_otherwise.ot` (fuzz programs fell back), and the
file, folder, JSON and syntax-error suites.

The one remaining platform difference: an error message that quotes a full
path uses `/` on Linux and macOS (`Cannot overwrite the item <work>/same.txt
with itself`), as otterPS on PowerShell 7 would.

## Native coverage

Runs in C#: the core language (values, variables, arithmetic, conditions,
loops, lists, functions, things and types, `try`/`fail`, `ask`), text and
list operations, dates, dynamic keys, `use` imports, logging, files and
folders, JSON, syntax diagnostics with otterPS's recovery, and the D-2
reserved-name check. In otterPS's node kinds: 64 of 226; most of the rest are
UI, web, networking, databases and system integration.

## Still handed to the PowerShell Otter

Programs that use any of these run through otterPS, unchanged (the decision
is made before anything runs):

- CSV, XML, random numbers, `say in color`, `ask secretly`, `larger of` and
  other `of` operations not yet ported, command results (`run`), clipboard,
  environment variables, `get current ...`, system folders, processes,
  registry, credentials, secrets, hashing and encryption, zip.
- HTTP, downloads, servers, websockets, TCP/UDP, file watching.
- Databases and queries.
- UI: windows, pages, state, events, animation, routing.
- Every `otter` command other than `run`, `check` and `--version` (`new`,
  `test`, `build`, `web`, `serve`, `studio`, the REPL, projects).
- A `use` path that is missing, absolute, circular or differs in capital
  letters from the disk (otterPS reports these).

## Startup and speed (native, median of 7 runs; ms)

| Program | otterPS 1.0.1 (Windows) | otter-net Windows | Linux | macOS |
|---|---:|---:|---:|---:|
| hello (cold start) | 2,318 | 16 | 4 | 35 |
| loops | 3,230 | 126 | 103 | 108 |
| functions | 3,297 | 126 | 208 | 132 |
| lists | 134,297 | 39 | 27 | 64 |
| text | 3,206 | 31 | 19 | 44 |
| data (files + JSON) | 5,046 | 95 | 55 | 59 |

Peak memory on Windows: otterPS 233-328 MB, otter-net 6-25 MB.
