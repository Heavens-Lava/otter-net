# Porting the `otter` command line

Inventory of every command, flag, mode and project feature of otterPS
1.0.1's `otter.ps1` (and the modules it calls), with where it lives in
otter-net and how far it is ported. Status: **native** (runs in C#, no
PowerShell), **partial** (native for the common forms, the rest falls back
before anything runs), **fallback** (always handed to the PowerShell Otter),
**n/a**.

otterPS sources are relative to the otterPS 1.0.1 checkout; otter-net
sources to `src/Otter.Cli/`. "cli test" names a case file in `tests/cli/`
run by `tools/compare-cli.ps1`.

## Argument binding

| Feature | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| Positional `Path` (0), `Target` (1), rest to `Arguments` | `otter.ps1` param block | `CommandLine.cs` `Bind` | native | - | `args.txt` | - |
| Own switches `-DebugTokens -DebugAst -ParseOnly -DebugErrors -Open -NoOpen`, exact name, any capitals, anywhere | param block | `CommandLine.cs` | native | - | `args.txt` | - |
| Own value flags `-Port N`, `-Breakpoints "5,12"` | param block | `CommandLine.cs` | native | a missing or non-integer `-Port` value (PowerShell's binder error) | `args.txt` | - |
| `-version`, `--version`, `-VersionFlag`; `-help`, `--help`, `-HelpFlag` anywhere on the line | `VersionFlag`/`HelpFlag` aliases (D57) | `CommandLine.cs` | native | - | `version.txt` | - |
| Program arguments recovered from the raw command line, own flags removed (D94) | `Get-OtterRawTrailingArguments` | `CommandLine.RawTrailing` | native, quirks included (the skip count counts flags written before the command) | - | `args.txt` | - |
| Any other token starting with `-` (`-x`, `--name`, `-5`, `-Verbose`, `-Debug`, `-OutVariable`, prefixes such as `-v`, `-Port:5`, `-Path x`) | PowerShell's parameter binder: common parameters, prefix matching, ambiguity errors | `CommandLine.cs` | fallback | yes, the whole command | `args.txt` (`-x`) | needs PowerShell's binder rules verified on 5.1 before porting |

## Commands

| Command / form | otterPS | otter-net | Status | Falls back | Tests | Remaining |
|---|---|---|---|---|---|---|
| `otter --version` | `otter.ps1` MAIN, `$OtterVersion` from `VERSION` | `Cli.Run` | native | - | `version.txt` | - |
| `otter version`, `otter -v` | not commands: "I do not recognize the command" / binder ambiguity | `Cli.Run` | native (`version`); `-v` falls back | `-v` | `version.txt` | (otter-net printed the version for both before: fixed) |
| `otter help`, `--help`, `-help` | `Show-OtterHelp` | `Cli.ShowHelp` | native | - | `version.txt` | - |
| `otter` (no arguments) | `Start-OtterRepl` | `Repl.cs` | native | - | `repl.txt` | - |
| `otter <file.ot> [args]` | MAIN, `Invoke-OtterFile` | `FileCommand.cs` | native | programs using unported features | `run.txt` | - |
| `otter <project folder or otter.json/project.json> [args]` (D118B) | MAIN, `Get-OtterProject` | `FileCommand.cs`, `Project.cs` | native | - | `project.txt` | - |
| `otter <anything else>` | "I do not recognize the command" / "cannot find an otter.json manifest" | `Cli.Run` | native | - | `usage.txt` | - |
| `otter run <file.ot> [args]` | `Invoke-OtterFile` | `FileCommand.cs` | native | unported features | `run.txt` | - |
| `otter run <project>` / `otter run .` | `Find-OtterProjectManifest`, `Get-OtterProject` | `FileCommand.cs` | native | - | `project.txt` | - |
| `otter run` (no target) | usage error | `Cli.Run` | native | - | `usage.txt` | - |
| `otter check <file.ot>` | `Invoke-OtterFile -CheckOnly` | `FileCommand.cs` | native | unported features | `check.txt` | - |
| `otter check <project>` (DisplayTarget in "is valid.") | `Invoke-OtterFile -DisplayTarget` | `FileCommand.cs` | native | - | `project.txt` | - |
| `otter check a.ot b.ot` (several files) | not supported: `b.ot` is a program argument, only `a.ot` is checked | same | native | - | `check.txt` | - |
| `-ParseOnly` ("ok: <path>") | `Invoke-OtterFile` | `FileCommand.cs` | native | - | `run.txt` | - |
| `-DebugTokens` (token dump) | `Invoke-OtterSource` | `FileCommand.cs` | native | - | `run.txt` | - |
| `-DebugAst` (AST dump) | `Show-OtterAst` (walks PowerShell node objects) | - | fallback | yes | - | needs otterPS's 226 node classes' property layout |
| `-DebugErrors` (stack trace of an internal error) | `Show-OtterFailure` | `FileCommand.cs` | partial: shows .NET's stack trace where otterPS shows PowerShell's | - | - | the trace text cannot match |
| Compiled engine (`OTTER_ENGINE=compiled/interpreter`, `OTTER_COMPILED_CACHE`, `OTTER_ENGINE_TRACE`) | `Invoke-OtterCompiledProgram`, `Otter.Compiler.Native.psm1` | n/a | n/a | - | - | otterPS compiles to C# at run time for speed only; output is the interpreter's, which otter-net reproduces. Not portable under Native AOT (no run-time code generation); see ARCHITECTURE.md section 6 |
| File errors: missing, folder, unreadable, not `.ot` (RC3 B12) | `Assert-OtterSourceFileReadable`, `Invoke-OtterFile` | `FileCommand.cs` | native | - | `files.txt` | - |
| `use` errors: missing, wrong capitals, circular, folder, unreadable, absolute paths (D60, D122, M1) | `Otter.Module.psm1` | `Modules.cs` | native | - | `modules.txt` | - |
| `otter profile <file.ot>` | `Otter.Profiler.psm1` | `Profiler.cs` | native (timings differ by nature) | unported features | `profile.txt` (structure only) | - |
| `otter debug <file.ot> -Breakpoints "5,12"` | `Otter.Debugger.psm1` | `Debugger.cs` | native | unported features | `debug.txt` | locals order (otterPS: hash order) |
| `otter new <archetype> <name>` | `New-OtterProject` | `Project.cs` | native | - | `new.txt` | - |
| `otter test [target]` | `Get-OtterProjectTestFiles`, `Invoke-OtterProjectTests` | `ProjectTests.cs` | native (each test runs in a child `otter run`, as otterPS) | per test file | `test.txt` | - |
| `otter build [target]` console/automation | `Invoke-OtterProjectBuild` | `ProjectBuild.cs` | native | - | `build.txt` | - |
| `otter build` web/desktop/game | `Export-OtterWebApplication` | - | fallback (UI compiler) | yes | - | UI agent's web compiler |
| `otter publish [target] [--output dir]` console/automation | `Invoke-OtterProjectPublish` | `ProjectBuild.cs` | partial: native without `--output`/`-Output` (binder-dependent forms fall back) | `--output`, web targets | `publish.txt` | zip bytes depend on the deflate library: the printed SHA-256 differs from otterPS's |
| `otter web`, `browse`, `serve`, `desktop` | `otter.ps1` + `Otter.Web/Server/Desktop.psm1` | `Cli.Run` | partial: usage line, project and file errors native; the command itself falls back | yes | `usage.txt` | UI / network agents |
| `otter studio` | `Start-OtterStudio` | - | fallback | yes | - | not part of the 1.0 release |

## Project files

| Feature | otterPS | otter-net | Status | Notes |
|---|---|---|---|---|
| `otter.json`, `project.json` fallback, manifest named directly | `Find-OtterProjectManifest` | `Project.Find` | native | |
| Manifest validation (types, required fields, targets, B4/B5) | `Get-OtterProject`, `Assert-OtterManifestFieldType` | `Project.Load` | native | invalid JSON falls back (its message is JavaScriptSerializer's) |
| Symbolic links / junctions containment (B4) | `Get-OtterRealPath`, `Resolve-OtterProjectInputPath` | `Project.RealPath` | native | |
| Output folder safety (marker files, .git, entry point) | `Assert-OtterReplaceableOutputDir` | `ProjectBuild.cs` | native | |
| Safe file name, version check (publish) | `Get-OtterSafeFileName` | `ProjectBuild.cs` | native | |

## Output conventions

- Everything otterPS writes with `Write-Host` goes to standard output, in
  colour only when standard output is a console (`Host.cs`).
- Files written with `Set-Content -Encoding UTF8` are written as Windows
  PowerShell 5.1 writes them (UTF-8 with a byte order mark, `\r\n` after the
  last line); PowerShell 7 writes no byte order mark and `\n` on macOS and
  Linux. JSON files use 5.1's `ConvertTo-Json` layout (`OtterJson`).
