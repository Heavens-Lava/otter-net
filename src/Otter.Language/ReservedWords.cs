namespace Otter.Language;

// otterPS's check stage (src/Otter.Validation.psm1) refuses some names that
// the grammar would silently misread: `to main` can never be called, and a
// variable named `completed` is read as a state check in a condition. The
// prototype does not reproduce those messages yet; a program that declares
// one of these names goes to the PowerShell Otter, which reports it.
public static class ReservedWords
{
    static readonly HashSet<string> FunctionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // a UI element at the start of a line
        "window", "page", "card", "heading", "text", "button", "panel", "section",
        "sidebar", "main", "link", "image", "input", "grid",
        // statement words
        "append", "average", "cancel", "choose", "commit", "connect", "convert", "copy",
        "count", "create", "decrease", "decrypt", "delete", "derive", "disconnect",
        "download", "each", "encrypt", "error", "execute", "fail", "find", "focus",
        "format", "get", "hash", "hide", "increase", "join", "kill", "layout", "listen",
        "lock", "log", "maximum", "memo", "minimum", "move", "notify", "on", "post",
        "print", "query", "random", "read", "replace", "respond", "restart", "reverse",
        "rollback", "route", "run", "send", "shared", "shut", "sign", "sort", "split",
        "start", "state", "stop", "sum", "try", "unzip", "use", "wait", "warn", "write", "zip",
        // grammar words
        "animate", "ascending", "between", "descending", "distinct", "file", "files",
        "folder", "folders", "gap", "has", "json", "motion", "otherwise", "parameter", "then",
        // built-in values
        "today", "now", "pi",
    };

    static readonly HashSet<string> VariableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending", "running", "completed", "failed", "cancelled",
        "connecting", "closing", "connected", "closed",
        "listening", "stopped", "secure", "watching",
    };

    // The first reserved name the program declares, or null.
    public static string? FirstReservedDeclaration(ProgramNode program)
    {
        foreach (var statement in program.Statements)
        {
            if (Find(statement) is { } found) return found;
        }
        return null;
    }

    static string? Variable(string? name) => name != null && VariableNames.Contains(name) ? name : null;

    static string? FindIn(IEnumerable<Stmt>? body)
    {
        if (body == null) return null;
        foreach (var statement in body) { if (Find(statement) is { } found) return found; }
        return null;
    }

    static string? Find(Stmt statement) => statement switch
    {
        FunctionDefStmt f => FunctionNames.Contains(f.Name) ? f.Name
            : f.Parameters.Select(Variable).FirstOrDefault(p => p != null) ?? FindIn(f.Body),
        AssignStmt a => Variable(a.Name),
        AskStmt a => Variable(a.Name),
        ListDefStmt l => Variable(l.Name),
        MathIntoStmt m => Variable(m.Target),
        AddToStmt a => Variable(a.Target),
        RemoveFromStmt r => Variable(r.Target),
        CallStmt c => Variable(c.ResultTarget),
        CountStmt c => Variable(c.VariableName) ?? FindIn(c.Body),
        ForEachStmt f => Variable(f.VariableName) ?? FindIn(f.Body),
        IfStmt i => i.Branches.Select(b => FindIn(b.Body)).FirstOrDefault(x => x != null) ?? FindIn(i.ElseBody),
        WhileStmt w => FindIn(w.Body),
        RepeatStmt r => FindIn(r.Body),
        _ => null,
    };
}
