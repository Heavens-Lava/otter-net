using System.Text;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// Where a project's manifest is (Find-OtterProjectManifest).
internal sealed record ProjectManifest(string RootDirectory, string ManifestPath, string ManifestFileName);

// An Otter project: otterPS's src/Otter.Project.psm1 (D118B, RC3 B4/B5),
// canonical otter.json with project.json as the fallback.
internal sealed class Project
{
    public required string RootDirectory { get; init; }
    public required string ManifestPath { get; init; }
    public required string ManifestFileName { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Archetype { get; init; }
    public required string Target { get; init; }
    public required string EntryPoint { get; init; }
    public required string ResolvedEntryPoint { get; init; }
    public string[] Assets { get; init; } = Array.Empty<string>();
    public string BuildOutputDir { get; init; } = "dist";
    public bool BuildClean { get; init; } = true;
    public string PublishOutputDir { get; init; } = "publish";
    // A PowerShell @{} hashtable: keys ignore capitals.
    public Dictionary<string, string> Scripts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // The host's file system ignores capitals (Windows); PowerShell's
    // Test-OtterProjectWindowsHost.
    public static bool WindowsHost => OperatingSystem.IsWindows();

    // Resolve-Path -LiteralPath: the full path of something that exists, or
    // null. PowerShell's own path forms ("~", drives such as Env:) are left
    // to the PowerShell Otter.
    public static string? PsResolvePath(string path)
    {
        if (path.StartsWith('~')) throw new NeedsPowerShell("a path starting with ~ is resolved by PowerShell");
        if (path.Length == 0) return null;
        try
        {
            string full = Path.GetFullPath(path);
            return File.Exists(full) || Directory.Exists(full) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }

    // Find-OtterProjectManifest.
    public static ProjectManifest? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = ".";
        string trimmed = path.Trim();
        if (trimmed.ToLowerInvariant().EndsWith(".ot", StringComparison.Ordinal)) return null;
        string? resolved = PsResolvePath(trimmed);
        if (resolved == null) return null;
        if (Directory.Exists(resolved))
        {
            string otterJson = Path.Combine(resolved, "otter.json");
            if (File.Exists(otterJson)) return new ProjectManifest(resolved, otterJson, "otter.json");
            string projectJson = Path.Combine(resolved, "project.json");
            if (File.Exists(projectJson)) return new ProjectManifest(resolved, projectJson, "project.json");
            return null;
        }
        string leaf = Path.GetFileName(resolved);
        if (leaf.Equals("otter.json", StringComparison.OrdinalIgnoreCase) || leaf.Equals("project.json", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectManifest(Path.GetDirectoryName(resolved)!, resolved, leaf);
        }
        return null;
    }

    static OtterError Problem(string message) => new(message, 0, "check");

    // Get-OtterManifestValueKind.
    static string ValueKind(object? value) => value switch
    {
        null => "null",
        string => "text",
        bool => "true/false",
        OtterThing => "an object",
        OtterList => "a list",
        double => "a number",
        _ => value.GetType().Name,
    };

    // Assert-OtterManifestFieldType.
    static void AssertType(string manifestName, string field, object? value, string expected)
    {
        bool ok = expected switch
        {
            "string" => value is string,
            "object" => value is OtterThing,
            "list" => value is OtterList,
            _ => value is bool,
        };
        if (ok) return;
        string expectedText = expected switch
        {
            "string" => "text (a JSON string)",
            "object" => "an object ({ ... })",
            "list" => "a list of text ([ ... ])",
            _ => "true or false",
        };
        throw Problem($"{manifestName}: property \"{field}\" must be {expectedText}, but it is {ValueKind(value)}.");
    }

    // $object.PSObject.Properties['name']: a property name ignores capitals.
    static bool Has(OtterThing thing, string name, out object? value)
    {
        foreach (string property in thing.PropertyNames)
        {
            if (string.Equals(property, name, StringComparison.OrdinalIgnoreCase))
            {
                value = thing.ReadProperty(property);
                return true;
            }
        }
        value = null;
        return false;
    }

    static bool Blank(object? value) => value is not string text || string.IsNullOrWhiteSpace(text);

    // The manifest as ConvertFrom-Json reads it.
    public static object? ReadManifestJson(string manifestPath, string manifestName)
    {
        string raw;
        try
        {
            raw = File.ReadAllText(manifestPath, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The message is .NET's wording.
            throw new NeedsPowerShell($"{manifestName} cannot be read ({e.GetType().Name}); otterPS quotes .NET's message");
        }
        if (raw.Length > 0 && raw[0] == '﻿') raw = raw.Substring(1);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return OtterJson.Read(raw);
        }
        catch (JsonProblem)
        {
            // "invalid JSON syntax: " is followed by JavaScriptSerializer's own message.
            throw new NeedsPowerShell($"{manifestName} is not valid JSON; otterPS quotes the JSON reader's message");
        }
    }

    // Get-OtterProject.
    public static Project Load(string path)
    {
        ProjectManifest found = Find(path) ?? throw Problem($"Otter: I cannot find an otter.json manifest in \"{path}\".");
        string manifestName = found.ManifestFileName;
        string rootDirectory = found.RootDirectory;

        object? parsed = ReadManifestJson(found.ManifestPath, manifestName);
        if (parsed == null) throw Problem($"{manifestName}: manifest is empty.");
        if (parsed is not OtterThing manifest)
        {
            throw Problem($"{manifestName}: the manifest must be a JSON object ({{ ... }}), but it is {ValueKind(parsed)}.");
        }

        // 1. Entry point
        string? entryPoint = null;
        string entryField = "entryPoint";
        if (Has(manifest, "entryPoint", out object? entryValue))
        {
            AssertType(manifestName, "entryPoint", entryValue, "string");
            if (!Blank(entryValue)) entryPoint = (string)entryValue!;
        }
        else if (manifestName == "project.json" && Has(manifest, "main", out object? mainValue))
        {
            AssertType(manifestName, "main", mainValue, "string");
            if (!Blank(mainValue)) entryPoint = (string)mainValue!;
            entryField = "main";
        }
        if (string.IsNullOrWhiteSpace(entryPoint)) throw Problem($"{manifestName}: property \"entryPoint\" is required.");

        string resolvedEntry;
        try
        {
            if (entryPoint.Contains('\0')) throw new ArgumentException("NUL");
            if (OperatingSystem.IsWindows() && entryPoint.IndexOfAny(new[] { '|', '<', '>', '"' }) >= 0) throw new ArgumentException("invalid");
            resolvedEntry = Path.GetFullPath(Path.Combine(rootDirectory, entryPoint));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Problem($"{manifestName}: entry point \"{entryPoint}\" is not a valid file path.");
        }
        if (!File.Exists(resolvedEntry)) throw Problem($"{manifestName}: entry point \"{entryPoint}\" does not exist.");
        ResolveInputPath(rootDirectory, entryPoint, $"{manifestName}: {entryField} \"{entryPoint}\"");

        // 2. Target
        string target = "console";
        bool hasTarget = Has(manifest, "target", out object? targetValue);
        if (hasTarget) AssertType(manifestName, "target", targetValue, "string");
        bool hasArchetype = Has(manifest, "archetype", out object? archetypeValue);
        if (hasArchetype) AssertType(manifestName, "archetype", archetypeValue, "string");
        if (hasTarget && !Blank(targetValue)) target = (string)targetValue!;
        else if (hasArchetype && !Blank(archetypeValue)) target = (string)archetypeValue!;
        string[] supportedTargets = { "console", "desktop", "web", "game", "server", "automation" };
        if (!supportedTargets.Contains(target.ToLowerInvariant())) throw Problem($"{manifestName}: target \"{target}\" is not supported.");

        foreach (string textField in new[] { "name", "version" })
        {
            if (Has(manifest, textField, out object? textValue)) AssertType(manifestName, textField, textValue, "string");
        }

        // 3. Build and publish settings
        string buildOutputDir = "dist";
        bool clean = true;
        if (Has(manifest, "build", out object? buildValue))
        {
            AssertType(manifestName, "build", buildValue, "object");
            var build = (OtterThing)buildValue!;
            if (Has(build, "outputDir", out object? outValue))
            {
                AssertType(manifestName, "build.outputDir", outValue, "string");
                string outDir = (string)outValue!;
                if (string.IsNullOrWhiteSpace(outDir)) throw Problem($"{manifestName}: build.outputDir must stay inside the project directory.");
                string resolvedOut = Path.GetFullPath(Path.Combine(rootDirectory, outDir));
                string rootWithSeparator = rootDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!resolvedOut.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw Problem($"{manifestName}: build.outputDir must stay inside the project directory.");
                }
                buildOutputDir = outDir;
            }
            foreach (string flag in new[] { "clean", "sourceMaps", "minify" })
            {
                if (Has(build, flag, out object? flagValue)) AssertType(manifestName, $"build.{flag}", flagValue, "boolean");
            }
            if (Has(build, "clean", out object? cleanValue)) clean = (bool)cleanValue!;
        }

        string publishOutputDir = "publish";
        if (Has(manifest, "publish", out object? publishValue))
        {
            AssertType(manifestName, "publish", publishValue, "object");
            if (Has((OtterThing)publishValue!, "outputDir", out object? publishOut))
            {
                AssertType(manifestName, "publish.outputDir", publishOut, "string");
                if (!string.IsNullOrWhiteSpace((string)publishOut!)) publishOutputDir = ((string)publishOut!).Trim();
            }
        }

        // 4. The project
        string archetype = hasArchetype && !Blank(archetypeValue) ? (string)archetypeValue! : target.ToLowerInvariant();
        string name = Has(manifest, "name", out object? nameValue) && !Blank(nameValue) ? (string)nameValue! : Path.GetFileName(rootDirectory);
        string version = Has(manifest, "version", out object? versionValue) && !Blank(versionValue) ? (string)versionValue! : "0.1.0";

        string[] assets = Array.Empty<string>();
        if (Has(manifest, "assets", out object? assetsValue))
        {
            AssertType(manifestName, "assets", assetsValue, "list");
            var list = (OtterList)assetsValue!;
            for (int i = 0; i < list.Count; i++) AssertType(manifestName, $"assets[{i}]", list[i], "string");
            assets = Enumerable.Range(0, list.Count).Select(i => (string)list[i]!).ToArray();
        }

        var scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Has(manifest, "scripts", out object? scriptsValue))
        {
            AssertType(manifestName, "scripts", scriptsValue, "object");
            var scriptsThing = (OtterThing)scriptsValue!;
            foreach (string scriptName in scriptsThing.PropertyNames)
            {
                object? command = scriptsThing.ReadProperty(scriptName);
                AssertType(manifestName, $"scripts.{scriptName}", command, "string");
                scripts[scriptName] = (string)command!;
            }
        }

        return new Project
        {
            RootDirectory = rootDirectory,
            ManifestPath = found.ManifestPath,
            ManifestFileName = manifestName,
            EntryPoint = entryPoint,
            ResolvedEntryPoint = resolvedEntry,
            Target = target.ToLowerInvariant(),
            Archetype = archetype,
            Name = name,
            Version = version,
            BuildOutputDir = buildOutputDir,
            BuildClean = clean,
            PublishOutputDir = publishOutputDir,
            Assets = assets,
            Scripts = scripts,
        };
    }

    // Get-OtterRealPath: every symbolic link or junction along the path
    // (the file itself and each folder above it) replaced by where it points.
    public static string RealPath(string path)
    {
        char[] separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full) ?? "";
        var pending = new List<string>(full.Substring(current.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries));
        int linkHops = 0;
        while (pending.Count > 0)
        {
            string segment = pending[0];
            pending.RemoveAt(0);
            if (segment == ".") continue;
            if (segment == "..")
            {
                string? parent = Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(parent)) current = parent;
                continue;
            }
            string candidate = Path.Combine(current, segment);
            string? linkTarget = null;
            try
            {
                FileSystemInfo info = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
                if (info.Exists && info.LinkTarget != null) linkTarget = info.LinkTarget;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (string.IsNullOrEmpty(linkTarget))
            {
                current = candidate;
                continue;
            }
            linkHops++;
            if (linkHops > 64) throw Problem($"Otter: too many symbolic links or junctions while resolving \"{path}\".");
            if (linkTarget.StartsWith(@"\??\", StringComparison.Ordinal)) linkTarget = linkTarget.Substring(4);
            if (!Path.IsPathRooted(linkTarget)) linkTarget = Path.Combine(current, linkTarget);
            string targetFull = Path.GetFullPath(linkTarget);
            current = Path.GetPathRoot(targetFull) ?? "";
            pending.InsertRange(0, targetFull.Substring(current.Length).Split(separators, StringSplitOptions.RemoveEmptyEntries));
        }
        return current;
    }

    // Resolve-OtterProjectInputPath (B4): an input must really be inside the project.
    public static string ResolveInputPath(string rootDirectory, string inputPath, string fieldDescription)
    {
        string realRoot = RealPath(rootDirectory);
        string realInput = RealPath(Path.Combine(rootDirectory, inputPath));
        string rootWithSeparator = realRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = WindowsHost ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!realInput.StartsWith(rootWithSeparator, comparison))
        {
            throw Problem($"{fieldDescription} escapes the project directory: it resolves to \"{realInput}\", which is outside \"{realRoot}\". Every file Otter builds or publishes must be inside the project folder (symbolic links and junctions are followed).");
        }
        return realInput;
    }
}
