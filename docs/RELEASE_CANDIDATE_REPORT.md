# otter-net 1.0.1-rc.1 - native coverage and certification report

**For:** the project owner's review of the release candidate.
**Branch:** `port/native` (draft PR #3, stacked on PR #1). Not merged, not
released, not published.
**Reference:** otterPS 1.0.1 (`v1.0.1`, `cfb657c`), frozen; on Windows
PowerShell 5.1, the certified behaviour.
**Date:** 2026-10-19.

## Verdict

otter-net runs the whole of Otter 1.0.1 in C#, without PowerShell, on six
platforms, and produces otterPS 1.0.1's output byte for byte - with **one
documented exception**: remote commands on Windows (`run command ... on
remote ... using credential`, D84), which for this release candidate are
still run by Windows' built-in PowerShell, exactly as Otter 1.0.1 runs them
(see "What is not native").

Everything below is measured; nothing is inferred from a sample.

## 1. Completeness, at the source level

Generated from the sources of both implementations by
`tools/native-coverage.ps1` (details, file:line for every row:
`docs/NATIVE_COVERAGE.md`).

| What | Result |
|---|---|
| otterPS language node kinds (`enum NodeKind`, 226) | 225 have a node class in otterPS; **225 of 225 are native**. 223 occur in test programs; the other 2 (`route parameter`, `query parameter`) cannot be written in 1.0.1's grammar (SPEC-QUESTIONS V4-1). `UiLayout` is an enum value nothing builds. |
| `otter` commands, flags and environment switches | 32 forms: **30 native, all 30 tested**; 2 out of scope (`otter studio`; otterPS's own compiled-engine switches) |
| Library functions exported by otterPS (`Otter.Library.psm1`, `Otter.Runtime.psm1`) | 95: **93 have a C# counterpart**; the 2 others are the remote-command functions |
| Messages otterPS can show (errors and refusals, by literal text) | 473: **439 present in otter-net**, 34 unreachable or out of scope (each explained), **0 missing** |
| otterPS's two runtimes | otterPS's `otter run` uses its *compiled engine* when every statement is in that engine's subset, and its interpreter otherwise; otter-net makes the same choice and reproduces both (SPEC-QUESTIONS V4-2) |
| Code coverage of otter-net over the whole corpus (1,337 runs) | 84.3% of lines, 78.5% of branches (Language 96.0/90.1, Web 97.3/92.2, Runtime 76.6/71.3, Cli 69.4/55.6 - most of the rest is Windows-only code a macOS run cannot reach); uncovered lines and unshown errors listed in `docs/coverage/` |
| Programs `otter check` takes on natively with no PowerShell (`tools/port-progress.ps1`) | **568 of 568** (otterPS's 237 programs and otter-net's tests) |

## 2. Compatibility, against otterPS 1.0.1 on Windows PowerShell 5.1

CI run 39210368730 (commit `5723162`; the release-candidate commit `a351d8c` passed again in run 39221464597), Windows Server 2025:

| Comparison | Compared | Different | Native |
|---|---:|---:|---:|
| Programs: conformance fixtures, examples, `tests/compat` | 271 (37 more skipped: windows, servers that never end) | **0** | 233 (1: remote commands) |
| Network and event programs (servers, HTTP, TCP, TLS, UDP, WebSockets, watching, jobs), native only | 18 | **0** | 18 |
| `otter` command lines (`tools/compare-cli.ps1`) | 259 | **0** | - |
| `otter web` pages, byte for byte | 363 | **0** | 361 (2: remote commands) |
| Differential fuzz (150 generated + 150 mutated) | 300 | **0** | 300 |
| Desktop windows (WPF), real windows on a Windows desktop, driven by UI Automation: window tree after every step, pixels, output, exit code (run 39221458648) | 26 | **0** | 26 |

Larger fuzzing, on macOS against otterPS on PowerShell 7 (`docs/port/fuzz.md`):
3,160 programs run (0 differences caused by otter-net), 18,877 parsed, 5,490
web pages compiled (0 differences after one fix).

## 3. Platforms and a clean machine

| | Result (CI runs 39210368730 and 39221464597; package runs 39210362133 and 39221458673) |
|---|---|
| Native AOT builds | win-x64, win-arm64, osx-arm64, osx-x64, linux-x64, linux-arm64 (Debian 12 = Chromebooks): all pass |
| Same output on every platform | passes; every platform difference declared with its reason in `.github/workflows/ci.yml` (path separators, Windows-only features' messages, the operating system's own words, one machine-specific folder) |
| **Without PowerShell installed** (Debian 12, x64 and Arm, before PowerShell is installed) | **293 of 293** test programs ran natively |
| Packages built and installed on clean machines without PowerShell | Windows MSI and ZIP (x64, Arm), macOS `.pkg` and tarball (Apple silicon, Intel), Linux `.deb` and tarball (x64, Arm): all 12 jobs pass - install, upgrade from an older package, the test corpus, `otter new`/`run`/`check`/`test`, uninstall leaving nothing behind (`docs/PACKAGING.md`) |

## 4. Desktop windows (WPF)

Native AOT cannot host WPF, so desktop windows run in a companion
`otter-desktop.exe` (WPF, the same interpreter, a port of otterPS's
`Otter.UI.psm1`), started by `otter` for a program that creates a window,
on Windows. 26 programs (every control kind and property, layout, events and
handlers with D130/D133, state/derive/watch, lifecycle, nested windows,
handler errors, the REPL, eight otterPS examples unchanged) are compared with
otterPS on a real Windows desktop (`.github/workflows/desktop.yml`,
`docs/port/desktop.md`). Tolerated, documented: hidden scroll bars only .NET
Framework lists, anti-aliasing up to 2/255 on 3 pixels, window placement.

Result: **26 of 26 programs identical** (run 39221458648, commit `a351d8c`).

## 5. Speed and memory

Windows, CI, median of 7 runs (`tools/bench.ps1`):

| Program | otterPS 1.0.1 | otter-net | Speed-up | Peak memory (MB) |
|---|---:|---:|---:|---|
| hello (cold start) | 2,332 ms | 16 ms | 146x | 233 / 10 |
| text | 3,254 ms | 31 ms | 105x | 312 / 21 |
| data (files + JSON) | 5,082 ms | 95 ms | 54x | 327 / 13 |
| functions | 3,374 ms | 142 ms | 24x | 310 / 27 |
| loops | 3,312 ms | 152 ms | 22x | 310 / 27 |
| lists | 137,544 ms | 46 ms | 2,990x | 310 / 20 |

`otter --version` from an installed package: 5-9 ms (Debian), 16 ms (macOS),
14-19 ms (Windows).

## 6. What is not native

Every place in otter-net's source that can still hand a program to the
PowerShell Otter (29), from `docs/NATIVE_COVERAGE.md`:

| Count | Category | What |
|---:|---|---|
| 6 | **Remote commands on Windows** (D84) | The one feature not native. The owner approved a native WinRM client; the attempt was stopped by an automated safety review before any code was written, and it was not retried. For this release candidate the statement is handed to the PowerShell Otter, which uses Windows' built-in PowerShell remoting, as Otter 1.0.1 does. **Owner decision:** keep this, implement it under direct supervision, or decide the statement's future as a specification question. |
| 7 | PowerShell-rendered errors | Failures PowerShell itself prints on Windows: `-InformationAction Stop/Inquire/Ignore`, `otter serve` of an empty program or a server that cannot start, three `otter desktop` (browser app window) failures |
| 4 | PowerShell-rendered output | `-Verbose`, under which otterPS lists the PowerShell modules it loads (SPEC-QUESTIONS C2-5) |
| 11 | Bug guards | Reached only if otter-net itself crashes or lacks a table entry (none in 1,337 corpus runs and the fuzzing) |
| 1 | Out of scope | `otter studio` - the IDE is developed separately |

`otter-desktop.exe` also falls back to the PowerShell Otter if it is missing
from the installation.

## 7. Open specification questions (48)

Recorded in `docs/SPEC-QUESTIONS.md`. In every case otter-net keeps otterPS
1.0.1's behaviour until the owner decides; none was changed. They fall into:

- **Bugs in otterPS 1.0.1 copied faithfully** (likely to be fixed in a later
  version): Q12 (an empty program is an internal error), Q14, UI-1 (invalid
  page script), UI-2, UI-3, D4-1, D4-4 (every UI resource equals every
  other), P2-1, V4-1, T3-3, and others.
- **Code and documents disagree**: Q1 (`months between`), Q10 (D99 queries
  ship though deferred), Q3/C2-2, Q7.
- **Windows PowerShell 5.1 and PowerShell 7 differ** (otterPS on macOS/Linux
  behaves differently from Windows): Q16, UI-4, UI2-3, C2-7, and the twelve
  baseline differences in `docs/baselines/`. The cross-platform proposal is
  PR #2.
- **Platform-dependent by nature**: Q8, Q9, Q11 (databases off Windows),
  Q15.
- **Should PowerShell's own behaviour be part of the language at all**: C2-1
  (PowerShell's parameter-binding errors, reproduced character for
  character), C2-5, V4-2 (the compiled engine's differences).
- **To confirm on a real Windows machine**: T3-1 (paused printers), T3-2
  (scheduled tasks order).

## 8. Decisions needed before a release

1. **Remote commands** (section 6).
2. **Code signing**: Windows (`otter.exe`, `otter-desktop.exe`, the MSI) and
   macOS (Developer ID, notarization). Blocks publishing.
3. **Replace otterPS or install beside it**: reuse otterPS's MSI upgrade
   code, winget and Store identity, or keep otter-net's new ones.
4. **`otter-desktop.exe`**: framework-dependent (needs the .NET 10 Desktop
   Runtime) or self-contained (about 60-100 MB more).
5. **The version** to release, and what `otter --version` prints.
6. **Placeholders**: the macOS package identifier and the `.deb` maintainer
   address.
7. **Review artifacts**: the unsigned packages from the package workflow are
   kept 14 days as GitHub Actions artifacts of a public repository.
8. **The specification questions** (section 7), at the owner's pace; none
   blocks the release candidate.

## 9. How to reproduce

    dotnet build otter-net.sln -c Release
    pwsh tools/compare.ps1     -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll -NativeOnly
    pwsh tools/compare-cli.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
    pwsh tools/compare-web.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
    pwsh tools/port-progress.ps1 -OtterPs ../otterPS-1.0.1 -OtterNet src/Otter.Cli/bin/Release/net10.0/otter.dll
    pwsh tools/native-coverage.ps1 -OtterPs ../otterPS-1.0.1

On macOS and Linux the reference runs on PowerShell 7; the authoritative
comparison is CI on Windows (`.github/workflows/ci.yml`, `desktop.yml`,
`package.yml`).
