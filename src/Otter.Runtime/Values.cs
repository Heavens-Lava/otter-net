using System.Globalization;
using Otter.Language;

namespace Otter.Runtime;

// Otter values, as otterPS holds them: a number is a double, text a string,
// true/false a bool, gone is null, a list is an OtterList. (The prototype keeps
// otterPS's representation so behaviour matches first; a compact value struct
// is a later, measured optimisation.)
public sealed class OtterList : List<object?>
{
    public OtterList() { }
    public OtterList(IEnumerable<object?> items) : base(items) { }
}

// A thing: properties in the order they were first set, as otterPS's
// OtterObject. Two things are equal only if they are the same thing.
public sealed class OtterThing(string typeName)
{
    readonly Dictionary<string, object?> properties = new(StringComparer.Ordinal);
    readonly List<string> order = new();

    public string TypeName { get; } = typeName;
    public IReadOnlyList<string> PropertyNames => order;
    public bool HasProperty(string name) => properties.ContainsKey(name);
    public object? ReadProperty(string name) => properties.TryGetValue(name, out var value) ? value : null;

    public void WriteProperty(string name, object? value)
    {
        if (!properties.ContainsKey(name)) order.Add(name);
        properties[name] = value;
    }
}

// A date, with or without a time of day (D32). A date with no time is pinned
// to midnight, so two dates made the same day are equal.
public sealed class OtterDate
{
    public OtterDate(DateTime value, bool hasTime)
    {
        HasTime = hasTime;
        Value = hasTime ? value : value.Date;
    }

    public DateTime Value { get; }
    public bool HasTime { get; }

    // D32.5: ISO-style, sortable as text.
    public override string ToString() => Value.ToString(HasTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// a Person has / name / age: the property names a new Person starts with.
public sealed class OtterType(string name, IReadOnlyList<string> fieldNames)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> FieldNames { get; } = fieldNames;
}

public sealed class OtterFunction(string name, IReadOnlyList<string> parameters, IReadOnlyList<Stmt> body)
{
    public string Name { get; } = name;
    public IReadOnlyList<string> Parameters { get; } = parameters;
    public IReadOnlyList<Stmt> Body { get; } = body;
}

public static class Values
{
    // D8, as otterPS's Format-OtterValue.
    public static string Format(object? value)
    {
        switch (value)
        {
            case null: return "gone";
            case bool b: return b ? "true" : "false";
            case double d: return FormatNumber(d);
            case string s: return s;
            case OtterList list: return string.Join(", ", list.Select(Format));
            // A thing prints the way it was declared: "a thing", "a Person".
            case OtterThing thing: return $"a {thing.TypeName}";
            case OtterType type: return $"the type {type.Name}";
            // Printing a function is almost always a forgotten call.
            case OtterFunction function: return $"<{function.Name}, something Otter can do>";
            default: return value.ToString() ?? "";
        }
    }

    // A whole number below 1e15 prints with no decimal point; anything else
    // with up to 10 decimals, trailing zeros trimmed. otterPS runs this on
    // .NET Framework, which never prints a negative zero; .NET 10 prints
    // "-0", so that one case is normalised.
    public static string FormatNumber(double number)
    {
        var culture = CultureInfo.InvariantCulture;
        bool finite = !double.IsNaN(number) && !double.IsInfinity(number);
        string text = finite && Math.Floor(number) == number && Math.Abs(number) < 1e15
            ? number.ToString("0", culture)
            : number.ToString("0.##########", culture);
        return text == "-0" ? "0" : text;
    }

    public static string TypeName(object? value) => value switch
    {
        null => "gone",
        bool => "a true or false value",
        OtterFunction => "something Otter can do",
        OtterThing thing => $"a {thing.TypeName}",
        OtterType type => $"the type {type.Name}",
        OtterDate date => date.HasTime ? "a date and time" : "a date",
        OtterList => "a list",
        double => "a number",
        string => "some text",
        _ => "a value Otter does not recognise",
    };

    // D9: gone, false, 0, "" and an empty list are false.
    public static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        OtterList list => list.Count > 0,
        string s => s.Length > 0,
        _ => true,
    };

    // "29" counts as a number; "banana" does not (otterPS: NumberStyles.Float,
    // invariant culture).
    public static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; return true;
            case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            default: number = 0; return false;
        }
    }

    public static bool AreEqual(object? left, object? right)
    {
        if (left is double l && right is double r) return l == r;
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;
        if (left is bool || right is bool) return left is bool lb && right is bool rb && lb == rb;
        // Two dates compare by their instant; a date never equals anything else.
        if (left is OtterDate || right is OtterDate) return left is OtterDate ld && right is OtterDate rd && ld.Value == rd.Value;
        if (TryNumber(left, out double ln) && TryNumber(right, out double rn)) return ln == rn;
        if (left is OtterList ll && right is OtterList rl)
        {
            if (ll.Count != rl.Count) return false;
            for (int i = 0; i < ll.Count; i++) { if (!AreEqual(ll[i], rl[i])) return false; }
            return true;
        }
        // Things compare by identity, never by their contents.
        if (left is OtterThing || right is OtterThing) return ReferenceEquals(left, right);
        // D10: text compares case-sensitively. PowerShell's [string] cast of a
        // list joins it with spaces, which is what otterPS compares here.
        return string.Equals(AsComparedText(left), AsComparedText(right), StringComparison.Ordinal);
    }

    static string AsComparedText(object value) => value switch
    {
        string s => s,
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        OtterList list => string.Join(" ", list.Select(i => i is null ? "" : AsComparedText(i))),
        _ => value.ToString() ?? "",
    };

    // D6: what `ask` stores - "29" becomes 29, "true" becomes true.
    public static object FromInput(string? text)
    {
        if (text is null) return "";
        string trimmed = text.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (trimmed.Length > 0 && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return number;
        return text;
    }
}
