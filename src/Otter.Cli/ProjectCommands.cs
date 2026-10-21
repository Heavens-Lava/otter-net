using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Cli;

// `otter new`, `otter test`, `otter build`, `otter publish` as otter.ps1
// runs them: an OtterError (or any other failure) from the project module is
// shown as its message between blank lines, exit code 1.
internal static partial class ProjectCommands
{
    static readonly string[] Archetypes = { "console", "desktop", "web", "automation", "game" };

    // otter.ps1's two catches of a project command: `catch [OtterError]`
    // with its Write-Host '' on line `otterErrorSite`, and `catch` on line
    // `otherSite`.
    static int ReportProjectFailure(Exception failure, int otterErrorSite, int otherSite)
    {
        if (failure is OtterNetGap or CliExit) throw failure;
        Host.Line("", site: PsSite.Otter(failure is OtterError ? otterErrorSite : otherSite));
        Host.Line(failure.Message, ConsoleColor.Red);
        Host.Line("");
        return Cli.ExitUsageError;
    }

    // ---- files as Windows PowerShell 5.1 writes them ----------------------

    static readonly UTF8Encoding Utf8WithBom = new(true);
    static readonly UTF8Encoding Utf8NoBom = new(false);

    // A here-string from a .psm1 checked out on Windows: its line breaks are "\r\n".
    static string HereString(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    // Set-Content -Encoding UTF8 on Windows PowerShell 5.1: a byte order
    // mark, the text, and the platform line ending (always "\r\n" there).
    internal static void SetContentUtf8(string path, string text) => File.WriteAllText(path, text + "\r\n", Utf8WithBom);

    // Set-Content -Encoding ASCII: anything outside ASCII becomes "?".
    internal static void SetContentAscii(string path, string text)
    {
        var bytes = (text + "\r\n").Select(c => c < 128 ? (byte)c : (byte)'?').ToArray();
        File.WriteAllBytes(path, bytes);
    }

    // ---- otter new ---------------------------------------------------------

    public static int New(CommandLine line)
    {
        if (!line.HasTarget)
        {
            Host.Line("Usage: otter new <archetype> <name>", ConsoleColor.Red, PsSite.Otter(704));
            Host.Line("Supported archetypes: console, desktop, web, automation, game.", ConsoleColor.Red);
            throw new CliExit(Cli.ExitUsageError);
        }
        string target = line.Target!;
        if (!Archetypes.Contains(target.ToLowerInvariant()))
        {
            Host.Line($"Otter: Unknown archetype \"{target}\".", ConsoleColor.Red, PsSite.Otter(710));
            Host.Line("Usage: otter new <archetype> <name>", ConsoleColor.Red);
            Host.Line("Supported archetypes: console, desktop, web, automation, game.", ConsoleColor.Red);
            throw new CliExit(Cli.ExitUsageError);
        }
        string archetype = target.ToLowerInvariant();
        string[] raw = line.RawTrailing(2);
        // if ($rawArgs -and $rawArgs.Count -gt 0) { $rawArgs[0] } elseif ($Arguments -and ...) { $Arguments[0] }
        string? projectName = CommandLine.IsTrue(raw) ? raw[0] : CommandLine.IsTrue(line.Arguments) ? line.Arguments![0] : null;
        if (string.IsNullOrEmpty(projectName))
        {
            Host.Line($"Usage: otter new {archetype} <name>", ConsoleColor.Red, PsSite.Otter(721));
            throw new CliExit(Cli.ExitUsageError);
        }
        Program.Trace("native: new");
        Project created;
        try
        {
            created = NewProject(archetype, projectName, Directory.GetCurrentDirectory());
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            throw new CliExit(ReportProjectFailure(failure, 741, 747));
        }
        // Still inside otter.ps1's try: a Write-Host that fails here
        // (-InformationAction) goes to its catch. Under Ignore PowerShell has
        // put the default back in otter.ps1's scope, so the catch's lines show.
        try
        {
            Host.Line($"Created new Otter {archetype} project in \"{created.RootDirectory}\".", ConsoleColor.Green, PsSite.Handled);
            Host.Line("");
            Host.Line("To get started:");
            Host.Line($"  cd {projectName}");
            Host.Line("  otter check .");
            Host.Line("  otter test .");
            Host.Line($"  {(created.Scripts.TryGetValue("start", out string? start) ? start : "")}");
            Host.Line("");
        }
        catch (WriteHostFailure failure)
        {
            if (failure.Mode == InformationAction.Mode.Ignore) InformationAction.Replaced = true;
            throw new CliExit(ReportProjectFailure(failure, 741, 747));
        }
        return Cli.ExitSuccess;
    }

    [GeneratedRegex("[<>:\"/\\\\|?*]")]
    private static partial Regex InvalidNameCharacters();

    // New-OtterProject.
    static Project NewProject(string archetype, string name, string basePath)
    {
        string arch = archetype.Trim().ToLowerInvariant();
        string cleanName = name.Trim();
        if (string.IsNullOrWhiteSpace(cleanName) || InvalidNameCharacters().IsMatch(cleanName))
        {
            throw new OtterError($"Otter: \"{name}\" is not a valid project name.", 0, "check");
        }
        // .NET Framework's Path.Combine refuses the characters Windows
        // cannot have in a path; otter.ps1 shows the method call's error.
        if (OperatingSystem.IsWindows() && cleanName.Any(c => c < ' '))
        {
            throw new InvalidOperationException("Exception calling \"Combine\" with \"2\" argument(s): \"Illegal characters in path.\"");
        }
        string projectDirectory = Path.GetFullPath(Path.Combine(basePath, cleanName));
        if (File.Exists(projectDirectory) || (Directory.Exists(projectDirectory) && Directory.EnumerateFileSystemEntries(projectDirectory).Any()))
        {
            throw new OtterError($"Otter: destination folder \"{cleanName}\" already exists and is not empty.", 0, "check");
        }
        Directory.CreateDirectory(projectDirectory);
        string srcDirectory = Path.Combine(projectDirectory, "src");
        string testsDirectory = Path.Combine(projectDirectory, "tests");
        string assetsDirectory = Path.Combine(projectDirectory, "assets");
        Directory.CreateDirectory(srcDirectory);
        Directory.CreateDirectory(testsDirectory);
        Directory.CreateDirectory(assetsDirectory);

        // The name goes into here-strings, whose own line breaks are "\r\n":
        // a line break inside the name stays as typed.
        const string N = "\u0000name\u0000";
        string mainCode = "";
        string targetName = "console";
        string[] assetList = Array.Empty<string>();
        string startCommand = "otter run .";
        switch (arch)
        {
            case "console":
                mainCode = $"# {N} - Console Application\n\nsay \"Hello from {N}!\"";
                break;
            case "desktop":
                targetName = "desktop";
                assetList = new[] { "assets/styles.css" };
                mainCode = $"# {N} - Desktop Application\n\ncreate window into app\ntitle of app is \"{N}\"\n\ncreate button into btn\ntext of btn is \"Click Me\"\n\nput btn in app\nshow app";
                SetContentUtf8(Path.Combine(assetsDirectory, "styles.css"), $"/* Stylesheet for {cleanName} */\nbody {{ margin: 0; padding: 16px; }}\n");
                break;
            case "web":
                targetName = "web";
                startCommand = "otter web .";
                mainCode = $"# {N} - Web Application\n\napp is a page\n    title is \"{N}\"\n.\n\nwelcomeText is a text\n    value is \"Welcome to {N}!\"\n.\n\nput welcomeText in app\nshow app";
                SetContentUtf8(Path.Combine(projectDirectory, "main.css"), $"/* Stylesheet for {cleanName} */\nbody {{ margin: 0; font-family: sans-serif; }}\n");
                break;
            case "automation":
                mainCode = $"# {N} - Automation Task\n\nget current directory into cwd\nsay \"Running automation task in:\" cwd\n\nget files in \".\" into projectFiles\nsay \"Found\" length of projectFiles \"files.\"";
                break;
            case "game":
                targetName = "game";
                startCommand = "otter web .";
                mainCode = $"# {N} - 2D Game\n\napp is a page\n    title is \"{N}\"\n.\n\ngameCanvas is a canvas\n    width is 640\n    height is 480\n    mode is \"2d\"\n.\n\nput gameCanvas in app\nshow app";
                SetContentUtf8(Path.Combine(projectDirectory, "main.css"), $"/* Stylesheet for {cleanName} */\nbody {{ margin: 0; background: #000; }}\n");
                break;
        }
        SetContentUtf8(Path.Combine(projectDirectory, "main.ot"), HereString(mainCode).Replace(N, cleanName));

        string testCode = $"# Tests for {N}\n\nscore is 100\n\nif score is not 100\n    fail with \"Expected score to be 100.\"\n.\n\nsay \"All checks passed\"";
        SetContentUtf8(Path.Combine(testsDirectory, "app_test.ot"), HereString(testCode).Replace(N, cleanName));

        // The assets list is joined with "`n" inside a here-string.
        string assetsJson = assetList.Length > 0
            ? "\n    " + string.Join(",\n    ", assetList.Select(a => "\"" + a + "\"")) + "\n  "
            : "";
        string manifest = HereString(
            "{\n" +
            "  \"$schema\": \"https://otter-lang.org/schema/project-v1.json\",\n" +
            $"  \"name\": \"{N}\",\n" +
            "  \"version\": \"0.1.0\",\n" +
            $"  \"archetype\": \"{arch}\",\n" +
            $"  \"target\": \"{targetName}\",\n" +
            "  \"entryPoint\": \"main.ot\",\n" +
            "  \"assets\": [ASSETS],\n" +
            "  \"build\": {\n" +
            "    \"outputDir\": \"dist\",\n" +
            "    \"clean\": true\n" +
            "  },\n" +
            "  \"scripts\": {\n" +
            $"    \"start\": \"{startCommand}\",\n" +
            "    \"test\": \"otter test .\",\n" +
            "    \"check\": \"otter check .\"\n" +
            "  }\n" +
            "}").Replace("[ASSETS]", "[" + assetsJson + "]").Replace(N, cleanName);
        SetContentUtf8(Path.Combine(projectDirectory, "otter.json"), manifest);

        return Project.Load(projectDirectory);
    }

    // ---- otter test --------------------------------------------------------

    public static int Test(CommandLine line)
    {
        string target = line.HasTarget ? line.Target! : ".";
        try
        {
            return Host.InModule(() => RunTests(target));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException and not CliExit and not OtterNetGap)
        {
            return ReportProjectFailure(failure, 761, 767);
        }
    }

    // Get-OtterProjectTestFiles.
    static List<string> TestFiles(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) target = ".";
        string trimmed = target.Trim();
        if (trimmed.ToLowerInvariant().EndsWith(".ot", StringComparison.Ordinal))
        {
            string? file = Project.PsResolvePath(trimmed);
            if (file != null && File.Exists(file)) return new List<string> { file };
            throw new OtterError($"Otter: I cannot find test file \"{target}\".", 0, "check");
        }
        string projectRoot;
        ProjectManifest? manifest = Project.Find(trimmed);
        if (manifest != null) projectRoot = manifest.RootDirectory;
        else
        {
            string? directory = Project.PsResolvePath(trimmed);
            if (directory != null && Directory.Exists(directory)) projectRoot = directory;
            else throw new OtterError($"Otter: I cannot find project or test directory \"{target}\".", 0, "check");
        }
        string testsDirectory = Path.Combine(projectRoot, "tests");
        if (!Directory.Exists(testsDirectory))
        {
            if (trimmed != "." && Path.GetFileName(projectRoot).ToLowerInvariant() == "tests") testsDirectory = projectRoot;
            else return new List<string>();
        }
        var files = PowerShellFiles.ChildItems(testsDirectory, recurse: true, files: true)
            .Where(f => f.EndsWith(".ot", StringComparison.OrdinalIgnoreCase))
            .Where(f =>
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                return name.EndsWith("_test.ot", StringComparison.Ordinal) || name.StartsWith("test_", StringComparison.Ordinal);
            })
            .ToList();
        files.Sort(PowerShellFiles.SortObjectComparer);
        return files;
    }

    // Invoke-OtterProjectTests: each test file runs in its own `otter run`,
    // as otterPS runs each in a child PowerShell.
    static int RunTests(string target)
    {
        List<string> testFiles = TestFiles(target);
        Program.Trace("native: test");
        if (testFiles.Count == 0)
        {
            Host.Line("No tests found.", ConsoleColor.Yellow);
            return 0;
        }
        Host.Line($"Running {(testFiles.Count == 1 ? "1 Otter test" : $"{testFiles.Count} Otter tests")}...");
        Host.Line("");

        int passed = 0, failed = 0, syntaxErrors = 0;
        var failureDetails = new List<string>();
        string? resolvedTarget = Directory.Exists(target) ? Project.PsResolvePath(target) : null;
        foreach (string file in testFiles)
        {
            string relativePath;
            if (resolvedTarget != null)
            {
                relativePath = file.StartsWith(resolvedTarget, StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(resolvedTarget.Length).TrimStart('\\', '/')
                    : Path.GetFileName(file);
            }
            else
            {
                string leaf = Path.GetFileName(file);
                string parent = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
                relativePath = parent.ToLowerInvariant() == "tests" ? $"tests/{leaf}" : leaf;
            }
            relativePath = relativePath.Replace('\\', '/');

            var (code, outputLines) = RunChildOtter("run", file);
            if (code == 0)
            {
                Host.Line($"PASS {relativePath}", ConsoleColor.Green);
                passed++;
            }
            else if (code == 2)
            {
                Host.Line($"FAIL {relativePath} (syntax error)", ConsoleColor.Red);
                syntaxErrors++;
                var diagnostics = outputLines.Select(l => l.Trim()).Where(l => l != "");
                failureDetails.Add($"FAIL {relativePath} (syntax error)\n" + string.Join("\n", diagnostics));
            }
            else
            {
                Host.Line($"FAIL {relativePath}", ConsoleColor.Red);
                failed++;
                string? errorLine = null;
                foreach (string l in outputLines)
                {
                    Match match = LineNumber().Match(l);
                    if (match.Success) errorLine = match.Groups[1].Value;
                }
                var filtered = outputLines.Where(l =>
                {
                    string t = l.Trim();
                    return t != "" && !HeaderOrLine().IsMatch(t) && !FailWith().IsMatch(t);
                }).ToList();
                string message = filtered.Count > 0 ? filtered[^1].Trim() : "Test assertion failed.";
                string location = errorLine != null ? $"{relativePath}:{errorLine}" : relativePath;
                failureDetails.Add($"FAIL {location}\n  {message}");
            }
        }

        if (failureDetails.Count > 0)
        {
            Host.Line("");
            foreach (string detail in failureDetails)
            {
                Host.Line(detail, ConsoleColor.Red);
                Host.Line("");
            }
        }
        Host.Line("");
        if (failed == 0 && syntaxErrors == 0)
        {
            Host.Line($"{passed} passed", ConsoleColor.Green);
            return 0;
        }
        var summary = new List<string>();
        if (passed > 0) summary.Add($"{passed} passed");
        summary.Add($"{failed + syntaxErrors} failed");
        Host.Line(string.Join(", ", summary), ConsoleColor.Red);
        return syntaxErrors > 0 ? 2 : 3;
    }

    // PowerShell's -match is case-insensitive.
    [GeneratedRegex(@"^Line\s+(\d+):", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineNumber();
    [GeneratedRegex(@"^(Otter Runtime Error|Line \d+:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderOrLine();
    [GeneratedRegex(@"^\s*fail with\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FailWith();

    // `& otter run <file> 2>&1`: this same otter, its exit code, and every
    // line it wrote. The child reads this process's standard input.
    static (int Code, List<string> Lines) RunChildOtter(params string[] arguments)
    {
        string executable = Environment.ProcessPath ?? "otter";
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Under `dotnet otter.dll` the program is the first argument.
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // otter-net's own trace would land in the captured output.
        start.Environment.Remove("OTTER_NET_TRACE");
        Host.Out.Flush();
        using Process process = Process.Start(start)!;
        var errorLines = new List<string>();
        var errorReader = Task.Run(() =>
        {
            string? errorLine;
            while ((errorLine = process.StandardError.ReadLine()) != null) errorLines.Add(errorLine);
        });
        var lines = new List<string>();
        string? outputLine;
        while ((outputLine = process.StandardOutput.ReadLine()) != null) lines.Add(outputLine);
        errorReader.Wait();
        process.WaitForExit();
        lines.AddRange(errorLines);
        return (process.ExitCode, lines);
    }
}
