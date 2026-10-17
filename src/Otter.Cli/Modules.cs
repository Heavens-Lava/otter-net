using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

// `use "helpers.ot"`, resolved at the source-text level before lexing, as
// otterPS's src/Otter.Module.psm1 does (D60): each use line becomes a header
// comment, the imported file's text, and a footer comment; a file imported
// twice is included once; every line of the combined source remembers its
// file and line, so an error inside an imported file says where it really is.
//
// The problems otterPS reports - a missing file, a path whose capital letters
// differ from the disk (M1), a circular import, a folder, an unreadable file -
// are OtterErrors with otterPS's words, lines and suggestions. Absolute use
// paths are allowed and checked the same way (D122).
public sealed class ResolvedProgram
{
    public ResolvedProgram(string combinedSource, List<(string File, int Line)> origins, string rootPath)
    {
        CombinedSource = combinedSource;
        Origins = origins;
        RootPath = rootPath;
    }

    public string CombinedSource { get; }
    // Origins[i] is where combined line i + 1 came from.
    public List<(string File, int Line)> Origins { get; }
    public string RootPath { get; }

    // otterPS's ConvertTo-OtterRemappedDiagnostics.
    public OtterError Remap(OtterError error)
    {
        if (error is OtterMultipleErrors multiple)
        {
            return new OtterMultipleErrors(multiple.Diagnostics.Select(RemapOne).ToList());
        }
        return RemapOne(error);
    }

    OtterError RemapOne(OtterError error)
    {
        if (error.Line <= 0 || error.Line > Origins.Count) return error;
        var (file, localLine) = Origins[error.Line - 1];
        string message = error.Message;
        if (!string.Equals(Modules.CanonicalPath(file), Modules.CanonicalPath(RootPath), StringComparison.OrdinalIgnoreCase))
        {
            string prefix = $"In \"{Path.GetFileName(file)}\": ";
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) message = prefix + message;
        }
        string? sourceLine = error.SourceLine;
        try
        {
            if (File.Exists(file))
            {
                string[] lines = File.ReadAllLines(file);
                if (localLine >= 1 && localLine <= lines.Length) sourceLine = lines[localLine - 1];
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new OtterError(message, localLine, error.Stage, error.Column, sourceLine, error.Suggestion, error.Code);
    }
}

public static class Modules
{
    // PowerShell's -match ignores case: `USE "x.ot"` is a use line too.
    static readonly Regex UseLine = new(@"^\s*use\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly char[] Separators = { '\\', '/' };

    sealed class Context
    {
        // Ordinal: module identity is the file's exact on-disk path (M1).
        public readonly HashSet<string> Loaded = new(StringComparer.Ordinal);
        public readonly List<string> Stack = new();
        public readonly List<(string, int)> Origins = new();
    }

    // Resolve-OtterModuleSource. Throws OtterError for every problem otterPS
    // reports; the caller shows it as a check-stage failure (exit code 2).
    public static ResolvedProgram Resolve(string rootFile)
    {
        var context = new Context();
        string combined = ResolveFile(rootFile, context);
        return new ResolvedProgram(combined, context.Origins, rootFile);
    }

    // Get-OtterCanonicalPath: the full path, without a trailing separator.
    public static string CanonicalPath(string path)
    {
        string full = Path.GetFullPath(path);
        string trimmed = full.TrimEnd('\\', '/');
        return trimmed.Length == 0 ? full : trimmed;
    }

    static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    static string ResolveFile(string filePath, Context context)
    {
        string canonical = CanonicalPath(filePath);
        if (!PathExists(canonical))
        {
            throw new OtterError($"Cannot find Otter source file \"{filePath}\".", 0, "parser");
        }
        canonical = OnDiskPath(canonical);

        if (context.Stack.Contains(canonical, StringComparer.Ordinal))
        {
            var cycle = new List<string>(context.Stack) { canonical };
            throw new OtterError("Circular import detected: " + string.Join(" -> ", cycle.Select(Path.GetFileName)), 0, "parser");
        }
        if (!context.Loaded.Add(canonical)) return "";
        context.Stack.Add(canonical);

        string directory = Path.GetDirectoryName(canonical) ?? canonical;
        if (Directory.Exists(canonical))
        {
            throw new OtterError($"\"{filePath}\" is a folder, not an Otter file.", 0, "parser");
        }
        string[] lines;
        try { lines = ReadLinesLikeGetContent(canonical); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new OtterError($"I could not read \"{filePath}\": {FileAccessReason(e)}.", 0, "parser");
        }

        var expanded = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            int localLine = i + 1;
            string line = lines[i];
            Match use = UseLine.Match(line);
            if (!use.Success)
            {
                expanded.Add(line);
                context.Origins.Add((canonical, localLine));
                continue;
            }
            string importRelative = use.Groups[1].Value;
            // [System.IO.Path]::Combine: an absolute path replaces the folder.
            string importTarget = Path.Combine(directory, importRelative);
            string canonicalImport = CanonicalPath(importTarget);

            // Case first, so a name that differs only in capitals gets the
            // same diagnostic on every host.
            AssertPathCase(directory, importRelative, localLine, line);
            if (!PathExists(canonicalImport))
            {
                throw new OtterError($"Cannot find imported Otter file \"{importRelative}\" at \"{importTarget}\".", localLine, "parser", 1, line,
                    $"Check that \"{importRelative}\" exists in \"{directory}\".");
            }

            expanded.Add($"# --- imported from {importRelative} ---");
            context.Origins.Add((canonical, localLine));
            string imported = ResolveFile(canonicalImport, context);
            if (imported.Length > 0) expanded.Add(imported);
            expanded.Add($"# --- end import {importRelative} ---");
            context.Origins.Add((canonical, localLine));
        }
        context.Stack.RemoveAt(context.Stack.Count - 1);
        return string.Join("\n", expanded);
    }

    // Get-OtterFileAccessReason.
    internal static string FileAccessReason(Exception exception)
    {
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return "permission denied";
            if (e.InnerException == null)
            {
                if (e is FileNotFoundException or DirectoryNotFoundException) return "the file does not exist";
                return e.Message.TrimEnd('.');
            }
        }
        return exception.Message.TrimEnd('.');
    }

    // What Get-Content -Encoding UTF8 returns: the lines, a byte order mark
    // dropped, and no empty line for the final line ending.
    public static string[] ReadLinesLikeGetContent(string path)
    {
        string text = File.ReadAllText(path, new UTF8Encoding(false));
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        if (text.Length == 0) return Array.Empty<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines.ToArray();
    }

    static string[] EntryNames(string? directory)
    {
        if (directory == null) return Array.Empty<string>();
        try { return Directory.GetFileSystemEntries(directory).Select(e => Path.GetFileName(e)).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return Array.Empty<string>(); }
    }

    // Assert-OtterModulePathCase (M1, D122): every name in a use path must be
    // spelled as it is on disk, capitals included.
    static void AssertPathCase(string baseDirectory, string importPath, int line, string sourceLine)
    {
        string? current = baseDirectory;
        string walkPath = importPath;
        string rootPrefix = "";
        if (Path.IsPathRooted(importPath))
        {
            string? pathRoot = Path.GetPathRoot(importPath);
            if (!string.IsNullOrEmpty(pathRoot))
            {
                current = pathRoot;
                walkPath = importPath.Substring(pathRoot.Length);
                rootPrefix = pathRoot.Replace('\\', '/');
                if (!rootPrefix.EndsWith('/')) rootPrefix += "/";
            }
        }
        string[] segments = walkPath.Split(Separators).Where(s => s.Length > 0).ToArray();
        var written = new List<string>();
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            if (segment == ".") { written.Add(segment); continue; }
            if (segment == "..") { written.Add(segment); current = current == null ? null : Path.GetDirectoryName(current); continue; }
            string[] names = EntryNames(current);
            if (names.Contains(segment, StringComparer.Ordinal))
            {
                written.Add(segment);
                current = Path.Combine(current!, segment);
                continue;
            }
            string? actual = names.FirstOrDefault(n => string.Equals(n, segment, StringComparison.OrdinalIgnoreCase));
            if (actual != null)
            {
                string kind = i == segments.Length - 1 ? "file" : "folder";
                var corrected = written.Concat(new[] { actual }).Concat(segments.Skip(i + 1));
                throw new OtterError(
                    $"The {kind} is named \"{actual}\", but this use says \"{segment}\". Module paths must match file and folder names exactly, including capital letters.",
                    line, "parser", 1, sourceLine,
                    $"use \"{rootPrefix}{string.Join("/", corrected)}\"");
            }
            // A Windows 8.3 short name ("RUNNER~1") is in no listing but exists.
            if (current != null && PathExists(Path.Combine(current, segment)))
            {
                written.Add(segment);
                current = Path.Combine(current, segment);
                continue;
            }
            return;   // not found at all: the caller's "Cannot find" applies
        }
    }

    // Get-OtterOnDiskPath: every name spelled as it is on disk.
    static string OnDiskPath(string fullPath)
    {
        string full = Path.GetFullPath(fullPath).TrimEnd('\\', '/');
        string root = Path.GetPathRoot(full) ?? "";
        if (full.Length < root.Length) full = root;
        string current = root;
        foreach (string segment in full.Substring(root.Length).Split(Separators).Where(s => s.Length > 0))
        {
            string[] names = EntryNames(current);
            string? match = names.Contains(segment, StringComparer.Ordinal)
                ? segment
                : names.FirstOrDefault(n => string.Equals(n, segment, StringComparison.OrdinalIgnoreCase));
            current = Path.Combine(current, match ?? segment);
        }
        return current;
    }
}
