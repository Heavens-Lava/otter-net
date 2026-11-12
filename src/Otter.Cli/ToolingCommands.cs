using System.Text.Json;
using System.Text.Json.Nodes;
using Otter.Language;

namespace Otter.Cli;

internal static class ToolingCommands
{
    // =========================================================================
    // otter format <file.ot> [--check]
    // =========================================================================
    public static int Format(CommandLine line)
    {
        string[] raw = line.Raw;
        string? targetFile = raw.Length > 1 && !raw[1].StartsWith("-") ? raw[1] : null;
        if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
        {
            Console.Error.WriteLine("Error: Please specify a valid Otter file to format. Example: otter format main.ot");
            return Cli.ExitUsageError;
        }

        bool checkOnly = raw.Any(a => string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase));
        string original = File.ReadAllText(targetFile);
        string formatted = LanguageService.FormatDocument(original);

        if (checkOnly)
        {
            if (original == formatted)
            {
                Console.WriteLine($"[FORMATTED] {targetFile}");
                return Cli.ExitSuccess;
            }
            else
            {
                Console.Error.WriteLine($"[NEEDS FORMATTING] {targetFile}");
                return Cli.ExitCheckError;
            }
        }

        if (original != formatted)
        {
            File.WriteAllText(targetFile, formatted);
            Console.WriteLine($"Formatted {targetFile}.");
        }
        else
        {
            Console.WriteLine($"{targetFile} is already cleanly formatted.");
        }
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // otter complete <file.ot> <line> <column> [--json]
    // =========================================================================
    public static int Complete(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 4)
        {
            Console.Error.WriteLine("Usage: otter complete <file.ot> <line> <column> [--json]");
            return Cli.ExitUsageError;
        }

        string file = raw[1];
        if (!int.TryParse(raw[2], out int lineNum) || !int.TryParse(raw[3], out int colNum))
        {
            Console.Error.WriteLine("Error: Line and column must be integers.");
            return Cli.ExitUsageError;
        }

        string source = File.Exists(file) ? File.ReadAllText(file) : "";
        string? libDir = null;
        try { libDir = LibraryCommands.FindLibraryDir(); } catch { }

        var completions = LanguageService.GetCompletions(source, lineNum, colNum, file, libDir);
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        if (json)
        {
            var arr = new JsonArray();
            foreach (var c in completions)
            {
                arr.Add(new JsonObject
                {
                    ["label"] = c.Label,
                    ["kind"] = c.Kind,
                    ["detail"] = c.Detail,
                    ["insertText"] = c.InsertText,
                    ["documentation"] = c.Documentation
                });
            }
            Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        Console.WriteLine($"Completions at {file}:{lineNum}:{colNum} ({completions.Count} items):");
        foreach (var c in completions.Take(25))
        {
            Console.WriteLine($"  [{c.Kind,-9}] {c.Label,-18} - {c.Detail}");
        }
        if (completions.Count > 25)
        {
            Console.WriteLine($"  ... and {completions.Count - 25} more items");
        }
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // otter definition <file.ot> <line> <column> [--json]
    // =========================================================================
    public static int Definition(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 4)
        {
            Console.Error.WriteLine("Usage: otter definition <file.ot> <line> <column> [--json]");
            return Cli.ExitUsageError;
        }

        string file = raw[1];
        if (!int.TryParse(raw[2], out int lineNum) || !int.TryParse(raw[3], out int colNum))
        {
            Console.Error.WriteLine("Error: Line and column must be integers.");
            return Cli.ExitUsageError;
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File '{file}' does not exist.");
            return Cli.ExitUsageError;
        }

        string source = File.ReadAllText(file);
        string? libDir = null;
        try { libDir = LibraryCommands.FindLibraryDir(); } catch { }

        var def = LanguageService.GetDefinition(source, lineNum, colNum, file, libDir);
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        if (def == null)
        {
            if (json) Console.WriteLine("null");
            else Console.WriteLine("No definition found at cursor.");
            return Cli.ExitSuccess;
        }

        if (json)
        {
            var obj = new JsonObject
            {
                ["symbol"] = def.Symbol,
                ["kind"] = def.Kind,
                ["file"] = def.File,
                ["line"] = def.Line,
                ["column"] = def.Column
            };
            Console.WriteLine(obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        Console.WriteLine($"Definition for {def.Kind.ToLowerInvariant()} '{def.Symbol}':");
        Console.WriteLine($"  {def.File}:{def.Line}:{def.Column}");
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // otter references <file.ot> <line> <column> [--json]
    // =========================================================================
    public static int References(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 4)
        {
            Console.Error.WriteLine("Usage: otter references <file.ot> <line> <column> [--json]");
            return Cli.ExitUsageError;
        }

        string file = raw[1];
        if (!int.TryParse(raw[2], out int lineNum) || !int.TryParse(raw[3], out int colNum))
        {
            Console.Error.WriteLine("Error: Line and column must be integers.");
            return Cli.ExitUsageError;
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File '{file}' does not exist.");
            return Cli.ExitUsageError;
        }

        string source = File.ReadAllText(file);
        var refs = LanguageService.FindReferences(source, lineNum, colNum, file);
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        if (json)
        {
            var arr = new JsonArray();
            foreach (var r in refs)
            {
                arr.Add(new JsonObject
                {
                    ["file"] = r.File,
                    ["line"] = r.Line,
                    ["column"] = r.Column,
                    ["lineText"] = r.LineText
                });
            }
            Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        Console.WriteLine($"Found {refs.Count} reference(s):");
        foreach (var r in refs)
        {
            Console.WriteLine($"  {r.File}:{r.Line}:{r.Column} -> {r.LineText}");
        }
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // otter rename <file.ot> <line> <column> <newName> [--dry-run]
    // =========================================================================
    public static int Rename(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 5)
        {
            Console.Error.WriteLine("Usage: otter rename <file.ot> <line> <column> <new-name> [--dry-run]");
            return Cli.ExitUsageError;
        }

        string file = raw[1];
        if (!int.TryParse(raw[2], out int lineNum) || !int.TryParse(raw[3], out int colNum))
        {
            Console.Error.WriteLine("Error: Line and column must be integers.");
            return Cli.ExitUsageError;
        }
        string newName = raw[4];
        bool dryRun = raw.Any(a => string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase));

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File '{file}' does not exist.");
            return Cli.ExitUsageError;
        }

        string source = File.ReadAllText(file);
        var (success, newSource, count, error) = LanguageService.RenameSymbol(source, lineNum, colNum, newName);

        if (!success)
        {
            Console.Error.WriteLine($"Rename Error: {error}");
            return Cli.ExitCheckError;
        }

        if (dryRun)
        {
            Console.WriteLine($"[Dry Run] Successfully renamed {count} occurrence(s) in memory. No files modified.");
            return Cli.ExitSuccess;
        }

        File.WriteAllText(file, newSource);
        Console.WriteLine($"Successfully renamed {count} occurrence(s) in '{file}'.");
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // otter doc <query> [--json]
    // =========================================================================
    public static int Doc(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 2 || raw[1].StartsWith("-"))
        {
            Console.Error.WriteLine("Usage: otter doc <symbol-or-query> [--json]");
            return Cli.ExitUsageError;
        }

        string query = raw[1];
        string? libDir = null;
        try { libDir = LibraryCommands.FindLibraryDir(); } catch { }

        var docs = LanguageService.LookupDocumentation(query, libDir);
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        if (json)
        {
            var arr = new JsonArray();
            foreach (var d in docs)
            {
                arr.Add(new JsonObject
                {
                    ["name"] = d.Name,
                    ["kind"] = d.Kind,
                    ["signature"] = d.Signature,
                    ["description"] = d.Description,
                    ["example"] = d.Example,
                    ["source"] = d.Source
                });
            }
            Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        if (docs.Count == 0)
        {
            Console.WriteLine($"No documentation found matching '{query}'.");
            return Cli.ExitSuccess;
        }

        Console.WriteLine($"=== Documentation for '{query}' ({docs.Count} result(s)) ===");
        foreach (var d in docs)
        {
            Console.WriteLine();
            Console.WriteLine($"[{d.Kind}] {d.Name} ({d.Source})");
            Console.WriteLine($"  Description: {d.Description}");
            if (!string.IsNullOrEmpty(d.Example))
            {
                Console.WriteLine("  Example:");
                string[] exLines = d.Example.Split('\n');
                foreach (var el in exLines) Console.WriteLine($"    {el}");
            }
        }
        return Cli.ExitSuccess;
    }
}
