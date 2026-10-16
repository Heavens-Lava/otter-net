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

    public static int Main(string[] args)
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
        catch (OtterError)
        {
            // Until the parser is complete, syntax errors are reported by the
            // PowerShell Otter, so they read exactly as they always have.
            return FallbackOrRefuse(args, engine, "a syntax error (reported by the PowerShell Otter)");
        }
        if (ReservedWords.FirstReservedDeclaration(program) is { } reserved)
        {
            return FallbackOrRefuse(args, engine, $"the reserved name '{reserved}' (reported by the PowerShell Otter)");
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
        var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
        // Input piped from Windows PowerShell starts with a byte order mark;
        // the reader drops it, as PowerShell's Read-Host does.
        var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        try
        {
            new Interpreter(output, input, source, programArguments, echoInput: Console.IsInputRedirected).Run(program);
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
