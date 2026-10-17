namespace Otter.Language;

// The parser for networking and events: HTTP, downloads, servers and routes, WebSockets, TCP, UDP, file watching, the event loop (D130, D133).
public sealed partial class Parser
{
    // A statement this area starts at `start`, or null (the token is left unread).
    Stmt? TryReadNetworkStatement(Token start) => null;

    // A value this area starts at `token`, or null.
    Expr? TryReadNetworkValue(Token token) => null;

    // A condition this area starts at `head`, or null.
    Expr? TryReadNetworkCondition(Token head) => null;
}
