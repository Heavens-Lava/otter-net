using Otter.Language;

namespace Otter.Runtime;

// The runtime for user interfaces: web and desktop apps, state, events, animation, routing.
public sealed partial class Interpreter
{
    bool TryExecuteUi(Stmt statement, Scope scope) => false;

    bool TryEvaluateUi(Expr expression, Scope scope, out object? value)
    {
        value = null;
        return false;
    }
}
