using System.Collections;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Web;

// otterPS src/Otter.LoopPasses.psm1: which names belong to each loop pass
// (D130) and each `when` handler run (D133), and the check that refuses a
// handler set up inside another deferred body that uses that body's run
// names. otterPS walks the nodes property by property; NodeFields says what
// those properties are.
public static class LoopPasses
{
    static readonly string[] BindingFields = { "Target", "ResultTarget", "TargetName", "ErrorTarget", "RequestTarget", "ItemName", "VariableName" };

    static object? Field(object node, string name)
    {
        foreach (var (fieldName, value) in NodeFields.Of(node)) if (fieldName == name) return value;
        return null;
    }

    // Get-OtterStatementBindings
    static List<string> StatementBindings(Node statement)
    {
        var names = new List<string>();
        if (statement is FunctionDefStmt) return names;
        if (statement is AssignStmt assign)
        {
            if (assign.Target is VariableExpr variable) names.Add(variable.Name);
            return names;
        }
        foreach (string field in BindingFields)
        {
            if (Field(statement, field) is string value && value.Length > 0) names.Add(value);
        }
        if (statement is ListDefStmt list && !string.IsNullOrEmpty(list.Name)) names.Add(list.Name);
        if (statement is ObjectDefStmt thing && !string.IsNullOrEmpty(thing.Name)) names.Add(thing.Name);
        return names;
    }

    // Get-OtterChildStatementLists: bodies, branches, otherwise blocks and
    // handler bodies - any list of nodes, and the Body of any other object in
    // a list (an if-branch). Function bodies are their own scope.
    static List<List<Node>> ChildStatementLists(Node statement)
    {
        var lists = new List<List<Node>>();
        if (statement is FunctionDefStmt) return lists;
        foreach (var (_, value) in NodeFields.Of(statement))
        {
            if (value is null or string or Node) continue;
            if (value is not IEnumerable items) continue;
            var nodes = new List<Node>();
            foreach (object? item in items)
            {
                if (item is Node node) nodes.Add(node);
                else if (item != null && NodeFields.IsContractObject(item) && Field(item, "Body") is IEnumerable body)
                {
                    lists.Add(body.OfType<Node>().ToList());
                }
            }
            if (nodes.Count > 0) lists.Add(nodes);
        }
        return lists;
    }

    static bool IsHandler(Node? statement) => statement is WhenStmt;

    static bool IsLoop(Node? statement) => statement is ForEachStmt or CountStmt or RepeatStmt or WhileStmt;

    // $script:OtterDeferredKinds: When, WebRoute, UiEvent, MemoDef, Watch,
    // Lifecycle and WatchEvent. (The list also names RouteChange and
    // NetworkEvent, which are not node kinds, so those never count.)
    static bool IsDeferred(Node statement) => statement is WhenStmt or WebRouteStmt or UiEventStmt or MemoDefStmt or WatchStmt or LifecycleStmt or WatchEventStmt;

    static bool ContainsDeferredBody(IEnumerable<Node> statements)
    {
        foreach (Node s in statements)
        {
            if (IsDeferred(s)) return true;
            foreach (var list in ChildStatementLists(s))
            {
                if (ContainsDeferredBody(list)) return true;
            }
        }
        return false;
    }

    static string? LoopVariable(Node statement) => statement switch
    {
        ForEachStmt f => f.VariableName,
        CountStmt c => c.VariableName,
        _ => null,
    };

    static IReadOnlyList<Stmt> Body(Node statement) => statement switch
    {
        ForEachStmt f => f.Body,
        CountStmt c => c.Body,
        RepeatStmt r => r.Body,
        WhileStmt w => w.Body,
        WhenStmt h => h.Body,
        _ => Array.Empty<Stmt>(),
    };

    sealed record Occurrence(string Name, List<Node> Loops);

    // Get-OtterLoopPassNames
    public static Dictionary<Node, HashSet<string>> Names(IReadOnlyList<Stmt> statements)
    {
        var result = new Dictionary<Node, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<(IEnumerable<Node> Body, IReadOnlyList<string> Parameters)>();
        pending.Enqueue((statements, Array.Empty<string>()));
        while (pending.Count > 0)
        {
            var scope = pending.Dequeue();
            var occurrences = new List<Occurrence>();
            var loops = new List<Node>();
            var functions = new List<FunctionDefStmt>();
            var handlers = new List<Node>();
            AddScopeBindings(scope.Body, new List<Node>(), occurrences, loops, functions, handlers);
            foreach (Node loop in loops.Concat(handlers))
            {
                var inside = new HashSet<string>(StringComparer.Ordinal);
                var outside = new HashSet<string>(scope.Parameters, StringComparer.Ordinal);
                foreach (Occurrence o in occurrences)
                {
                    if (o.Loops.Any(l => ReferenceEquals(l, loop))) inside.Add(o.Name);
                    else outside.Add(o.Name);
                }
                inside.ExceptWith(outside);
                if (!ContainsDeferredBody(Body(loop))) inside.Clear();
                result[loop] = inside;
            }
            foreach (FunctionDefStmt f in functions) pending.Enqueue((f.Body, f.Parameters));
        }
        return result;
    }

    static void AddScopeBindings(IEnumerable<Node> statements, List<Node> loopStack, List<Occurrence> occurrences,
        List<Node> loops, List<FunctionDefStmt> scopes, List<Node> handlers)
    {
        foreach (Node s in statements)
        {
            if (s is FunctionDefStmt function) { scopes.Add(function); continue; }
            foreach (string n in StatementBindings(s))
            {
                // A loop's own variable belongs to that loop's passes (-eq ignores case).
                bool own = IsLoop(s) && LoopVariable(s) is { } variable && string.Equals(n, variable, StringComparison.OrdinalIgnoreCase);
                var owner = new List<Node>(loopStack);
                if (own) owner.Add(s);
                occurrences.Add(new Occurrence(n, owner));
            }
            var inner = loopStack;
            if (IsLoop(s)) { loops.Add(s); inner = new List<Node>(loopStack) { s }; }
            else if (IsHandler(s)) { handlers.Add(s); inner = new List<Node>(loopStack) { s }; }
            foreach (var list in ChildStatementLists(s))
            {
                AddScopeBindings(list, inner, occurrences, loops, scopes, handlers);
            }
        }
    }

    // Get-OtterReferencedNames: variables in expressions, and the name
    // fields statements carry as text.
    static readonly Regex NameField = new("Target|Name|Container|Parent|Element|Resource|Variable", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static void ReferencedNames(object? value, HashSet<string> names)
    {
        switch (value)
        {
            case null: return;
            case string: return;
            case VariableExpr variable: names.Add(variable.Name); return;
            case FunctionDefStmt: return;
            case Node node:
                foreach (var (fieldName, fieldValue) in NodeFields.Of(node))
                {
                    if (fieldValue is string text)
                    {
                        if (text.Length > 0 && NameField.IsMatch(fieldName)) names.Add(text);
                        continue;
                    }
                    ReferencedNames(fieldValue, names);
                }
                return;
            case IEnumerable items:
                foreach (object? item in items) ReferencedNames(item, names);
                return;
            default:
                if (NodeFields.IsContractObject(value) && Field(value, "Body") is { } body) ReferencedNames(body, names);
                return;
        }
    }

    sealed record RunOccurrence(string Name, List<Node> Handlers, bool InLoop);

    sealed record HandlerEntry(Node Node, List<Node> Outer);

    // Get-OtterHandlerRunCaptures: one (name, line) per inner handler that
    // uses a name its outer deferred body makes afresh on each run.
    public static List<(string Name, int Line)> HandlerRunCaptures(IReadOnlyList<Stmt> statements)
    {
        var found = new List<(string, int)>();
        var pending = new Queue<(IEnumerable<Node> Body, IReadOnlyList<string> Parameters)>();
        pending.Enqueue((statements, Array.Empty<string>()));
        while (pending.Count > 0)
        {
            var scope = pending.Dequeue();
            var occurrences = new List<RunOccurrence>();
            var handlers = new List<HandlerEntry>();
            var functions = new List<FunctionDefStmt>();
            AddRunBindings(scope.Body, new List<Node>(), false, occurrences, handlers, functions);
            foreach (HandlerEntry inner in handlers)
            {
                if (inner.Outer.Count == 0) continue;
                Node outer = inner.Outer[^1];
                if (IsHandler(outer) && IsHandler(inner.Node)) continue;
                var run = new HashSet<string>(StringComparer.Ordinal);
                var elsewhere = new HashSet<string>(scope.Parameters, StringComparer.Ordinal);
                foreach (RunOccurrence o in occurrences)
                {
                    bool within = o.Handlers.Any(h => ReferenceEquals(h, outer));
                    bool innerOwns = o.Handlers.Any(h => ReferenceEquals(h, inner.Node));
                    if (within && !o.InLoop && !innerOwns) run.Add(o.Name);
                    else if (!within) elsewhere.Add(o.Name);
                }
                run.ExceptWith(elsewhere);
                if (run.Count == 0) continue;
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var list in ChildStatementLists(inner.Node)) ReferencedNames(list, used);
                used.IntersectWith(run);
                foreach (string name in PowerShellSort.Strings(used)) found.Add((name, inner.Node.Line));
            }
            foreach (FunctionDefStmt f in functions) pending.Enqueue((f.Body, f.Parameters));
        }
        return found;
    }

    static void AddRunBindings(IEnumerable<Node> statements, List<Node> handlerStack, bool inLoop,
        List<RunOccurrence> occurrences, List<HandlerEntry> handlers, List<FunctionDefStmt> scopes)
    {
        foreach (Node s in statements)
        {
            if (s is FunctionDefStmt function) { scopes.Add(function); continue; }
            foreach (string n in StatementBindings(s)) occurrences.Add(new RunOccurrence(n, new List<Node>(handlerStack), inLoop));
            var stack = handlerStack;
            if (IsDeferred(s))
            {
                handlers.Add(new HandlerEntry(s, new List<Node>(handlerStack)));
                stack = new List<Node>(handlerStack) { s };
            }
            bool loop = inLoop || IsLoop(s);
            foreach (var list in ChildStatementLists(s))
            {
                AddRunBindings(list, stack, loop, occurrences, handlers, scopes);
            }
        }
    }

    // The check-stage errors otterPS's Get-OtterLanguageContractDiagnostics
    // adds for those captures.
    public static List<OtterError> HandlerRunDiagnostics(IReadOnlyList<Stmt> statements, string[] sourceLines)
    {
        var errors = new List<OtterError>();
        foreach (var (name, line) in HandlerRunCaptures(statements))
        {
            string? sourceLine = line >= 1 && line <= sourceLines.Length ? sourceLines[line - 1] : null;
            errors.Add(new OtterError(
                $"This handler uses \"{name}\", which the handler around it makes again each time it runs. In Otter 1.0 the inner handler would only ever see the most recent \"{name}\", so it would act on the wrong one.",
                line, "check", 0, sourceLine,
                $"Make each {name} in a function and set up this handler inside that function, for example: to addRow ... create row into {name} ... when deleteButton is clicked ... . Then call addRow from the outer handler.",
                "HandlerRunName"));
        }
        return errors;
    }
}
