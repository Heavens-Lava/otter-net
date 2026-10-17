using Otter.Language;

namespace Otter.Runtime;

// The runtime for data and console: databases, queries, CSV, console interaction, remaining library operations.
public sealed partial class Interpreter
{
    bool TryExecuteData(Stmt statement, Scope scope) => false;

    bool TryEvaluateData(Expr expression, Scope scope, out object? value)
    {
        value = null;
        return false;
    }
}
