using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// `otter` with nothing else: otter.ps1's Start-OtterRepl. One environment
// lives for the whole session; a line that opens a block keeps collecting
// lines ("..... ") until a blank one. Errors are shown in their short form.
//
// A REPL cannot be handed to the PowerShell Otter halfway through a session
// without losing what was typed before, so a line that uses something
// otter-net does not have yet is refused on its own, and the session goes on
// (OTTER_ENGINE=powershell starts the PowerShell Otter's REPL instead).
internal static class Repl
{
    static readonly string[] BlockOpeners = { "if", "while", "repeat", "count", "for", "to", "otherwise" };

    // Test-OtterOpensBlock. PowerShell's -contains and -match ignore capitals.
    static bool OpensBlock(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0) return false;
        string firstWord = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (BlockOpeners.Contains(firstWord, StringComparer.OrdinalIgnoreCase)) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"\bare$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    // Read-Host: with input redirected, each character read is written
    // back as it is read (a last line without a line break is echoed
    // without one).
    static string? ReadHost() => Console.IsInputRedirected
        ? ConsoleInput.ReadLine(Host.In, Host.Out)
        : Host.In.ReadLine();

    static Interpreter NewSession() =>
        new(Host.Out, Host.In, "", Array.Empty<string>(), echoInput: Console.IsInputRedirected)
        {
            WriteColoredLine = Console.IsOutputRedirected ? null : (text, color) => Program.WriteInColor(Host.Out, text, color),
            ReadSecretLine = Console.IsInputRedirected ? null : () => Program.ReadSecretLine(Host.Out),
            MoveCursor = Console.IsOutputRedirected ? null : (column, row) => { Host.Out.Flush(); Console.SetCursorPosition(column, row); },
        };

    public static int Start(CommandLine line)
    {
        Program.Trace("native: repl");
        Host.Line(Cli.VersionText);
        Host.Line("Readable like English. Precise like code.");
        Host.Line("");

        Interpreter session = NewSession();
        while (true)
        {
            Host.Text("otter> ");
            string? typed = ReadHost();
            if (typed == null) break;
            string trimmed = typed.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;
            if (trimmed.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                session = NewSession();
                Host.Line("Otter: session reset - all variables and functions cleared.", ConsoleColor.DarkGray);
                continue;
            }
            if (trimmed.Length == 0) continue;

            var buffer = new List<string> { typed };
            if (OpensBlock(typed))
            {
                while (true)
                {
                    Host.Text("..... ");
                    string? more = ReadHost();
                    if (more == null || more.Trim().Length == 0) break;
                    buffer.Add(more);
                }
            }
            string source = string.Join("\n", buffer);
            RunEntry(line, session, source);
        }
        return Cli.ExitSuccess;
    }

    // Invoke-OtterSource in the session's environment; a failure is shown
    // short (Show-OtterFailure -Short).
    static void RunEntry(CommandLine line, Interpreter session, string source)
    {
        string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
        FileMode.Compiled compiled;
        try
        {
            compiled = FileMode.Compile(source, sourceLines, line.DebugTokens);
        }
        catch (NeedsPowerShell unsupported)
        {
            Host.Line($"Otter: otter-net cannot run this yet: {unsupported.Message}. Start the REPL with OTTER_ENGINE=powershell to use it.", ConsoleColor.Red);
            return;
        }
        List<string>? ast = null;
        if (compiled.Failure == null && line.DebugAst && !line.ParseOnly)
        {
            try { ast = AstDump.Lines(compiled.Program!); }
            catch (NeedsPowerShell unsupported)
            {
                Host.Line($"Otter: otter-net cannot run this yet: {unsupported.Message}. Start the REPL with OTTER_ENGINE=powershell to use it.", ConsoleColor.Red);
                return;
            }
        }
        compiled.WriteTokens();
        Exception? failure = compiled.Failure;
        if (ast != null) FileMode.WriteAst(ast);
        if (failure == null && !line.ParseOnly)
        {
            try { session.Run(compiled.Program!); }
            catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
        }
        if (failure == null) return;
        if (failure is OtterError otterError)
        {
            Host.Line(otterError.Format(), ConsoleColor.Red);
            return;
        }
        FileMode.ShowFailure(failure, line.DebugErrors);
    }
}
