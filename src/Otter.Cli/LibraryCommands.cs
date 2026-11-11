using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Otter.Cli;

// LibraryCommands implements the CLI-first, IDE-independent Otter Reusable
// Library & Blueprints specification (Section 15). It provides discovery,
// inspection, insertion, pack installation, integrity verification, and
// deterministic Blueprint planning and code generation.
internal static class LibraryCommands
{
    public const int ExitSuccess = 0;
    public const int ExitUsageError = 1;
    public const int ExitValidationError = 2;
    public const int ExitRuntimeError = 3;

    // Resolve the library directory by checking environment, working directory,
    // and parent directories.
    public static string FindLibraryDir()
    {
        string? env = Environment.GetEnvironmentVariable("OTTER_LIBRARY_PATH");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return Path.GetFullPath(env);

        string cur = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(cur))
        {
            string candidate = Path.Combine(cur, "library");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "snippets", "catalog.json")))
            {
                return candidate;
            }
            string? parent = Directory.GetParent(cur)?.FullName;
            if (parent == cur) break;
            cur = parent!;
        }
        return Path.GetFullPath("library");
    }

    static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    static string? GetOption(string[] args, string option)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            if (args[i].StartsWith(option + "=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i].Substring(option.Length + 1);
            }
        }
        return null;
    }

    // =========================================================================
    // 1. otter library <search|list>
    // =========================================================================
    public static int RunLibrary(CommandLine line)
    {
        string[] raw = line.Raw;
        string subCommand = raw.Length > 1 ? raw[1] : "help";
        bool json = HasFlag(raw, "--json");

        if (string.Equals(subCommand, "search", StringComparison.OrdinalIgnoreCase))
        {
            string query = raw.Length > 2 && !raw[2].StartsWith("-") ? raw[2] : "";
            string? kindFilter = GetOption(raw, "--kind");
            return SearchCatalog(query, kindFilter, json);
        }
        if (string.Equals(subCommand, "list", StringComparison.OrdinalIgnoreCase))
        {
            bool installed = HasFlag(raw, "--installed");
            return ListCatalog(installed, json);
        }

        Console.WriteLine("Usage: otter library <command> [options]");
        Console.WriteLine("Commands:");
        Console.WriteLine("  search <query> [--kind snippet|pack|blueprint] [--json]");
        Console.WriteLine("  list [--installed] [--json]");
        return ExitUsageError;
    }

    // =========================================================================
    // 2. otter snippet <show|insert>
    // =========================================================================
    public static int RunSnippet(CommandLine line)
    {
        string[] raw = line.Raw;
        string subCommand = raw.Length > 1 ? raw[1] : "help";
        bool json = HasFlag(raw, "--json");

        if (string.Equals(subCommand, "show", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: snippet id required. Example: otter snippet show cf-for-each");
                return ExitUsageError;
            }
            return ShowSnippet(raw[2], json);
        }
        if (string.Equals(subCommand, "insert", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: snippet id required. Example: otter snippet insert cf-for-each --file main.ot");
                return ExitUsageError;
            }
            string snippetId = raw[2];
            string? targetFile = GetOption(raw, "--file");
            string? atLineStr = GetOption(raw, "--at-line");
            int atLine = atLineStr != null && int.TryParse(atLineStr, out int l) ? l : -1;
            bool dryRun = HasFlag(raw, "--dry-run");
            return InsertSnippet(snippetId, targetFile, atLine, dryRun, json);
        }

        Console.WriteLine("Usage: otter snippet <command> [options]");
        Console.WriteLine("Commands:");
        Console.WriteLine("  show <id> [--json]");
        Console.WriteLine("  insert <id> [--file <path>] [--at-line <n>] [--dry-run] [--json]");
        return ExitUsageError;
    }

    // =========================================================================
    // 3. otter pack <inspect|install|list|verify|uninstall>
    // =========================================================================
    public static int RunPack(CommandLine line)
    {
        string[] raw = line.Raw;
        string subCommand = raw.Length > 1 ? raw[1] : "help";
        bool json = HasFlag(raw, "--json");

        if (string.Equals(subCommand, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: pack id required. Example: otter pack inspect otter-official/math-utils");
                return ExitUsageError;
            }
            return InspectPack(raw[2], json);
        }
        if (string.Equals(subCommand, "list", StringComparison.OrdinalIgnoreCase))
        {
            return ListPacks(json);
        }
        if (string.Equals(subCommand, "install", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: pack id required. Example: otter pack install otter-official/math-utils");
                return ExitUsageError;
            }
            string packId = raw[2];
            string? version = GetOption(raw, "--version");
            string projectDir = GetOption(raw, "--project") ?? Directory.GetCurrentDirectory();
            bool dryRun = HasFlag(raw, "--dry-run");
            return InstallPack(packId, version, projectDir, dryRun, json);
        }
        if (string.Equals(subCommand, "verify", StringComparison.OrdinalIgnoreCase))
        {
            string projectDir = GetOption(raw, "--project") ?? Directory.GetCurrentDirectory();
            return VerifyPacks(projectDir, json);
        }
        if (string.Equals(subCommand, "uninstall", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: pack id required. Example: otter pack uninstall otter-official/math-utils");
                return ExitUsageError;
            }
            string packId = raw[2];
            string projectDir = GetOption(raw, "--project") ?? Directory.GetCurrentDirectory();
            return UninstallPack(packId, projectDir, json);
        }

        Console.WriteLine("Usage: otter pack <command> [options]");
        Console.WriteLine("Commands:");
        Console.WriteLine("  inspect <id> [--json]");
        Console.WriteLine("  install <id> [--version <v>] [--project <dir>] [--dry-run] [--offline] [--json]");
        Console.WriteLine("  list [--json]");
        Console.WriteLine("  verify [--project <dir>] [--offline] [--json]");
        Console.WriteLine("  uninstall <id> [--project <dir>] [--json]");
        return ExitUsageError;
    }

    // =========================================================================
    // 4. otter blueprint <search|inspect|explain|plan|apply>
    // =========================================================================
    public static int RunBlueprint(CommandLine line)
    {
        string[] raw = line.Raw;
        string subCommand = raw.Length > 1 ? raw[1] : "help";
        bool json = HasFlag(raw, "--json");

        if (string.Equals(subCommand, "search", StringComparison.OrdinalIgnoreCase))
        {
            string query = raw.Length > 2 && !raw[2].StartsWith("-") ? raw[2] : "";
            return SearchBlueprints(query, json);
        }
        if (string.Equals(subCommand, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: blueprint id required. Example: otter blueprint inspect otter-official/game-combat");
                return ExitUsageError;
            }
            return InspectBlueprint(raw[2], json);
        }
        if (string.Equals(subCommand, "explain", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: blueprint id required. Example: otter blueprint explain otter-official/game-combat");
                return ExitUsageError;
            }
            string level = GetOption(raw, "--level") ?? "intermediate";
            return ExplainBlueprint(raw[2], level, json);
        }
        if (string.Equals(subCommand, "plan", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: blueprint id required. Example: otter blueprint plan otter-official/game-combat");
                return ExitUsageError;
            }
            string bpId = raw[2];
            string? configFile = GetOption(raw, "--config");
            string projectDir = GetOption(raw, "--project") ?? Directory.GetCurrentDirectory();
            return PlanBlueprint(bpId, configFile, projectDir, json);
        }
        if (string.Equals(subCommand, "apply", StringComparison.OrdinalIgnoreCase))
        {
            if (raw.Length < 3 || raw[2].StartsWith("-"))
            {
                Console.Error.WriteLine("Error: blueprint id required. Example: otter blueprint apply otter-official/game-combat");
                return ExitUsageError;
            }
            string bpId = raw[2];
            string? configFile = GetOption(raw, "--config");
            string projectDir = GetOption(raw, "--project") ?? Directory.GetCurrentDirectory();
            bool dryRun = HasFlag(raw, "--dry-run");
            bool force = HasFlag(raw, "--force");
            return ApplyBlueprint(bpId, configFile, projectDir, dryRun, force, json);
        }

        Console.WriteLine("Usage: otter blueprint <command> [options]");
        Console.WriteLine("Commands:");
        Console.WriteLine("  search <query> [--json]");
        Console.WriteLine("  inspect <id> [--json]");
        Console.WriteLine("  explain <id> [--level beginner|intermediate|advanced] [--json]");
        Console.WriteLine("  plan <id> [--config <file>] [--project <dir>] [--json]");
        Console.WriteLine("  apply <id> [--config <file>] [--project <dir>] [--dry-run] [--force] [--json]");
        return ExitUsageError;
    }

    // =========================================================================
    // Core Implementation Methods
    // =========================================================================

    static JsonNode? LoadCatalogJson()
    {
        string lib = FindLibraryDir();
        string catalogPath = Path.Combine(lib, "snippets", "catalog.json");
        if (!File.Exists(catalogPath)) return null;
        return JsonNode.Parse(File.ReadAllText(catalogPath));
    }

    static int SearchCatalog(string query, string? kindFilter, bool json)
    {
        var results = new List<JsonObject>();
        string[] tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Snippets
        var catalog = LoadCatalogJson();
        if (catalog != null && (kindFilter == null || kindFilter == "snippet"))
        {
            var snips = catalog["snippets"]?.AsArray();
            if (snips != null)
            {
                foreach (var s in snips)
                {
                    if (s == null) continue;
                    string id = s["id"]?.ToString() ?? "";
                    string title = s["title"]?.ToString() ?? "";
                    string desc = s["description"]?.ToString() ?? "";
                    string cat = s["category"]?.ToString() ?? "";
                    string haystack = $"{id} {title} {desc} {cat}".ToLowerInvariant();
                    if (tokens.Length == 0 || tokens.All(t => haystack.Contains(t)))
                    {
                        var obj = new JsonObject
                        {
                            ["kind"] = "snippet",
                            ["id"] = id,
                            ["title"] = title,
                            ["category"] = cat,
                            ["description"] = desc
                        };
                        results.Add(obj);
                    }
                }
            }
        }

        // Packs
        if (kindFilter == null || kindFilter == "pack")
        {
            string lib = FindLibraryDir();
            string packsDir = Path.Combine(lib, "packs");
            if (Directory.Exists(packsDir))
            {
                foreach (var mf in Directory.GetFiles(packsDir, "otter.pack.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var node = JsonNode.Parse(File.ReadAllText(mf));
                        if (node == null) continue;
                        string pub = node["publisher"]?.ToString() ?? "";
                        string name = node["name"]?.ToString() ?? "";
                        string id = $"{pub}/{name}";
                        string desc = node["description"]?.ToString() ?? "";
                        string ver = node["version"]?.ToString() ?? "";
                        string haystack = $"{id} {desc} {pub} {name}".ToLowerInvariant();
                        if (tokens.Length == 0 || tokens.All(t => haystack.Contains(t)))
                        {
                            results.Add(new JsonObject
                            {
                                ["kind"] = "pack",
                                ["id"] = id,
                                ["version"] = ver,
                                ["title"] = name,
                                ["description"] = desc
                            });
                        }
                    }
                    catch { }
                }
            }
        }

        // Blueprints
        if (kindFilter == null || kindFilter == "blueprint")
        {
            string lib = FindLibraryDir();
            string bpDir = Path.Combine(lib, "blueprints");
            if (Directory.Exists(bpDir))
            {
                foreach (var mf in Directory.GetFiles(bpDir, "otter.blueprint.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var node = JsonNode.Parse(File.ReadAllText(mf));
                        if (node == null) continue;
                        string id = node["id"]?.ToString() ?? "";
                        string title = node["title"]?.ToString() ?? "";
                        string summary = node["summary"]?.ToString() ?? "";
                        string ver = node["version"]?.ToString() ?? "";
                        string haystack = $"{id} {title} {summary}".ToLowerInvariant();
                        if (tokens.Length == 0 || tokens.All(t => haystack.Contains(t)))
                        {
                            results.Add(new JsonObject
                            {
                                ["kind"] = "blueprint",
                                ["id"] = id,
                                ["version"] = ver,
                                ["title"] = title,
                                ["description"] = summary
                            });
                        }
                    }
                    catch { }
                }
            }
        }

        if (json)
        {
            var arr = new JsonArray();
            foreach (var r in results) arr.Add(r);
            Console.WriteLine(arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return ExitSuccess;
        }

        Console.WriteLine($"Found {results.Count} items matching '{query}':");
        Console.WriteLine();
        foreach (var r in results)
        {
            Console.WriteLine($"[{r["kind"]}] {r["id"]} - {r["title"]}");
            Console.WriteLine($"  {r["description"]}");
        }
        return ExitSuccess;
    }

    static int ListCatalog(bool installedOnly, bool json)
    {
        if (installedOnly)
        {
            string lockPath = Path.Combine(Directory.GetCurrentDirectory(), "otter.lock");
            if (!File.Exists(lockPath))
            {
                if (json) Console.WriteLine("[]");
                else Console.WriteLine("No otter.lock found in current directory. No packs installed.");
                return ExitSuccess;
            }
            var node = JsonNode.Parse(File.ReadAllText(lockPath));
            if (json)
            {
                Console.WriteLine(node?["packages"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]");
                return ExitSuccess;
            }
            Console.WriteLine("Installed packages in current project:");
            var pkgs = node?["packages"]?.AsObject();
            if (pkgs != null)
            {
                foreach (var kvp in pkgs)
                {
                    Console.WriteLine($"  {kvp.Key} v{kvp.Value?["version"]}");
                }
            }
            return ExitSuccess;
        }

        return SearchCatalog("", null, json);
    }

    static int ShowSnippet(string id, bool json)
    {
        var catalog = LoadCatalogJson();
        if (catalog == null)
        {
            Console.Error.WriteLine("Error: snippet catalog not found.");
            return ExitRuntimeError;
        }
        var snips = catalog["snippets"]?.AsArray();
        var s = snips?.FirstOrDefault(x => string.Equals(x?["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase));
        if (s == null)
        {
            Console.Error.WriteLine($"Error: snippet '{id}' not found.");
            return ExitUsageError;
        }

        if (json)
        {
            Console.WriteLine(s.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return ExitSuccess;
        }

        Console.WriteLine($"Snippet: {s["id"]} - {s["title"]}");
        Console.WriteLine($"Category: {s["category"]}");
        Console.WriteLine($"Description: {s["description"]}");
        Console.WriteLine();
        Console.WriteLine("Template:");
        Console.WriteLine(s["template"]?.ToString());
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine(s["example"]?.ToString());
        return ExitSuccess;
    }

    static int InsertSnippet(string id, string? targetFile, int atLine, bool dryRun, bool json)
    {
        var catalog = LoadCatalogJson();
        var snips = catalog?["snippets"]?.AsArray();
        var s = snips?.FirstOrDefault(x => string.Equals(x?["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase));
        if (s == null)
        {
            Console.Error.WriteLine($"Error: snippet '{id}' not found.");
            return ExitUsageError;
        }

        string template = s["template"]?.ToString() ?? "";

        if (string.IsNullOrEmpty(targetFile))
        {
            if (json)
            {
                var res = new JsonObject { ["snippetId"] = id, ["insertedCode"] = template };
                Console.WriteLine(res.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine(template);
            }
            return ExitSuccess;
        }

        string fullPath = Path.GetFullPath(targetFile);
        if (dryRun)
        {
            if (json)
            {
                var plan = new JsonObject
                {
                    ["dryRun"] = true,
                    ["targetFile"] = fullPath,
                    ["snippetId"] = id,
                    ["action"] = File.Exists(fullPath) ? "insert" : "create",
                    ["line"] = atLine,
                    ["content"] = template
                };
                Console.WriteLine(plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"[Dry Run] Would insert snippet '{id}' into '{targetFile}' at line {(atLine > 0 ? atLine : "end")}:");
                Console.WriteLine(template);
            }
            return ExitSuccess;
        }

        if (!File.Exists(fullPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, template + Environment.NewLine, new UTF8Encoding(false));
        }
        else
        {
            var lines = File.ReadAllLines(fullPath).ToList();
            if (atLine > 0 && atLine <= lines.Count)
            {
                lines.Insert(atLine - 1, template);
            }
            else
            {
                lines.Add("");
                lines.Add(template);
            }
            File.WriteAllLines(fullPath, lines, new UTF8Encoding(false));
        }

        if (json)
        {
            Console.WriteLine(new JsonObject { ["status"] = "success", ["file"] = fullPath, ["snippet"] = id }.ToJsonString());
        }
        else
        {
            Console.WriteLine($"Successfully inserted snippet '{id}' into '{targetFile}'.");
        }
        return ExitSuccess;
    }

    static string? FindPackManifest(string packId)
    {
        string lib = FindLibraryDir();
        string candidate = Path.Combine(lib, "packs", packId, "otter.pack.json");
        if (File.Exists(candidate)) return candidate;

        // Try searching inside packs/
        string packsDir = Path.Combine(lib, "packs");
        if (Directory.Exists(packsDir))
        {
            foreach (var mf in Directory.GetFiles(packsDir, "otter.pack.json", SearchOption.AllDirectories))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(mf));
                    string pub = node?["publisher"]?.ToString() ?? "";
                    string name = node?["name"]?.ToString() ?? "";
                    if (string.Equals($"{pub}/{name}", packId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, packId, StringComparison.OrdinalIgnoreCase))
                    {
                        return mf;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    static int InspectPack(string packId, bool json)
    {
        string? manifestPath = FindPackManifest(packId);
        if (manifestPath == null)
        {
            Console.Error.WriteLine($"Error: pack '{packId}' not found.");
            return ExitUsageError;
        }
        string content = File.ReadAllText(manifestPath);
        if (json)
        {
            Console.WriteLine(content);
            return ExitSuccess;
        }
        var node = JsonNode.Parse(content);
        Console.WriteLine($"Pack: {node?["publisher"]}/{node?["name"]} v{node?["version"]}");
        Console.WriteLine($"Description: {node?["description"]}");
        Console.WriteLine($"License: {node?["license"]}");
        Console.WriteLine($"Entry point: {node?["entry"]}");
        Console.WriteLine($"Platforms: {node?["platforms"]}");
        return ExitSuccess;
    }

    static int ListPacks(bool json)
    {
        string lib = FindLibraryDir();
        string packsDir = Path.Combine(lib, "packs");
        var list = new JsonArray();
        if (Directory.Exists(packsDir))
        {
            foreach (var mf in Directory.GetFiles(packsDir, "otter.pack.json", SearchOption.AllDirectories))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(mf));
                    if (node != null) list.Add(node);
                }
                catch { }
            }
        }

        if (json)
        {
            Console.WriteLine(list.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return ExitSuccess;
        }

        Console.WriteLine($"Available official source packs ({list.Count}):");
        foreach (var item in list)
        {
            Console.WriteLine($"  {item?["publisher"]}/{item?["name"]} v{item?["version"]} - {item?["description"]}");
        }
        return ExitSuccess;
    }

    static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    static int InstallPack(string packId, string? reqVersion, string projectDir, bool dryRun, bool json)
    {
        string? manifestPath = FindPackManifest(packId);
        if (manifestPath == null)
        {
            Console.Error.WriteLine($"Error: pack '{packId}' not found in library.");
            return ExitUsageError;
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        string publisher = manifest["publisher"]?.ToString() ?? "otter-official";
        string name = manifest["name"]?.ToString() ?? packId;
        string version = manifest["version"]?.ToString() ?? "1.0.0";
        string entryRel = manifest["entry"]?.ToString() ?? "src/main.ot";
        string packRoot = Path.GetDirectoryName(manifestPath)!;

        // Path traversal guard
        if (entryRel.Contains("..") || entryRel.StartsWith("/") || entryRel.StartsWith("\\"))
        {
            Console.Error.WriteLine("Error: Security violation: pack entry point contains illegal traversal characters.");
            return ExitValidationError;
        }

        string sourceEntryFile = Path.Combine(packRoot, entryRel);
        if (!File.Exists(sourceEntryFile))
        {
            Console.Error.WriteLine($"Error: Entry file '{entryRel}' not found in pack source.");
            return ExitRuntimeError;
        }

        string targetPackDir = Path.Combine(projectDir, ".otter", "packages", publisher, name, version);
        string targetEntryFile = Path.Combine(targetPackDir, entryRel);
        string sha256 = ComputeSha256(sourceEntryFile);

        if (dryRun)
        {
            if (json)
            {
                var plan = new JsonObject
                {
                    ["dryRun"] = true,
                    ["pack"] = $"{publisher}/{name}",
                    ["version"] = version,
                    ["targetDir"] = targetPackDir,
                    ["entryFile"] = targetEntryFile,
                    ["sha256"] = sha256
                };
                Console.WriteLine(plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"[Dry Run] Would install {publisher}/{name} v{version} to {targetPackDir}");
                Console.WriteLine($"[Dry Run] SHA-256 digest: {sha256}");
            }
            return ExitSuccess;
        }

        // Install files
        Directory.CreateDirectory(targetPackDir);
        File.Copy(manifestPath, Path.Combine(targetPackDir, "otter.pack.json"), true);

        // Copy src directory
        string srcDir = Path.Combine(packRoot, "src");
        string targetSrcDir = Path.Combine(targetPackDir, "src");
        if (Directory.Exists(srcDir))
        {
            Directory.CreateDirectory(targetSrcDir);
            foreach (var f in Directory.GetFiles(srcDir, "*.ot", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(srcDir, f);
                string dest = Path.Combine(targetSrcDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, true);
            }
        }

        // Update otter.lock
        string lockPath = Path.Combine(projectDir, "otter.lock");
        JsonObject lockObj;
        if (File.Exists(lockPath))
        {
            try { lockObj = JsonNode.Parse(File.ReadAllText(lockPath))?.AsObject() ?? new JsonObject(); }
            catch { lockObj = new JsonObject(); }
        }
        else
        {
            lockObj = new JsonObject
            {
                ["lockfileVersion"] = 1,
                ["generatedBy"] = "otter-cli 1.0.1",
                ["packages"] = new JsonObject()
            };
        }

        if (lockObj["packages"] == null) lockObj["packages"] = new JsonObject();
        var pkgs = lockObj["packages"]!.AsObject();
        pkgs[$"{publisher}/{name}"] = new JsonObject
        {
            ["version"] = version,
            ["entry"] = entryRel,
            ["sha256"] = sha256,
            ["installedAt"] = DateTime.UtcNow.ToString("o")
        };

        File.WriteAllText(lockPath, lockObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

        if (json)
        {
            Console.WriteLine(new JsonObject
            {
                ["status"] = "installed",
                ["pack"] = $"{publisher}/{name}",
                ["version"] = version,
                ["targetDir"] = targetPackDir,
                ["sha256"] = sha256
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Successfully installed {publisher}/{name} v{version}");
            Console.WriteLine($"  Installed location: {targetPackDir}");
            Console.WriteLine($"  Entry point: {entryRel} (SHA-256: {sha256})");
            Console.WriteLine($"  Pinned in otter.lock");
        }
        return ExitSuccess;
    }

    static int VerifyPacks(string projectDir, bool json)
    {
        string lockPath = Path.Combine(projectDir, "otter.lock");
        if (!File.Exists(lockPath))
        {
            Console.Error.WriteLine("Error: No otter.lock found in project.");
            return ExitUsageError;
        }

        var lockObj = JsonNode.Parse(File.ReadAllText(lockPath))?.AsObject();
        var pkgs = lockObj?["packages"]?.AsObject();
        if (pkgs == null || pkgs.Count == 0)
        {
            if (json) Console.WriteLine("[]");
            else Console.WriteLine("No packages pinned in otter.lock.");
            return ExitSuccess;
        }

        var results = new JsonArray();
        bool allOk = true;

        foreach (var kvp in pkgs)
        {
            string packId = kvp.Key;
            string ver = kvp.Value?["version"]?.ToString() ?? "";
            string entry = kvp.Value?["entry"]?.ToString() ?? "src/main.ot";
            string expectedHash = kvp.Value?["sha256"]?.ToString() ?? "";

            string[] parts = packId.Split('/');
            string pub = parts.Length > 1 ? parts[0] : "otter-official";
            string name = parts.Length > 1 ? parts[1] : parts[0];

            string onDiskEntry = Path.Combine(projectDir, ".otter", "packages", pub, name, ver, entry);
            string status;
            string actualHash = "";

            if (!File.Exists(onDiskEntry))
            {
                status = "MISSING";
                allOk = false;
            }
            else
            {
                actualHash = ComputeSha256(onDiskEntry);
                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    status = "OK";
                }
                else
                {
                    status = "CORRUPT";
                    allOk = false;
                }
            }

            var item = new JsonObject
            {
                ["pack"] = packId,
                ["version"] = ver,
                ["status"] = status,
                ["expectedHash"] = expectedHash,
                ["actualHash"] = actualHash
            };
            results.Add(item);
        }

        if (json)
        {
            Console.WriteLine(results.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return allOk ? ExitSuccess : ExitValidationError;
        }

        Console.WriteLine($"Integrity check ({results.Count} packages):");
        foreach (var item in results)
        {
            string status = item?["status"]?.ToString() ?? "";
            Console.WriteLine($"  [{status}] {item?["pack"]} v{item?["version"]}");
            if (status == "CORRUPT")
            {
                Console.WriteLine($"    Expected SHA-256: {item?["expectedHash"]}");
                Console.WriteLine($"    Actual SHA-256:   {item?["actualHash"]}");
            }
        }
        return allOk ? ExitSuccess : ExitValidationError;
    }

    static int UninstallPack(string packId, string projectDir, bool json)
    {
        string lockPath = Path.Combine(projectDir, "otter.lock");
        if (!File.Exists(lockPath))
        {
            Console.Error.WriteLine("Error: otter.lock not found.");
            return ExitUsageError;
        }

        var lockObj = JsonNode.Parse(File.ReadAllText(lockPath))?.AsObject();
        var pkgs = lockObj?["packages"]?.AsObject();
        if (pkgs == null || !pkgs.ContainsKey(packId))
        {
            Console.Error.WriteLine($"Error: Pack '{packId}' is not installed.");
            return ExitUsageError;
        }

        string ver = pkgs[packId]?["version"]?.ToString() ?? "";
        pkgs.Remove(packId);
        File.WriteAllText(lockPath, lockObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

        string[] parts = packId.Split('/');
        string pub = parts.Length > 1 ? parts[0] : "otter-official";
        string name = parts.Length > 1 ? parts[1] : parts[0];
        string packDir = Path.Combine(projectDir, ".otter", "packages", pub, name);
        if (Directory.Exists(packDir))
        {
            Directory.Delete(packDir, true);
        }

        if (json)
        {
            Console.WriteLine(new JsonObject { ["status"] = "uninstalled", ["pack"] = packId }.ToJsonString());
        }
        else
        {
            Console.WriteLine($"Uninstalled {packId} v{ver} and updated otter.lock.");
        }
        return ExitSuccess;
    }

    static string? FindBlueprintManifest(string bpId)
    {
        string lib = FindLibraryDir();
        string candidate = Path.Combine(lib, "blueprints", bpId, "otter.blueprint.json");
        if (File.Exists(candidate)) return candidate;

        string bpDir = Path.Combine(lib, "blueprints");
        if (Directory.Exists(bpDir))
        {
            foreach (var mf in Directory.GetFiles(bpDir, "otter.blueprint.json", SearchOption.AllDirectories))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(mf));
                    string id = node?["id"]?.ToString() ?? "";
                    if (string.Equals(id, bpId, StringComparison.OrdinalIgnoreCase) ||
                        id.EndsWith("/" + bpId, StringComparison.OrdinalIgnoreCase))
                    {
                        return mf;
                    }
                }
                catch { }
            }
        }
        return null;
    }

    static int SearchBlueprints(string query, bool json)
    {
        return SearchCatalog(query, "blueprint", json);
    }

    static int InspectBlueprint(string bpId, bool json)
    {
        string? manifestPath = FindBlueprintManifest(bpId);
        if (manifestPath == null)
        {
            Console.Error.WriteLine($"Error: blueprint '{bpId}' not found.");
            return ExitUsageError;
        }
        string content = File.ReadAllText(manifestPath);
        if (json)
        {
            Console.WriteLine(content);
            return ExitSuccess;
        }
        var node = JsonNode.Parse(content);
        Console.WriteLine($"Blueprint: {node?["id"]} v{node?["version"]}");
        Console.WriteLine($"Title: {node?["title"]}");
        Console.WriteLine($"Summary: {node?["summary"]}");
        Console.WriteLine($"Targets: {node?["targets"]}");
        Console.WriteLine($"Required Packs: {node?["packs"]?.AsArray()?.Count ?? 0}");
        Console.WriteLine($"Nodes: {node?["nodes"]?.AsArray()?.Count ?? 0}");
        Console.WriteLine($"Edges: {node?["edges"]?.AsArray()?.Count ?? 0}");
        return ExitSuccess;
    }

    static int ExplainBlueprint(string bpId, string level, bool json)
    {
        string? manifestPath = FindBlueprintManifest(bpId);
        if (manifestPath == null)
        {
            Console.Error.WriteLine($"Error: blueprint '{bpId}' not found.");
            return ExitUsageError;
        }
        var node = JsonNode.Parse(File.ReadAllText(manifestPath));
        var exp = node?["explanations"]?.AsObject();
        string key = level.ToLowerInvariant();
        string explanation = exp?[key]?.ToString() ?? exp?["intermediate"]?.ToString() ?? "No explanation available.";

        if (json)
        {
            var res = new JsonObject
            {
                ["blueprint"] = bpId,
                ["level"] = level,
                ["explanation"] = explanation
            };
            Console.WriteLine(res.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return ExitSuccess;
        }

        Console.WriteLine($"=== Explanation ({level}) for {bpId} ===");
        Console.WriteLine(explanation);
        return ExitSuccess;
    }

    public static JsonObject GenerateBlueprintPlan(string bpId, string? configFile, string projectDir)
    {
        projectDir = Path.GetFullPath(projectDir);
        string? manifestPath = FindBlueprintManifest(bpId);
        if (manifestPath == null)
        {
            throw new InvalidOperationException($"Blueprint '{bpId}' not found.");
        }
        var bp = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        string bpRoot = Path.GetDirectoryName(manifestPath)!;

        // Load config overrides if provided
        var configVals = new Dictionary<string, object>();
        var cfgDefs = bp["configuration"]?.AsArray();
        if (cfgDefs != null)
        {
            foreach (var c in cfgDefs)
            {
                if (c == null) continue;
                string cid = c["id"]?.ToString() ?? "";
                var def = c["default"];
                configVals[cid] = def != null ? def.ToString() : "";
            }
        }
        if (!string.IsNullOrEmpty(configFile) && File.Exists(configFile))
        {
            var custom = JsonNode.Parse(File.ReadAllText(configFile))?.AsObject();
            if (custom != null)
            {
                foreach (var kvp in custom)
                {
                    if (kvp.Value != null) configVals[kvp.Key] = kvp.Value.ToString();
                }
            }
        }

        // Generate files
        var plannedFiles = new JsonArray();
        var genFiles = bp["generatedFiles"]?.AsArray();
        if (genFiles != null)
        {
            foreach (var gf in genFiles)
            {
                if (gf == null) continue;
                string outPath = gf["path"]?.ToString() ?? "src/main.ot";
                string tmplRel = gf["template"]?.ToString() ?? "";
                string tmplPath = Path.Combine(bpRoot, tmplRel);
                if (!File.Exists(tmplPath))
                {
                    throw new FileNotFoundException($"Blueprint template '{tmplRel}' not found.");
                }
                string tmpl = File.ReadAllText(tmplPath);

                // Render configuration placeholders
                foreach (var kvp in configVals)
                {
                    tmpl = tmpl.Replace("${" + kvp.Key + "}", kvp.Value.ToString());
                }

                string fullDest = Path.Combine(projectDir, outPath);
                string fileDir = Path.GetDirectoryName(fullDest) ?? projectDir;

                // Resolve pack import paths relative to this file's destination directory (Section 15.3)
                var packsList = bp["packs"]?.AsArray();
                if (packsList != null)
                {
                    foreach (var p in packsList)
                    {
                        if (p == null) continue;
                        string pid = p["id"]?.ToString() ?? "";
                        string pver = p["version"]?.ToString() ?? "1.0.0";
                        string[] parts = pid.Split('/');
                        string pub = parts.Length > 1 ? parts[0] : "otter-official";
                        string name = parts.Length > 1 ? parts[1] : parts[0];

                        string packEntryRel = Path.Combine(".otter", "packages", pub, name, pver, "src", "main.ot");
                        string absPackEntry = Path.GetFullPath(Path.Combine(projectDir, packEntryRel));
                        string relImport = Path.GetRelativePath(fileDir, absPackEntry).Replace('\\', '/');
                        if (!relImport.StartsWith(".") && !relImport.StartsWith("/")) relImport = "./" + relImport;

                        tmpl = tmpl.Replace("${import:" + pid + "}", relImport);
                        tmpl = tmpl.Replace($"use \".otter/packages/{pub}/{name}/src/main.ot\"", $"use \"{relImport}\"");
                    }
                }

                plannedFiles.Add(new JsonObject
                {
                    ["path"] = outPath,
                    ["absolutePath"] = fullDest,
                    ["action"] = File.Exists(fullDest) ? "modify" : "create",
                    ["content"] = tmpl
                });
            }
        }

        // Dependencies
        var plannedPacks = new JsonArray();
        var packs = bp["packs"]?.AsArray();
        if (packs != null)
        {
            foreach (var p in packs)
            {
                if (p == null) continue;
                plannedPacks.Add(new JsonObject
                {
                    ["id"] = p["id"]?.ToString(),
                    ["version"] = p["version"]?.ToString()
                });
            }
        }

        var configObj = new JsonObject();
        foreach (var kvp in configVals)
        {
            configObj[kvp.Key] = JsonValue.Create(kvp.Value?.ToString() ?? "");
        }

        return new JsonObject
        {
            ["blueprintId"] = bp["id"]?.ToString(),
            ["version"] = bp["version"]?.ToString(),
            ["title"] = bp["title"]?.ToString(),
            ["plannedFiles"] = plannedFiles,
            ["dependencies"] = plannedPacks,
            ["configuration"] = configObj
        };
    }

    static int PlanBlueprint(string bpId, string? configFile, string projectDir, bool json)
    {
        try
        {
            var plan = GenerateBlueprintPlan(bpId, configFile, projectDir);
            if (json)
            {
                Console.WriteLine(plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return ExitSuccess;
            }

            Console.WriteLine($"=== Blueprint Apply Plan: {plan["blueprintId"]} v{plan["version"]} ===");
            Console.WriteLine($"Title: {plan["title"]}");
            Console.WriteLine();
            Console.WriteLine("Dependencies to install:");
            var deps = plan["dependencies"]?.AsArray();
            if (deps != null)
            {
                foreach (var d in deps) Console.WriteLine($"  + Pack: {d?["id"]} v{d?["version"]}");
            }
            Console.WriteLine();
            Console.WriteLine("Files to generate:");
            var files = plan["plannedFiles"]?.AsArray();
            if (files != null)
            {
                foreach (var f in files) Console.WriteLine($"  [{f?["action"]}] {f?["path"]}");
            }
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Plan Error: {ex.Message}");
            return ExitRuntimeError;
        }
    }

    static int ApplyBlueprint(string bpId, string? configFile, string projectDir, bool dryRun, bool force, bool json)
    {
        try
        {
            var plan = GenerateBlueprintPlan(bpId, configFile, projectDir);
            if (dryRun)
            {
                plan["dryRun"] = true;
                if (json) Console.WriteLine(plan.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                else Console.WriteLine("[Dry Run] Plan generated successfully without modifying files.");
                return ExitSuccess;
            }

            // 1. Install required packs
            var deps = plan["dependencies"]?.AsArray();
            if (deps != null)
            {
                foreach (var d in deps)
                {
                    string pid = d?["id"]?.ToString() ?? "";
                    string pver = d?["version"]?.ToString() ?? "";
                    InstallPack(pid, pver, projectDir, dryRun: false, json: false);
                }
            }

            // 2. Write generated files
            var files = plan["plannedFiles"]?.AsArray();
            if (files != null)
            {
                foreach (var f in files)
                {
                    string abs = f?["absolutePath"]?.ToString() ?? "";
                    string content = f?["content"]?.ToString() ?? "";
                    if (File.Exists(abs) && !force)
                    {
                        Console.WriteLine($"Notice: Overwriting existing file '{abs}' with blueprint code.");
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                    File.WriteAllText(abs, content, new UTF8Encoding(false));
                }
            }

            // 3. Record provenance in .otter/blueprints/instances.json
            string instDir = Path.Combine(projectDir, ".otter", "blueprints");
            Directory.CreateDirectory(instDir);
            string instPath = Path.Combine(instDir, "instances.json");
            var instances = File.Exists(instPath) ? JsonNode.Parse(File.ReadAllText(instPath))?.AsArray() ?? new JsonArray() : new JsonArray();
            instances.Add(new JsonObject
            {
                ["blueprintId"] = plan["blueprintId"]?.ToString(),
                ["version"] = plan["version"]?.ToString(),
                ["appliedAt"] = DateTime.UtcNow.ToString("o"),
                ["configuration"] = plan["configuration"]?.DeepClone()
            });
            File.WriteAllText(instPath, instances.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));

            if (json)
            {
                Console.WriteLine(new JsonObject
                {
                    ["status"] = "applied",
                    ["blueprintId"] = plan["blueprintId"]?.ToString(),
                    ["version"] = plan["version"]?.ToString()
                }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                Console.WriteLine($"Successfully applied Blueprint '{plan["blueprintId"]}' v{plan["version"]}.");
                Console.WriteLine("Generated files:");
                if (files != null)
                {
                    foreach (var f in files) Console.WriteLine($"  -> {f?["path"]}");
                }
            }
            return ExitSuccess;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Apply Error: {ex.Message}");
            return ExitRuntimeError;
        }
    }
}
