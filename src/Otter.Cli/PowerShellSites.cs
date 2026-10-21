namespace Otter.Cli;

// A statement of otter.ps1 or of one of its modules, where PowerShell
// reports an error itself: the file (relative to otter.ps1's folder), the
// line, and the statement's start and length on it. The lines come from
// otterPS 1.0.1 (PowerShellSites.g.cs, tools/New-PowerShellSiteTable.ps1).
internal sealed class PsSite
{
    PsSite(string? file, int line, string text, int column, int length)
    {
        File = file;
        Line = line;
        Text = text;
        Column = column;
        Length = length;
    }

    public string? File { get; }
    public int Line { get; }
    public string Text { get; }
    public int Column { get; }
    public int Length { get; }

    // A Write-Host whose failure a catch of otterPS always handles (in a
    // module function called inside otter.ps1's try, the program's own
    // output, the -DebugTokens list): its position is never shown.
    public static readonly PsSite Handled = new(null, 0, "", 0, 0);

    public bool HasPosition => File != null;

    // The statement that starts with `command` on that line and runs to its end.
    public static PsSite At(string file, int line, string command = "Write-Host")
    {
        string text = PowerShellSites.Line(file, line);
        int index = text.IndexOf(command, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"{file}:{line} has no {command}");
        return new PsSite(file, line, text, index + 1, text.TrimEnd().Length - index);
    }

    // otter.ps1's own Write-Host on that line.
    public static PsSite Otter(int line) => At("otter.ps1", line);

    // InvocationInfo.PositionMessage of the statement, with the file where
    // the PowerShell Otter is.
    public string Position => PowerShellError.PositionMessage(PowerShellError.OtterPsFile(File!), Line, Text, Column, Length);
}

internal static partial class PowerShellSites
{
    public static string Line(string file, int line) =>
        Lines.TryGetValue($"{file}:{line}", out string? text) ? text : throw new InvalidOperationException($"{file}:{line} is not in PowerShellSites.g.cs");
}
