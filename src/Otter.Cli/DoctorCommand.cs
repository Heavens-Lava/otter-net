using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Otter.Cli;

internal static class DoctorCommand
{
    public static int Run(CommandLine line)
    {
        bool json = line.Raw.Any(a => string.Equals(a, "--json", StringComparison.OrdinalIgnoreCase));
        var checks = new List<(string Category, string Name, string Status, string Message, string? Fix)>();

        // 1. Runtime & OS
        string osDesc = RuntimeInformation.OSDescription;
        string osArch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        checks.Add(("Environment", "Operating System", "OK", $"{osDesc} ({osArch})", null));

        string dotnetVer = Environment.Version.ToString();
        checks.Add(("Environment", ".NET Runtime", "OK", $".NET {dotnetVer}", null));

        string? execPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(execPath) && File.Exists(execPath))
        {
            checks.Add(("Environment", "Otter Executable", "OK", execPath, null));
        }
        else
        {
            checks.Add(("Environment", "Otter Executable", "WARN", "Running via dotnet host or unknown binary path", null));
        }

        // 2. PATH check
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] pathDirs = pathVar.Split(Path.PathSeparator);
        string? currentExeDir = !string.IsNullOrEmpty(execPath) ? Path.GetDirectoryName(execPath) : null;
        string userOtterBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".otter", "bin");

        bool inPath = false;
        if (currentExeDir != null && pathDirs.Any(p => string.Equals(p.TrimEnd('/', '\\'), currentExeDir.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)))
        {
            inPath = true;
        }
        else if (pathDirs.Any(p => string.Equals(p.TrimEnd('/', '\\'), userOtterBin.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase)))
        {
            inPath = true;
        }

        if (inPath)
        {
            checks.Add(("Configuration", "PATH Environment", "OK", "Otter binary directory is on system PATH", null));
        }
        else
        {
            checks.Add(("Configuration", "PATH Environment", "WARN", $"Otter binary directory is not on PATH (checked '{currentExeDir}' and '{userOtterBin}')",
                $"Add '{userOtterBin}' to your PATH in ~/.zshrc or ~/.bashrc"));
        }

        // 3. User Cache & Directory Permissions
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string otterUserDir = Path.Combine(userHome, ".otter");
        string packagesDir = Path.Combine(otterUserDir, "packages");

        try
        {
            Directory.CreateDirectory(packagesDir);
            string testFile = Path.Combine(packagesDir, ".write_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            checks.Add(("Permissions", "Package Cache", "OK", $"Writable: {packagesDir}", null));
        }
        catch (Exception ex)
        {
            checks.Add(("Permissions", "Package Cache", "FAIL", $"Cannot write to {packagesDir}: {ex.Message}", "Check file permissions for ~/.otter"));
        }

        // 4. Global Library Discovery
        string? libDir = null;
        try
        {
            libDir = LibraryCommands.FindLibraryDir();
            checks.Add(("Library", "Location", "OK", libDir, null));
        }
        catch (Exception ex)
        {
            checks.Add(("Library", "Location", "FAIL", ex.Message, "Run tools/install.sh or install Otter standard library to ~/.otter/library"));
        }

        if (libDir != null && Directory.Exists(libDir))
        {
            // Snippets
            string snippetCatalog = Path.Combine(libDir, "snippets", "catalog.json");
            if (File.Exists(snippetCatalog))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(snippetCatalog));
                    int snipCount = node?["snippets"]?.AsArray()?.Count ?? 0;
                    checks.Add(("Library", "Snippets Catalog", "OK", $"{snipCount} snippets available", null));
                }
                catch (Exception ex)
                {
                    checks.Add(("Library", "Snippets Catalog", "FAIL", $"Invalid JSON: {ex.Message}", "Re-install standard library"));
                }
            }
            else
            {
                checks.Add(("Library", "Snippets Catalog", "FAIL", "Missing snippets/catalog.json", "Re-install standard library"));
            }

            // Packs
            string packsDir = Path.Combine(libDir, "packs");
            if (Directory.Exists(packsDir))
            {
                var packManifests = Directory.GetFiles(packsDir, "otter.pack.json", SearchOption.AllDirectories);
                checks.Add(("Library", "Official Packs", packManifests.Length >= 5 ? "OK" : "WARN", $"{packManifests.Length} pack(s) found", null));
            }
            else
            {
                checks.Add(("Library", "Official Packs", "FAIL", "Missing packs directory", "Re-install standard library"));
            }

            // Blueprints
            string bpDir = Path.Combine(libDir, "blueprints");
            if (Directory.Exists(bpDir))
            {
                var bpManifests = Directory.GetFiles(bpDir, "otter.blueprint.json", SearchOption.AllDirectories);
                checks.Add(("Library", "Official Blueprints", bpManifests.Length >= 4 ? "OK" : "WARN", $"{bpManifests.Length} blueprint(s) found", null));
            }
            else
            {
                checks.Add(("Library", "Official Blueprints", "FAIL", "Missing blueprints directory", "Re-install standard library"));
            }

            // Recipes
            string recDir = Path.Combine(libDir, "recipes");
            if (Directory.Exists(recDir))
            {
                var recManifests = Directory.GetFiles(recDir, "otter.recipe.json", SearchOption.AllDirectories);
                checks.Add(("Library", "Official Recipes", recManifests.Length >= 4 ? "OK" : "WARN", $"{recManifests.Length} recipe(s) found", null));
            }
            else
            {
                checks.Add(("Library", "Official Recipes", "FAIL", "Missing recipes directory", "Re-install standard library"));
            }

            // Schemas
            string schemaDir = Path.Combine(libDir, "schema");
            if (Directory.Exists(schemaDir))
            {
                var schemas = Directory.GetFiles(schemaDir, "*.schema.json");
                checks.Add(("Library", "JSON Schemas", schemas.Length >= 4 ? "OK" : "WARN", $"{schemas.Length} schema(s) verified", null));
            }
            else
            {
                checks.Add(("Library", "JSON Schemas", "FAIL", "Missing schema directory", "Re-install standard library"));
            }
        }

        // Summary
        int failCount = checks.Count(c => c.Status == "FAIL");
        int warnCount = checks.Count(c => c.Status == "WARN");
        int passCount = checks.Count(c => c.Status == "OK");

        if (json)
        {
            var arr = new JsonArray();
            foreach (var chk in checks)
            {
                var obj = new JsonObject
                {
                    ["category"] = chk.Category,
                    ["name"] = chk.Name,
                    ["status"] = chk.Status,
                    ["message"] = chk.Message
                };
                if (chk.Fix != null) obj["fix"] = chk.Fix;
                arr.Add(obj);
            }
            var root = new JsonObject
            {
                ["otterVersion"] = Program.Version,
                ["overallStatus"] = failCount == 0 ? (warnCount == 0 ? "ok" : "warning") : "error",
                ["passed"] = passCount,
                ["warnings"] = warnCount,
                ["failures"] = failCount,
                ["checks"] = arr
            };
            Console.WriteLine(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return failCount == 0 ? Cli.ExitSuccess : Cli.ExitUsageError;
        }

        Host.Line($"=== Otter Doctor Diagnostics (v{Program.Version}) ===");
        Host.Line("");

        string currentCat = "";
        foreach (var chk in checks)
        {
            if (chk.Category != currentCat)
            {
                currentCat = chk.Category;
                Host.Line($"[{currentCat}]", ConsoleColor.Cyan);
            }

            ConsoleColor color = chk.Status switch
            {
                "OK" => ConsoleColor.Green,
                "WARN" => ConsoleColor.Yellow,
                _ => ConsoleColor.Red
            };
            string tag = $"  [{chk.Status,-4}]";
            Host.Line($"{tag} {chk.Name}: {chk.Message}", color);
            if (chk.Fix != null)
            {
                Host.Line($"         -> Recommendation: {chk.Fix}", ConsoleColor.DarkGray);
            }
        }

        Host.Line("");
        if (failCount == 0)
        {
            if (warnCount == 0)
            {
                Host.Line("All diagnostics passed cleanly. Otter is ready to build and run applications!", ConsoleColor.Green);
            }
            else
            {
                Host.Line($"Otter is operational with {warnCount} notice(s).", ConsoleColor.Yellow);
            }
            return Cli.ExitSuccess;
        }

        Host.Line($"Diagnostics detected {failCount} error(s). Please review the recommendations above.", ConsoleColor.Red);
        return Cli.ExitUsageError;
    }
}
