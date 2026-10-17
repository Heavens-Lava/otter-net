namespace Otter.Language;

// The parser for operating system integration: processes, commands, system information, clipboard, notifications, dialogs, links, owners, read-only files, registry, event log, credentials, secrets, power, printing, remote commands, zip, folder copy/move.
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadSystemStatement(Token start) => null;

    // A value this area starts at `token`, or null.
    Expr? TryReadSystemValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadSystemCondition(Token head) => null;
}
