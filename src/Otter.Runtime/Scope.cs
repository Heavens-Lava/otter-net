namespace Otter.Runtime;

// A scope with a parent chain, as otterPS's OtterEnvironment. Names are
// case-sensitive (D10). Set updates a name where it already lives, so a
// function can change a global; SetLocal always creates the name here, so a
// parameter shadows a global instead of overwriting it.
public sealed class Scope(Scope? parent = null)
{
    readonly Dictionary<string, object?> variables = new(StringComparer.Ordinal);

    public Scope? Parent { get; } = parent;

    // The names this scope holds itself, not its parents' (the debugger's locals).
    public IEnumerable<KeyValuePair<string, object?>> OwnVariables => variables;

    public bool Has(string name) => variables.ContainsKey(name) || (Parent?.Has(name) ?? false);

    // D56: while a derived value is being worked out, the names it reads
    // (otterPS's ActiveDependencyTracker, which ignores capitals).
    [ThreadStatic] public static HashSet<string>? DependencyTracker;

    // A name's value; a state variable gives its value and a derived value
    // is worked out (Reactivity.cs).
    public object? Get(string name)
    {
        object? raw = GetRaw(name);
        if (raw is OtterSignal signal)
        {
            DependencyTracker?.Add(signal.Name);
            return signal.Value;
        }
        if (raw is OtterDerived derived)
        {
            DependencyTracker?.Add(derived.Name);
            return derived.Read(this);
        }
        return raw;
    }

    // What the scope holds, a state variable or derived value itself included.
    public object? GetRaw(string name)
    {
        for (Scope? scope = this; scope != null; scope = scope.Parent)
        {
            if (scope.variables.TryGetValue(name, out var value)) return value;
        }
        return null;
    }

    public void SetLocal(string name, object? value) => variables[name] = value;

    // D130 loop passes (EventLoop.cs) look at one scope without its parents.
    public bool HasLocal(string name) => variables.ContainsKey(name);
    public bool TryGetLocal(string name, out object? value) => variables.TryGetValue(name, out value);
    public IEnumerable<KeyValuePair<string, object?>> Locals => variables;

    public void Set(string name, object? value)
    {
        if (variables.TryGetValue(name, out object? current))
        {
            // D56: a state variable keeps its identity; its watchers run only
            // when the value really changes (Otter's own equality).
            if (current is OtterSignal signal)
            {
                bool changed = !Values.AreEqual(signal.Value, value);
                signal.Value = value;
                if (changed) signal.Notify();
                return;
            }
            variables[name] = value;
            return;
        }
        if (Parent != null && Parent.Has(name)) { Parent.Set(name, value); return; }
        variables[name] = value;
    }
}

// One Otter call: which function, the line that called it, and its scope.
public sealed record CallFrame(string FunctionName, int CallLine, Scope Scope);
