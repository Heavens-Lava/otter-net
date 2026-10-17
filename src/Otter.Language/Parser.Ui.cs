namespace Otter.Language;

// The parser for user interfaces: web and desktop apps, state, events, animation, routing.
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadUiStatement(Token start) => null;

    // A value this area starts at `token`, or null.
    Expr? TryReadUiValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadUiCondition(Token head) => null;
}
