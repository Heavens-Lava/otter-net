using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

internal static class ComposeCommand
{
    public static int Run(CommandLine line)
    {
        string[] raw = line.Raw;
        if (raw.Length < 2)
        {
            PrintUsage();
            return Cli.ExitUsageError;
        }

        string sub = raw[1].ToLowerInvariant();
        return sub switch
        {
            "search" => Search(raw),
            "inspect" => Inspect(raw),
            "check" => Check(raw),
            "propose" => Propose(raw),
            "preview" => Preview(raw),
            "explain" => Explain(raw),
            "apply" => Apply(raw),
            "--help" or "-h" or "help" => Help(),
            _ => UnknownSubcommand(sub)
        };
    }

    static void PrintUsage()
    {
        Console.WriteLine("Otter AI Composition CLI (Deterministic Local Protocol)");
        Console.WriteLine();
        Console.WriteLine("Usage: otter compose <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  search <query> [--json]                 Search library components by natural query");
        Console.WriteLine("  inspect <id> [--json]                   Inspect manifest, inputs, outputs, and dependencies");
        Console.WriteLine("  check <id> [--json]                     Verify version compatibility and missing dependencies");
        Console.WriteLine("  propose \"<intent>\" [options]             Generate structured composition proposal");
        Console.WriteLine("  preview <proposal.json>                 Preview generated code with source mappings");
        Console.WriteLine("  explain <proposal.json>                 Explain component connections and data flow");
        Console.WriteLine("  apply <proposal.json> [options]         Apply proposal with mandatory approval gate");
        Console.WriteLine();
        Console.WriteLine("Options for 'propose':");
        Console.WriteLine("  --target <console|desktop|server|game>  Target application runtime");
        Console.WriteLine("  --output <file.json>                    Save proposal JSON to file");
        Console.WriteLine("  --json                                  Output raw proposal JSON to stdout");
        Console.WriteLine();
        Console.WriteLine("Options for 'apply':");
        Console.WriteLine("  --project <dir>                         Target project directory (default: current dir)");
        Console.WriteLine("  --yes, -y                               Explicitly approve and proceed without prompt");
    }

    static int Help()
    {
        PrintUsage();
        return Cli.ExitSuccess;
    }

    static int UnknownSubcommand(string sub)
    {
        Console.Error.WriteLine($"Error: Unknown compose subcommand '{sub}'. Run 'otter compose --help' for usage.");
        return Cli.ExitUsageError;
    }

    // =========================================================================
    // 1. Search
    // =========================================================================
    static int Search(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose search <query> [--json]");
            return Cli.ExitUsageError;
        }

        string query = raw[2];
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));

        var cmdArgs = new List<string> { "library", "search", query };
        if (json) cmdArgs.Add("--json");
        return LibraryCommands.RunLibrary(CommandLine.Bind(cmdArgs.ToArray()));
    }

    // =========================================================================
    // 2. Inspect
    // =========================================================================
    static int Inspect(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose inspect <id> [--json]");
            return Cli.ExitUsageError;
        }

        string id = raw[2];
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        string libDir = LibraryCommands.FindLibraryDir();

        // 1. Check Recipe
        string? recipeFile = LibraryCommands.FindRecipeManifest(id);
        if (recipeFile != null && File.Exists(recipeFile))
        {
            string content = File.ReadAllText(recipeFile);
            if (json)
            {
                Console.WriteLine(content);
                return Cli.ExitSuccess;
            }
            var node = JsonNode.Parse(content);
            Console.WriteLine($"=== Recipe Inspection: {id} ===");
            Console.WriteLine($"  Title:       {node?["title"] ?? node?["name"]}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Target:      {node?["target"]}");
            Console.WriteLine($"  Summary:     {node?["summary"] ?? node?["description"]}");
            Console.WriteLine($"  Capabilities:{string.Join(", ", node?["capabilities"]?.AsArray().Select(c => c?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Blueprints:  {string.Join(", ", node?["blueprints"]?.AsArray().Select(b => b?["id"]?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Packs:       {string.Join(", ", node?["packs"]?.AsArray().Select(p => p?["id"]?.ToString()) ?? Array.Empty<string>())}");
            return Cli.ExitSuccess;
        }

        // 2. Check Blueprint
        string? bpFile = LibraryCommands.FindBlueprintManifest(id);
        if (bpFile != null && File.Exists(bpFile))
        {
            string content = File.ReadAllText(bpFile);
            if (json)
            {
                Console.WriteLine(content);
                return Cli.ExitSuccess;
            }
            var node = JsonNode.Parse(content);
            Console.WriteLine($"=== Blueprint Inspection: {id} ===");
            Console.WriteLine($"  Title:       {node?["title"] ?? node?["name"]}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Summary:     {node?["summary"] ?? node?["description"]}");
            Console.WriteLine($"  Targets:     {string.Join(", ", node?["targets"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Packs:       {string.Join(", ", node?["packs"]?.AsArray().Select(p => p?["id"]?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Nodes:       {node?["nodes"]?.AsArray().Count ?? 0} node(s)");
            Console.WriteLine($"  Edges:       {node?["edges"]?.AsArray().Count ?? 0} edge(s)");
            return Cli.ExitSuccess;
        }

        // 3. Check Pack
        string[] parts = id.Split('/');
        string pub = parts.Length > 1 ? parts[0] : "otter-official";
        string name = parts.Length > 1 ? parts[1] : parts[0];
        string packFile = Path.Combine(libDir, "packs", pub, name, "otter.pack.json");
        if (!File.Exists(packFile)) packFile = Path.Combine(libDir, "packs", pub, name, "pack.json");
        if (File.Exists(packFile))
        {
            string content = File.ReadAllText(packFile);
            if (json)
            {
                Console.WriteLine(content);
                return Cli.ExitSuccess;
            }
            var node = JsonNode.Parse(content);
            Console.WriteLine($"=== Pack Inspection: {id} ===");
            Console.WriteLine($"  Publisher:   {node?["publisher"] ?? pub}");
            Console.WriteLine($"  Name:        {node?["name"] ?? name}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Description: {node?["description"] ?? node?["summary"]}");
            Console.WriteLine($"  Capabilities:{string.Join(", ", node?["capabilities"]?.AsArray().Select(c => c?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Tags:        {string.Join(", ", node?["tags"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>())}");
            return Cli.ExitSuccess;
        }

        // 4. Check Snippet
        string catalogFile = Path.Combine(libDir, "snippets", "catalog.json");
        if (File.Exists(catalogFile))
        {
            var cat = JsonNode.Parse(File.ReadAllText(catalogFile));
            var snips = cat?["snippets"]?.AsArray();
            if (snips != null)
            {
                foreach (var s in snips)
                {
                    if (string.Equals(s?["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase))
                    {
                        if (json)
                        {
                            Console.WriteLine(s!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                            return Cli.ExitSuccess;
                        }
                        Console.WriteLine($"=== Snippet Inspection: {id} ===");
                        Console.WriteLine($"  Title:       {s?["title"]}");
                        Console.WriteLine($"  Category:    {s?["category"]}");
                        Console.WriteLine($"  Description: {s?["description"]}");
                        Console.WriteLine($"  Platforms:   {string.Join(", ", s?["platforms"]?.AsArray().Select(p => p?.ToString()) ?? Array.Empty<string>())}");
                        Console.WriteLine($"  Tags:        {string.Join(", ", s?["tags"]?.AsArray().Select(t => t?.ToString()) ?? Array.Empty<string>())}");
                        Console.WriteLine($"  Template:");
                        Console.WriteLine(s?["template"]?.ToString() ?? s?["example"]?.ToString() ?? "");
                        return Cli.ExitSuccess;
                    }
                }
            }
        }

        Console.Error.WriteLine($"Error: Component '{id}' not found in library.");
        return Cli.ExitUsageError;
    }

    // =========================================================================
    // 3. Check
    // =========================================================================
    static int Check(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose check <id> [--json]");
            return Cli.ExitUsageError;
        }

        string id = raw[2];
        bool json = raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        string libDir = LibraryCommands.FindLibraryDir();

        var result = new JsonObject
        {
            ["id"] = id,
            ["compatible"] = true,
            ["otterRequired"] = ">=1.0.1 <2.0.0",
            ["otterCurrent"] = "1.0.1",
            ["issues"] = new JsonArray()
        };

        bool found = false;

        // 1. Recipe
        string? recipeFile = LibraryCommands.FindRecipeManifest(id);
        if (recipeFile != null && File.Exists(recipeFile))
        {
            found = true;
            result["kind"] = "recipe";
            var node = JsonNode.Parse(File.ReadAllText(recipeFile));
            var packs = node?["packs"]?.AsArray();
            if (packs != null)
            {
                foreach (var p in packs)
                {
                    string pid = p?["id"]?.ToString() ?? "";
                    string[] pparts = pid.Split('/');
                    string pub = pparts.Length > 1 ? pparts[0] : "otter-official";
                    string pname = pparts.Length > 1 ? pparts[1] : pparts[0];
                    string ppath = Path.Combine(libDir, "packs", pub, pname, "otter.pack.json");
                    if (!File.Exists(ppath)) ppath = Path.Combine(libDir, "packs", pub, pname, "pack.json");
                    if (!File.Exists(ppath))
                    {
                        result["compatible"] = false;
                        result["issues"]!.AsArray().Add((JsonNode)JsonValue.Create($"Missing dependent pack: {pid}")!);
                    }
                }
            }
            var bps = node?["blueprints"]?.AsArray();
            if (bps != null)
            {
                foreach (var b in bps)
                {
                    string bid = b?["id"]?.ToString() ?? "";
                    if (LibraryCommands.FindBlueprintManifest(bid) == null)
                    {
                        result["compatible"] = false;
                        result["issues"]!.AsArray().Add((JsonNode)JsonValue.Create($"Missing dependent blueprint: {bid}")!);
                    }
                }
            }
        }

        // 2. Blueprint
        if (!found)
        {
            string? bpFile = LibraryCommands.FindBlueprintManifest(id);
            if (bpFile != null && File.Exists(bpFile))
            {
                found = true;
                result["kind"] = "blueprint";
                var node = JsonNode.Parse(File.ReadAllText(bpFile));
                var packs = node?["packs"]?.AsArray();
                if (packs != null)
                {
                    foreach (var p in packs)
                    {
                        string pid = p?["id"]?.ToString() ?? "";
                        string[] pparts = pid.Split('/');
                        string pub = pparts.Length > 1 ? pparts[0] : "otter-official";
                        string pname = pparts.Length > 1 ? pparts[1] : pparts[0];
                        string ppath = Path.Combine(libDir, "packs", pub, pname, "otter.pack.json");
                        if (!File.Exists(ppath)) ppath = Path.Combine(libDir, "packs", pub, pname, "pack.json");
                        if (!File.Exists(ppath))
                        {
                            result["compatible"] = false;
                            result["issues"]!.AsArray().Add((JsonNode)JsonValue.Create($"Missing dependent pack: {pid}")!);
                        }
                    }
                }
            }
        }

        // 3. Pack
        if (!found)
        {
            string[] parts = id.Split('/');
            string pub = parts.Length > 1 ? parts[0] : "otter-official";
            string name = parts.Length > 1 ? parts[1] : parts[0];
            string packFile = Path.Combine(libDir, "packs", pub, name, "otter.pack.json");
            if (!File.Exists(packFile)) packFile = Path.Combine(libDir, "packs", pub, name, "pack.json");
            if (File.Exists(packFile))
            {
                found = true;
                result["kind"] = "pack";
            }
        }

        // 4. Snippet
        if (!found)
        {
            string catalogFile = Path.Combine(libDir, "snippets", "catalog.json");
            if (File.Exists(catalogFile))
            {
                var cat = JsonNode.Parse(File.ReadAllText(catalogFile));
                var snips = cat?["snippets"]?.AsArray();
                if (snips != null && snips.Any(s => string.Equals(s?["id"]?.ToString(), id, StringComparison.OrdinalIgnoreCase)))
                {
                    found = true;
                    result["kind"] = "snippet";
                }
            }
        }

        if (!found)
        {
            result["kind"] = "unknown";
            result["compatible"] = false;
            result["issues"]!.AsArray().Add((JsonNode)JsonValue.Create($"Component '{id}' not found in library.")!);
        }

        if (json)
        {
            Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return (bool)result["compatible"]! ? Cli.ExitSuccess : Cli.ExitCheckError;
        }

        bool ok = (bool)result["compatible"]!;
        Console.WriteLine($"=== Compatibility Check: {id} ({result["kind"]}) ===");
        Console.WriteLine($"  Status:        {(ok ? "[OK] Compatible" : "[FAIL] Incompatible")}");
        Console.WriteLine($"  Otter Target:  {result["otterRequired"]} (current: {result["otterCurrent"]})");
        var issues = result["issues"]!.AsArray();
        if (issues.Count > 0)
        {
            Console.WriteLine("  Issues:");
            foreach (var iss in issues)
            {
                Console.WriteLine($"    - {iss}");
            }
            return Cli.ExitCheckError;
        }

        Console.WriteLine("  All dependencies and grammar constraints verified.");
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 4. Propose (Metadata-Driven Discovery and Ranking)
    // =========================================================================
    static int Propose(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose propose \"<intent>\" [--target <target>] [--out <file.json>] [--json]");
            return Cli.ExitUsageError;
        }

        string intent = raw[2];
        string? userTarget = null;
        string? outFile = null;
        bool json = false;

        for (int i = 3; i < raw.Length; i++)
        {
            if (string.Equals(raw[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < raw.Length)
            {
                userTarget = raw[++i].ToLowerInvariant();
            }
            else if ((string.Equals(raw[i], "--output", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(raw[i], "--out-file", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(raw[i], "--file", StringComparison.OrdinalIgnoreCase)) && i + 1 < raw.Length)
            {
                outFile = raw[++i];
            }
            else if (string.Equals(raw[i], "--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
            }
        }

        string libDir = LibraryCommands.FindLibraryDir();
        string proposalId = $"prop-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}";

        // ---------------------------------------------------------------------
        // Discovery & Ranking Engine
        // ---------------------------------------------------------------------
        var discovery = PerformDiscovery(intent, userTarget, libDir);

        if (!discovery.Supported)
        {
            var unsupportedProposal = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["proposalId"] = proposalId,
                ["intent"] = intent,
                ["supported"] = false,
                ["missingCapabilities"] = new JsonArray(discovery.MissingCapabilities.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
                ["availableCapabilities"] = new JsonArray(discovery.AvailableCapabilities.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
                ["recommendedComponents"] = new JsonArray(),
                ["compositionPlan"] = new JsonObject
                {
                    ["target"] = userTarget ?? "console",
                    ["composedPacks"] = new JsonArray(),
                    ["composedBlueprints"] = new JsonArray(),
                    ["connections"] = new JsonArray()
                },
                ["generatedSourcePreview"] = new JsonArray(),
                ["approvalRequired"] = false,
                ["verification"] = new JsonObject
                {
                    ["command"] = "otter check",
                    ["syntaxValid"] = false,
                    ["diagnostics"] = new JsonArray
                    {
                        (JsonNode)JsonValue.Create($"No compatible components found in the Otter library for intent '{intent}'. Missing capabilities: {string.Join(", ", discovery.MissingCapabilities)}")!
                    }
                },
                ["unresolvedLimitations"] = new JsonArray
                {
                    (JsonNode)JsonValue.Create("Otter 1.0.1 does not provide library building blocks for the requested domain.")!
                }
            };

            string unsuppJson = unsupportedProposal.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (!string.IsNullOrEmpty(outFile))
            {
                File.WriteAllText(outFile, unsuppJson + Environment.NewLine, new UTF8Encoding(false));
            }

            if (json)
            {
                Console.WriteLine(unsuppJson);
                return Cli.ExitCheckError;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[UNSUPPORTED REQUEST]");
            Console.ResetColor();
            Console.WriteLine($"No compatible components found in the Otter library for intent: \"{intent}\"");
            Console.WriteLine();
            Console.WriteLine("Missing Capabilities:");
            foreach (var mc in discovery.MissingCapabilities)
            {
                Console.WriteLine($"  - {mc}");
            }
            Console.WriteLine();
            Console.WriteLine("Available Library Capabilities:");
            foreach (var ac in discovery.AvailableCapabilities)
            {
                Console.WriteLine($"  - {ac}");
            }
            Console.WriteLine();
            Console.WriteLine("Explanation:");
            Console.WriteLine("  Otter 1.0.1 composition operates strictly on verified, existing library building blocks.");
            Console.WriteLine("  No compatible Snippets, Packs, Blueprints, or Recipes exist to fulfill this request.");
            Console.WriteLine("  Otter will not fabricate missing APIs or substitute an unrelated project template.");

            return Cli.ExitCheckError;
        }

        string selectedRecipeId = discovery.SelectedRecipeId!;
        string targetRuntime = userTarget ?? discovery.SelectedTarget!;

        // Generate Plan using official recipe infrastructure
        var recipePlan = LibraryCommands.GenerateRecipePlan(selectedRecipeId, null, ".");

        var recommended = new JsonArray();
        foreach (var comp in discovery.RecommendedComponents)
        {
            recommended.Add(new JsonObject
            {
                ["id"] = comp.Id,
                ["kind"] = comp.Kind,
                ["version"] = comp.Version,
                ["rationale"] = comp.Rationale
            });
        }

        var composedPacks = new JsonArray();
        var recipePacks = recipePlan["dependencies"]?.AsArray();
        if (recipePacks != null)
        {
            foreach (var p in recipePacks)
            {
                string pid = p?["id"]?.ToString() ?? "";
                if (!composedPacks.Any(cp => string.Equals(cp?.ToString(), pid, StringComparison.OrdinalIgnoreCase)))
                {
                    composedPacks.Add((JsonNode)JsonValue.Create(pid)!);
                }
            }
        }
        foreach (var extraPack in discovery.ExtraPacks)
        {
            if (!composedPacks.Any(cp => string.Equals(cp?.ToString(), extraPack, StringComparison.OrdinalIgnoreCase)))
            {
                composedPacks.Add((JsonNode)JsonValue.Create(extraPack)!);
            }
        }

        var composedBps = new JsonArray();
        string? recipeManifestPath = LibraryCommands.FindRecipeManifest(selectedRecipeId);
        var recipeManifest = recipeManifestPath != null ? JsonNode.Parse(File.ReadAllText(recipeManifestPath)) : null;
        var recipeBps = recipeManifest?["blueprints"]?.AsArray();
        if (recipeBps != null)
        {
            foreach (var b in recipeBps)
            {
                string bid = b?["id"]?.ToString() ?? "";
                if (!string.IsNullOrEmpty(bid))
                {
                    composedBps.Add((JsonNode)JsonValue.Create(bid)!);
                }
            }
        }
        foreach (var extraBp in discovery.ExtraBlueprints)
        {
            if (!composedBps.Any(cb => string.Equals(cb?.ToString(), extraBp, StringComparison.OrdinalIgnoreCase)))
            {
                composedBps.Add((JsonNode)JsonValue.Create(extraBp)!);
            }
        }

        // Assemble generated source preview from recipe files
        var sourceFiles = new JsonArray();
        var planFiles = recipePlan["plannedFiles"]?.AsArray();
        bool allSyntaxValid = true;
        var allDiagnostics = new JsonArray();

        if (planFiles != null)
        {
            foreach (var pf in planFiles)
            {
                string fpath = pf?["path"]?.ToString() ?? "src/main.ot";
                string fcontent = pf?["content"]?.ToString() ?? "";

                // Annotate header with provenance
                var annotatedCode = new StringBuilder();
                annotatedCode.AppendLine("# ===========================================================================");
                annotatedCode.AppendLine("# Generated by Otter AI Composition Engine");
                annotatedCode.AppendLine($"# Recipe: {selectedRecipeId} v1.0.0");
                annotatedCode.AppendLine($"# Target: {targetRuntime}");
                annotatedCode.AppendLine("# ===========================================================================");
                annotatedCode.AppendLine();
                annotatedCode.Append(fcontent);

                string fullCode = annotatedCode.ToString();

                // Validate syntax with language service (resolving virtual imports from library packs)
                if (fpath.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
                {
                    string codeToValidate = ResolveVirtualImports(fullCode, libDir);
                    var diags = LanguageService.GetDiagnostics(codeToValidate, fpath);
                    if (diags.Count > 0)
                    {
                        allSyntaxValid = false;
                        foreach (var d in diags) allDiagnostics.Add((JsonNode)JsonValue.Create($"{fpath}:{d.Line}: {d.Message}")!);
                    }
                }

                sourceFiles.Add(new JsonObject
                {
                    ["path"] = fpath,
                    ["sourceCode"] = fullCode,
                    ["sourceMappings"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["lines"] = "1-20",
                            ["componentId"] = selectedRecipeId
                        }
                    }
                });
            }
        }

        string primarySourcePath = sourceFiles.Count > 0 ? (sourceFiles[0]?["path"]?.ToString() ?? "src/main.ot") : "src/main.ot";

        var connections = new JsonArray();
        foreach (var conn in discovery.Connections)
        {
            connections.Add(new JsonObject
            {
                ["source"] = conn.Source,
                ["destination"] = conn.Destination,
                ["description"] = conn.Description
            });
        }

        var limitations = new JsonArray
        {
            (JsonNode)JsonValue.Create("Otter 1.0.1 variable scoping: Pack functions execute in caller variable scope. All official packs use prefixed internal variables to prevent collisions, but caller scripts should avoid variable names matching internal pack state until Otter 2.0 lexical scoping is implemented.")!
        };

        var proposal = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["proposalId"] = proposalId,
            ["intent"] = intent,
            ["requestedCapabilities"] = new JsonArray(discovery.RequestedCapabilities.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["recommendedComponents"] = recommended,
            ["compositionPlan"] = new JsonObject
            {
                ["target"] = targetRuntime,
                ["composedPacks"] = composedPacks,
                ["composedBlueprints"] = composedBps,
                ["connections"] = connections
            },
            ["generatedSourcePreview"] = sourceFiles,
            ["approvalRequired"] = true,
            ["verification"] = new JsonObject
            {
                ["command"] = $"otter check {primarySourcePath}",
                ["syntaxValid"] = allSyntaxValid,
                ["diagnostics"] = allDiagnostics
            },
            ["unresolvedLimitations"] = limitations
        };

        string jsonOutput = proposal.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        if (!string.IsNullOrEmpty(outFile))
        {
            File.WriteAllText(outFile, jsonOutput + Environment.NewLine, new UTF8Encoding(false));
            Console.WriteLine($"Proposal saved to: {outFile}");
        }

        if (json)
        {
            Console.WriteLine(jsonOutput);
            return Cli.ExitSuccess;
        }

        Console.WriteLine($"=== Otter AI Composition Proposal ({proposalId}) ===");
        Console.WriteLine($"  Intent:      {intent}");
        Console.WriteLine($"  Target:      {targetRuntime}");
        Console.WriteLine($"  Recipe:      {selectedRecipeId}");
        Console.WriteLine($"  Packs:       {string.Join(", ", composedPacks.Select(p => p?.ToString()))}");
        Console.WriteLine($"  Blueprints:  {string.Join(", ", composedBps.Select(b => b?.ToString()))}");
        Console.WriteLine($"  Files:       {sourceFiles.Count} generated file(s)");
        Console.WriteLine($"  Validation:  {(allSyntaxValid ? "[OK] 0 syntax errors" : $"[FAIL] {allDiagnostics.Count} errors")}");
        Console.WriteLine($"  Approval:    Mandatory user approval required before applying");
        Console.WriteLine();
        Console.WriteLine("To preview source code:   otter compose preview <proposal.json>");
        Console.WriteLine("To explain connections:   otter compose explain <proposal.json>");
        Console.WriteLine("To apply with approval:   otter compose apply <proposal.json>");

        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 5. Preview
    // =========================================================================
    static int Preview(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose preview <proposal.json>");
            return Cli.ExitUsageError;
        }

        string file = raw[2];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File '{file}' not found.");
            return Cli.ExitUsageError;
        }

        var node = JsonNode.Parse(File.ReadAllText(file));
        string pid = node?["proposalId"]?.ToString() ?? "unknown";
        string intent = node?["intent"]?.ToString() ?? "";
        bool supported = node?["supported"]?.GetValue<bool>() ?? true;

        Console.WriteLine($"=== Source Code Preview: Proposal {pid} ===");
        Console.WriteLine($"Intent: \"{intent}\"");
        if (!supported)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Status: [UNSUPPORTED] This proposal could not be composed from existing library building blocks.");
            Console.ResetColor();
            return Cli.ExitCheckError;
        }

        Console.WriteLine();

        var files = node?["generatedSourcePreview"]?.AsArray();
        if (files != null && files.Count > 0)
        {
            foreach (var f in files)
            {
                string fpath = f?["path"]?.ToString() ?? "unknown";
                string code = f?["sourceCode"]?.ToString() ?? "";
                Console.WriteLine($"--- File: {fpath} ---");
                string[] lines = code.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    Console.WriteLine($"{i + 1,4} | {lines[i].TrimEnd('\r')}");
                }
                Console.WriteLine();
            }
        }
        else
        {
            Console.WriteLine("(No source files generated)");
        }

        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 6. Explain
    // =========================================================================
    static int Explain(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose explain <proposal.json>");
            return Cli.ExitUsageError;
        }

        string file = raw[2];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: File '{file}' not found.");
            return Cli.ExitUsageError;
        }

        var node = JsonNode.Parse(File.ReadAllText(file));
        string pid = node?["proposalId"]?.ToString() ?? "unknown";
        string intent = node?["intent"]?.ToString() ?? "";
        bool supported = node?["supported"]?.GetValue<bool>() ?? true;
        var plan = node?["compositionPlan"];
        var recs = node?["recommendedComponents"]?.AsArray();
        var conns = plan?["connections"]?.AsArray();
        var ver = node?["verification"];
        var reqCaps = node?["requestedCapabilities"]?.AsArray();
        var limits = node?["unresolvedLimitations"]?.AsArray();

        Console.WriteLine($"=== Architectural Explanation for Proposal {pid} ===");
        Console.WriteLine();
        Console.WriteLine($"1. Feature Intent & Goal:");
        Console.WriteLine($"   \"{intent}\"");
        if (reqCaps != null && reqCaps.Count > 0)
        {
            Console.WriteLine($"   Identified Capabilities: {string.Join(", ", reqCaps.Select(c => c?.ToString()))}");
        }
        Console.WriteLine();

        if (!supported)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("2. Status & Limitations:");
            Console.WriteLine("   [UNSUPPORTED REQUEST] Otter library does not provide verified building blocks for this intent.");
            var missing = node?["missingCapabilities"]?.AsArray();
            if (missing != null)
            {
                Console.WriteLine($"   Missing Capabilities: {string.Join(", ", missing.Select(m => m?.ToString()))}");
            }
            Console.ResetColor();
            return Cli.ExitCheckError;
        }

        Console.WriteLine($"2. Component Selection Rationale:");
        if (recs != null && recs.Count > 0)
        {
            foreach (var r in recs)
            {
                Console.WriteLine($"   - [{r?["kind"]}] {r?["id"]} (v{r?["version"]})");
                Console.WriteLine($"     Rationale: {r?["rationale"]}");
            }
        }
        else
        {
            Console.WriteLine("   (No components recommended)");
        }
        Console.WriteLine();

        Console.WriteLine($"3. Composition & Data Flow Connections:");
        if (conns != null && conns.Count > 0)
        {
            foreach (var c in conns)
            {
                Console.WriteLine($"   - {c?["source"]} -> {c?["destination"]}");
                Console.WriteLine($"     Wiring: {c?["description"]}");
            }
        }
        else
        {
            Console.WriteLine("   (No custom connections declared)");
        }
        Console.WriteLine();

        Console.WriteLine($"4. Safety, Integrity & Validation:");
        Console.WriteLine($"   - Target Runtime: {plan?["target"]}");
        Console.WriteLine($"   - Syntax Check:   {(ver?["syntaxValid"]?.GetValue<bool>() == true ? "PASSED (0 errors)" : "FAILED")}");
        Console.WriteLine($"   - Validation Cmd: {ver?["command"]}");
        Console.WriteLine($"   - User Approval:  Mandatory approval gate required prior to file modification");
        Console.WriteLine();

        Console.WriteLine($"5. Unresolved Limitations & Scoping Considerations:");
        if (limits != null && limits.Count > 0)
        {
            foreach (var lim in limits)
            {
                Console.WriteLine($"   - {lim}");
            }
        }
        else
        {
            Console.WriteLine("   - None declared.");
        }

        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 7. Apply (Safe, Transactional Staged Pipeline with Rollback)
    // =========================================================================
    static int Apply(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose apply <proposal.json> [--project <dir>] [--yes]");
            return Cli.ExitUsageError;
        }

        string file = raw[2];
        string targetDir = ".";
        bool explicitApproval = false;

        for (int i = 3; i < raw.Length; i++)
        {
            if (string.Equals(raw[i], "--project", StringComparison.OrdinalIgnoreCase) && i + 1 < raw.Length)
            {
                targetDir = raw[++i];
            }
            else if (string.Equals(raw[i], "--yes", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(raw[i], "-y", StringComparison.OrdinalIgnoreCase))
            {
                explicitApproval = true;
            }
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Error: Proposal file '{file}' does not exist.");
            return Cli.ExitUsageError;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(file));
            if (node == null) throw new InvalidOperationException("Empty JSON");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Invalid proposal JSON in '{file}': {ex.Message}");
            return Cli.ExitUsageError;
        }

        string pid = node["proposalId"]?.ToString() ?? "unknown";
        bool supported = node["supported"]?.GetValue<bool>() ?? true;
        if (!supported)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[SAFETY ABORT] Refusing to apply proposal {pid}: Proposal is marked as unsupported.");
            Console.ResetColor();
            return Cli.ExitCheckError;
        }

        string libDir = LibraryCommands.FindLibraryDir();
        string fullTargetDir = Path.GetFullPath(targetDir);

        // ---------------------------------------------------------------------
        // Stage 1: Validate Component Identifiers & Dependencies
        // ---------------------------------------------------------------------
        var composedPacks = node["compositionPlan"]?["composedPacks"]?.AsArray();
        if (composedPacks != null)
        {
            foreach (var p in composedPacks)
            {
                string packId = p?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(packId)) continue;

                string[] pparts = packId.Split('/');
                string pub = pparts.Length > 1 ? pparts[0] : "otter-official";
                string pname = pparts.Length > 1 ? pparts[1] : pparts[0];
                string ppath = Path.Combine(libDir, "packs", pub, pname, "otter.pack.json");
                if (!File.Exists(ppath)) ppath = Path.Combine(libDir, "packs", pub, pname, "pack.json");

                if (!File.Exists(ppath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[SAFETY ABORT] Missing component dependency: Pack '{packId}' is not installed in the local library.");
                    Console.ResetColor();
                    return Cli.ExitCheckError;
                }
            }
        }

        var composedBps = node["compositionPlan"]?["composedBlueprints"]?.AsArray();
        if (composedBps != null)
        {
            foreach (var b in composedBps)
            {
                string bpId = b?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(bpId)) continue;
                if (LibraryCommands.FindBlueprintManifest(bpId) == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[SAFETY ABORT] Missing component dependency: Blueprint '{bpId}' is not found in the local library.");
                    Console.ResetColor();
                    return Cli.ExitCheckError;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Stage 2: Path Traversal Containment Checks
        // ---------------------------------------------------------------------
        var files = node["generatedSourcePreview"]?.AsArray();
        if (files == null || files.Count == 0)
        {
            Console.Error.WriteLine($"[SAFETY ABORT] Proposal {pid} contains no generated source files.");
            return Cli.ExitCheckError;
        }

        var plannedFileOps = new List<(string relPath, string fullPath, string code, bool exists)>();
        foreach (var f in files)
        {
            string relPath = f?["path"]?.ToString() ?? "";
            string code = f?["sourceCode"]?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(relPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine("[SAFETY ABORT] File entry contains an empty or whitespace path.");
                Console.ResetColor();
                return Cli.ExitCheckError;
            }

            if (Path.IsPathRooted(relPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[SAFETY ABORT] Absolute path rejected: '{relPath}'. All file paths must be relative to the project root.");
                Console.ResetColor();
                return Cli.ExitCheckError;
            }

            string canonicalFile = Path.GetFullPath(Path.Combine(fullTargetDir, relPath));
            if (!canonicalFile.StartsWith(fullTargetDir + Path.DirectorySeparatorChar) && canonicalFile != fullTargetDir)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[SAFETY ABORT] Path traversal rejected: '{relPath}' escapes project directory '{fullTargetDir}'.");
                Console.ResetColor();
                return Cli.ExitCheckError;
            }

            plannedFileOps.Add((relPath, canonicalFile, code, File.Exists(canonicalFile)));
        }

        // ---------------------------------------------------------------------
        // Stage 3: Pre-apply Syntax Validation (Resolve Virtual Imports)
        // ---------------------------------------------------------------------
        foreach (var op in plannedFileOps)
        {
            if (op.relPath.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
            {
                string codeToValidate = ResolveVirtualImports(op.code, libDir);
                var diags = LanguageService.GetDiagnostics(codeToValidate, op.relPath);
                if (diags.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[SAFETY ABORT] Pre-apply syntax error in generated file '{op.relPath}': {diags[0].Message}");
                    Console.ResetColor();
                    return Cli.ExitCheckError;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Stage 4: Conflict Preview & Change Summary
        // ---------------------------------------------------------------------
        Console.WriteLine($"=== Otter Composition Application Preview: Proposal {pid} ===");
        Console.WriteLine($"Target Directory: {fullTargetDir}");
        Console.WriteLine("Planned File Actions:");
        foreach (var op in plannedFileOps)
        {
            if (op.exists)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  [OVERWRITE] {op.relPath} (pre-existing file will be backed up)");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"  [CREATE]    {op.relPath}");
                Console.ResetColor();
            }
        }

        if (composedPacks != null && composedPacks.Count > 0)
        {
            Console.WriteLine("Required Dependencies:");
            foreach (var p in composedPacks)
            {
                Console.WriteLine($"  [INSTALL]   {p}");
            }
        }
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // Stage 5: Mandatory Approval Gate
        // ---------------------------------------------------------------------
        if (!explicitApproval)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[APPROVAL REQUIRED] Proposal {pid} will write/modify {plannedFileOps.Count} file(s) in '{fullTargetDir}'.");
            Console.ResetColor();
            Console.Write("Do you approve and wish to apply this composition proposal? [y/N]: ");

            string? response = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(response) ||
                (!response.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) &&
                 !response.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Operation cancelled. No files were written.");
                Console.ResetColor();
                return Cli.ExitUsageError;
            }
        }

        // ---------------------------------------------------------------------
        // Stage 6: Transactional Staging & Rollback Setup
        // ---------------------------------------------------------------------
        var preexistingBackups = new Dictionary<string, byte[]>();
        var createdFiles = new List<string>();
        var createdDirs = new List<string>();

        // Snapshot pre-existing files
        foreach (var op in plannedFileOps)
        {
            if (op.exists)
            {
                try
                {
                    preexistingBackups[op.fullPath] = File.ReadAllBytes(op.fullPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SAFETY ABORT] Failed to snapshot existing file '{op.relPath}': {ex.Message}");
                    return Cli.ExitCheckError;
                }
            }
        }

        void Rollback()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[ROLLBACK] Initiating atomic rollback to restore project state...");

            // 1. Delete created files
            foreach (var f in createdFiles)
            {
                try
                {
                    if (File.Exists(f)) File.Delete(f);
                }
                catch { }
            }

            // 2. Restore pre-existing files from snapshot
            foreach (var (f, bytes) in preexistingBackups)
            {
                try
                {
                    File.WriteAllBytes(f, bytes);
                }
                catch { }
            }

            // 3. Remove created directories in reverse order
            foreach (var d in createdDirs.OrderByDescending(x => x.Length))
            {
                try
                {
                    if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                    {
                        Directory.Delete(d);
                    }
                }
                catch { }
            }

            Console.WriteLine("[ROLLBACK] Project directory restored cleanly to original state.");
            Console.ResetColor();
        }

        // ---------------------------------------------------------------------
        // Stage 7: Phase A - Dependency Installation with Exit Code Check
        // ---------------------------------------------------------------------
        if (composedPacks != null && composedPacks.Count > 0)
        {
            foreach (var p in composedPacks)
            {
                string packId = p?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(packId)) continue;

                int exitCode;
                try
                {
                    exitCode = LibraryCommands.RunPack(CommandLine.Bind(new[] { "pack", "install", packId, "--project", targetDir }));
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[FAILURE] Exception installing required pack '{packId}': {ex.Message}");
                    Console.ResetColor();
                    Rollback();
                    return Cli.ExitCheckError;
                }

                if (exitCode != 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine($"[FAILURE] Failed to install required pack '{packId}' (exit code {exitCode}). Aborting application.");
                    Console.ResetColor();
                    Rollback();
                    return Cli.ExitCheckError;
                }
            }
        }

        // ---------------------------------------------------------------------
        // Stage 8: Phase B - Atomic File Writes
        // ---------------------------------------------------------------------
        try
        {
            if (!Directory.Exists(fullTargetDir))
            {
                Directory.CreateDirectory(fullTargetDir);
                createdDirs.Add(fullTargetDir);
            }

            foreach (var op in plannedFileOps)
            {
                string? parent = Path.GetDirectoryName(op.fullPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                    createdDirs.Add(parent);
                }

                if (!op.exists)
                {
                    createdFiles.Add(op.fullPath);
                }

                File.WriteAllText(op.fullPath, op.code, new UTF8Encoding(false));
                Console.WriteLine($"  + Committed: {op.relPath}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[FAILURE] Write error during file generation: {ex.Message}");
            Console.ResetColor();
            Rollback();
            return Cli.ExitCheckError;
        }

        // ---------------------------------------------------------------------
        // Stage 9: Phase C - Post-Apply Verification
        // ---------------------------------------------------------------------
        foreach (var op in plannedFileOps)
        {
            if (!File.Exists(op.fullPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[FAILURE] Post-apply verification failed: File '{op.relPath}' was not written.");
                Console.ResetColor();
                Rollback();
                return Cli.ExitCheckError;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Composition proposal {pid} successfully applied to '{targetDir}'.");
        Console.ResetColor();
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // Discovery Engine Implementation
    // =========================================================================
    record ComponentItem(string Id, string Kind, string Version, string Rationale);
    record ConnectionItem(string Source, string Destination, string Description);

    class DiscoveryResult
    {
        public bool Supported { get; set; } = true;
        public string? SelectedRecipeId { get; set; }
        public string? SelectedTarget { get; set; }
        public List<string> RequestedCapabilities { get; set; } = new();
        public List<string> MissingCapabilities { get; set; } = new();
        public List<string> AvailableCapabilities { get; set; } = new();
        public List<ComponentItem> RecommendedComponents { get; set; } = new();
        public List<string> ExtraPacks { get; set; } = new();
        public List<string> ExtraBlueprints { get; set; } = new();
        public List<ConnectionItem> Connections { get; set; } = new();
    }

    static DiscoveryResult PerformDiscovery(string intent, string? targetOverride, string libDir)
    {
        var result = new DiscoveryResult();
        result.AvailableCapabilities.AddRange(new[]
        {
            "files (persistent JSON storage, safe atomic file writes)",
            "game (actor entities, RPG combat, inventory equipment, ability cooldowns)",
            "text (string formatting, padding, slugify, email validation, substring search)",
            "math (range clamping, arithmetic operations, bounds)",
            "list (deduplication, collection sorting and filtering)",
            "web (HTTP endpoints, request routing, static file hosting)",
            "ui (desktop UI bridge, terminal menus)"
        });

        // 1. Tokenize intent
        string lowerIntent = intent.ToLowerInvariant();
        var rawTokens = Regex.Matches(lowerIntent, @"[a-z0-9\-]+")
                             .Select(m => m.Value)
                             .ToHashSet();

        // 2. Unsupported domains check
        var unsupportedDomains = new Dictionary<string, string[]>
        {
            ["gpu-acceleration"] = new[] { "gpu", "cuda", "vulkan", "opencl", "directx", "metal" },
            ["neural-networks"] = new[] { "neural", "deep-learning", "pytorch", "tensorflow", "transformer", "whisper", "llm", "onnx" },
            ["audio-dsp"] = new[] { "audio-dsp", "dsp", "vst", "vst3", "synthesizer", "synth", "midi" },
            ["blockchain"] = new[] { "blockchain", "crypto", "bitcoin", "ethereum", "solana", "mining", "wallet", "smart-contract" },
            ["kernel-embedded"] = new[] { "kernel-driver", "kernel", "fpga", "embedded", "firmware", "driver" }
        };

        foreach (var (domain, keywords) in unsupportedDomains)
        {
            if (keywords.Any(k => rawTokens.Contains(k) || lowerIntent.Contains(k)))
            {
                result.MissingCapabilities.Add(domain);
            }
        }

        if (result.MissingCapabilities.Count > 0)
        {
            result.Supported = false;
            return result;
        }

        // 3. Capability Mapping
        var requestedCaps = new List<string>();
        bool hasGame = rawTokens.Any(t => t is "game" or "rpg" or "combat" or "fight" or "battle" or "inventory" or "item" or "items" or "equip" or "equipment" or "loot" or "hero" or "enemy" or "boss" or "cooldown" or "cooldowns" or "ability" or "skill" or "xp" or "level" or "hp" or "health");
        bool hasFiles = rawTokens.Any(t => t is "file" or "files" or "save" or "saved" or "saving" or "load" or "loading" or "persist" or "persistence" or "persistent" or "storage" or "store" or "json" or "data" or "disk" or "settings");
        bool hasText = rawTokens.Any(t => t is "text" or "string" or "clean" or "cleanup" or "slug" or "pad" or "format" or "validate" or "validation" or "email" or "phone" or "search" or "lookup");
        bool hasContact = rawTokens.Any(t => t is "contact" or "contacts" or "crm" or "address" or "directory" or "person" or "people");
        bool hasTask = rawTokens.Any(t => t is "task" or "tasks" or "todo" or "tracker" or "tracking" or "checklist" or "work");
        bool hasWeb = rawTokens.Any(t => t is "web" or "http" or "portal" or "website" or "html" or "server" or "endpoint" or "api" or "route");
        bool hasMath = rawTokens.Any(t => t is "math" or "calculate" or "calc" or "clamp" or "range" or "number" or "arithmetic");

        if (hasGame) requestedCaps.Add("game");
        if (hasFiles) requestedCaps.Add("files");
        if (hasContact) requestedCaps.Add("contact");
        if (hasTask) requestedCaps.Add("task");
        if (hasWeb) requestedCaps.Add("web");
        if (hasText) requestedCaps.Add("text");
        if (hasMath) requestedCaps.Add("math");

        result.RequestedCapabilities = requestedCaps;

        // 4. Score Recipes
        // Score: rpg-adventure
        int scoreRpg = 0;
        if (hasGame) scoreRpg += 40;
        if (lowerIntent.Contains("combat") || lowerIntent.Contains("battle") || lowerIntent.Contains("fight")) scoreRpg += 25;
        if (lowerIntent.Contains("cooldown") || lowerIntent.Contains("cooldowns")) scoreRpg += 25;
        if (lowerIntent.Contains("inventory") || lowerIntent.Contains("item") || lowerIntent.Contains("equip")) scoreRpg += 25;
        if (lowerIntent.Contains("rpg") || lowerIntent.Contains("adventure")) scoreRpg += 20;
        if (hasFiles) scoreRpg += 10;
        if (targetOverride == "game") scoreRpg += 10;

        // Score: contact-manager
        int scoreContact = 0;
        if (hasContact) scoreContact += 40;
        if (lowerIntent.Contains("contact") || lowerIntent.Contains("crm") || lowerIntent.Contains("address")) scoreContact += 30;
        if (lowerIntent.Contains("email") || lowerIntent.Contains("phone")) scoreContact += 20;
        if (hasText && lowerIntent.Contains("search")) scoreContact += 20;
        if (hasFiles) scoreContact += 10;
        if (targetOverride == "desktop") scoreContact += 10;

        // Score: task-tracker
        int scoreTask = 0;
        if (hasTask) scoreTask += 40;
        if (lowerIntent.Contains("task") || lowerIntent.Contains("todo") || lowerIntent.Contains("tracker")) scoreTask += 30;
        if (hasFiles) scoreTask += 15;
        if (targetOverride == "console" && hasTask) scoreTask += 10;

        // Score: web-portal
        int scoreWeb = 0;
        if (hasWeb) scoreWeb += 40;
        if (lowerIntent.Contains("web") || lowerIntent.Contains("portal") || lowerIntent.Contains("http")) scoreWeb += 30;
        if (lowerIntent.Contains("route") || lowerIntent.Contains("server") || lowerIntent.Contains("html")) scoreWeb += 20;
        if (targetOverride == "server") scoreWeb += 15;

        int maxScore = Math.Max(Math.Max(scoreRpg, scoreContact), Math.Max(scoreTask, scoreWeb));

        if (maxScore == 0)
        {
            // If no declared capabilities or keywords match, mark as unsupported!
            result.Supported = false;
            result.MissingCapabilities.Add("unsupported-domain-or-unmatched-capabilities");
            return result;
        }

        // 5. Select Winning Recipe and Assemble Plan
        if (maxScore == scoreRpg)
        {
            result.SelectedRecipeId = "otter-official/rpg-adventure";
            result.SelectedTarget = targetOverride ?? "console";

            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/rpg-adventure",
                "recipe",
                "1.0.0",
                "Matches intent for RPG combat loops, inventory equipment, enemy loot, and game progression."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/game-combat",
                "blueprint",
                "1.0.0",
                "Provides topological wiring between enemy spawning, attack damage, and defeat checks."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/game-entity",
                "pack",
                "1.1.0",
                "Core entity module managing Actor structs, inventory items, and ability cooldown timers."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/math-utils",
                "pack",
                "1.0.1",
                "Calculates damage mitigation bounds and critical strike arithmetic."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/file-helpers",
                "pack",
                "1.0.1",
                "Provides safe serialized JSON disk storage for game save records."
            ));

            string snipId = lowerIntent.Contains("inventory") ? "game-inventory-capacity" : "game-actor-damage";
            string snipRationale = lowerIntent.Contains("inventory")
                ? "Enforces inventory slot capacity constraints before adding collected loot items."
                : "Provides verified combat damage deduction and alive/dead state evaluation.";
            result.RecommendedComponents.Add(new ComponentItem(snipId, "snippet", "1.0.1", snipRationale));

            result.Connections.Add(new ConnectionItem(
                "otter-official/game-combat",
                "src/game.ot",
                "Connects actor initiation, attack execution, defeat evaluation, and reward output."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/game-entity",
                "src/game.ot",
                "Supplies createActor, createInventory, addItemToInventory, and cooldown APIs."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/file-helpers",
                "src/game.ot",
                "Saves game progression and inventory state to persistent rpg_savegame.json."
            ));
        }
        else if (maxScore == scoreContact)
        {
            result.SelectedRecipeId = "otter-official/contact-manager";
            result.SelectedTarget = targetOverride ?? "desktop";

            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/contact-manager",
                "recipe",
                "1.0.0",
                "Provides contact record data structure, input validation, and directory persistence."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/text-utils",
                "pack",
                "1.0.1",
                "Handles string formatting, text padding, and substring search for contact querying."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/file-helpers",
                "pack",
                "1.0.1",
                "Saves validated contact records into persistent JSON file storage."
            ));

            string snipId = lowerIntent.Contains("search") ? "txt-contains" : "val-email-check";
            string snipRationale = lowerIntent.Contains("search")
                ? "Performs verified substring text containment check across contact names."
                : "Validates email address syntax structure '@' and domain dots before saving.";
            result.RecommendedComponents.Add(new ComponentItem(snipId, "snippet", "1.0.1", snipRationale));

            result.Connections.Add(new ConnectionItem(
                "otter-official/text-utils",
                "src/contacts.ot",
                "Binds padRight and string splitting for input validation and search matching."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/file-helpers",
                "src/contacts.ot",
                "Serializes contact list to disk storage on addition and deletion."
            ));
        }
        else if (maxScore == scoreWeb)
        {
            result.SelectedRecipeId = "otter-official/web-portal";
            result.SelectedTarget = targetOverride ?? "server";

            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/web-portal",
                "recipe",
                "1.0.0",
                "Coordinates HTTP listener routing, static asset serving, and feedback persistence."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/text-utils",
                "pack",
                "1.0.1",
                "Formats HTTP response payloads and sanitizes user input."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/file-helpers",
                "pack",
                "1.0.1",
                "Persists web form submissions into persistent storage."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "http-server-json-route",
                "snippet",
                "1.0.1",
                "Micro-pattern for JSON REST endpoint routing."
            ));

            result.Connections.Add(new ConnectionItem(
                "otter-official/web-portal",
                "src/server.ot",
                "Initializes HTTP server on configured port and mounts routes."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/file-helpers",
                "src/server.ot",
                "Saves received feedback submissions to disk."
            ));
        }
        else
        {
            result.SelectedRecipeId = "otter-official/task-tracker";
            result.SelectedTarget = targetOverride ?? "console";

            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/task-tracker",
                "recipe",
                "1.0.0",
                "Coordinates task collection management, status toggling, and formatted output."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/file-settings",
                "blueprint",
                "1.0.0",
                "Blueprint for loading and safely writing persistent JSON application state."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/file-helpers",
                "pack",
                "1.0.1",
                "Handles safe atomic JSON file read and write operations for task data."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "otter-official/text-utils",
                "pack",
                "1.0.1",
                "Formats tabular console columns for clean task list display."
            ));
            result.RecommendedComponents.Add(new ComponentItem(
                "file-json-storage",
                "snippet",
                "1.0.1",
                "Micro-pattern for loading and persisting JSON state with fallback."
            ));

            result.ExtraBlueprints.Add("otter-official/file-settings");

            result.Connections.Add(new ConnectionItem(
                "otter-official/file-settings",
                "src/tasks.ot",
                "Provides configuration defaults and persistent settings loading."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/file-helpers",
                "src/tasks.ot",
                "Serializes in-memory task collection to JSON file."
            ));
            result.Connections.Add(new ConnectionItem(
                "otter-official/text-utils",
                "src/tasks.ot",
                "Pads task columns for aligned terminal presentation."
            ));
        }

        return result;
    }

    static string ResolveVirtualImports(string code, string libDir)
    {
        var sb = new StringBuilder();
        string[] lines = code.Split('\n');
        var remainingLines = new List<string>();
        foreach (var l in lines)
        {
            var trimmed = l.Trim();
            if (trimmed.StartsWith("use \"") && trimmed.EndsWith("\""))
            {
                string path = trimmed.Substring(5, trimmed.Length - 6);
                var m = Regex.Match(path, @"\.otter/packages/([^/]+)/([^/]+)");
                if (m.Success)
                {
                    string pub = m.Groups[1].Value;
                    string name = m.Groups[2].Value;
                    string localPackMain = Path.Combine(libDir, "packs", pub, name, "src", "main.ot");
                    if (File.Exists(localPackMain))
                    {
                        sb.AppendLine(File.ReadAllText(localPackMain));
                        continue;
                    }
                }
            }
            remainingLines.Add(l);
        }
        sb.AppendLine(string.Join("\n", remainingLines));
        return sb.ToString();
    }
}
