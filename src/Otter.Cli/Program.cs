using System.Diagnostics;
using System.Text;

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
//   OTTER_DESKTOP             path of the otter-desktop host (desktop windows on
//                             Windows; default: otter-desktop.exe beside otter)
public static class Program
{
    public const string Version = "1.0.1";

    // otterPS's exit codes.
    const int ExitUsageError = 1, ExitRuntimeError = 3;

    // Each Otter call nests several interpreter frames, so the default 1 MB
    // stack ran out before Otter's own call depth limit (250, as otterPS) and
    // the process died with a raw stack overflow. Parsing and running happen
    // on a thread whose stack is large enough for the limit to be reached
    // first, with the error the user would see from otterPS.
    const int StackSize = 256 * 1024 * 1024;

    public static int Main(string[] args) => RunOnLargeStack(args, singleThreadedApartment: false);

    // The otter-desktop host runs the same command on a single-threaded
    // apartment thread, which WPF needs (src/Otter.Desktop).
    internal static int RunOnLargeStack(string[] args, bool singleThreadedApartment)
    {
        int exitCode = ExitUsageError;
        var worker = new Thread(() => exitCode = Run(args), StackSize);
        if (singleThreadedApartment && OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
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
        try
        {
            CommandLine line = CommandLine.Bind(args);
            int code = Cli.Run(line);
            Trace($"native: exit {code}");
            return code;
        }
        catch (CliExit exit)
        {
            Trace($"native: exit {exit.Code}");
            return exit.Code;
        }
        // An error that ends otter.ps1 itself: powershell.exe reports it, exit code 1.
        catch (PowerShellError error)
        {
            Trace("native: PowerShell's own error");
            error.Write();
            return ExitUsageError;
        }
        catch (WriteHostFailure failure) when (failure.Site.HasPosition)
        {
            Trace("native: a Write-Host failed (-InformationAction)");
            failure.Report().Write();
            return ExitUsageError;
        }
        catch (WriteHostFailure failure)
        {
            // A Write-Host that otterPS's own catch would have handled reached
            // here: a gap in otter-net.
            Console.Error.WriteLine($"otter-net: internal error, a Write-Host failure was not handled: {failure.Message}");
            return ExitRuntimeError;
        }
        catch (NeedsPowerShell unsupported)
        {
            if (Host.HasWritten)
            {
                // A command that has started must never start again elsewhere.
                Console.Error.WriteLine($"otter-net: internal error, {unsupported.Message} after output began.");
                return ExitRuntimeError;
            }
            return FallbackOrRefuse(args, engine, unsupported.Message);
        }
        catch (NeedsDesktop desktop)
        {
            if (Host.HasWritten)
            {
                Console.Error.WriteLine($"otter-net: internal error, {desktop.Message} after output began.");
                return ExitRuntimeError;
            }
            return Desktop(args, engine, desktop.Message);
        }
        finally
        {
            Host.Out.Flush();
        }
    }

    // Write-Host -ForegroundColor: the line in that color, then the
    // console's own color again.
    internal static void WriteInColor(TextWriter output, string text, ConsoleColor color)
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
    internal static string ReadSecretLine(TextWriter output)
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

    internal static void Trace(string text)
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
        Process process;
        try { process = Process.Start(start)!; }
        catch (System.ComponentModel.Win32Exception)
        {
            // Linux, macOS and a Chromebook's Linux need PowerShell 7 for the
            // PowerShell Otter, and it may not be installed.
            Console.Error.WriteLine(windows
                ? "Otter: this program needs the PowerShell Otter, but Windows PowerShell (powershell.exe) could not be started."
                : "Otter: this program needs the PowerShell Otter, which runs on PowerShell 7 (pwsh). Install it from https://aka.ms/install-powershell.");
            return ExitUsageError;
        }
        using (process)
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    // Runs the otter-desktop host (src/Otter.Desktop: the same Otter with
    // otterPS's WPF provider) with the same arguments, the same console and
    // the same standard input, and returns its exit code. Without the host,
    // the PowerShell Otter runs the program, as before the host existed.
    static int Desktop(string[] args, string engine, string reason)
    {
        string? host = DesktopHostPath();
        if (host == null)
        {
            Trace($"desktop: otter-desktop.exe is missing next to otter, so the PowerShell Otter runs {reason}");
            return FallbackOrRefuse(args, engine, $"{reason} needs otter-desktop.exe, which is missing");
        }
        Trace($"desktop: {reason} runs in {host}");
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(start)!; }
        catch (System.ComponentModel.Win32Exception e)
        {
            Trace($"desktop: otter-desktop.exe could not start ({e.Message}), so the PowerShell Otter runs {reason}");
            return FallbackOrRefuse(args, engine, $"{reason} needs otter-desktop.exe, which could not start");
        }
        using (process)
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    // otter-desktop.exe next to this otter, or OTTER_DESKTOP (a development
    // build, a test). Null when there is none.
    internal static string? DesktopHostPath()
    {
        string? configured = Environment.GetEnvironmentVariable("OTTER_DESKTOP");
        if (!string.IsNullOrEmpty(configured)) return File.Exists(configured) ? configured : null;
        string beside = Path.Combine(AppContext.BaseDirectory, "otter-desktop.exe");
        return File.Exists(beside) ? beside : null;
    }

    internal static string? FindPowerShellOtter()
    {
        string? configured = Environment.GetEnvironmentVariable("OTTER_POWERSHELL");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        string versioned = Path.Combine(PowerShellOtterInstallRoot(), Version, "otter.ps1");
        return File.Exists(versioned) ? versioned : null;
    }

    // Where Install-Otter.ps1 puts the PowerShell Otter: %LOCALAPPDATA%\Otter
    // on Windows; on Linux and macOS the XDG data folder's otter, which is
    // ~/.local/share/otter unless XDG_DATA_HOME says otherwise (on macOS too,
    // not .NET's ~/Library/Application Support).
    internal static string PowerShellOtterInstallRoot()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Otter");
        string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(dataHome, "otter");
    }
}
