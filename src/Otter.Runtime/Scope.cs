namespace Otter.Runtime;

// A scope with a parent chain, as otterPS's OtterEnvironment. Names are
// case-sensitive (D10). Set updates a name where it already lives, so a
// function can change a global; SetLocal always creates the name here, so a
// parameter shadows a global instead of overwriting it.
public sealed class Scope(Scope? parent = null)
{
    readonly Dictionary<string, object?> variables = new(StringComparer.Ordinal);

    public Scope? Parent { get; } = parent;

    public bool Has(string name) => variables.ContainsKey(name) || (Parent?.Has(name) ?? false);

    public object? Get(string name)
    {
        for (Scope? scope = this; scope != null; scope = scope.Parent)
        {
            if (scope.variables.TryGetValue(name, out var value)) return value;
        }
        return null;
    }

    public void SetLocal(string name, object? value) => variables[name] = value;

    public void Set(string name, object? value)
    {
        if (variables.ContainsKey(name)) { variables[name] = value; return; }
        if (Parent != null && Parent.Has(name)) { Parent.Set(name, value); return; }
        variables[name] = value;
    }
}
