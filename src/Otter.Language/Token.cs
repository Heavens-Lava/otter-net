namespace Otter.Language;

// One token: its kind, the exact source text, the decoded value (a number as
// double, a string without quotes, true/false as bool), and where it is.
public sealed class Token
{
    public Token(TokenKind kind, string text, object? value, int line, int column)
    {
        Kind = kind;
        Text = text;
        Value = value;
        Line = line;
        Column = column;
    }

    public TokenKind Kind { get; }
    public string Text { get; }
    public object? Value { get; }
    public int Line { get; }
    public int Column { get; }

    public override string ToString() => $"{Kind}[{Text}] L{Line}";
}
