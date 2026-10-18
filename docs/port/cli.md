# Porting the `otter` command line

Inventory of every command, flag, mode and project feature of otterPS
1.0.1's `otter.ps1` (and the modules it calls), with where it lives in
otter-net and how far it is ported. Status: **native** (runs in C#, no
PowerShell), **partial** (native for the common forms, the rest is handed
to the PowerShell Otter before anything runs), **fallback** (always handed
over), **n/a**.

otterPS sources are relative to the otterPS 1.0.1 checkout; otter-net
sources to `src/Otter.Cli/`. Tests are case files in `tests/cli/`, run by
`tools/compare-cli.ps1` (the CLI) and `tools/compare.ps1` (programs).

## Argument binding

| Feature | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| Positional `Path` (0), `Target` (1), the rest to `Arguments` | `otter.ps1` param block | `CommandLine.Bind` | native | - | `run.psd1` | - |
| Own switches `-DebugTokens -DebugAst -ParseOnly -DebugErrors -Open -NoOpen`, exact name, any capitals, anywhere | param block | `CommandLine.Bind` | native | - | `run.psd1` | - |
| Own value flags `-Port N`, `-Breakpoints "5,12"`: the next argument (or `-Name:value`, `-Name: value`), `[int]` converted as PowerShell does (hexadecimal, through Double, rounding half to even) | param block | `CommandLine.Bind`, `PowerShellConvert` | native, binder errors included | - | `run.psd1`, `binding.psd1` | - |
| `-version`, `--version`, `-VersionFlag`; `-help`, `--help`, `-HelpFlag`, anywhere on the line | `VersionFlag`/`HelpFlag` aliases (D57) | `CommandLine.Bind` | native | - | `basics.psd1` | - |
| Program arguments from the raw command line without otter.ps1's flags (D94) | `Get-OtterRawTrailingArguments` | `CommandLine.RawTrailing` | native, quirk included: the skip count also counts flags written before the command (`otter -DebugErrors run a.ot k` gives the program `a.ot, k`) | - | `run.psd1` | - |
| Every other token starting with `-` (`-x`, `--name`, `-5`, `--`, prefixes such as `-v` or `-Deb`, `-Port:5`, `-Path x`) | powershell.exe -File's argument parsing and PowerShell's binder: unique prefixes of names and aliases, otter.ps1's own parameters preferred over common ones, ambiguity, a known parameter taking the next argument, unknown names (into `$Arguments`) taking the next plain argument with them, `ValueFromRemainingArguments` | `CommandLine.Bind` | native; binding failures are PowerShell 5.1's error records on the error stream, exit 1 (`PowerShellError`) | - | `binding.psd1` | the error text names otter.ps1's path and assumes a 120-column console (C2-1); a lone `-` (C2-3) |
| Common parameters `-Debug -ErrorAction -WarningAction -ErrorVariable -WarningVariable -InformationVariable -OutVariable -OutBuffer -PipelineVariable` and aliases | advanced script | `CommandLine.Bind` | native: bound, converted and validated as 5.1 does, nothing visible changes | - | `binding.psd1` | - |
| `-Verbose` | Import-Module's VERBOSE messages (module and function lists, install path) | `Cli.VerboseModules` | native where otterPS imports nothing (check, new, test, REPL, ...) | `profile`, `debug`, `build`, `publish`, a `run` that runs a program | - | C2-5 |
| `-InformationAction Stop / Inquire / Ignore` | changes every Write-Host | `Cli.Run` | native for `Continue` and `SilentlyContinue` | the other values | - | C2-5 |
| `-?` | Get-Help: otter.ps1's syntax line | `CommandLine.BindArguments` | native | - | `binding.psd1` | 5.1's wrapping unverified (C2-6) |

## Commands

| Command / form | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| `otter --version` | MAIN, `$OtterVersion` from `VERSION` | `Cli.Run` | native | - | `basics.psd1` | - |
| `otter version` | not a command: "I do not recognize the command" | `Cli.RunPath` | native (otter-net printed the version before: fixed) | - | `basics.psd1` | - |
| `otter -v` | the binder prefers `-VersionFlag` over `-Verbose`: the version | `CommandLine.Bind` | native | - | `binding.psd1` | Q3 is mistaken (C2-2) |
| `otter help`, `--help`, `-help` (any capitals) | `Show-OtterHelp` | `Cli.ShowHelp` | native | - | `basics.psd1` | - |
| `otter` (no arguments): the REPL | `Start-OtterRepl` | `Repl.cs` | native, `-DebugAst` included; a line using an unported feature is refused on its own and the session goes on (a session cannot be handed over halfway); redirected input is echoed as read (`ConsoleInput.ReadLine`) | - | `interactive.psd1` | - |
| `otter <file.ot> [args]` | MAIN, `Invoke-OtterFile` | `FileMode.Invoke` | native | programs using unported features | `run.psd1`, `tools/compare.ps1` | - |
| `otter <project folder, otter.json or project.json> [args]` (D118B) | MAIN, `Get-OtterProject` | `Cli.RunPath`, `Project.cs` | native | - | `projects.psd1` | - |
| `otter <anything else>` | "I do not recognize the command" / "cannot find an otter.json manifest" | `Cli.RunPath` | native | - | `basics.psd1` | - |
| `otter run <file.ot or project> [args]` | `Invoke-OtterFile` | `Cli.RunOrCheck` | native | unported features | `run.psd1`, `projects.psd1` | - |
| `otter check <file.ot or project>` ("is valid", the project's name as typed) | `Invoke-OtterFile -CheckOnly -DisplayTarget` | `Cli.RunOrCheck` | native | unported features | `run.psd1`, `projects.psd1` | - |
| `otter check a.ot b.ot` | only `a.ot` is checked: `b.ot` is a program argument | same | native | - | `run.psd1` | spec question Q6 |
| Usage errors (`otter run`, `check`, `profile`, `debug`, `new`, `web`, ... without a target) | MAIN | `Cli.cs`, `ProjectCommands.cs` | native | - | `basics.psd1` | - |
| `-ParseOnly` ("ok: <path>") | `Invoke-OtterFile` | `FileMode.Invoke` | native | - | `run.psd1` | - |
| `-DebugTokens` (token list before the run, also in the REPL) | `Invoke-OtterSource` | `FileMode.Compiled.WriteTokens` | native | - | `run.psd1`, `interactive.psd1` | - |
| `-DebugAst` | `Show-OtterAst` (walks otterPS's PowerShell node objects) | `AstDump.cs`, table generated from `Otter.Contract.psm1` by `tools/New-AstDumpTable.ps1` | native for every node class otter-net parses | a node class the table does not know (none today) | `run.psd1`, `interactive.psd1` | - |
| `-DebugErrors` | `Show-OtterFailure` | `FileMode.ShowFailure` | partial: an internal error shows .NET's stack trace where otterPS shows PowerShell's script stack | - | `run.psd1` | the trace text cannot match |
| Compiled engine (`OTTER_ENGINE=compiled/interpreter`, `OTTER_COMPILED_CACHE`, `OTTER_ENGINE_TRACE`) | `Invoke-OtterCompiledProgram`, `Otter.Compiler.Native.psm1` | n/a | n/a | - | - | otterPS compiles programs to C# at run time (Add-Type) purely for speed; its output is the interpreter's, which otter-net reproduces. Not portable under Native AOT (no run-time code generation) and not needed: otter-net's interpreter is already faster. `OTTER_ENGINE` means something else to otter-net (native/powershell). |
| File errors: missing, a folder, unreadable, not `.ot` (RC3 B12); `~` and `~/...` are the home folder (Resolve-Path) | `Assert-OtterSourceFileReadable`, `Invoke-OtterFile` | `FileMode.AssertReadable`, `Project.ExpandHome` | native | - | `basics.psd1`, `projects.psd1` | - |
| `use` errors: missing, wrong capitals (file or folder), circular, a folder, unreadable; absolute paths (D60, D122, M1) | `Otter.Module.psm1` | `Modules.cs` | native (all fell back before) | - | `modules.psd1` | - |
| `otter profile <file.ot> [args]` | `Otter.Profiler.psm1` | `Debugging.cs` `Profiler` | native; timings and memory differ by nature, the layout is otterPS's | unported features | `interactive.psd1` (numbers masked) | - |
| `otter debug <file.ot> -Breakpoints "5,12" [args]` (`@@OTTER_DEBUG@@` protocol) | `Otter.Debugger.psm1` | `Debugging.cs` `Debugger` | native; a breakpoint is `[int]` converted (1.5 is 2), one that is not a number is PowerShell's ForEach-Object error, exit 1 | - | `interactive.psd1`, `binding.psd1` | locals are listed in the order they were set; otterPS lists them in hash-table order (spec question Q4) |
| `otter new <archetype> <name>` | `New-OtterProject` | `ProjectCommands.New` | native, files as Windows PowerShell 5.1 writes them; a name with a control character too (on Windows, .NET Framework's "Illegal characters in path.") | - | `new.psd1` | spec question Q2 (line endings) |
| `otter test [target]` | `Get-OtterProjectTestFiles`, `Invoke-OtterProjectTests` | `ProjectCommands.Test` | native: each test runs in a child `otter run` of the same otter-net, as otterPS runs each in a child PowerShell | per test file (in the child) | `test.psd1` | file order: Sort-Object's culture order, approximated without culture data |
| `otter build [target]`, console and automation projects | `Invoke-OtterProjectBuild` | `ProjectBuild.cs` | native | - | `build.psd1` | - |
| `otter build`, web / desktop / game projects | `Export-OtterWebApplication -PassThruExceptions` | `ProjectBuild.cs`, `WebCommand.CompilePage` | native: `index.html`, `run-desktop.cmd` for desktop, the sidecar stylesheet check; the page is compiled before anything is printed | a page the web compiler here cannot make | `ui.psd1` | - |
| `otter publish [target]`, every project type | `Invoke-OtterProjectPublish` | `ProjectBuild.cs` | native (web targets: `index.html` entry point, otterPS's runtime requirements) | `--output` / `-Output` (dash arguments, see binding) | `build.psd1` | the zip's bytes depend on the deflate implementation, so the SHA-256 differs from otterPS's (spec question Q5) |
| `otter web`, `browse`, `serve` (files and projects) | `otter.ps1`, `Otter.Web.psm1`, `Otter.Server.psm1` | `Cli.UiCommand`, `WebCommand.cs`, `ServeCommand.cs` | native; the usage line exits 0 (otter.ps1's plain `exit 1`, SPEC-QUESTIONS UI2-1) | statements the web compiler cannot compile yet; a server that cannot start | `basics.psd1`, `ui.psd1` | - |
| `otter desktop` | `Start-OtterDesktopApplication` | `Cli.UiCommand`, `DesktopCommand.cs` | native (Windows: page, bridge, app window; elsewhere the "only on Windows" message) | an import problem, a page the web compiler cannot make | `basics.psd1` | see `docs/port/ui.md` |
| `otter studio` | `Start-OtterStudio` | - | fallback | yes | - | not part of the 1.0 release |

## Project files

| Feature | otterPS | otter-net | Status | Notes |
|---|---|---|---|---|
| `otter.json`, `project.json` fallback (`main`), a manifest named directly | `Find-OtterProjectManifest` | `Project.Find` | native | |
| Manifest validation: types, required fields, targets, output folders (B4/B5) | `Get-OtterProject`, `Assert-OtterManifestFieldType` | `Project.Load` | native | property names ignore capitals, as PSObject properties do. A manifest that cannot be read quotes .NET Framework's message inside PowerShell's MethodInvocationException; one that is not valid JSON quotes Windows PowerShell 5.1's ConvertFrom-Json (JavaScriptSerializer, `JsonSyntaxError.cs`; C2-6) |
| Symbolic links and junctions (B4) | `Get-OtterRealPath`, `Resolve-OtterProjectInputPath` | `Project.RealPath`, `Project.ResolveInputPath` | native | |
| Output folder safety: marker files, `.git`, the entry point | `Assert-OtterReplaceableOutputDir` | `ProjectBuild.cs` | native | |
| Safe artifact name, plain version (publish) | `Get-OtterSafeFileName` | `ProjectBuild.cs` | native | |
| Deterministic zip (sorted entries, fixed date, `run` mode 755) | `New-OtterDeterministicZip` | `ProjectBuild.cs` | native | |
| `Expand-OtterDeterministicArchive` | `Otter.Project.psm1` | - | n/a | exported but not used by any command |

## Output conventions

- Everything otterPS writes with `Write-Host` goes to standard output, in
  colour only when standard output is a console (`Host.cs`).
- Files written with `Set-Content -Encoding UTF8` are written as Windows
  PowerShell 5.1 writes them: UTF-8 with a byte order mark and `\r\n` after
  the last line; here-strings with `\r\n` line breaks (a Windows checkout).
  PowerShell 7 writes no byte order mark, and `\n` on macOS and Linux.
  JSON files have 5.1's `ConvertTo-Json` layout (`OtterJson.Write`). On a
  PowerShell 7 reference these are the `KnownOnPwsh7` cases of
  `tests/cli`.
- `otter-net native: exit N` (with `OTTER_NET_TRACE=1`) ends every command
  otter-net ran itself.

## Tests and results

`tools/compare-cli.ps1` runs the 148 cases of `tests/cli/*.psd1` through
both implementations. It could not be run against otterPS while porting
(PowerShell was not available to the porting session); every case was run
through otter-net and its output checked by hand against otterPS 1.0.1's
code. `tools/compare.ps1` on `tests/compat` plus otterPS's conformance
fixtures and examples gives byte-identical results to otter-net before
this work (149 programs, 0 differences).
