using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Otter.Runtime;

// run command "git status" [into result] (rules.md section 31, D65, D119-R1):
// a port of otterPS's Split-OtterCommandLine, New-OtterProcessStartInfo and
// Invoke-OtterCommand. There is never a shell in between: the text is split
// into a program and its arguments, so &&, |, > and %VAR% are just text.
//
// The program is found the way Get-Command finds it in otterPS: a runnable
// file named directly, then PowerShell's own aliases, functions and cmdlets
// (which come before any program - `sort` is an alias, and is then started by
// that name), then PATH. A .ps1 script runs in PowerShell, a .cmd or .bat
// script in cmd.exe with every argument checked so it cannot start a second
// command.
public sealed class CommandProblem(string message) : Exception(message);

public static class OtterCommands
{
    public static OtterThing Run(string commandLine)
    {
        var (start, program) = StartInfoFor(commandLine);
        string output, errorOutput;
        double exitCode;
        try
        {
            using Process process = Process.Start(start) ?? throw new CommandProblem($"\"{program}\" could not run. The program process did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            Task.WaitAll(stdout, stderr);
            // Keep interior lines; drop the line ending programs put last.
            output = stdout.Result.TrimEnd();
            errorOutput = stderr.Result.TrimEnd();
            exitCode = process.ExitCode;
        }
        catch (Win32Exception e)
        {
            throw new CommandProblem($"\"{program}\" could not run. Exception calling \"Start\" with \"1\" argument(s): \"{FrameworkMessage(e)}\"");
        }
        var result = new OtterThing("command result");
        result.WriteProperty("output", output);
        result.WriteProperty("error output", errorOutput);
        result.WriteProperty("exit code", exitCode);
        return result;
    }

    // .NET Framework's Win32Exception text, which PowerShell 5.1 shows: no
    // final period.
    static string FrameworkMessage(Win32Exception e) => e.NativeErrorCode switch
    {
        2 => "The system cannot find the file specified",
        3 => "The system cannot find the path specified",
        5 => "Access is denied",
        193 => "%1 is not a valid Win32 application",
        _ => new Win32Exception(e.NativeErrorCode).Message.TrimEnd('.'),
    };

    // ---- splitting ----------------------------------------------------------

    // "git commit -m ""hello world""" -> git, commit, -m, hello world
    public static List<string> Split(string commandLine)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, any = false;
        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];
            if (c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
            {
                current.Append('"');
                any = true;
                i++;
                continue;
            }
            if (c == '"')
            {
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');
                    any = true;
                    i++;
                    continue;
                }
                inQuotes = !inQuotes;
                any = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (any)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (inQuotes) throw new CommandProblem("This command has a quote that never closes.");
        if (any) parts.Add(current.ToString());
        if (parts.Count == 0) throw new CommandProblem("I need the name of a program to run.");
        return parts;
    }

    // ---- finding the program --------------------------------------------------

    static (ProcessStartInfo Start, string Program) StartInfoFor(string commandLine)
    {
        List<string> parts = Split(commandLine);
        string program = parts[0];
        List<string> arguments = parts.Skip(1).ToList();
        string resolvedPath = Resolve(program) ?? throw new CommandProblem($"I could not find a program called \"{program}\".");
        string extension = Path.GetExtension(resolvedPath).ToLowerInvariant();

        var start = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (extension == ".ps1")
        {
            // The PowerShell that runs otterPS: Windows PowerShell, or pwsh elsewhere.
            start.FileName = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
                : "pwsh";
            start.Arguments = string.Join(" ", new[] { "-NoProfile", "-File", resolvedPath }.Concat(arguments).Select(ProcessArgument));
        }
        else if (extension is ".cmd" or ".bat" && OperatingSystem.IsWindows())
        {
            string cmdExe = Environment.GetEnvironmentVariable("ComSpec") is { } comSpec && !string.IsNullOrWhiteSpace(comSpec) ? comSpec : "cmd.exe";
            string target = CmdArgument(resolvedPath);
            string formatted = string.Join(" ", arguments.Select(CmdArgument));
            string inner = formatted.Length > 0 ? $"{target} {formatted}" : target;
            start.FileName = cmdExe;
            start.Arguments = $"/d /v:off /s /c \"{inner}\"";
        }
        else
        {
            start.FileName = resolvedPath;
            start.Arguments = string.Join(" ", arguments.Select(ProcessArgument));
        }
        return (start, program);
    }

    static string? Resolve(string program)
    {
        // A file named directly is the program when Windows can run it.
        if (File.Exists(program) && (!OperatingSystem.IsWindows() || RunnableExtensions().Contains(Path.GetExtension(program).ToLowerInvariant())))
        {
            return Path.GetFullPath(program);
        }
        // Get-Command: aliases, functions and cmdlets first. An alias or function
        // is then started by its own name; a cmdlet by the name of its module.
        if (PowerShellCommands.BuiltIn.TryGetValue(program, out var command))
        {
            return command.Source.Length > 0 ? command.Source : program;
        }
        if (FindOnPath(program) is { } onPath) return onPath;
        foreach (string extension in new[] { ".exe", ".cmd", ".bat", ".ps1" })
        {
            if (File.Exists(program + extension)) return Path.GetFullPath(program + extension);
        }
        return null;
    }

    static List<string> PathExtensions()
    {
        string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? "";
        var extensions = pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.ToLowerInvariant()).ToList();
        return extensions.Count > 0 ? extensions : new List<string> { ".com", ".exe", ".bat", ".cmd" };
    }

    static HashSet<string> RunnableExtensions()
    {
        var runnable = new HashSet<string>(StringComparer.Ordinal) { ".ps1" };
        foreach (string extension in PathExtensions()) runnable.Add(extension);
        return runnable;
    }

    // A program on PATH, the PATHEXT extensions tried in order, then a script.
    static string? FindOnPath(string program)
    {
        if (program.IndexOfAny(new[] { '\\', '/' }) >= 0) return null;
        string[] folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        bool windows = OperatingSystem.IsWindows();
        var extensions = windows ? PathExtensions().Append(".ps1").ToList() : new List<string>();
        bool hasRunnableExtension = windows && RunnableExtensions().Contains(Path.GetExtension(program).ToLowerInvariant());
        foreach (string folder in folders)
        {
            try
            {
                if (!windows || hasRunnableExtension)
                {
                    string direct = Path.Combine(folder, program);
                    if (File.Exists(direct)) return direct;
                    continue;
                }
                foreach (string extension in extensions)
                {
                    string candidate = Path.Combine(folder, program + extension);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    // ---- quoting ----------------------------------------------------------

    // ConvertTo-OtterProcessArgument: the Windows command-line rules native
    // programs read their arguments with.
    static string ProcessArgument(string argument)
    {
        if (argument.Length == 0) return "\"\"";
        if (!Regex.IsMatch(argument, "[\\s\"]")) return argument;
        string escaped = Regex.Replace(argument, "(\\\\*)\"", "$1$1\\\"");
        escaped = Regex.Replace(escaped, "(\\\\+)$", "$1$1");
        return "\"" + escaped + "\"";
    }

    // ConvertTo-OtterCmdArgument: cmd.exe reads the line itself, so what it
    // cannot take literally is refused, and anything it would act on is quoted.
    static string CmdArgument(string argument)
    {
        if (Regex.IsMatch(argument, "[\"%\r\n\0]"))
        {
            string shown = Regex.Replace(argument, "[\r\n\0]", " ");
            throw new CommandProblem($"I cannot pass \"{shown}\" safely to a .cmd or .bat script. cmd.exe would treat a double quote, a percent sign or a line break in it as part of the command, so arguments to a .cmd or .bat script cannot contain those characters.");
        }
        if (argument.Length == 0) return "\"\"";
        if (!Regex.IsMatch(argument, "[\\s&|<>^(),;=!]")) return argument;
        return "\"" + argument + "\"";
    }
}
