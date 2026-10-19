using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Otter.Language;

// Which names belong to each loop pass (D130) and each `when` handler run
// (D133): a port of otterPS's src/Otter.LoopPasses.psm1. The runtime half
// (pass scopes, handler runs) is Otter.Runtime/EventLoop.cs.
//
// A handler set up during a loop pass remembers that pass. The names that
// belong to a pass are the loop variable and every name the loop body sets
// that is set nowhere else in the same scope (the program's top level, or one
// function's body with its parameters). Everything else is shared and read
// live. Only a loop whose body sets up a handler (a deferred body) gets pass
// names at all.
//
// otterPS walks its contract classes by their property names (Target,
// ResultTarget, VariableName ... and every list of nodes), so this does the
// same through reflection over the syntax nodes - whose property names follow
// otterPS's - and so covers every area's nodes without a list of them.
// Statement kinds are recognised by their otterPS class names, because the
// UI area's nodes (WhenStmt, UiEventStmt, ...) live in its own files.
// (The web compiler has its own copy, Otter.Web.LoopPasses, over NodeFields.)
public static partial class LoopPassAnalysis
{
    static readonly string[] BindingProperties = { "Target", "ResultTarget", "TargetName", "ErrorTarget", "RequestTarget", "ItemName", "VariableName" };

    // Statements whose body runs later, with the variables of the place it
    // was written. otterPS's list also names NetworkEvent and RouteChange,
    // which are not NodeKinds there: those entries are empty, so `on message
    // from` (a WebSocketEvent) and `on route change` do not count.
    static readonly HashSet<string> DeferredKinds = new(StringComparer.Ordinal)
    {
        "WhenStmt", "WebRouteStmt", "UiEventStmt", "MemoDefStmt", "WatchStmt", "LifecycleStmt", "WatchEventStmt",
    };

    static bool IsLoop(Node node) => node is WhileStmt or RepeatStmt or CountStmt or ForEachStmt;

    // A `when` handler, whose runs get their own values (D133).
    static bool IsHandler(Node node) => node.GetType().Name == "WhenStmt";

    static bool IsDeferred(Node node) => DeferredKinds.Contains(node.GetType().Name);

    // Node keeps its public properties for Native AOT (see Syntax.cs).
    static PropertyInfo[] PropertiesOf(Node node) => node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

    static object? PropertyValue(Node node, string name)
    {
        PropertyInfo? property = node.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(node);
    }

    static string? LoopVariable(Node node) => node switch
    {
        CountStmt count => count.VariableName,
        ForEachStmt forEach => forEach.VariableName,
        _ => null,
    };

    // Get-OtterStatementBindings: the names one statement sets (not its children's).
    static List<string> Bindings(Node statement)
    {
        var names = new List<string>();
        if (statement is FunctionDefStmt) return names;
        if (statement is AssignStmt assign)
        {
            if (assign.Target is VariableExpr variable) names.Add(variable.Name);
            return names;
        }
        foreach (string property in BindingProperties)
        {
            if (PropertyValue(statement, property) is string { Length: > 0 } text) names.Add(text);
        }
        if (statement is ListDefStmt list && !string.IsNullOrEmpty(list.Name)) names.Add(list.Name);
        if (statement is ObjectDefStmt thing && !string.IsNullOrEmpty(thing.Name)) names.Add(thing.Name);
        return names;
    }

    // Get-OtterChildStatementLists: the lists of nodes directly inside one
    // statement (bodies, branches, otherwise blocks, handler bodies).
    // Function bodies are their own scope.
    static List<List<Node>> ChildLists(Node statement)
    {
        var lists = new List<List<Node>>();
        if (statement is FunctionDefStmt) return lists;
        foreach (PropertyInfo property in PropertiesOf(statement))
        {
            if (property.GetIndexParameters().Length > 0) continue;
            object? value = property.GetValue(statement);
            if (value is null or string or Node || value is not IEnumerable items) continue;
            var nodes = new List<Node>();
            foreach (object? item in items)
            {
                if (item is Node node) nodes.Add(node);
                else if (item is IfBranch branch) lists.Add(branch.Body.Cast<Node>().ToList());
            }
            if (nodes.Count > 0) lists.Add(nodes);
        }
        return lists;
    }

    static IReadOnlyList<Node> BodyOf(Node node) =>
        PropertyValue(node, "Body") is IEnumerable body ? body.OfType<Node>().ToList() : Array.Empty<Node>();

    static bool ContainsDeferredBody(IEnumerable<Node> statements)
    {
        foreach (Node statement in statements)
        {
            if (IsDeferred(statement)) return true;
            foreach (var list in ChildLists(statement))
            {
                if (ContainsDeferredBody(list)) return true;
            }
        }
        return false;
    }

    sealed record Occurrence(string Name, Node[] Loops);

    // Get-OtterLoopPassNames: each loop and `when` handler (by reference) to
    // its pass (or run) names. otterPS runs the program without pass names
    // if the analysis fails, and so does this.
    public static Dictionary<Node, HashSet<string>> Compute(IReadOnlyList<Stmt> statements)
    {
        var result = new Dictionary<Node, HashSet<string>>(ReferenceEqualityComparer.Instance);
        try
        {
            var pending = new Queue<(IEnumerable<Node> Body, IReadOnlyList<string> Parameters)>();
            pending.Enqueue((statements, Array.Empty<string>()));
            while (pending.Count > 0)
            {
                var (body, parameters) = pending.Dequeue();
                var occurrences = new List<Occurrence>();
                var loops = new List<Node>();
                var functions = new List<FunctionDefStmt>();
                var handlers = new List<Node>();
                AddScopeBindings(body, Array.Empty<Node>(), occurrences, loops, functions, handlers);
                // Loops and handlers use the same rule: a loop's passes
                // (D130), a handler's runs (D133).
                foreach (Node loop in loops.Concat(handlers))
                {
                    var inside = new HashSet<string>(StringComparer.Ordinal);
                    var outside = new HashSet<string>(parameters, StringComparer.Ordinal);
                    foreach (var occurrence in occurrences)
                    {
                        if (occurrence.Loops.Any(l => ReferenceEquals(l, loop))) inside.Add(occurrence.Name);
                        else outside.Add(occurrence.Name);
                    }
                    inside.ExceptWith(outside);
                    if (!ContainsDeferredBody(BodyOf(loop))) inside.Clear();
                    result[loop] = inside;
                }
                foreach (var function in functions) pending.Enqueue((function.Body, function.Parameters));
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new Dictionary<Node, HashSet<string>>(ReferenceEqualityComparer.Instance);
        }
        return result;
    }

    static void AddScopeBindings(IEnumerable<Node> statements, Node[] loopStack, List<Occurrence> occurrences,
        List<Node> loops, List<FunctionDefStmt> functions, List<Node> handlers)
    {
        foreach (Node statement in statements)
        {
            if (statement is FunctionDefStmt function) { functions.Add(function); continue; }
            foreach (string name in Bindings(statement))
            {
                // A loop's own variable belongs to that loop's passes.
                bool own = IsLoop(statement) && string.Equals(name, LoopVariable(statement), StringComparison.OrdinalIgnoreCase);
                occurrences.Add(new Occurrence(name, own ? loopStack.Append(statement).ToArray() : loopStack));
            }
            Node[] inner = loopStack;
            if (IsLoop(statement)) { loops.Add(statement); inner = loopStack.Append(statement).ToArray(); }
            else if (IsHandler(statement)) { handlers.Add(statement); inner = loopStack.Append(statement).ToArray(); }
            foreach (var list in ChildLists(statement)) AddScopeBindings(list, inner, occurrences, loops, functions, handlers);
        }
    }

    // ---- the check stage's refusal (Get-OtterHandlerRunCaptures) ----

    sealed record RunOccurrence(string Name, Node[] Handlers, bool InLoop);

    sealed record InnerHandler(Node Node, Node[] Outer);

    [GeneratedRegex("Target|Name|Container|Parent|Element|Resource|Variable", RegexOptions.IgnoreCase)]
    private static partial Regex NameLikeProperty();

    // Get-OtterReferencedNames: the names a statement list reads or names.
    static void ReferencedNames(object? value, HashSet<string> names)
    {
        switch (value)
        {
            case null or string:
                return;
            case VariableExpr variable:
                names.Add(variable.Name);
                return;
            case FunctionDefStmt:
                return;
            case Node node:
                foreach (PropertyInfo property in PropertiesOf(node))
                {
                    if (property.GetIndexParameters().Length > 0 || property.Name is "Kind" or "Line" or "Column") continue;
                    object? item = property.GetValue(node);
                    if (item is string text)
                    {
                        if (text.Length > 0 && NameLikeProperty().IsMatch(property.Name)) names.Add(text);
                        continue;
                    }
                    ReferencedNames(item, names);
                }
                return;
            case IEnumerable items:
                foreach (object? item in items) ReferencedNames(item, names);
                return;
            case IfBranch branch:
                ReferencedNames(branch.Body, names);
                return;
        }
    }

    // A handler set up inside another deferred body that uses a name the
    // outer body sets afresh on each run would act on the last run's value.
    // D133 gives `when` inside `when` its own run names; every other pairing
    // (a watcher, a route, a timer ...) is refused before the program runs.
    // One entry per name an inner handler uses: (Name, the inner handler's line).
    public static List<(string Name, int Line)> HandlerRunCaptures(IReadOnlyList<Stmt> statements)
    {
        var found = new List<(string Name, int Line)>();
        var pending = new Queue<(IEnumerable<Node> Body, IReadOnlyList<string> Parameters)>();
        pending.Enqueue((statements, Array.Empty<string>()));
        while (pending.Count > 0)
        {
            var (body, parameters) = pending.Dequeue();
            var occurrences = new List<RunOccurrence>();
            var handlers = new List<InnerHandler>();
            var functions = new List<FunctionDefStmt>();
            AddRunBindings(body, Array.Empty<Node>(), false, occurrences, handlers, functions);
            foreach (var inner in handlers)
            {
                if (inner.Outer.Length == 0) continue;
                Node outer = inner.Outer[^1];
                if (IsHandler(outer) && IsHandler(inner.Node)) continue;
                var run = new HashSet<string>(StringComparer.Ordinal);
                var elsewhere = new HashSet<string>(parameters, StringComparer.Ordinal);
                foreach (var occurrence in occurrences)
                {
                    bool within = occurrence.Handlers.Any(h => ReferenceEquals(h, outer));
                    bool innerOwns = occurrence.Handlers.Any(h => ReferenceEquals(h, inner.Node));
                    if (within && !occurrence.InLoop && !innerOwns) run.Add(occurrence.Name);
                    else if (!within) elsewhere.Add(occurrence.Name);
                }
                run.ExceptWith(elsewhere);
                if (run.Count == 0) continue;
                var used = new HashSet<string>(StringComparer.Ordinal);
                // Only the body runs later; the handler's target is read while
                // the outer run is still going.
                foreach (var list in ChildLists(inner.Node)) ReferencedNames(list, used);
                used.IntersectWith(run);
                foreach (string name in used.OrderBy(n => n, StringComparer.InvariantCultureIgnoreCase)) found.Add((name, inner.Node.Line));
            }
            foreach (var function in functions) pending.Enqueue((function.Body, function.Parameters));
        }
        return found;
    }

    static void AddRunBindings(IEnumerable<Node> statements, Node[] handlerStack, bool inLoop, List<RunOccurrence> occurrences,
        List<InnerHandler> handlers, List<FunctionDefStmt> functions)
    {
        foreach (Node statement in statements)
        {
            if (statement is FunctionDefStmt function) { functions.Add(function); continue; }
            foreach (string name in Bindings(statement)) occurrences.Add(new RunOccurrence(name, handlerStack, inLoop));
            Node[] stack = handlerStack;
            if (IsDeferred(statement))
            {
                handlers.Add(new InnerHandler(statement, handlerStack));
                stack = handlerStack.Append(statement).ToArray();
            }
            bool loop = inLoop || IsLoop(statement);
            foreach (var list in ChildLists(statement)) AddRunBindings(list, stack, loop, occurrences, handlers, functions);
        }
    }

    // Get-OtterLanguageContractDiagnostics' HandlerRunName refusals, as check errors.
    public static List<OtterError> Diagnostics(ProgramNode program, string[] sourceLines)
    {
        var errors = new List<OtterError>();
        foreach (var (name, line) in HandlerRunCaptures(program.Statements))
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
