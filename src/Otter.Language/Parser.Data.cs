namespace Otter.Language;

// The parser for data: databases (D97, D98), the query language (D99) and
// the readability article `the` (`the name of person`). Each method follows
// its otterPS counterpart in src/Otter.Parser.psm1 token by token, with the
// same messages.
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadDataStatement(Token start)
    {
        // `the property of target is value` is the assignment counterpart of
        // the readability form in ReadValue.
        if (Same(start.Text, "the") && At(1) is { } property && IsIdentifier(property) && IsAt(2, TokenKind.Of))
        {
            Expr target = ReadValue();
            Expect(TokenKind.Is, "I expected \"is\" after the property target.");
            Expr value = ReadMathExpression();
            Expect(TokenKind.Newline, "I expected the property assignment to end here.");
            return new AssignStmt(target, value, start.Line);
        }
        return null;
    }

    // A value this area starts at `token`, or null.
    Expr? TryReadDataValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadDataCondition(Token head) => null;

    // ---- the readability article -------------------------------------------

    // `the` is skipped when it introduces a real `<property> of ...` (or a
    // two-word property such as `exit code of`), and, where otterPS reads a
    // property target (-PropertyTarget), before any name. Elsewhere `the` is
    // an ordinary name.
    void SkipReadabilityArticle(bool propertyTarget)
    {
        if (!Same(Current.Text, "the")) return;
        bool beforeName = position + 1 < tokens.Count && IsIdentifier(tokens[position + 1]);
        bool beforeProperty = position + 2 < tokens.Count && IsIdentifier(tokens[position + 1]) &&
                              (tokens[position + 2].Kind == TokenKind.Of || CommandResultPropertyAt(position + 1));
        if ((propertyTarget && beforeName) || beforeProperty) Read();
    }

    // otterPS's Test-OtterCommandResultPropertyAt.
    bool CommandResultPropertyAt(int at)
    {
        if (at >= tokens.Count) return false;
        Token first = tokens[at];
        if (!IsIdentifier(first)) return false;
        if (at + 2 >= tokens.Count) return false;
        Token second = tokens[at + 1], third = tokens[at + 2];
        bool Pair(string a, string b) => Same(first.Text, a) && Same(second.Text, b);
        if ((Pair("exit", "code") || Pair("error", "output") || Pair("rows", "affected") || Pair("primary", "key") ||
             Pair("database", "type") || Pair("default", "expression")) && third.Kind == TokenKind.Of) return true;
        if (at + 3 < tokens.Count)
        {
            Token fourth = tokens[at + 3];
            if (Pair("last", "inserted") && Same(third.Text, "id") && fourth.Kind == TokenKind.Of) return true;
            if (Pair("primary", "key") && Same(third.Text, "position") && fourth.Kind == TokenKind.Of) return true;
        }
        return false;
    }
}
