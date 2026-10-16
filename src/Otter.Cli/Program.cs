using System.Diagnostics;
using System.Text;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// The `otter` command. It runs a program itself when otter-net supports
// everything the program uses, and otherwise hands the whole command, before
// anything has run, to the PowerShell Otter - so the user always gets Otter's
// behaviour, and gets it fast where otter-net is ready.
//
//   OTTER_ENGINE=powershell   always use the PowerShell Otter
//   OTTER_ENGINE=native       never fall back (for tests); say why instead
//   OTTER_NET_TRACE=1         say on stderr which engine ran and why
//                             (not OTTER_ENGINE_TRACE: the PowerShell Otter reads that one)
//   OTTER_POWERSHELL          path of the PowerShell Otter's otter.ps1
public static class Program
{
    public const string Version = "1.0.1";

    // otterPS's exit codes.
    const int ExitSuccess = 0, ExitUsageError = 1, ExitCheckError = 2, ExitRuntimeError = 3;

    // Each Otter call nests several interpreter frames, so the default 1 MB
    // stack ran out before Otter's own call depth limit (250, as otterPS) and
    // the process died with a raw stack overflow. Parsing and running happen
    // on a thread whose stack is large enough for the limit to be reached
    // first, with the error the user would see from otterPS.
    const int StackSize = 256 * 1024 * 1024;

    public static int Main(string[] args)
    {
        int exitCode = ExitUsageError;
        var worker = new Thread(() => exitCode = Run(args), StackSize);
        worker.Start();
        worker.Join();
        return exitCode;
    }

    static int Run(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string engine = Environment.GetEnvironmentVariable("OTTER_ENGINE") ?? "";
        if (string.Equals(engine, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            return Fallback(args, "OTTER_ENGINE=powershell");
        }

        if (args.Length == 1 && args[0] is "--version" or "-v" or "version")
        {
            Trace("native: --version");
            Console.Out.Write($"Otter {Version}\n");
            return ExitSuccess;
        }

        // otter file.ot [args]  |  otter run file.ot [args]  |  otter check file.ot
        string? mode = null;
        string? path = null;
        string[] programArguments = Array.Empty<string>();
        if (args.Length >= 1 && args[0].EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
        {
            mode = "run"; path = args[0]; programArguments = args[1..];
        }
        else if (args.Length >= 2 && args[0] == "run" && args[1].EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
        {
            mode = "run"; path = args[1]; programArguments = args[2..];
        }
        else if (args.Length == 2 && args[0] == "check" && args[1].EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
        {
            mode = "check"; path = args[1];
        }
        if (mode == null || path == null) return FallbackOrRefuse(args, engine, "this command is not native yet");
        if (!File.Exists(path)) return FallbackOrRefuse(args, engine, "the file does not exist (the PowerShell Otter reports it)");

        // `use` lines are expanded first, exactly as otterPS does (D60); a
        // program without them reads back as its own lines.
        ResolvedProgram resolved;
        try { resolved = Modules.Resolve(path); }
        catch (ModuleProblem problem) { return FallbackOrRefuse(args, engine, $"{problem.Message} (reported by the PowerShell Otter)"); }
        string source = resolved.CombinedSource;
        ProgramNode program;
        try
        {
            program = Parser.Parse(Lexer.Tokenize(source));
        }
        catch (NotSupportedYet unsupported)
        {
            return FallbackOrRefuse(args, engine, $"{unsupported.Feature} (line {unsupported.Line}) is not native yet");
        }
        catch (OtterError syntaxError)
        {
            // Every syntax error, as otterPS reports them (D117).
            Trace("native: syntax errors");
            return ReportCheckError(resolved.Remap(syntaxError));
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            // A parser bug must not change what the user sees.
            return FallbackOrRefuse(args, engine, $"the parser stopped unexpectedly ({unexpected.GetType().Name})");
        }
        // D-2: names the grammar would misread are refused before anything runs.
        // PowerShell: $Source -split "`r?`n"
        string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
        List<OtterError> reserved = ReservedWords.Diagnostics(program, sourceLines);
        if (reserved.Count > 0)
        {
            Trace("native: reserved names");
            return ReportCheckError(resolved.Remap(new OtterMultipleErrors(reserved)));
        }

        if (mode == "check")
        {
            Trace("native: check");
            Console.Out.Write($"Otter: {path} is valid.\n");
            return ExitSuccess;
        }

        Trace("native: run");
        // otterPS writes through Write-Host, which ends every line with "\n"
        // (only the lines inside an error block end with Environment.NewLine).
        // Each write reaches the console at once, as Write-Host's does: a
        // program stopped with Ctrl+C, or one that never ends, has still shown
        // everything it said (found by the fuzz corpus, 2026-10-16).
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        // Input piped from Windows PowerShell starts with a byte order mark;
        // the reader drops it, as PowerShell's Read-Host does.
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        try
        {
            new Interpreter(output, input, source, programArguments, echoInput: Console.IsInputRedirected)
            {
                WriteColoredLine = Console.IsOutputRedirected ? null : (text, color) => WriteInColor(output, text, color),
                ReadSecretLine = Console.IsInputRedirected ? null : () => ReadSecretLine(output),
            }.Run(program);
            output.Flush();
            return ExitSuccess;
        }
        catch (OtterError error)
        {
            output.WriteLine("");
            // Line and file as the programmer wrote them, even inside an
            // imported file.
            output.WriteLine(resolved.Remap(error).FormatDetailed());
            output.WriteLine("");
            output.Flush();
            return ExitRuntimeError;
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            // Never a raw .NET exception in front of a user (D14).
            output.WriteLine("");
            output.WriteLine("Otter hit a problem inside itself, which means this is a bug in Otter.");
            output.WriteLine($"  {unexpected.Message}");
            output.WriteLine("");
            output.Flush();
            return ExitRuntimeError;
        }
    }

    // Write-Host -ForegroundColor: the line in that color, then the
    // console's own color again.
    static void WriteInColor(TextWriter output, string text, ConsoleColor color)
    {
        output.Flush();
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        try { output.Write(text); output.Flush(); }
        finally { Console.ForegroundColor = previous; }
        output.Write("\n");
        output.Flush();
    }

    // Read-OtterSecretLine: a "*" for each character typed, Backspace takes
    // one back, Enter ends the line.
    static string ReadSecretLine(TextWriter output)
    {
        var typed = new StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.Write("\n");
                return typed.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                    output.Write("\b \b");
                }
                continue;
            }
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
                output.Write("*");
            }
        }
    }

    // A program that does not lex, parse or pass the check stage: nothing
    // runs, and the errors are shown the way otter.ps1's Show-OtterFailure
    // shows them, with the check exit code (D57).
    static int ReportCheckError(OtterError error)
    {
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n" };
        output.WriteLine("");
        output.WriteLine(error.FormatDetailed());
        output.WriteLine("");
        output.Flush();
        return ExitCheckError;
    }

    static void Trace(string text)
    {
        if (Environment.GetEnvironmentVariable("OTTER_NET_TRACE") == "1") Console.Error.WriteLine($"otter-net {text}");
    }

    static int FallbackOrRefuse(string[] args, string engine, string reason)
    {
        if (string.Equals(engine, "native", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"otter-net: OTTER_ENGINE=native, but {reason}.");
            return ExitUsageError;
        }
        return Fallback(args, reason);
    }

    // Runs the PowerShell Otter with the same arguments, the same console and
    // the same standard input, and returns its exit code.
    static int Fallback(string[] args, string reason)
    {
        Trace($"fallback: {reason}");
        string? otterPs1 = FindPowerShellOtter();
        if (otterPs1 == null)
        {
            Console.Error.WriteLine("Otter: this program needs the PowerShell Otter, which is not installed. Set OTTER_POWERSHELL to its otter.ps1.");
            return ExitUsageError;
        }
        bool windows = OperatingSystem.IsWindows();
        var start = new ProcessStartInfo(windows ? "powershell.exe" : "pwsh") { UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile");
        if (windows) { start.ArgumentList.Add("-ExecutionPolicy"); start.ArgumentList.Add("Bypass"); }
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(otterPs1);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    static string? FindPowerShellOtter()
    {
        string? configured = Environment.GetEnvironmentVariable("OTTER_POWERSHELL");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        // Where Install-Otter.ps1 puts it: %LOCALAPPDATA%\Otter\<version>\otter.ps1
        string installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Otter");
        string versioned = Path.Combine(installRoot, Version, "otter.ps1");
        return File.Exists(versioned) ? versioned : null;
    }
}
