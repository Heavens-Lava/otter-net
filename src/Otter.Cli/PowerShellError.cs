using System.Text;

namespace Otter.Cli;

// An error PowerShell itself reports while running otter.ps1 (a parameter
// binding failure, a conversion in otter.ps1's own code), shown the way
// powershell.exe -File shows it on Windows PowerShell 5.1: the default
// error view ("NormalView") on the error stream, then exit code 1.
//
//     C:\...\otter.ps1 : <message>
//         + CategoryInfo          : InvalidArgument: (:) [otter.ps1], ParentContainsErrorRecordException
//         + FullyQualifiedErrorId : AmbiguousParameter,otter.ps1
//     (a line holding one space)
//
// The view's two "+" lines are cut into pieces of the console width less 6
// (written one after another on the same line); a standard 120-column
// console is assumed. otter.ps1's path is that of an otter.ps1 next to
// this program, where the installer puts it (docs/SPEC-QUESTIONS.md C2-1).
internal sealed class PowerShellError : Exception
{
    const int ConsoleWidth = 120;

    public PowerShellError(string prefix, string message, string? position, string category, string activity, string reason, string errorId)
        : base(message)
    {
        Prefix = prefix;
        Position = position;
        Category = category;
        Activity = activity;
        Reason = reason;
        ErrorId = errorId;
    }

    public string Prefix { get; }
    public string? Position { get; }
    public string Category { get; }
    public string Activity { get; }
    public string Reason { get; }
    public string ErrorId { get; }

    // $MyInvocation.MyCommand.Path of otter.ps1.
    public static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "otter.ps1");

    // A ParameterBindingException from binding otter.ps1's command line.
    public static PowerShellError Binding(string message, string errorId, string category = "InvalidArgument") =>
        new($"{ScriptPath} : ", message, null, category, "otter.ps1", "ParentContainsErrorRecordException", $"{errorId},otter.ps1");

    public string Render()
    {
        string newLine = Environment.NewLine;
        int width = ConsoleWidth - 4 - 2;
        var text = new StringBuilder();
        text.Append(Prefix).Append(Message);
        if (Position != null) text.Append(newLine).Append(Position);
        text.Append(newLine).Append(Indented($"+ CategoryInfo          : {Category}: (:) [{Activity}], {Reason}", width));
        text.Append(newLine).Append(Indented($"+ FullyQualifiedErrorId : {ErrorId}", width));
        text.Append(newLine).Append(' ');
        return text.ToString();
    }

    // foreach ($line in @($s -split "(.{$width})")) { if ($line) { "    " + $line } }
    static string Indented(string line, int width)
    {
        var result = new StringBuilder();
        for (int i = 0; i < line.Length; i += width)
        {
            result.Append("    ").Append(line, i, Math.Min(width, line.Length - i));
        }
        return result.ToString();
    }

    // The console host's WriteErrorLine: red on a console, the plain text
    // when the error stream is redirected.
    public void Write()
    {
        Host.Out.Flush();
        string text = Render();
        if (!Console.IsErrorRedirected)
        {
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            try { Console.Error.Write(text); Console.Error.Flush(); }
            finally { Console.ForegroundColor = previous; }
            Console.Error.Write(Environment.NewLine);
        }
        else
        {
            Console.Error.Write(text + Environment.NewLine);
        }
        Console.Error.Flush();
    }

    // InvocationInfo.PositionMessage (PositionUtilities.VerboseMessage): the
    // file, line and column, then the line, cut to 69 characters around the
    // error with "... " / " ...", and "~" under the error.
    public static string PositionMessage(string file, int lineNumber, string sourceLine, int startColumn, int length)
    {
        string newLine = Environment.NewLine;
        sourceLine = sourceLine.TrimEnd();
        int spacesBeforeError = startColumn - 1;
        int errorLength = length;
        const int maxLineLength = 69;
        bool needsPrefixDots = false, needsSuffixDots = false;
        int lineLength = sourceLine.Length;
        if (lineLength > maxLineLength)
        {
            int totalPrefix = spacesBeforeError;
            int prefix = Math.Min(totalPrefix, 12);
            int totalSuffix = lineLength - errorLength - spacesBeforeError;
            int suffix = Math.Min(totalSuffix, 8);
            int candidateLength = prefix + errorLength + suffix;
            if (candidateLength >= maxLineLength)
            {
                if (prefix + errorLength >= maxLineLength) errorLength = maxLineLength - prefix;
                needsSuffixDots = true;
            }
            else
            {
                int prefixAvailable = totalPrefix - prefix;
                if (prefixAvailable > 0)
                {
                    prefix += Math.Min(prefixAvailable, maxLineLength - candidateLength);
                    candidateLength = prefix + errorLength + suffix;
                }
                if (candidateLength < maxLineLength && totalSuffix > 0)
                {
                    suffix += Math.Min(totalSuffix, maxLineLength - candidateLength);
                }
                needsSuffixDots = suffix < totalSuffix;
            }
            needsPrefixDots = prefix < totalPrefix;
            int startIndex = Math.Max(spacesBeforeError - prefix, 0);
            sourceLine = sourceLine.Substring(startIndex, maxLineLength);
            spacesBeforeError = Math.Min(spacesBeforeError, prefix);
            errorLength = Math.Min(errorLength, maxLineLength - spacesBeforeError);
        }
        var message = new StringBuilder();
        if (needsPrefixDots) message.Append("... ");
        message.Append(sourceLine);
        if (needsSuffixDots) message.Append(" ...");
        message.Append(newLine).Append("+ ");
        message.Append(' ', spacesBeforeError + (needsPrefixDots ? 4 : 0));
        message.Append('~', errorLength > 0 ? errorLength : 1);
        return $"At {file}:{lineNumber} char:{startColumn}{newLine}+ {message}";
    }
}
