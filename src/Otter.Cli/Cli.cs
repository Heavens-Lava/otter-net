using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// otter.ps1's MAIN section: every command, in otter.ps1's order, with its
// messages and exit codes (D57). Commands compare their name the way
// PowerShell's -eq does, ignoring capitals.
internal static class Cli
{
    // otterPS's exit codes (D57).
    public const int ExitSuccess = 0, ExitUsageError = 1, ExitCheckError = 2, ExitRuntimeError = 3;

    public static string VersionText => $"Otter {Program.Version}";

    static bool Is(string? text, string word) => string.Equals(text, word, StringComparison.OrdinalIgnoreCase);

    public static int Run(CommandLine line)
    {
        if (line.Version || Is(line.Path, "--version"))
        {
            Program.Trace("native: --version");
            Host.Line(VersionText);
            return ExitSuccess;
        }
        if (line.Help || Is(line.Path, "help") || Is(line.Path, "--help"))
        {
            Program.Trace("native: help");
            ShowHelp();
            return ExitSuccess;
        }
        string? command = line.Path;
        if (Is(command, "new")) return ProjectCommands.New(line);
        if (Is(command, "test")) return ProjectCommands.Test(line);
        if (Is(command, "build")) return ProjectCommands.Build(line);
        if (Is(command, "publish")) return ProjectCommands.Publish(line);
        if (Is(command, "run") || Is(command, "check")) return RunOrCheck(line, Is(command, "check"));
        if (Is(command, "profile")) return Profile(line);
        if (Is(command, "debug")) return Debug(line);
        if (Is(command, "web") || Is(command, "browse") || Is(command, "serve") || Is(command, "desktop") || Is(command, "studio"))
        {
            return UiCommand(line);
        }
        if (line.HasPath) return RunPath(line);
        return Repl.Start(line);
    }

    // Show-OtterHelp. `otter studio` is deliberately not listed (RC3 B14).
    static void ShowHelp()
    {
        Host.Line(VersionText);
        Host.Line("");
        Host.Line("Usage:");
        Host.Line("  otter <file.ot>        Run an Otter program (shortest form)");
        Host.Line("  otter run <file.ot>    Run an Otter program or project");
        Host.Line("  otter check <file.ot>  Validate a program or project without running it");
        Host.Line("  otter build [target]   Build an Otter project into its output directory (dist/)");
        Host.Line("  otter publish [target] Publish an Otter project into a distributable archive (publish/)");
        Host.Line("  otter new <type> <name> Create a new Otter project (console, desktop, web, automation, game)");
        Host.Line("  otter test [target]    Run tests in an Otter project or test file");
        Host.Line("  otter profile <file.ot> Run a program and report which functions and lines took the time");
        Host.Line("  otter web <file.ot>    Compile an Otter web application to HTML/JS");
        Host.Line("  otter serve <file.ot> [-Port N] Run an Otter web server program");
        Host.Line("  otter desktop <file.ot> Run an Otter Desktop app with system bridge");
        Host.Line("  otter help             Show this help");
        Host.Line("  otter --help           Show this help");
        Host.Line("  otter --version        Show the Otter version");
        Host.Line("  otter                  Start the interactive REPL");
        Host.Line("");
    }

    static CliExit UsageError(string message)
    {
        Host.Line(message, ConsoleColor.Red);
        return new CliExit(ExitUsageError);
    }

    // `otter run <target>` and `otter check <target>`: a .ot file, or a
    // project folder or manifest (D118B).
    static int RunOrCheck(CommandLine line, bool check)
    {
        if (!line.HasTarget) throw UsageError($"Usage: otter {line.Path} <file.ot>");
        string target = line.Target!;
        string[] arguments = line.RawTrailing(2);
        if (!check && line.DebugAst && !line.ParseOnly) throw new NeedsPowerShell("-DebugAst shows otterPS's own syntax tree");

        if (!target.ToLowerInvariant().EndsWith(".ot", StringComparison.Ordinal))
        {
            ProjectManifest? manifest = Project.Find(target);
            if (manifest != null)
            {
                Project project = LoadProjectOrExit(target, ExitCheckError);
                return FileMode.Invoke(line, project.ResolvedEntryPoint, check ? FileMode.Kind.Check : FileMode.Kind.Run, arguments, displayTarget: target);
            }
            string? resolvedDirectory = Project.PsResolvePath(target);
            if ((resolvedDirectory != null && Directory.Exists(resolvedDirectory)) || target == ".")
            {
                throw UsageError($"Otter: I cannot find an otter.json manifest in \"{target}\".");
            }
            throw UsageError($"Otter: \"{target}\" is not an Otter file - expected a .ot file.");
        }
        return FileMode.Invoke(line, target, check ? FileMode.Kind.Check : FileMode.Kind.Run, arguments);
    }

    // Get-OtterProject's OtterError, shown as otter.ps1 shows it: the
    // message alone between blank lines.
    public static Project LoadProjectOrExit(string target, int exitCode)
    {
        try { return Project.Load(target); }
        catch (OtterError error)
        {
            Host.Line("");
            Host.Line(error.Message, ConsoleColor.Red);
            Host.Line("");
            throw new CliExit(exitCode);
        }
    }

    // `otter <path>`: a project, a .ot file, or a mistake.
    static int RunPath(CommandLine line)
    {
        string path = line.Path!;
        if (Project.Find(path) != null)
        {
            if (line.DebugAst && !line.ParseOnly) throw new NeedsPowerShell("-DebugAst shows otterPS's own syntax tree");
            string[] projectArguments = line.RawTrailing(1);
            Project project = LoadProjectOrExit(path, ExitCheckError);
            return FileMode.Invoke(line, project.ResolvedEntryPoint, FileMode.Kind.Run, projectArguments, displayTarget: path);
        }
        if (!path.ToLowerInvariant().EndsWith(".ot", StringComparison.Ordinal))
        {
            string? resolvedDirectory = Project.PsResolvePath(path);
            if ((resolvedDirectory != null && Directory.Exists(resolvedDirectory)) || path == ".")
            {
                throw UsageError($"Otter: I cannot find an otter.json manifest in \"{path}\".");
            }
            Host.Line($"Otter: I do not recognize the command \"{path}\".", ConsoleColor.Red);
            Host.Line("Run 'otter help' to see the available commands.", ConsoleColor.Red);
            throw new CliExit(ExitUsageError);
        }
        if (line.DebugAst && !line.ParseOnly) throw new NeedsPowerShell("-DebugAst shows otterPS's own syntax tree");
        return FileMode.Invoke(line, path, FileMode.Kind.Run, line.RawTrailing(1));
    }

    static int Profile(CommandLine line)
    {
        if (!line.HasTarget) throw UsageError("Usage: otter profile <file.ot>");
        if (line.DebugAst && !line.ParseOnly) throw new NeedsPowerShell("-DebugAst shows otterPS's own syntax tree");
        var profiler = new Profiler();
        return FileMode.Invoke(line, line.Target!, FileMode.Kind.Run, line.RawTrailing(2), profiler: profiler);
    }

    static int Debug(CommandLine line)
    {
        if (!line.HasTarget) throw UsageError("Usage: otter debug <file.ot> -Breakpoints \"5,12\"");
        if (line.DebugAst && !line.ParseOnly) throw new NeedsPowerShell("-DebugAst shows otterPS's own syntax tree");
        var breakpoints = new List<int>();
        if (!string.IsNullOrEmpty(line.Breakpoints))
        {
            // [int]($_.Trim()) for each comma-separated part; "" is 0.
            foreach (string part in line.Breakpoints.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0) { breakpoints.Add(0); continue; }
                if (!int.TryParse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int number))
                {
                    throw new NeedsPowerShell("a breakpoint that is not a whole number is PowerShell's own conversion error");
                }
                breakpoints.Add(number);
            }
        }
        string target = line.Target!;
        var debugger = new Debugger(Path.GetFileName(target.Replace('\\', '/').TrimEnd('/')), breakpoints);
        return FileMode.Invoke(line, target, FileMode.Kind.Run, line.RawTrailing(2), debugger: debugger);
    }

    // web, browse, serve, desktop, studio: the checks otter.ps1 makes before
    // the command itself are native; the command (a compiler, a server, a
    // window) is handed to the PowerShell Otter.
    static int UiCommand(CommandLine line)
    {
        string command = line.Path!;
        if (Is(command, "studio")) throw new NeedsPowerShell("otter studio is not native");
        if (!line.HasTarget)
        {
            // otter.ps1 ends this with a plain `exit 1`, and a plain `exit`
            // does not set the exit code of a script run with -File (otterPS's
            // own comment on `otter studio`): the process exits with 0
            // (docs/SPEC-QUESTIONS.md UI2-1).
            Host.Line($"Usage: otter {command} <script.ot>");
            throw new CliExit(ExitSuccess);
        }
        string scriptFile = line.Target!;
        bool project = Project.Find(scriptFile) != null;
        if (project)
        {
            scriptFile = LoadProjectOrExit(scriptFile, ExitCheckError).ResolvedEntryPoint;
        }
        else if (Directory.Exists(scriptFile))
        {
            throw UsageError($"Otter: I cannot find an otter.json manifest in \"{scriptFile}\".");
        }
        scriptFile = FileMode.AssertReadable(scriptFile);
        if (Is(command, "desktop") && !OperatingSystem.IsWindows() && !DesktopCommand.TestMode)
        {
            Program.Trace("native: desktop off Windows");
            throw UsageError("Otter: desktop windows are only available on Windows. Build this program as a web application instead: otter web <file.ot>");
        }
        // otter web / browse: the web compiler (WebCommand.cs), for a file or
        // a project's entry point, whatever its extension.
        if (Is(command, "web") || Is(command, "browse"))
        {
            return WebCommand.Run(scriptFile, line.Target!, line.NoOpen, Program.Trace, out string webReason) ?? throw new NeedsPowerShell(webReason);
        }
        if (Is(command, "serve")) return ServeCommand.Run(line, scriptFile);
        if (Is(command, "desktop")) return DesktopCommand.Run(scriptFile);
        throw new NeedsPowerShell($"otter {command.ToLowerInvariant()} is not native yet");
    }
}
