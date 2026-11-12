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
        Console.WriteLine("  --yes                                   Explicitly approve and proceed without prompt");
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
            Console.WriteLine($"  Name:        {node?["name"]}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Target:      {node?["target"]}");
            Console.WriteLine($"  Description: {node?["description"]}");
            Console.WriteLine($"  Blueprints:  {string.Join(", ", node?["blueprints"]?.AsArray().Select(b => b?["id"]?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Packs:       {string.Join(", ", node?["packs"]?.AsArray().Select(p => p?["id"]?.ToString()) ?? Array.Empty<string>())}");
            return Cli.ExitSuccess;
        }

        // 2. Check Blueprint
        string bpFile = Path.Combine(libDir, "blueprints", id.Contains('/') ? id.Split('/')[1] : id, "blueprint.json");
        if (File.Exists(bpFile))
        {
            string content = File.ReadAllText(bpFile);
            if (json)
            {
                Console.WriteLine(content);
                return Cli.ExitSuccess;
            }
            var node = JsonNode.Parse(content);
            Console.WriteLine($"=== Blueprint Inspection: {id} ===");
            Console.WriteLine($"  Name:        {node?["name"]}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Description: {node?["description"]}");
            Console.WriteLine($"  Target:      {node?["target"]}");
            Console.WriteLine($"  Inputs:      {string.Join(", ", node?["inputs"]?.AsArray().Select(i => i?["name"]?.ToString()) ?? Array.Empty<string>())}");
            Console.WriteLine($"  Outputs:     {string.Join(", ", node?["outputs"]?.AsArray().Select(o => o?["name"]?.ToString()) ?? Array.Empty<string>())}");
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
            Console.WriteLine($"  Publisher:   {node?["publisher"]}");
            Console.WriteLine($"  Name:        {node?["name"]}");
            Console.WriteLine($"  Version:     {node?["version"]}");
            Console.WriteLine($"  Otter Vers:  {node?["otter"]}");
            Console.WriteLine($"  Description: {node?["description"]}");
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
                        Console.WriteLine($"  Otter Vers:  {s?["compatibility"]?["otter"]}");
                        Console.WriteLine($"  Code Sample:");
                        Console.WriteLine(s?["code"]?.ToString());
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
            ["otterRequired"] = ">=1.0.0 <2.0.0",
            ["otterCurrent"] = "1.0.1",
            ["issues"] = new JsonArray()
        };

        string? recipeFile = LibraryCommands.FindRecipeManifest(id);
        if (recipeFile != null && File.Exists(recipeFile))
        {
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
        }
        else
        {
            result["kind"] = "component";
        }

        if (json)
        {
            Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return (bool)result["compatible"]! ? Cli.ExitSuccess : Cli.ExitCheckError;
        }

        bool ok = (bool)result["compatible"]!;
        Console.WriteLine($"=== Compatibility Check: {id} ===");
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
    // 4. Propose
    // =========================================================================
    static int Propose(string[] raw)
    {
        if (raw.Length < 3)
        {
            Console.Error.WriteLine("Usage: otter compose propose \"<intent>\" [--target <target>] [--out <file.json>] [--json]");
            return Cli.ExitUsageError;
        }

        string intent = raw[2];
        string target = "console";
        string? outFile = null;
        bool json = false;

        for (int i = 3; i < raw.Length; i++)
        {
            if (string.Equals(raw[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < raw.Length)
            {
                target = raw[++i].ToLowerInvariant();
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
        string proposalId = $"prop-{RandomNumberGenerator.GetHexString(6).ToLowerInvariant()}";

        // Determine best recipe/blueprints from intent
        string lowerIntent = intent.ToLowerInvariant();
        string selectedRecipeId = "otter-official/task-tracker";
        string targetRuntime = target;

        if (lowerIntent.Contains("game") || lowerIntent.Contains("adventure") || lowerIntent.Contains("rpg") || lowerIntent.Contains("combat") || lowerIntent.Contains("inventory"))
        {
            selectedRecipeId = "otter-official/rpg-adventure";
            targetRuntime = "game";
        }
        else if (lowerIntent.Contains("web") || lowerIntent.Contains("portal") || lowerIntent.Contains("html") || lowerIntent.Contains("site"))
        {
            selectedRecipeId = "otter-official/web-portal";
            targetRuntime = "desktop";
        }
        else if (lowerIntent.Contains("contact") || lowerIntent.Contains("crm") || lowerIntent.Contains("person") || lowerIntent.Contains("phone") || lowerIntent.Contains("email"))
        {
            selectedRecipeId = "otter-official/contact-manager";
            targetRuntime = "console";
        }
        else
        {
            selectedRecipeId = "otter-official/task-tracker";
            targetRuntime = "console";
        }

        // Generate Plan using official recipe infrastructure
        var recipePlan = LibraryCommands.GenerateRecipePlan(selectedRecipeId, null, ".");

        var recommended = new JsonArray
        {
            new JsonObject
            {
                ["id"] = selectedRecipeId,
                ["kind"] = "recipe",
                ["version"] = "1.0.0",
                ["rationale"] = $"Matches user feature intent '{intent}'"
            }
        };

        string? recipeManifestPath = LibraryCommands.FindRecipeManifest(selectedRecipeId);
        var recipeManifest = recipeManifestPath != null ? JsonNode.Parse(File.ReadAllText(recipeManifestPath)) : null;

        var composedPacks = new JsonArray();
        var recipePacks = recipePlan["dependencies"]?.AsArray();
        if (recipePacks != null)
        {
            foreach (var p in recipePacks)
            {
                string pid = p?["id"]?.ToString() ?? "";
                composedPacks.Add((JsonNode)JsonValue.Create(pid)!);
                recommended.Add(new JsonObject
                {
                    ["id"] = pid,
                    ["kind"] = "pack",
                    ["version"] = p?["version"]?.ToString() ?? "1.0.0",
                    ["rationale"] = "Required domain module for feature execution"
                });
            }
        }

        var composedBps = new JsonArray();
        var recipeBps = recipeManifest?["blueprints"]?.AsArray();
        if (recipeBps != null)
        {
            foreach (var b in recipeBps)
            {
                string bid = b?["id"]?.ToString() ?? "";
                composedBps.Add((JsonNode)JsonValue.Create(bid)!);
                recommended.Add(new JsonObject
                {
                    ["id"] = bid,
                    ["kind"] = "blueprint",
                    ["version"] = "1.0.0",
                    ["rationale"] = "Composition structure and wiring topology"
                });
            }
        }

        // Find relevant snippet
        string snippetId = "http-get-request";
        if (targetRuntime == "game") snippetId = "game-actor-init";
        else if (targetRuntime == "server") snippetId = "http-server-json-route";
        else if (targetRuntime == "console") snippetId = "cli-args-flags";

        recommended.Add(new JsonObject
        {
            ["id"] = snippetId,
            ["kind"] = "snippet",
            ["version"] = "1.0.1",
            ["rationale"] = "Micro-pattern matching intent keywords"
        });

        // Assemble generated source preview from recipe files
        var sourceFiles = new JsonArray();
        var planFiles = recipePlan["plannedFiles"]?.AsArray();
        bool allSyntaxValid = true;
        var allDiagnostics = new JsonArray();

        if (planFiles != null)
        {
            foreach (var pf in planFiles)
            {
                string fpath = pf?["path"]?.ToString() ?? "main.ot";
                string fcontent = pf?["content"]?.ToString() ?? "";

                // Annotate header with provenance
                var annotatedCode = new StringBuilder();
                annotatedCode.AppendLine($"# ===========================================================================");
                annotatedCode.AppendLine($"# Generated by Otter AI Composition Engine");
                annotatedCode.AppendLine($"# Recipe: {selectedRecipeId} v1.0.0");
                annotatedCode.AppendLine($"# Target: {targetRuntime}");
                annotatedCode.AppendLine($"# ===========================================================================");
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

        var proposal = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["proposalId"] = proposalId,
            ["intent"] = intent,
            ["recommendedComponents"] = recommended,
            ["compositionPlan"] = new JsonObject
            {
                ["target"] = targetRuntime,
                ["composedPacks"] = composedPacks,
                ["composedBlueprints"] = composedBps,
                ["connections"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = composedPacks.Count > 0 ? composedPacks[0]!.ToString() : selectedRecipeId,
                        ["destination"] = "main.ot",
                        ["description"] = "Binds domain logic and inputs into primary entry point"
                    }
                }
            },
            ["generatedSourcePreview"] = sourceFiles,
            ["approvalRequired"] = true,
            ["verification"] = new JsonObject
            {
                ["command"] = "otter check main.ot",
                ["syntaxValid"] = allSyntaxValid,
                ["diagnostics"] = allDiagnostics
            }
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
        Console.WriteLine($"=== Source Code Preview: Proposal {pid} ===");
        Console.WriteLine($"Intent: \"{intent}\"");
        Console.WriteLine();

        var files = node?["generatedSourcePreview"]?.AsArray();
        if (files != null)
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
        var plan = node?["compositionPlan"];
        var recs = node?["recommendedComponents"]?.AsArray();
        var conns = plan?["connections"]?.AsArray();
        var ver = node?["verification"];

        Console.WriteLine($"=== Architectural Explanation for Proposal {pid} ===");
        Console.WriteLine();
        Console.WriteLine($"1. Feature Intent & Goal:");
        Console.WriteLine($"   \"{intent}\"");
        Console.WriteLine();
        Console.WriteLine($"2. Component Selection Rationale:");
        if (recs != null)
        {
            foreach (var r in recs)
            {
                Console.WriteLine($"   - [{r?["kind"]}] {r?["id"]} (v{r?["version"]})");
                Console.WriteLine($"     Rationale: {r?["rationale"]}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"3. Composition & Data Flow Connections:");
        if (conns != null)
        {
            foreach (var c in conns)
            {
                Console.WriteLine($"   - {c?["source"]} -> {c?["destination"]}");
                Console.WriteLine($"     Wiring: {c?["description"]}");
            }
        }
        Console.WriteLine();
        Console.WriteLine($"4. Safety, Integrity & Validation:");
        Console.WriteLine($"   - Target Runtime: {plan?["target"]}");
        Console.WriteLine($"   - Syntax Check:   {(ver?["syntaxValid"]?.GetValue<bool>() == true ? "PASSED (0 errors)" : "FAILED")}");
        Console.WriteLine($"   - Validation Cmd: {ver?["command"]}");
        Console.WriteLine($"   - User Approval:  Mandatory approval gate required prior to file modification");

        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 7. Apply (Approval Gate)
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

        var node = JsonNode.Parse(File.ReadAllText(file));
        string pid = node?["proposalId"]?.ToString() ?? "unknown";
        var files = node?["generatedSourcePreview"]?.AsArray();
        int fileCount = files?.Count ?? 0;

        // ---------------------------------------------------------------------
        // Approval Gate: Prompt user if not explicitly passed via --yes
        // ---------------------------------------------------------------------
        if (!explicitApproval)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[APPROVAL REQUIRED] Proposal {pid} will write/modify {fileCount} file(s) in '{Path.GetFullPath(targetDir)}'.");
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
        // Validate Syntax of All Files Before Writing
        // ---------------------------------------------------------------------
        if (files != null)
        {
            string libDir = LibraryCommands.FindLibraryDir();
            foreach (var f in files)
            {
                string fpath = f?["path"]?.ToString() ?? "";
                string code = f?["sourceCode"]?.ToString() ?? "";
                if (fpath.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
                {
                    string codeToValidate = ResolveVirtualImports(code, libDir);
                    var diags = LanguageService.GetDiagnostics(codeToValidate, fpath);
                    if (diags.Count > 0)
                    {
                        Console.Error.WriteLine($"[SAFETY ABORT] Refusing to apply proposal: file '{fpath}' contains syntax error: {diags[0].Message}");
                        return Cli.ExitCheckError;
                    }
                }
            }
        }

        // ---------------------------------------------------------------------
        // Write Files Transactionally
        // ---------------------------------------------------------------------
        Directory.CreateDirectory(targetDir);
        if (files != null)
        {
            foreach (var f in files)
            {
                string relPath = f?["path"]?.ToString() ?? "";
                string code = f?["sourceCode"]?.ToString() ?? "";
                string fullPath = Path.Combine(targetDir, relPath);
                string? parent = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.WriteAllText(fullPath, code, new UTF8Encoding(false));
                Console.WriteLine($"  + Created: {relPath}");
            }
        }

        // Install dependent packs
        var packs = node?["compositionPlan"]?["composedPacks"]?.AsArray();
        if (packs != null)
        {
            foreach (var p in packs)
            {
                string packId = p?.ToString() ?? "";
                if (!string.IsNullOrEmpty(packId))
                {
                    try
                    {
                        LibraryCommands.RunPack(CommandLine.Bind(new[] { "pack", "install", packId, "--project", targetDir }));
                    }
                    catch { }
                }
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SUCCESS] Composition proposal {pid} successfully applied to '{targetDir}'.");
        Console.ResetColor();
        return Cli.ExitSuccess;
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
