using System.Text;

namespace Otter.Runtime;

// Paths and route patterns of the Otter Web runtime (docs/web/RUNTIME_CONTRACT.md
// §3): shared by the runtime (which route answers) and the host (a static
// file is served only when no route matches), so both decide the same way.
public static class OtterWebPaths
{
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // The request target's path (no query), split at "/" and each segment
    // percent-decoded as UTF-8. A trailing "/" is ignored ("/a/" = "/a").
    // False when a segment is not valid percent-encoded UTF-8.
    public static bool TryDecodeSegments(string rawPath, out List<string> segments)
    {
        segments = new List<string>();
        string path = rawPath;
        int query = path.IndexOf('?');
        if (query >= 0) path = path[..query];
        string[] parts = path.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i == 0 && parts[i].Length == 0) continue;                     // the leading "/"
            if (i == parts.Length - 1 && parts[i].Length == 0) continue;      // a trailing "/"
            if (!TryDecode(parts[i], out string segment)) return false;
            segments.Add(segment);
        }
        return true;
    }

    static bool TryDecode(string text, out string decoded)
    {
        decoded = "";
        if (text.IndexOf('%') < 0)
        {
            decoded = text;
            return true;
        }
        var bytes = new List<byte>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%')
            {
                if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])) return false;
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c < 0x80) bytes.Add((byte)c);
            else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
        }
        try { decoded = StrictUtf8.GetString(bytes.ToArray()); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}

// A route's path: literal segments (matched ignoring capitals) and ":name"
// parameters (one non-empty segment each).
public sealed class OtterWebRoutePattern
{
    readonly (bool IsParameter, string Text)[] segments;

    public OtterWebRoutePattern(string path)
    {
        string trimmed = path.Trim('/');
        segments = trimmed.Length == 0
            ? Array.Empty<(bool, string)>()
            : trimmed.Split('/').Select(s => s.Length > 1 && s[0] == ':' ? (true, s[1..]) : (false, s)).ToArray();
        LiteralCount = segments.Count(s => !s.IsParameter);
        Shape = "/" + string.Join("/", segments.Select(s => s.IsParameter ? ":" : s.Text.ToLowerInvariant()));
    }

    // More literal segments win (§3 precedence).
    public int LiteralCount { get; }
    // Two patterns with the same shape answer the same paths.
    public string Shape { get; }
    public IEnumerable<string> ParameterNames => segments.Where(s => s.IsParameter).Select(s => s.Text);

    public bool Matches(IReadOnlyList<string> path) => TryMatch(path, out _);

    // The parameters by name; a parameter never takes an empty segment.
    public bool TryMatch(IReadOnlyList<string> path, out List<KeyValuePair<string, string>> parameters)
    {
        parameters = new List<KeyValuePair<string, string>>();
        if (path.Count != segments.Length) return false;
        for (int i = 0; i < segments.Length; i++)
        {
            var (isParameter, text) = segments[i];
            if (isParameter)
            {
                if (path[i].Length == 0) return false;
                parameters.Add(new(text, path[i]));
            }
            else if (!string.Equals(text, path[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }
}

// The routes of one server, readable from the host's threads (to decide
// whether a static file may answer) while the program adds routes.
public sealed class OtterWebRouteTable
{
    volatile OtterWebRoutePattern[] patterns = Array.Empty<OtterWebRoutePattern>();
    readonly object gate = new();

    public void Add(OtterWebRoutePattern pattern)
    {
        lock (gate) patterns = patterns.Append(pattern).ToArray();
    }

    public bool AnyMatches(IReadOnlyList<string> path) => patterns.Any(p => p.Matches(path));
}
