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

    public static void Line(string text = "", ConsoleColor? color = null)
    {
        HasWritten = true;
        if (color is ConsoleColor c && !Console.IsOutputRedirected)
        {
            Out.Flush();
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = c;
            try { Out.Write(text); Out.Flush(); }
            finally { Console.ForegroundColor = previous; }
            Out.Write("\n");
            return;
        }
        Out.Write(text);
        Out.Write("\n");
    }

    // Write-Host -NoNewline.
    public static void Text(string text)
    {
        HasWritten = true;
        Out.Write(text);
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
