namespace Otter.Web;

// PowerShell's [ordered]@{}: insertion order, keys compared ignoring case,
// and a key keeps the spelling it was first added with.
sealed class OrderedMap<T>
{
    readonly List<string> keys = new();
    readonly Dictionary<string, T> values = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> spelling = new(StringComparer.OrdinalIgnoreCase);

    public int Count => keys.Count;
    public IEnumerable<string> Keys => keys;
    public IEnumerable<T> Values => keys.Select(k => values[k]);
    public bool Contains(string key) => values.ContainsKey(key);

    public T this[string key]
    {
        get => values[key];
        set
        {
            if (!values.ContainsKey(key))
            {
                keys.Add(key);
                spelling[key] = key;
            }
            values[key] = value;
        }
    }

    public bool TryGetValue(string key, out T value) => values.TryGetValue(key, out value!);

    public void Remove(string key)
    {
        if (!values.Remove(key)) return;
        string original = spelling[key];
        spelling.Remove(key);
        keys.Remove(original);
    }
}
