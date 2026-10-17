using Otter.Language;

namespace Otter.Runtime;

// The runtime for networking and events: HTTP, downloads, servers and routes, WebSockets, TCP, UDP, file watching, the event loop (D130, D133).
public sealed partial class Interpreter
{
    bool TryExecuteNetwork(Stmt statement, Scope scope) => false;

    bool TryEvaluateNetwork(Expr expression, Scope scope, out object? value)
    {
        value = null;
        return false;
    }
}
