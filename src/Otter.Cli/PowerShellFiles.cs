namespace Otter.Cli;

// The PowerShell file-system behaviour the project commands depend on.
internal static class PowerShellFiles
{
    // Get-ChildItem without -Force leaves hidden items out (and does not
    // look inside hidden folders): the Hidden attribute on Windows, a name
    // starting with "." on Linux and macOS (PowerShell 7's rule there).
    static bool Hidden(FileSystemInfo item)
    {
        if (OperatingSystem.IsWindows()) return (item.Attributes & FileAttributes.Hidden) != 0;
        return item.Name.StartsWith('.');
    }

    // Get-ChildItem -LiteralPath <folder> [-Recurse] [-File]: full paths.
    public static List<string> ChildItems(string folder, bool recurse, bool files, bool force = false)
    {
        var result = new List<string>();
        Walk(new DirectoryInfo(folder), recurse, files, force, result);
        return result;
    }

    static void Walk(DirectoryInfo folder, bool recurse, bool filesOnly, bool force, List<string> result)
    {
        FileSystemInfo[] entries;
        try { entries = folder.GetFileSystemInfos(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (FileSystemInfo entry in entries)
        {
            if (!force && Hidden(entry)) continue;
            bool isFolder = entry is DirectoryInfo;
            if (!isFolder || !filesOnly) result.Add(entry.FullName);
            // -Recurse does not follow links to folders.
            if (isFolder && recurse && entry.LinkTarget == null) Walk((DirectoryInfo)entry, recurse, filesOnly, force, result);
        }
    }

    // Sort-Object on text: the current culture's comparison, ignoring
    // capitals. Approximated without culture data (otter-net runs with the
    // invariant culture): punctuation and symbols before digits, digits
    // before letters, letters alphabetically; then lower case before upper
    // case; then ordinal.
    public static readonly Comparison<string> SortObjectComparer = Compare;

    static int Class(char c) => char.IsLetter(c) ? 2 : char.IsDigit(c) ? 1 : 0;

    static int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        int length = Math.Min(left.Length, right.Length);
        for (int i = 0; i < length; i++)
        {
            char a = left[i], b = right[i];
            if (a == b) continue;
            int classA = Class(a), classB = Class(b);
            if (classA != classB) return classA.CompareTo(classB);
            int primary = char.ToLowerInvariant(a).CompareTo(char.ToLowerInvariant(b));
            if (primary != 0) return primary;
        }
        if (left.Length != right.Length) return left.Length.CompareTo(right.Length);
        for (int i = 0; i < length; i++)
        {
            char a = left[i], b = right[i];
            if (a == b) continue;
            // Lower case first.
            if (char.IsLower(a) && char.IsUpper(b)) return -1;
            if (char.IsUpper(a) && char.IsLower(b)) return 1;
        }
        return string.CompareOrdinal(left, right);
    }
}
