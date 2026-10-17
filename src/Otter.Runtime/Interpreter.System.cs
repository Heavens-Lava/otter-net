using Otter.Language;

namespace Otter.Runtime;

// The runtime for operating system integration: processes, commands, system information, clipboard, notifications, dialogs, links, owners, read-only files, registry, event log, credentials, secrets, power, printing, remote commands, zip, folder copy/move.
public sealed partial class Interpreter
{
    bool TryExecuteSystem(Stmt statement, Scope scope) => false;

    bool TryEvaluateSystem(Expr expression, Scope scope, out object? value)
    {
        value = null;
        return false;
    }
}
