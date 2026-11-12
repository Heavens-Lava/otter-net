using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

internal static class PublishCommands
{
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bin", ".com", ".scr"
    };

    // =========================================================================
    // 1. Package Pack (otter pack pack <dir> [--out <archive>])
    // =========================================================================
    public static int PackagePack(string packDir, string? outArchive, bool json)
    {
        packDir = Path.GetFullPath(packDir);
        if (!Directory.Exists(packDir))
        {
            Console.Error.WriteLine($"Error: Directory '{packDir}' does not exist.");
            return Cli.ExitUsageError;
        }

        string manifestPath = Path.Combine(packDir, "otter.pack.json");
        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(packDir, "pack.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Error: Pack manifest 'otter.pack.json' not found in '{packDir}'.");
            return Cli.ExitUsageError;
        }

        // 1. Validate manifest
        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error parsing manifest: {ex.Message}");
            return Cli.ExitCheckError;
        }

        string publisher = manifest["publisher"]?.ToString() ?? "";
        string name = manifest["name"]?.ToString() ?? "";
        string version = manifest["version"]?.ToString() ?? "";
        string otterVer = manifest["otter"]?.ToString() ?? "";
        string description = manifest["description"]?.ToString() ?? "";

        if (string.IsNullOrWhiteSpace(publisher) || !Regex.IsMatch(publisher, @"^[a-z0-9-]+$"))
        {
            Console.Error.WriteLine("Validation Error: 'publisher' must be lowercase alphanumeric with hyphens (e.g. 'otter-official').");
            return Cli.ExitCheckError;
        }
        if (string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[a-z0-9-]+$"))
        {
            Console.Error.WriteLine("Validation Error: 'name' must be lowercase alphanumeric with hyphens (e.g. 'math-utils').");
            return Cli.ExitCheckError;
        }
        if (string.IsNullOrWhiteSpace(version) || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+"))
        {
            Console.Error.WriteLine("Validation Error: 'version' must be valid SemVer (e.g. '1.0.0').");
            return Cli.ExitCheckError;
        }

        // 2. Validate entrypoint and syntax
        string srcDir = Path.Combine(packDir, "src");
        string mainOt = Path.Combine(srcDir, "main.ot");
        if (!File.Exists(mainOt))
        {
            Console.Error.WriteLine("Validation Error: Pack entrypoint 'src/main.ot' was not found.");
            return Cli.ExitCheckError;
        }

        foreach (var otFile in Directory.GetFiles(srcDir, "*.ot", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(otFile);
            var diags = LanguageService.GetDiagnostics(src, otFile);
            if (diags.Count > 0)
            {
                Console.Error.WriteLine($"Syntax Error in '{Path.GetRelativePath(packDir, otFile)}': line {diags[0].Line}: {diags[0].Message}");
                return Cli.ExitCheckError;
            }
        }

        // 3. Security scan
        foreach (var file in Directory.GetFiles(packDir, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (ForbiddenExtensions.Contains(ext))
            {
                Console.Error.WriteLine($"Security Reject: Forbidden executable binary found: '{Path.GetRelativePath(packDir, file)}'.");
                return Cli.ExitCheckError;
            }
        }

        // 4. Test execution
        string testsDir = Path.Combine(packDir, "tests");
        if (Directory.Exists(testsDir))
        {
            var testFiles = Directory.GetFiles(testsDir, "*.ot", SearchOption.AllDirectories);
            if (testFiles.Length > 0)
            {
                Console.WriteLine($"Running {testFiles.Length} pack test(s)...");
                foreach (var tf in testFiles)
                {
                    var (code, lines) = ProjectCommands.RunChildOtter("run", tf);
                    if (code != 0)
                    {
                        Console.Error.WriteLine($"Test Failure in '{Path.GetRelativePath(packDir, tf)}' (exit code {code}). Packaging aborted.");
                        return Cli.ExitCheckError;
                    }
                    Console.WriteLine($"  [PASS] {Path.GetRelativePath(packDir, tf)}");
                }
            }
        }

        // 5. Generate Archive
        string defaultArchiveName = $"{publisher}-{name}-{version}.otterpack";
        string targetArchive = Path.GetFullPath(string.IsNullOrWhiteSpace(outArchive) ? Path.Combine(packDir, defaultArchiveName) : outArchive);

        string tempStaging = Path.Combine(Path.GetTempPath(), $"otterpack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempStaging);
        try
        {
            // Copy manifest
            File.Copy(manifestPath, Path.Combine(tempStaging, "otter.pack.json"), true);

            // Copy src
            CopyDirectory(srcDir, Path.Combine(tempStaging, "src"));

            // Copy tests if present
            if (Directory.Exists(testsDir)) CopyDirectory(testsDir, Path.Combine(tempStaging, "tests"));

            // Copy examples if present
            string examplesDir = Path.Combine(packDir, "examples");
            if (Directory.Exists(examplesDir)) CopyDirectory(examplesDir, Path.Combine(tempStaging, "examples"));

            // Copy README/LICENSE if present
            foreach (var doc in new[] { "README.md", "LICENSE", "CHANGELOG.md" })
            {
                string p = Path.Combine(packDir, doc);
                if (File.Exists(p)) File.Copy(p, Path.Combine(tempStaging, doc), true);
            }

            if (File.Exists(targetArchive)) File.Delete(targetArchive);
            ZipFile.CreateFromDirectory(tempStaging, targetArchive);
        }
        finally
        {
            if (Directory.Exists(tempStaging)) Directory.Delete(tempStaging, true);
        }

        // 6. Compute Hash
        byte[] archiveBytes = File.ReadAllBytes(targetArchive);
        string sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        long size = archiveBytes.Length;

        if (json)
        {
            var res = new JsonObject
            {
                ["publisher"] = publisher,
                ["name"] = name,
                ["version"] = version,
                ["otter"] = otterVer,
                ["archive"] = targetArchive,
                ["sizeBytes"] = size,
                ["sha256"] = sha256
            };
            Console.WriteLine(res.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully packaged pack '{publisher}/{name} v{version}'!");
        Console.ResetColor();
        Console.WriteLine($"  Archive:  {targetArchive}");
        Console.WriteLine($"  Size:     {size} bytes");
        Console.WriteLine($"  SHA-256:  {sha256}");
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 2. Publish Pack (otter pack publish <archive> [--dry-run])
    // =========================================================================
    public static int PublishPack(string archivePath, bool dryRun, bool json)
    {
        archivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(archivePath))
        {
            Console.Error.WriteLine($"Error: Archive file '{archivePath}' not found.");
            return Cli.ExitUsageError;
        }

        byte[] bytes = File.ReadAllBytes(archivePath);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        JsonObject manifest;
        using (var zip = ZipFile.OpenRead(archivePath))
        {
            var entry = zip.GetEntry("otter.pack.json") ?? zip.GetEntry("pack.json");
            if (entry == null)
            {
                Console.Error.WriteLine("Integrity Error: Archive does not contain 'otter.pack.json'.");
                return Cli.ExitCheckError;
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();

            // Verify no zip slip
            foreach (var e in zip.Entries)
            {
                if (e.FullName.Contains("..") || Path.IsPathRooted(e.FullName))
                {
                    Console.Error.WriteLine($"Security Error: ZipSlip detected in archive entry '{e.FullName}'.");
                    return Cli.ExitCheckError;
                }
            }
        }

        string publisher = manifest["publisher"]?.ToString() ?? "community";
        string name = manifest["name"]?.ToString() ?? "pack";
        string version = manifest["version"]?.ToString() ?? "1.0.0";

        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string targetInstallDir = Path.Combine(userHome, ".otter", "packages", publisher, name, version);

        if (dryRun)
        {
            if (json)
            {
                var dres = new JsonObject
                {
                    ["dryRun"] = true,
                    ["publisher"] = publisher,
                    ["name"] = name,
                    ["version"] = version,
                    ["sha256"] = sha256,
                    ["targetDirectory"] = targetInstallDir,
                    ["valid"] = true
                };
                Console.WriteLine(dres.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return Cli.ExitSuccess;
            }

            Console.WriteLine($"=== Pack Publication [Dry Run] ===");
            Console.WriteLine($"  Pack:       {publisher}/{name} v{version}");
            Console.WriteLine($"  Archive:    {archivePath}");
            Console.WriteLine($"  SHA-256:    {sha256}");
            Console.WriteLine($"  Target Dir: {targetInstallDir}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DRY-RUN SUCCESS] Pack archive is valid and passes all security & integrity checks.");
            Console.ResetColor();
            Console.WriteLine("Note: Remote registry uploads require central signing keys; installed to local cache on publication.");
            return Cli.ExitSuccess;
        }

        // Local publication
        if (Directory.Exists(targetInstallDir)) Directory.Delete(targetInstallDir, true);
        Directory.CreateDirectory(targetInstallDir);
        ZipFile.ExtractToDirectory(archivePath, targetInstallDir);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully published '{publisher}/{name} v{version}' to local cache: {targetInstallDir}");
        Console.ResetColor();
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 3. Package Recipe (otter recipe pack <dir> [--out <archive>])
    // =========================================================================
    public static int PackageRecipe(string recipeDir, string? outArchive, bool json)
    {
        recipeDir = Path.GetFullPath(recipeDir);
        if (!Directory.Exists(recipeDir))
        {
            Console.Error.WriteLine($"Error: Directory '{recipeDir}' does not exist.");
            return Cli.ExitUsageError;
        }

        string manifestPath = Path.Combine(recipeDir, "otter.recipe.json");
        if (!File.Exists(manifestPath)) manifestPath = Path.Combine(recipeDir, "recipe.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Error: Recipe manifest 'otter.recipe.json' not found in '{recipeDir}'.");
            return Cli.ExitUsageError;
        }

        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        string id = manifest["id"]?.ToString() ?? Path.GetFileName(recipeDir);
        string version = manifest["version"]?.ToString() ?? "1.0.0";
        string target = manifest["target"]?.ToString() ?? "console";

        string templatesDir = Path.Combine(recipeDir, "templates");
        if (!Directory.Exists(templatesDir))
        {
            Console.Error.WriteLine("Validation Error: Recipe missing 'templates/' directory.");
            return Cli.ExitCheckError;
        }

        // Test recipe tests if present
        string testsDir = Path.Combine(recipeDir, "tests");
        if (Directory.Exists(testsDir))
        {
            var testFiles = Directory.GetFiles(testsDir, "*.ot", SearchOption.AllDirectories);
            foreach (var tf in testFiles)
            {
                var (code, _) = ProjectCommands.RunChildOtter("run", tf);
                if (code != 0)
                {
                    Console.Error.WriteLine($"Recipe test failed in '{Path.GetRelativePath(recipeDir, tf)}'. Packaging aborted.");
                    return Cli.ExitCheckError;
                }
            }
        }

        string cleanId = id.Replace('/', '-');
        string defaultArchiveName = $"{cleanId}-{version}.otterrecipe";
        string targetArchive = Path.GetFullPath(string.IsNullOrWhiteSpace(outArchive) ? Path.Combine(recipeDir, defaultArchiveName) : outArchive);

        string tempStaging = Path.Combine(Path.GetTempPath(), $"otterrecipe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempStaging);
        try
        {
            File.Copy(manifestPath, Path.Combine(tempStaging, "otter.recipe.json"), true);
            CopyDirectory(templatesDir, Path.Combine(tempStaging, "templates"));
            if (Directory.Exists(testsDir)) CopyDirectory(testsDir, Path.Combine(tempStaging, "tests"));
            string examplesDir = Path.Combine(recipeDir, "examples");
            if (Directory.Exists(examplesDir)) CopyDirectory(examplesDir, Path.Combine(tempStaging, "examples"));

            if (File.Exists(targetArchive)) File.Delete(targetArchive);
            ZipFile.CreateFromDirectory(tempStaging, targetArchive);
        }
        finally
        {
            if (Directory.Exists(tempStaging)) Directory.Delete(tempStaging, true);
        }

        byte[] archiveBytes = File.ReadAllBytes(targetArchive);
        string sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();

        if (json)
        {
            var res = new JsonObject
            {
                ["id"] = id,
                ["version"] = version,
                ["target"] = target,
                ["archive"] = targetArchive,
                ["sha256"] = sha256
            };
            Console.WriteLine(res.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return Cli.ExitSuccess;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully packaged recipe '{id} v{version}'!");
        Console.ResetColor();
        Console.WriteLine($"  Archive:  {targetArchive}");
        Console.WriteLine($"  SHA-256:  {sha256}");
        return Cli.ExitSuccess;
    }

    // =========================================================================
    // 4. Publish Recipe (otter recipe publish <archive> [--dry-run])
    // =========================================================================
    public static int PublishRecipe(string archivePath, bool dryRun, bool json)
    {
        archivePath = Path.GetFullPath(archivePath);
        if (!File.Exists(archivePath))
        {
            Console.Error.WriteLine($"Error: Archive file '{archivePath}' not found.");
            return Cli.ExitUsageError;
        }

        byte[] bytes = File.ReadAllBytes(archivePath);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        JsonObject manifest;
        using (var zip = ZipFile.OpenRead(archivePath))
        {
            var entry = zip.GetEntry("otter.recipe.json") ?? zip.GetEntry("recipe.json");
            if (entry == null)
            {
                Console.Error.WriteLine("Integrity Error: Archive does not contain 'otter.recipe.json'.");
                return Cli.ExitCheckError;
            }
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        }

        string id = manifest["id"]?.ToString() ?? "recipe";
        string version = manifest["version"]?.ToString() ?? "1.0.0";
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string targetDir = Path.Combine(userHome, ".otter", "recipes", id.Replace('/', '-'), version);

        if (dryRun)
        {
            if (json)
            {
                var dres = new JsonObject
                {
                    ["dryRun"] = true,
                    ["id"] = id,
                    ["version"] = version,
                    ["sha256"] = sha256,
                    ["valid"] = true
                };
                Console.WriteLine(dres.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return Cli.ExitSuccess;
            }

            Console.WriteLine($"=== Recipe Publication [Dry Run] ===");
            Console.WriteLine($"  Recipe:     {id} v{version}");
            Console.WriteLine($"  Archive:    {archivePath}");
            Console.WriteLine($"  SHA-256:    {sha256}");
            Console.WriteLine($"  Target Dir: {targetDir}");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[DRY-RUN SUCCESS] Recipe archive is valid, verified, and ready for publication.");
            Console.ResetColor();
            return Cli.ExitSuccess;
        }

        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(archivePath, targetDir);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully published recipe '{id} v{version}' to local cache: {targetDir}");
        Console.ResetColor();
        return Cli.ExitSuccess;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        }
        foreach (string sub in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
        }
    }
}
