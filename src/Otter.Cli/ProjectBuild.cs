using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// `otter build` and `otter publish` (otterPS's Invoke-OtterProjectBuild and
// Invoke-OtterProjectPublish). Web, desktop and game projects are built by
// the web compiler (WebCommand.CompilePage): their page is compiled before
// anything is printed, so a page it cannot make yet goes to the PowerShell
// Otter whole.
internal static partial class ProjectCommands
{
    static readonly string[] WebTargets = { "web", "desktop", "game" };

    public static int Build(CommandLine line)
    {
        string target = line.HasTarget ? line.Target! : ".";
        try
        {
            return BuildProject(target, quiet: false, forceClean: false, alreadyChecked: false);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not CliExit and not NeedsPowerShell)
        {
            return ReportProjectFailure(failure);
        }
    }

    static OtterError CheckProblem(string message) => new(message, 0, "check");

    // What the build's "Checking project..." step finds: nothing (null), an
    // Otter error, or Otter's own bug. Something not native yet throws
    // NeedsPowerShell, before anything is written or printed.
    static Exception? CheckProgram(Project project, out ResolvedProgram? resolved)
    {
        resolved = null;
        try
        {
            resolved = Modules.Resolve(project.ResolvedEntryPoint);
        }
        catch (OtterError error)
        {
            return error;
        }
        string source = resolved.CombinedSource;
        FileMode.Compiled compiled = FileMode.Compile(source, source.Replace("\r\n", "\n").Split('\n'), showTokens: false);
        if (compiled.Failure is OtterError otterError) return resolved.Remap(otterError);
        return compiled.Failure;
    }

    static bool IsWebTarget(Project project) => WebTargets.Contains(project.Target.ToLowerInvariant());

    // The page Export-OtterWebApplication -PassThruExceptions writes for a
    // web, desktop or game project: compiled ahead, or the OtterError the
    // build reports when it gets there.
    static string? preparedPage;
    static OtterError? preparedPageFailure;

    static void PrepareWebPage(Project project)
    {
        preparedPage = null;
        preparedPageFailure = null;
        if (!IsWebTarget(project)) return;
        try
        {
            ResolvedProgram resolved = Modules.Resolve(project.ResolvedEntryPoint);
            preparedPage = WebCommand.CompilePage(resolved, project.ResolvedEntryPoint);
        }
        catch (NotSupportedYet unsupported)
        {
            throw new NeedsPowerShell($"{unsupported.Feature} (line {unsupported.Line}) is not native yet");
        }
        catch (OtterError error)
        {
            preparedPageFailure = error;
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            throw new NeedsPowerShell($"the web compiler stopped unexpectedly ({unexpected.GetType().Name})");
        }
    }

    static int BuildFailed(string message, int code)
    {
        Host.Line("Build failed.", ConsoleColor.Red);
        Host.Line("");
        Host.Line(message, ConsoleColor.Red);
        Host.Line("");
        return code;
    }

    static int BuildProject(string target, bool quiet, bool forceClean, bool alreadyChecked)
    {
        if (string.IsNullOrWhiteSpace(target)) target = ".";
        Project project = Project.Load(target);
        Exception? checkFailure = CheckProgram(project, out ResolvedProgram? resolved);
        if (!alreadyChecked)
        {
            if (checkFailure == null) PrepareWebPage(project);
            Program.Trace("native: build");
        }

        string rootDirectory = project.RootDirectory;
        bool cleanOutput = project.BuildClean || forceClean;

        // 1. The output folder must be inside the project, and not the project itself.
        string resolvedOutDir = Path.GetFullPath(Path.Combine(rootDirectory, project.BuildOutputDir));
        if (resolvedOutDir == rootDirectory || !resolvedOutDir.StartsWith(rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw CheckProblem("otter.json: build.outputDir must stay inside the project directory and cannot be the project root itself.");
        }
        AssertReplaceableOutputDir(project, resolvedOutDir, "otter.build.json", "otter.json: build.outputDir", cleanOutput);

        if (!quiet)
        {
            Host.Line($"Building {project.Name}...");
            Host.Line($"Target: {project.Target}");
            Host.Line("");
            Host.Line("Checking project...");
        }

        // 2. Syntax and modules, before any output is touched.
        if (checkFailure is OtterError otterError) return BuildFailed(otterError.FormatDetailed(), 2);
        if (checkFailure != null) return BuildFailed($"Otter hit a problem inside itself: {checkFailure.Message}", 1);

        // 3. Assets.
        var realAssetPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string asset in project.Assets)
        {
            if (string.IsNullOrWhiteSpace(asset)) continue;
            string trimmed = asset.Trim();
            if (trimmed.StartsWith('/') || trimmed.StartsWith('\\') || DriveLetter().IsMatch(trimmed))
            {
                return BuildFailed($"otter.json: asset \"{asset}\" cannot be an absolute path.", 1);
            }
            string resolvedAsset = Path.GetFullPath(Path.Combine(rootDirectory, trimmed));
            if (!resolvedAsset.StartsWith(rootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return BuildFailed($"otter.json: asset \"{asset}\" escapes the project directory.", 1);
            }
            if (!File.Exists(resolvedAsset))
            {
                return BuildFailed($"otter.json: declared asset \"{asset}\" does not exist.", 1);
            }
            try
            {
                realAssetPaths[trimmed] = Project.ResolveInputPath(rootDirectory, trimmed, $"otter.json: asset \"{asset}\"");
            }
            catch (OtterError error)
            {
                return BuildFailed(error.Message, 1);
            }
        }

        // B4: the page inlines <entry>.css, so it must stay inside the project too.
        if (IsWebTarget(project))
        {
            string sidecarCss = Path.ChangeExtension(project.ResolvedEntryPoint, ".css");
            if (File.Exists(sidecarCss) || Directory.Exists(sidecarCss))
            {
                try
                {
                    Project.ResolveInputPath(rootDirectory, sidecarCss,
                        $"otter.json: stylesheet \"{Path.GetFileName(sidecarCss)}\" (beside entryPoint \"{project.EntryPoint}\")");
                }
                catch (OtterError error)
                {
                    return BuildFailed(error.Message, 1);
                }
            }
        }

        // 4. A staging folder, promoted at the end.
        string stagingDirectory = Path.Combine(rootDirectory, ".otter_build_staging_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            if (!quiet) Host.Line("Building application...");
            string targetLower = project.Target.ToLowerInvariant();
            string entryLeaf = Path.GetFileName(project.ResolvedEntryPoint);
            if (IsWebTarget(project))
            {
                // Export-OtterWebApplication -OutputPath index.html -PassThruExceptions.
                if (preparedPageFailure != null) throw preparedPageFailure;
                SetContentUtf8(Path.Combine(stagingDirectory, "index.html"), preparedPage!);
                if (targetLower == "desktop")
                {
                    SetContentAscii(Path.Combine(stagingDirectory, "run-desktop.cmd"), "@echo off\r\notter desktop \"%~dp0index.html\" %*\r\n");
                }
                entryLeaf = "index.html";
            }
            else if (targetLower is not ("console" or "automation"))
            {
                return BuildFailed($"Otter: target \"{project.Target}\" is not supported for build.", 1);
            }
            else
            {
                SetContentUtf8(Path.Combine(stagingDirectory, entryLeaf), resolved!.CombinedSource);

                var builtManifest = new OtterThing("thing");
                builtManifest.WriteProperty("$schema", "https://otter-lang.org/schema/project-v1.json");
                builtManifest.WriteProperty("name", project.Name);
                builtManifest.WriteProperty("version", project.Version);
                builtManifest.WriteProperty("archetype", project.Archetype);
                builtManifest.WriteProperty("target", project.Target);
                builtManifest.WriteProperty("entryPoint", entryLeaf);
                SetContentUtf8(Path.Combine(stagingDirectory, "otter.json"), OtterJson.Write(builtManifest)!);

                // run.cmd for Windows, run for macOS and Linux (D129).
                SetContentAscii(Path.Combine(stagingDirectory, "run.cmd"), $"@echo off\r\notter run \"%~dp0{entryLeaf}\" %*\r\n");
                string launcher = Path.Combine(stagingDirectory, "run");
                File.WriteAllText(launcher, $"#!/bin/sh\nexec otter run \"$(dirname \"$0\")/{entryLeaf}\" \"$@\"\n", Utf8NoBom);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(launcher, File.GetUnixFileMode(launcher) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            }

            // 5. Declared assets.
            if (project.Assets.Length > 0)
            {
                if (!quiet) Host.Line("Copying assets...");
                foreach (string asset in project.Assets)
                {
                    if (string.IsNullOrWhiteSpace(asset)) continue;
                    string trimmed = asset.Trim();
                    string destination = Path.GetFullPath(Path.Combine(stagingDirectory, trimmed));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(realAssetPaths[trimmed], destination, overwrite: true);
                }
            }

            // 6. Build metadata.
            var meta = new OtterThing("thing");
            meta.WriteProperty("name", project.Name);
            meta.WriteProperty("version", project.Version);
            meta.WriteProperty("target", project.Target);
            meta.WriteProperty("entryPoint", entryLeaf);
            meta.WriteProperty("assets", new OtterList(project.Assets.Select(a => (object?)a)));
            SetContentUtf8(Path.Combine(stagingDirectory, "otter.build.json"), OtterJson.Write(meta)!);

            // 7. Promotion.
            if (cleanOutput && (Directory.Exists(resolvedOutDir) || File.Exists(resolvedOutDir))) DeleteItem(resolvedOutDir);
            Directory.CreateDirectory(resolvedOutDir);
            foreach (string item in PowerShellFiles.ChildItems(stagingDirectory, recurse: false, files: false, force: true))
            {
                CopyItem(item, resolvedOutDir);
            }
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        if (!quiet)
        {
            Host.Line("");
            Host.Line("Build succeeded.", ConsoleColor.Green);
            string relativeOutput = resolvedOutDir.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase)
                ? resolvedOutDir.Substring(rootDirectory.Length).TrimStart('\\', '/')
                : project.BuildOutputDir;
            Host.Line($"Output: {relativeOutput.Replace('\\', '/')}/");
        }
        return 0;
    }

    [GeneratedRegex("^[a-zA-Z]:")]
    private static partial Regex DriveLetter();

    static void DeleteItem(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    // Copy-Item -LiteralPath <item> -Destination <folder> -Recurse -Force:
    // the item lands inside the folder; a folder merges into one already there.
    static void CopyItem(string source, string destinationFolder)
    {
        string destination = Path.Combine(destinationFolder, Path.GetFileName(source));
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            foreach (string child in Directory.GetFileSystemEntries(source)) CopyItem(child, destination);
        }
        else
        {
            File.Copy(source, destination, overwrite: true);
        }
    }

    // Assert-OtterReplaceableOutputDir: an output folder is replaced only
    // when Otter can tell it owns it.
    static void AssertReplaceableOutputDir(Project project, string resolvedDir, string markerFileName, string settingName, bool willDelete)
    {
        char separator = Path.DirectorySeparatorChar;
        string directoryWithSeparator = resolvedDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + separator;
        string rootDirectory = project.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string relative = resolvedDir.Substring(Math.Min(rootDirectory.Length, resolvedDir.Length));
        foreach (string segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
            {
                throw CheckProblem($"{settingName} cannot be a .git folder or inside one: {resolvedDir}. Choose a different output folder.");
            }
        }
        foreach (string protectedFile in new[] { project.ResolvedEntryPoint, project.ManifestPath })
        {
            if (string.IsNullOrWhiteSpace(protectedFile)) continue;
            string protectedFull = Path.GetFullPath(protectedFile);
            if (protectedFull.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw CheckProblem($"{settingName} \"{resolvedDir}\" contains the project's own file \"{protectedFull}\". Choose an empty output folder such as \"dist\" or \"publish\".");
            }
        }
        if (!willDelete) return;
        if (!Directory.Exists(resolvedDir) && !File.Exists(resolvedDir)) return;
        if (!Directory.Exists(resolvedDir))
        {
            throw CheckProblem($"{settingName} \"{resolvedDir}\" is a file, not a folder. Otter will not delete it; choose a different output folder or remove the file yourself.");
        }
        if (!Directory.EnumerateFileSystemEntries(resolvedDir).Any()) return;
        if (File.Exists(Path.Combine(resolvedDir, markerFileName))) return;
        throw CheckProblem($"{settingName} \"{resolvedDir}\" already exists and was not created by Otter (it has no {markerFileName}), so Otter will not delete it. Choose an empty or new output folder, or delete that folder yourself if you no longer need its contents.");
    }

    // Get-OtterSafeFileName.
    static string SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "app";
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            bool invalid = c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || c < 32;
            sb.Append(invalid || char.IsWhiteSpace(c) ? '-' : c);
        }
        string sanitized = Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "app" : sanitized;
    }

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z.+-]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlainVersion();

    public static int Publish(CommandLine line)
    {
        // `--output`/`-Output` start with "-", so they have already been
        // handed to the PowerShell Otter by the binder.
        string target = line.HasTarget ? line.Target! : ".";
        try
        {
            return PublishProject(target);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not CliExit and not NeedsPowerShell)
        {
            return ReportProjectFailure(failure);
        }
    }

    static int PublishFailed(string message)
    {
        Host.Line("Publish failed.", ConsoleColor.Red);
        Host.Line("");
        Host.Line(message, ConsoleColor.Red);
        Host.Line("");
        return 1;
    }

    static int PublishProject(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) target = ".";
        Project project = Project.Load(target);
        // Decide before anything is printed whether the program is native.
        if (CheckProgram(project, out _) == null) PrepareWebPage(project);
        Program.Trace("native: publish");
        string rootDirectory = project.RootDirectory;

        // 1. name and version are required to publish.
        var manifest = Project.ReadManifestJson(project.ManifestPath, project.ManifestFileName) as OtterThing;
        string? RawText(string field)
        {
            if (manifest == null) return null;
            foreach (string property in manifest.PropertyNames)
            {
                if (string.Equals(property, field, StringComparison.OrdinalIgnoreCase)) return manifest.ReadProperty(property) as string ?? "";
            }
            return null;
        }
        if (string.IsNullOrWhiteSpace(RawText("name"))) throw CheckProblem("otter.json: property \"name\" is required for publish.");
        if (string.IsNullOrWhiteSpace(RawText("version"))) throw CheckProblem("otter.json: property \"version\" is required for publish.");

        // 2. The publish folder.
        string outDirName = string.IsNullOrWhiteSpace(project.PublishOutputDir) ? "publish" : project.PublishOutputDir.Trim();
        if (outDirName.StartsWith('/') || outDirName.StartsWith('\\') || DriveLetter().IsMatch(outDirName))
        {
            throw CheckProblem($"publish.outputDir cannot be an absolute path: {outDirName}");
        }
        string resolvedPublishDir = Path.GetFullPath(Path.Combine(rootDirectory, outDirName));
        string rootWithSeparator = rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (resolvedPublishDir == rootDirectory || !resolvedPublishDir.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw CheckProblem("publish.outputDir must stay inside the project directory and cannot be the project root itself.");
        }
        AssertReplaceableOutputDir(project, resolvedPublishDir, "otter.publish.json", "publish.outputDir", willDelete: true);

        string safeName = SafeFileName(project.Name);
        string version = project.Version.Trim();
        if (string.IsNullOrEmpty(version) || !PlainVersion().IsMatch(version) || version.Contains(".."))
        {
            throw CheckProblem($"otter.json: property \"version\" must be a plain version such as \"1.2.3\" or \"1.0.0-rc.2\" (letters, digits, '.', '+', '-'; no '..' or path separators) to publish. Got: \"{version}\".");
        }
        string packageFolder = $"{safeName}-{version}";
        string zipName = $"{safeName}-{version}.zip";
        string sha256Name = $"{safeName}-{version}.zip.sha256";

        Host.Line($"Publishing {project.Name} {version}...");
        Host.Line($"Target: {project.Target}");
        Host.Line("");

        // 3. A clean build first.
        Host.Line("Building project...");
        int buildExitCode = BuildProject(rootDirectory, quiet: true, forceClean: true, alreadyChecked: true);
        if (buildExitCode != 0)
        {
            Host.Line("Publish failed.", ConsoleColor.Red);
            return buildExitCode;
        }

        // 4. Package in a staging folder, then promote.
        string stagingDirectory = Path.Combine(rootDirectory, ".otter_publish_staging_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        string fileHash;
        try
        {
            Host.Line("Packaging files...");
            string stagedPackage = Path.Combine(stagingDirectory, packageFolder);
            string stagingWithSeparator = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string candidate in new[] { stagedPackage, Path.Combine(stagingDirectory, zipName) })
            {
                if (!Path.GetFullPath(candidate).StartsWith(stagingWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    throw CheckProblem($"otter.json: name and version must not form a path outside the publish staging folder: {candidate}");
                }
            }
            Directory.CreateDirectory(stagedPackage);

            string resolvedBuildOutDir = Path.GetFullPath(Path.Combine(rootDirectory, project.BuildOutputDir));
            if (!Directory.Exists(resolvedBuildOutDir))
            {
                return PublishFailed($"Build output directory does not exist: {resolvedBuildOutDir}");
            }
            foreach (string item in PowerShellFiles.ChildItems(resolvedBuildOutDir, recurse: false, files: false, force: true))
            {
                CopyItem(item, stagedPackage);
            }

            var includedFiles = PowerShellFiles.ChildItems(stagedPackage, recurse: true, files: true)
                .Select(f => f.Substring(stagedPackage.Length).TrimStart('\\', '/').Replace('\\', '/'))
                .ToList();
            includedFiles.Sort(PowerShellFiles.SortObjectComparer);

            string stagedZip = Path.Combine(stagingDirectory, zipName);
            WriteDeterministicZip(stagedPackage, stagedZip);

            Host.Line("Computing SHA-256...");
            fileHash = Sha256(stagedZip);
            File.WriteAllText(Path.Combine(stagingDirectory, sha256Name), $"{fileHash}  {zipName}\r\n", Utf8NoBom);

            string runtimeRequirements = project.Target.ToLowerInvariant() switch
            {
                "web" => "Modern web browser with JavaScript enabled.",
                "game" => "Modern web browser with HTML5 Canvas / WebGL enabled.",
                "desktop" => "Requires Otter runtime (otter in PATH) to run desktop application shell.",
                "console" or "automation" => "Requires Otter runtime (otter in PATH) to execute application.",
                _ => "Standard execution environment.",
            };
            string entryRelative = IsWebTarget(project) ? "index.html" : Path.GetFileName(project.ResolvedEntryPoint);
            var meta = new OtterThing("thing");
            meta.WriteProperty("name", project.Name);
            meta.WriteProperty("version", version);
            meta.WriteProperty("target", project.Target);
            meta.WriteProperty("archetype", project.Archetype);
            meta.WriteProperty("entryPoint", entryRelative);
            meta.WriteProperty("otterVersion", Program.Version);
            meta.WriteProperty("includedFiles", new OtterList(includedFiles.Select(f => (object?)f)));
            meta.WriteProperty("assetManifest", new OtterList(project.Assets.Select(a => (object?)a)));
            meta.WriteProperty("artifactFilename", zipName);
            meta.WriteProperty("sha256", fileHash);
            meta.WriteProperty("runtimeRequirements", runtimeRequirements);
            string metaJson = OtterJson.Write(meta)!;
            SetContentUtf8(Path.Combine(stagingDirectory, "otter.publish.json"), metaJson);
            SetContentUtf8(Path.Combine(stagedPackage, "otter.publish.json"), metaJson);

            Host.Line("Verifying package...");
            using (ZipArchive check = ZipFile.OpenRead(stagedZip))
            {
                if (check.Entries.Count == 0) throw new OtterError("Generated archive has zero entries.", 0, "publish");
                if (!check.Entries.Any(e => string.Equals(e.FullName, entryRelative, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new OtterError($"Generated archive is missing entry point {entryRelative}.", 0, "publish");
                }
            }
            if (Sha256(stagedZip) != fileHash) throw new OtterError($"Checksum verification mismatch for {zipName}.", 0, "publish");

            // 5. Promotion.
            if (Directory.Exists(resolvedPublishDir) || File.Exists(resolvedPublishDir)) DeleteItem(resolvedPublishDir);
            Directory.CreateDirectory(resolvedPublishDir);
            foreach (string item in PowerShellFiles.ChildItems(stagingDirectory, recurse: false, files: false, force: true))
            {
                CopyItem(item, resolvedPublishDir);
            }
        }
        catch (OtterError error)
        {
            return PublishFailed(error.Message);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not CliExit and not NeedsPowerShell)
        {
            return PublishFailed($"Otter hit a problem while publishing: {failure.Message}");
        }
        finally
        {
            try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        Host.Line("");
        Host.Line("Publish succeeded.", ConsoleColor.Green);
        Host.Line("");
        Host.Line("Artifact:");
        string relativeOutput = resolvedPublishDir.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase)
            ? resolvedPublishDir.Substring(rootDirectory.Length).TrimStart('\\', '/')
            : outDirName;
        Host.Line($"  {relativeOutput.Replace('\\', '/')}/{zipName}");
        Host.Line("");
        Host.Line("SHA-256:");
        Host.Line($"  {fileHash}");
        return 0;
    }

    static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    // New-OtterDeterministicZip: files in sorted order, a fixed date, the
    // `run` launcher marked executable (mode 755).
    static void WriteDeterministicZip(string sourceDirectory, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        string sourceFull = Path.GetFullPath(sourceDirectory);
        var files = PowerShellFiles.ChildItems(sourceFull, recurse: true, files: true);
        files.Sort(PowerShellFiles.SortObjectComparer);
        var fixedDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using FileStream zipStream = File.Create(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        foreach (string file in files)
        {
            string relative = file.Substring(sourceFull.Length).TrimStart('\\', '/').Replace('\\', '/');
            if (relative.Contains("..") || relative.StartsWith('/') || DriveLetter().IsMatch(relative))
            {
                throw new OtterError($"Forbidden path in package archive: {relative}", 0, "publish");
            }
            ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = fixedDate;
            if (relative == "run") entry.ExternalAttributes = unchecked((int)0x81ED0000);
            using Stream entryStream = entry.Open();
            using FileStream fileStream = File.OpenRead(file);
            fileStream.CopyTo(entryStream);
        }
    }
}
