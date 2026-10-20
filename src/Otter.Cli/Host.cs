using System.Text;

namespace Otter.Cli;

// What otter.ps1 writes with Write-Host: standard output, each line ended
// with "\n", and the colour given with -ForegroundColor only when standard
// output is a console (redirected, Write-Host writes the text plainly).
// Every write reaches the console at once, as Write-Host's does.
internal static class Host
{
    static StreamWriter? output;
    static StreamReader? input;

    public static StreamWriter Out => output ??= new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

    // Input piped from Windows PowerShell starts with a byte order mark; the
    // reader drops it, as PowerShell's Read-Host does. One reader for the
    // whole process, so the REPL, `ask` and the debugger share what is typed.
    public static StreamReader In => input ??= new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);

    // True once anything has been written: a command that has started
    // talking must never be handed to the PowerShell Otter afterwards.
    public static bool HasWritten { get; private set; }

    // `site`: where otterPS's Write-Host is, which PowerShell names when that
    // Write-Host fails under -InformationAction (InformationAction.cs).
    public static void Line(string text = "", ConsoleColor? color = null, PsSite? site = null)
    {
        site ??= DefaultSite;
        InformationAction.Before(text, site);
        HasWritten = true;
        if (color is ConsoleColor c && !Console.IsOutputRedirected)
        {
            Out.Flush();
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = c;
            try { Out.Write(text); Out.Flush(); }
            finally { Console.ForegroundColor = previous; }
            Out.Write("\n");
        }
        else
        {
            Out.Write(text);
            Out.Write("\n");
        }
        InformationAction.After(text, site);
    }

    // Inside a function of otterPS's modules that otter.ps1 calls in a try
    // (the project commands): its Write-Host failures go to that catch.
    public static PsSite? DefaultSite { get; private set; }

    public static T InModule<T>(Func<T> work)
    {
        PsSite? previous = DefaultSite;
        DefaultSite = PsSite.Handled;
        try { return work(); }
        finally { DefaultSite = previous; }
    }

    // [Console]::Out.Write: not a Write-Host, so -InformationAction does not touch it.
    public static void Raw(string text)
    {
        HasWritten = true;
        Out.Write(text);
    }

    // Write-Host -NoNewline.
    public static void Text(string text, PsSite? site = null)
    {
        site ??= DefaultSite;
        InformationAction.Before(text, site);
        HasWritten = true;
        Out.Write(text);
        InformationAction.After(text, site);
    }

    // A line of PowerShell's VERBOSE stream (-Verbose), as the console host
    // writes it to standard output: "VERBOSE: ", wrapped at the console
    // width, ended by "\n". Not a Write-Host: -InformationAction does not
    // touch it.
    public static void Verbose(string message)
    {
        HasWritten = true;
        if (!Console.IsOutputRedirected)
        {
            // A console wraps the line itself; the host's verbose colour is yellow.
            Out.Flush();
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            try { Out.Write("VERBOSE: " + message); Out.Flush(); }
            finally { Console.ForegroundColor = previous; }
            Out.Write("\n");
            return;
        }
        Out.Write(PowerShellError.HostWrapped("VERBOSE: " + message));
        Out.Write("\n");
    }

    public static void MarkWritten() => HasWritten = true;
}

// [Environment]::Exit(code) from anywhere in a command.
internal sealed class CliExit(int code) : Exception("exit")
{
    public int Code { get; } = code;
}

// Something otter-net does not do natively yet, found before anything ran:
// the whole command goes to the PowerShell Otter.
internal sealed class NeedsPowerShell(string reason) : Exception(reason);

// A program that opens desktop windows (WPF), found before anything ran: on
// Windows the whole command goes to the otter-desktop host (Program.cs).
internal sealed class NeedsDesktop(string reason) : Exception(reason);
