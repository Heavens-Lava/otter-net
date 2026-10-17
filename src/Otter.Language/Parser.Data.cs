namespace Otter.Language;

// The parser for data and console: databases, queries, CSV, console interaction, remaining library operations.
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadDataStatement(Token start) => null;

    // A value this area starts at `token`, or null.
    Expr? TryReadDataValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadDataCondition(Token head) => null;
}
