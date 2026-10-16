using System.Text;

namespace Otter.Language;

// The one kind of error an Otter user sees. FormatDetailed() reproduces
// otterPS's OtterError.FormatDetailed() exactly - heading, "Line N:", the
// source line with a caret, the message, "Try:" and the suggestion - because
// compatibility includes error text.
public class OtterError : Exception
{
    public OtterError(string message, int line, string stage, int column = 0,
                      string? sourceLine = null, string? suggestion = null, string? code = null)
        : base(message)
    {
        Line = line;
        Stage = stage;
        Column = column;
        SourceLine = sourceLine;
        Suggestion = suggestion;
        Code = code;
    }

    public int Line { get; }
    public string Stage { get; }        // "lexer" | "parser" | "check" | "runtime"
    public int Column { get; }          // 0 when unknown
    public string? SourceLine { get; }
    public string? Suggestion { get; }
    public string? Code { get; }

    // Short one-line form, used by the REPL.
    public virtual string Format() =>
        Line > 0 ? $"Otter: {Message} (line {Line})" : $"Otter: {Message}";

    // Full block form, used when running a file. Like otterPS (StringBuilder
    // .AppendLine), lines inside the block end with Environment.NewLine.
    public virtual string FormatDetailed()
    {
        string heading = Stage switch
        {
            "lexer" => "Otter Syntax Error",
            "parser" => "Otter Syntax Error",
            "runtime" => "Otter Runtime Error",
            _ => "Otter Error",
        };

        var output = new StringBuilder();
        output.AppendLine(heading);
        output.AppendLine("");

        if (Line > 0)
        {
            output.AppendLine($"Line {Line}:");
            if (!string.IsNullOrEmpty(SourceLine))
            {
                string trimmed = SourceLine.TrimStart();
                int leadingWhitespace = SourceLine.Length - trimmed.Length;
                int adjustedColumn = Math.Max(0, Column - 1 - leadingWhitespace);
                output.AppendLine("    " + trimmed.TrimEnd());
                if (Column > 0)
                {
                    output.AppendLine(new string(' ', 4 + adjustedColumn) + "^");
                }
            }
            output.AppendLine("");
        }

        output.AppendLine(Message);

        if (!string.IsNullOrEmpty(Suggestion))
        {
            output.AppendLine("");
            output.AppendLine("Try:");
            output.AppendLine("    " + Suggestion);
        }

        return output.ToString().TrimEnd();
    }
}

// Several syntax errors found in one pass (D117). Blocks are joined with
// "\n\n" - not Environment.NewLine - exactly as otterPS joins them.
public sealed class OtterMultipleErrors : OtterError
{
    public OtterMultipleErrors(IReadOnlyList<OtterError> diagnostics)
        : base(diagnostics[0].Message, diagnostics[0].Line, diagnostics[0].Stage, diagnostics[0].Column,
               diagnostics[0].SourceLine, diagnostics[0].Suggestion, diagnostics[0].Code)
    {
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<OtterError> Diagnostics { get; }

    public override string Format()
    {
        if (Diagnostics.Count <= 1) return base.Format();
        var lines = Diagnostics.Select(d => d.Format()).ToList();
        lines.Add($"Found {Diagnostics.Count} errors in source.");
        return string.Join("\n", lines);
    }

    public override string FormatDetailed()
    {
        if (Diagnostics.Count <= 1) return base.FormatDetailed();
        var blocks = Diagnostics.Select(d => d.FormatDetailed()).ToList();
        blocks.Add($"Found {Diagnostics.Count} errors in source.");
        return string.Join("\n\n", blocks);
    }
}

// What otterPS shows when a .NET call inside it fails with something that is
// not an Otter error: "Otter hit a problem inside itself ...". otter-net
// raises it where otterPS is known to do that, with the same message.
public sealed class InternalProblem(string message) : Exception(message);
