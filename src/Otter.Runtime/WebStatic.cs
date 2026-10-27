using System.Text.RegularExpressions;

namespace Otter.Runtime;

// Safe static files for the Otter Web runtime (docs/web/RUNTIME_CONTRACT.md
// §2b): which file a request path names, if any. Used by the host for every
// static request and by the runtime to check `fallback`. A path is refused
// (null: the request gets 404) rather than "fixed" whenever it could reach
// anything outside the static folder or anything hidden.
public static partial class OtterWebStatic
{
    // Windows and macOS file systems ignore capitals by default.
    static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar), PathComparison);

    // True when `path` is `root` or below it.
    public static bool IsInside(string path, string root)
    {
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return SamePath(path, root) || path.StartsWith(prefix, PathComparison);
    }

    // The path with every symbolic link and junction along it resolved, as
    // the operating system would follow them (each component, in order).
    public static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string current = root;
        string[] parts = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string next = Path.Combine(current, parts[i]);
            FileSystemInfo? info = Directory.Exists(next) ? new DirectoryInfo(next) : File.Exists(next) ? new FileInfo(next) : null;
            if (info?.LinkTarget != null)
            {
                FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    // The target may itself sit below other links.
                    next = RealPath(target.FullName);
                }
            }
            current = next;
        }
        return Path.GetFullPath(current.Length == 0 ? full : current);
    }

    // The file a request names inside `staticRoot` (a real path), or null.
    // `segments` are the decoded path segments (OtterWebPaths). A folder
    // gives its index.html; there is no directory listing.
    public static string? Resolve(string staticRoot, IReadOnlyList<string> segments)
    {
        foreach (string segment in segments)
        {
            if (!SafeSegment(segment)) return null;
        }
        string candidate = segments.Count == 0 ? staticRoot : Path.Combine(staticRoot, Path.Combine(segments.ToArray()));
        if (!IsInside(Path.GetFullPath(candidate), staticRoot)) return null;
        if (Directory.Exists(candidate))
        {
            candidate = Path.Combine(candidate, "index.html");
        }
        if (!File.Exists(candidate)) return null;
        string real = RealPath(candidate);
        // Through links too: the real file must be inside the folder, and no
        // part of its real path below the folder may be hidden (".name").
        if (!IsInside(real, staticRoot) || SamePath(real, staticRoot)) return null;
        string relative = real[staticRoot.TrimEnd(Path.DirectorySeparatorChar).Length..];
        if (relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Any(part => part.StartsWith('.'))) return null;
        if (!File.Exists(real)) return null;
        return real;
    }

    // A segment that names one ordinary file or folder: no dot names (".",
    // "..", ".env", ".git"), no separators of any kind, no drive letters or
    // streams (":"), no NUL or control characters.
    static bool SafeSegment(string segment) =>
        segment.Length > 0 && segment.Length <= 255 && segment[0] != '.' &&
        segment.IndexOfAny(new[] { '/', '\\', ':', '\0' }) < 0 && !segment.Any(char.IsControl) &&
        !segment.EndsWith(' ') && !(OperatingSystem.IsWindows() && segment.EndsWith('.'));

    // ---- what is sent ----

    // The fixed MIME table (§2b); anything else is application/octet-stream.
    static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8", [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8", [".mjs"] = "text/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8", [".map"] = "application/json; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".wasm"] = "application/wasm",
        [".glb"] = "model/gltf-binary", [".gltf"] = "model/gltf+json",
        [".bin"] = "application/octet-stream",
        [".ktx2"] = "image/ktx2", [".hdr"] = "image/vnd.radiance",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".avif"] = "image/avif", [".svg"] = "image/svg+xml", [".ico"] = "image/x-icon",
        [".woff"] = "font/woff", [".woff2"] = "font/woff2", [".ttf"] = "font/ttf", [".otf"] = "font/otf",
        [".mp3"] = "audio/mpeg", [".ogg"] = "audio/ogg", [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm",
    };

    public static string ContentType(string path) =>
        Types.TryGetValue(Path.GetExtension(path), out string? type) ? type : "application/octet-stream";

    // A Vite-style content-hashed name (name-[hash].ext or name.[hash].ext):
    // 8 to 16 characters of [A-Za-z0-9_-] with at least one letter and one
    // digit. Such a file never changes, so it may be cached for a year.
    // Recognising fewer hashed names only costs caching; never more.
    public static bool IsHashedName(string path)
    {
        Match match = HashedName().Match(Path.GetFileName(path));
        if (!match.Success) return false;
        string hash = match.Groups["hash"].Value;
        return hash.Any(char.IsAsciiDigit) && hash.Any(char.IsAsciiLetter);
    }

    [GeneratedRegex(@"^.+[-.](?<hash>[A-Za-z0-9_-]{8,16})\.[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HashedName();

    // A strong validator from the file's size and last write time.
    public static string ETag(FileInfo file) =>
        "\"" + file.Length.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "-" +
        file.LastWriteTimeUtc.Ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture) + "\"";

    // SPA fallback (§2b): only a page navigation that nothing else answered.
    public static bool FallbackApplies(string method, string? accept, IReadOnlyList<string> segments)
    {
        if (method is not ("GET" or "HEAD")) return false;
        if (accept == null || !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)) return false;
        if (segments.Count > 0 && (string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) || string.Equals(segments[0], "ws", StringComparison.OrdinalIgnoreCase))) return false;
        if (segments.Count > 0 && Path.HasExtension(segments[^1])) return false;
        return true;
    }
}
