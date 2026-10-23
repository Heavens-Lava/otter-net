# Certifying otter-net

How a commit of otter-net is certified for release, and the results for the
commit certified last. The process is otterPS 1.0.1's release certification
(`tools/Invoke-OtterReleaseCertification.ps1`,
`docs/OTTER_1_0_1_RELEASE_EVIDENCE.md` in otterPS) adapted to otter-net: a
clean, detached checkout of exactly one commit, every gate run once by the
repository's own tools, and a machine-readable record of each.

## The process

### One command per platform

    pwsh -NoProfile -File tools/Invoke-OtterNetCertification.ps1 -Commit <sha> -OtterPs <otterPS 1.0.1 checkout>

(on Windows: `powershell -NoProfile -ExecutionPolicy Bypass -File
tools\Invoke-OtterNetCertification.ps1 ...`, with `-IncludeMachineState` on a
CI runner only - it adds printers, tasks, services and event logs.)

- The run counts as **certification** only in a clean (nothing modified or
  untracked), detached checkout whose HEAD is the commit. When the script is
  started anywhere else it makes one (`git worktree add --detach`), runs that
  commit's own copy of the script there and removes it afterwards. A commit
  older than the script cannot be certified by it.
- The reference is otterPS 1.0.1 (`v1.0.1`, `cfb657c`); the script refuses
  any other checkout.
- `-Only <gates>` makes a development run (not certification), as does
  `-FrameworkDependent` (for a machine that cannot link Native AOT).

Output, under `release/certification/` (or `-OutputDirectory`):

| File | What |
|---|---|
| `<sha>/<platform>.json` | the platform's record: commit, checkout, reference, host, CI run, every gate with pass/fail, counts, failures and evidence paths |
| `<sha>/<platform>.md` | the same for people |
| `<sha>/<platform>/reports/`, `logs/`, `captures/` | the evidence: each tool's report and counts, the full output of each gate, and what each side printed (for the cross-platform check) |
| `<sha>.json`, `<sha>.md` | `-Combine`: all six platforms and each one compared with Windows |
| `<sha>/cross-platform/<platform>.md` | that comparison, case by case |

### The gates

| Gate | What runs | Passes when |
|---|---|---|
| build | `dotnet build otter-net.sln -c Release`; Native AOT `dotnet publish` of `otter` for the platform (and `otter-desktop` on Windows) | both succeed with 0 warnings |
| conformance | otterPS's `conformance/manifest.json` fixtures through `otter run/check/web`, against their expected output (as otterPS's `Test-OtterReleaseConformance.ps1`, with the headless-browser check where Edge or Chrome is installed); then every program of `conformance/` against otterPS (`tools/compare.ps1`) | every fixture and program matches |
| programs | `tools/compare.ps1 -NativeOnly`: otterPS's conformance and examples, `tests/compat` | 0 different |
| network | `tools/compare.ps1 -IncludeEventPrograms -NativeOnly`: `tests/compat/net_*`, and `tests/network` against `tools/network-test-server.js` | 0 different |
| cli | `tools/compare-cli.ps1`: every case of `tests/cli` | 0 different |
| web | `tools/compare-web.ps1`: `tests/web`, otterPS's examples and conformance, `tests/compat`, every written file byte for byte | 0 different |
| fuzz | otterPS's own fuzz generators (`tools/New-FuzzCorpus.ps1`, seed 20261001): 150 generated programs, 150 grammar-aware mutations, and 2 mutations and 2 case variants of each conformance and top-level example program, through `tools/compare.ps1` | 0 different |
| projects | every project of otterPS's `projects/` and `OtterWorkspace/` (`check .`, `check <entry>`, `run .` for a program that ends, `test .`, `build .`) and `check` of every example in `examples/` (they also run in *programs* and compile in *web*), through `tools/compare-cli.ps1` | 0 different |
| site | the website: otterPS's `otter-docs/scripts/build-otter-pages.ps1` (how otterPS builds it) run as it is, and run with its one `Export-OtterWebApplication` call replaced by otter-net's `otter web`; both sites (100 pages, static files, search index) and `otter web site.ot` compared file by file (`tools/compare-site.ps1`) | every file identical |
| no-powershell | the whole corpus again - programs including the ones that never end (20 s each), web pages, every CLI and project case, the fuzz programs, the site - with fake `pwsh`, `powershell` and `powershell.exe` first on otter-net's `PATH`, logging each launch; on Windows, as administrator, also Windows' process-creation audit (event 4688), for every `powershell.exe`/`pwsh.exe` whose parent is `otter` | no launch, except a program running its own `.ps1` file (`-File <file>.ps1`, never `otter.ps1`) |
| native-coverage | `tools/native-coverage.ps1`; and every process start in `src/` listed with what it starts | 0 places handing anything to PowerShell, every node kind native, 0 messages missing, and only `Commands.cs` (a program's `.ps1`) and `DesktopCommand.cs` (a terminal the user opens) name PowerShell |
| desktop | Windows: `tests/desktop` harness, real WPF windows driven by UI Automation against otterPS; and a window program without `otter-desktop.exe` asks for a reinstall | every program identical |
| machine-state | Windows: `tools/windows-state.ps1`, then `tests/windows-state` and the system programs of `tests/compat` against otterPS, also under German and Finnish formats | 0 different |
| repository-clean | `git status` after the gates | unchanged |

### The reference, and the six platforms

`.github/workflows/certify.yml` (on `workflow_dispatch` with a commit, and
on every push to `cert/certify`) runs the script on all six configurations:

| Platform | Runner | Reference |
|---|---|---|
| win-x64 | windows-latest | **otterPS 1.0.1 on Windows PowerShell 5.1, compared directly** (authoritative) |
| win-arm64 | windows-11-arm | otter-net alone; desktop windows and machine state against otterPS on that machine's Windows PowerShell 5.1 |
| osx-arm64 | macos-latest | otter-net alone |
| osx-x64 | macos-15-intel | otter-net alone |
| linux-x64 | ubuntu-latest, `debian:bookworm` container | otter-net alone |
| linux-arm64 | ubuntu-24.04-arm, `debian:bookworm` container | otter-net alone |

"otter-net alone" means a gate fails on any gap in otter-net or crash, and
what otter-net printed is captured (exit code, standard output, standard
error, every file left behind with its hash). The `combine` job then
compares each platform's captures, case by case, with the Windows
reference's (`tools/compare-captures.ps1`): the case folder and the path
separator after it are normalised; every other difference fails unless
`tests/cross-platform.psd1` declares it with its reason, as `ci.yml`'s
cross-platform check does.

Both repositories are checked out as Windows checks them out
(`core.autocrlf true`) on every platform, so every platform runs
byte-identical programs, and otterPS is as released: its release archive is
built on Windows, and its modules' here-strings become the web pages and
project files it writes, so an LF checkout of otterPS ends their lines with LF
where the released otterPS (and otter-net) write CRLF - found by the first
certification run, a property of the reference checkout, not of either
Otter.

Only records, reports and captured output are uploaded, never a binary.

### Certifying another commit or branch

- **A commit**: Actions -> certify -> Run workflow, on any branch, with
  `sha` set to the commit (default `HEAD`, the branch head). The commit must
  contain this tooling (`tools/Invoke-OtterNetCertification.ps1` and the
  tools it runs); a commit without it is refused with that reason.
- **A branch on every push**: add it to `on.push.branches` in
  `.github/workflows/certify.yml`. Runs are grouped per branch, and a new
  push cancels the branch's unfinished run.
- The `combine` job's artifact `certification` holds `<sha>.json`, `<sha>.md`
  and `<sha>/cross-platform/`; each platform's artifact (`cert-<platform>`)
  holds its record and evidence. Commit the combined record under
  `release/certification/` as the evidence of the release.

## Results: the internal preview's commit

**Commit `f4e513431b23d64898ac44543f057dc5699952a0`** (branch
`release/0.9.0-preview.1`, the internal preview 0.9.0-preview.1), run
**39881403736** (2026-10-23): **every gate passed on all six platforms
with the same totals as below, and every platform matches the Windows
reference** (0 unexpected; declared win-arm64 9, macOS 50, Linux 51).
Record: `release/certification/f4e513431b23d64898ac44543f057dc5699952a0.md`
and `.json`. The same gates also passed on `fbc46a4` (run 39747899302).
The section below is the first certification, with the full detail.

## Results: the first commit certified

**Commit `2f63abc4de9a0e19604096e1d279e59b89524eae`** (branch
`cert/certify`: `c474ab8` plus this certification tooling; no change to
otter-net's `src/`). **Certified: every gate passed on all six platforms,
and every platform matches the Windows reference.**

GitHub Actions run **39704142897** (2026-10-22): all seven jobs succeeded.
Record: `release/certification/2f63abc4de9a0e19604096e1d279e59b89524eae.json`
and `.md`, with `.../cross-platform/<platform>.md` (the platform records and
evidence are the run's `cert-<platform>` artifacts).

| Gate | win-x64 (against otterPS on Windows PowerShell 5.1) | win-arm64 | osx-arm64 | osx-x64 | linux-x64 | linux-arm64 |
|---|---|---|---|---|---|---|
| build | 0 warnings (solution, AOT, otter-desktop) | 0 warnings | 0 | 0 | 0 | 0 |
| conformance | 15/15 fixtures (2 in headless Edge), 16/16 programs identical (3 skipped) | 15/15, 16 ran | 15/15, 16 ran | 15/15, 16 ran | 15/15, 16 ran | 15/15, 16 ran |
| programs | 234 of 234 identical, all native (37 skipped: never end or nondeterministic) | 234 ran | 234 | 234 | 234 | 234 |
| network | 22 of 22 identical (18 `net_*` + 4 against the test server) | 22 | 22 | 22 | 22 | 22 |
| cli | 398 of 398 identical | 398 | 398 | 398 | 398 | 398 |
| web | 363 of 363 pages identical byte for byte | 363 | 363 | 363 | 363 | 363 |
| fuzz | 444 of 444 identical (150 generated, 150 mutated, 106 mutations and 110 case variants; 68 skipped) | 444 | 444 | 444 | 444 | 444 |
| projects | 84 of 84 identical (9 projects, 43 examples) | 84 | 84 | 84 | 84 | 84 |
| site | 100 pages + static + search index + raw pages + site.ot: 218 of 218 files identical | 101 captured | 101 | 101 | 101 | 101 |
| no-powershell | 1,867 runs; 0 implicit launches; audit: 7 PowerShells started by otter, all asked for by the program (5 run its own `.ps1`, 2 run `powershell -Command Start-Sleep` themselves) | same, 7 / 0 | 1,867 runs, 3 launches, all a program's own `.ps1`, 0 implicit | same | same | same |
| native-coverage | 0 places hand anything to PowerShell; 225 of 225 node kinds native; 0 messages missing; 19 process starts, 1 naming PowerShell (`Commands.cs`, a program's `.ps1`) | same | same | same | same | same |
| desktop | 26 of 26 programs identical; without `otter-desktop.exe`: reinstall message, exit 1 | 26 of 26 | - | - | - | - |
| machine-state | 32 of 32 identical; de-DE 11/11; fi-FI 11/11 | 32/32, 11/11, 11/11 | - | - | - | - |
| repository-clean | unchanged | unchanged | unchanged | unchanged | unchanged | unchanged |
| **against Windows** | (the reference) | 0 unexpected, 9 declared | 0 unexpected, 50 declared | 0 / 50 | 0 / 51 | 0 / 51 |

Off win-x64 the counts are programs otter-net ran without a gap or a crash;
their output is what the last row compares with the Windows reference.

### Defects found and fixed

No defect in otter-net itself was found: every difference from otterPS was in
the certification's own tooling, the reference checkout, or the nature of the
platform. Fixed on the way (each a separate commit on `cert/certify`):

| Severity | What | Fix |
|---|---|---|
| minor (tooling) | `tools/native-coverage.ps1` could not run on Windows PowerShell 5.1: a UTF-8 arrow in a script without a byte order mark is read as ANSI there (a parse error) | `0d12a8e`: the arrow as `[char]0x2192`; regression: the native-coverage gate runs it on Windows PowerShell 5.1 |
| minor (tooling) | Two CLI cases whose names differ only in capitals (`run ...`, `RUN ...`) overwrote each other's capture on case-insensitive file systems | `bce4c32`, `7c2013e`: names with capitals get a hash of the name |
| minor (tooling) | The capture listed files in culture order, which differs between PowerShell 5.1 and 7; the site build's page order likewise | `6a72879`, `2f63abc`: ordinal order |
| minor (process) | An LF checkout of otterPS changes otterPS's own output (its pages and new projects end lines with LF): the reference must be checked out as released | `5569629`: both repositories checked out as Windows does, on every platform (SPEC-QUESTIONS R6-1) |

### Remaining issues, by release severity

- **Blocker**: none.
- **Major**: none.
- **Minor**: none open.
- **Known differences** (declared in `tests/cross-platform.psd1`, each with
  its reason; none is a difference from otterPS on the same platform):
  Windows-only features off Windows (registry, credentials, notifications,
  printing, event log, tasks, desktop windows and `otter desktop`, remote
  commands, `cmd.exe`) give otterPS's own messages; the operating system's
  or .NET's own words (a missing program, HttpListener's errors - http.sys
  on Windows, .NET's managed listener elsewhere); Windows refuses a line
  break or tab in a folder name; the path separator; the Debian container's
  missing `~/.config`; machine-specific output (the time, the machine's
  environment variables and temporary folder, its scheduled tasks and
  printers on win-arm64, a case folder's absolute path written into a
  file). PowerShell's own texts that name the otterPS folder wrap at
  different places (120 columns) and are compared without their line
  breaks (2 to 27 per platform).
- **Specification question**: R6-1 (otterPS's output takes its line endings
  from its own files).

### Not certified here

- **Packaging and installers** (MSI, `.pkg`, `.deb`, tarballs, clean-machine
  installs) and **remote commands against a real WinRM endpoint**: certified
  separately (`package.yml`; `cert/winrm`, `winrm.yml`), to be merged into
  this report.
- **Code signing**: not done (RELEASE_CANDIDATE_REPORT.md, section 8).
- **Desktop windows** exist only on Windows; they are certified on win-x64
  and win-arm64.
- Off win-x64 the reference is the Windows capture, not a live otterPS on
  that platform (otterPS's certified host is Windows PowerShell 5.1).
- Fuzzing in certification uses 512 programs per platform; otterPS's own
  certification ran 1,000 generated and 1,000 mutated (`-FuzzCount 1000`
  reproduces that size; larger fuzzing was done earlier, `docs/port/fuzz.md`).
