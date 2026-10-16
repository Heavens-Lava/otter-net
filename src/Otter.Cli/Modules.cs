using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

// `use "helpers.ot"`, resolved at the source-text level before lexing, as
// otterPS's src/Otter.Module.psm1 does: each use line becomes a header
// comment, the imported file's text, and a footer comment; a file imported
// twice is included once; every line of the combined source remembers its
// file and line, so an error inside an imported file says where it really is.
//
// Anything unusual - a missing file, a path whose capital letters differ from
// the file on disk, a circular import, an unreadable file - is left to the
// PowerShell Otter, which reports it in its own words.
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
        if (!string.Equals(Path.GetFullPath(file), RootPath, StringComparison.OrdinalIgnoreCase))
        {
            string prefix = $"In \"{Path.GetFileName(file)}\": ";
            if (!message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) message = prefix + message;
        }
        string? sourceLine = error.SourceLine;
        try
        {
            string[] lines = File.ReadAllLines(file);
            if (localLine >= 1 && localLine <= lines.Length) sourceLine = lines[localLine - 1];
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return new OtterError(message, localLine, error.Stage, error.Column, sourceLine, error.Suggestion, error.Code);
    }
}

public sealed class ModuleProblem(string reason) : Exception(reason);

public static class Modules
{
    // PowerShell's -match ignores case: `USE "x.ot"` is a use line too.
    static readonly Regex UseLine = new(@"^\s*use\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ResolvedProgram Resolve(string rootFile)
    {
        string root = Path.GetFullPath(rootFile);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var stack = new List<string>();
        var origins = new List<(string, int)>();
        string combined = ResolveFile(root, loaded, stack, origins);
        return new ResolvedProgram(combined, origins, root);
    }

    static string ResolveFile(string path, HashSet<string> loaded, List<string> stack, List<(string, int)> origins)
    {
        if (!File.Exists(path)) throw new ModuleProblem($"cannot find {path}");
        if (stack.Contains(path, StringComparer.Ordinal)) throw new ModuleProblem("circular import");
        if (!loaded.Add(path)) return "";
        stack.Add(path);
        string directory = Path.GetDirectoryName(path)!;
        var expanded = new List<string>();
        string[] lines = ReadLinesLikeGetContent(path);
        for (int i = 0; i < lines.Length; i++)
        {
            Match use = UseLine.Match(lines[i]);
            if (!use.Success)
            {
                expanded.Add(lines[i]);
                origins.Add((path, i + 1));
                continue;
            }
            string relative = use.Groups[1].Value;
            if (Path.IsPathRooted(relative)) throw new ModuleProblem("an absolute use path");
            string target = Path.GetFullPath(Path.Combine(directory, relative));
            if (!PathMatchesDiskExactly(directory, relative)) throw new ModuleProblem($"use \"{relative}\" (missing, or capital letters differ from the file on disk)");
            expanded.Add($"# --- imported from {relative} ---");
            origins.Add((path, i + 1));
            string imported = ResolveFile(target, loaded, stack, origins);
            if (imported.Length > 0) expanded.Add(imported);
            expanded.Add($"# --- end import {relative} ---");
            origins.Add((path, i + 1));
        }
        stack.RemoveAt(stack.Count - 1);
        return string.Join("\n", expanded);
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

    // M1: every name in a use path must match the disk exactly, capitals
    // included. Anything otterPS would complain about returns false.
    static bool PathMatchesDiskExactly(string baseDirectory, string relative)
    {
        string current = baseDirectory;
        foreach (string segment in relative.Split('\\', '/').Where(s => s.Length > 0))
        {
            if (segment == ".") continue;
            if (segment == "..") { current = Path.GetDirectoryName(current) ?? current; continue; }
            string[] names;
            try { names = Directory.GetFileSystemEntries(current).Select(Path.GetFileName).Select(n => n!).ToArray(); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            if (!names.Contains(segment, StringComparer.Ordinal)) return false;
            current = Path.Combine(current, segment);
        }
        return File.Exists(current);
    }
}
