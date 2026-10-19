using Otter.Language;

namespace Otter.Runtime;

// D56's state, derived values and watchers as `otter run` runs them
// (otterPS src/Otter.Runtime.psm1 OtterSignal / OtterDerived and the
// interpreter's StateDef, DeriveDef and Watch cases). A scope holds the
// signal or derived value itself; Scope.Get hands out what it stands for.

// state count is 0
public sealed class OtterSignal(string name, object? value)
{
    public string Name { get; } = name;
    public object? Value { get; set; } = value;
    public int Version { get; private set; } = 1;
    // OtterDerived values to mark dirty, and watchers (Action<object?>) to run,
    // in the order they subscribed.
    readonly List<object> subscribers = new();

    public void Subscribe(object subscriber)
    {
        if (!subscribers.Contains(subscriber)) subscribers.Add(subscriber);
    }

    public void Notify()
    {
        Version++;
        foreach (object subscriber in subscribers.ToArray())
        {
            if (subscriber is OtterDerived derived) derived.MarkDirty();
            else if (subscriber is Action<object?> watcher) watcher(Value);
        }
    }
}

// derive doubled is count times 2: worked out when read, in the scope that
// reads it (otterPS's DerivedEvaluator gets the reading environment), and
// kept until something it read changes.
public sealed class OtterDerived(string name, Expr expression, Func<OtterDerived, Scope, object?> evaluator)
{
    public string Name { get; } = name;
    public Expr Expression { get; } = expression;
    public object? CachedValue { get; set; }
    public bool IsDirty { get; set; } = true;
    public bool IsEvaluating { get; set; }
    readonly List<object> subscribers = new();

    public void Subscribe(object subscriber)
    {
        if (!subscribers.Contains(subscriber)) subscribers.Add(subscriber);
    }

    public void MarkDirty()
    {
        IsDirty = true;
        foreach (object subscriber in subscribers.ToArray())
        {
            if (subscriber is OtterDerived derived) derived.MarkDirty();
            else if (subscriber is Action<object?> watcher) watcher(CachedValue);
        }
    }

    public object? Read(Scope scope) => evaluator(this, scope);
}
