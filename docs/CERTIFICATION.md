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
