using System.Diagnostics;
using System.Text;
using Otter.Language;
using Otter.Web;

namespace Otter.Cli;

// `otter web <file.ot> [-NoOpen]` (also `otter browse`): the program as one
// standalone HTML page next to it, as otter.ps1 and otterPS's
// Export-OtterWebApplication (src/Otter.Web.psm1) make it.
//
// Run returns the exit code, or null with a reason when the PowerShell Otter
// should do it instead: a statement the web compiler here does not know yet,
// or Otter failing inside itself. That decision is made before anything is
// written. otter.ps1 has already checked the file (Cli.UiCommand).
static class WebCommand
{
    const int ExitSuccess = 0, ExitUsageError = 1, ExitCheckError = 2;

    // primarySource: the program's full path (a project's entry point);
    // target: what the user typed, for the "could not write" message.
    public static int? Run(string primarySource, string target, bool noOpen, Action<string> trace, out string reason)
    {
        reason = "";
        ResolvedProgram resolved;
        try { resolved = Modules.Resolve(primarySource); }
        // An import problem (otter.ps1's catch [OtterError]): the error as is.
        catch (OtterError problem)
        {
            trace("native: web (import error)");
            return ReportError(problem);
        }
        string html;
        try
        {
            html = CompilePage(resolved, primarySource);
        }
        catch (NotSupportedYet unsupported)
        {
            reason = $"{unsupported.Feature} (line {unsupported.Line}) is not native yet";
            return null;
        }
        catch (OtterError error)
        {
            trace("native: web (errors)");
            return ReportError(resolved.Remap(error));
        }
        catch (WebInternalProblem problem)
        {
            trace("native: web (internal problem)");
            return ReportInternalProblem(problem);
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            reason = $"the web compiler stopped unexpectedly ({unexpected.GetType().Name})";
            return null;
        }

        // Set-Content -Encoding UTF8 on Windows PowerShell 5.1: a byte order
        // mark, the page and a line ending.
        string htmlPath = Path.ChangeExtension(primarySource, ".html");
        try
        {
            File.WriteAllText(htmlPath, html + "\r\n", new UTF8Encoding(true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            trace("native: web (page not written)");
            Host.Line($"Otter: I could not write the web page for \"{target}\": {Modules.FileAccessReason(e)}.", ConsoleColor.Red);
            return ExitUsageError;
        }
        trace("native: web");
        Host.Line($"Otter Web application compiled to: {htmlPath}");
        if (noOpen) return ExitSuccess;
        if (OpenInBrowser(htmlPath))
        {
            Host.Line("Opened in your default browser.");
            return ExitSuccess;
        }
        Host.Line($"Otter: I could not open a browser. The page is at {htmlPath} - open it yourself, or use -NoOpen to skip this step.", ConsoleColor.Red);
        return ExitUsageError;
    }

    // Export-OtterWebApplication's compile step: the page with its sidecar
    // stylesheet. Throws an OtterError (not remapped), NotSupportedYet for
    // a statement the web compiler here does not know yet, or anything else
    // Otter would report as its own failure.
    public static string CompilePage(ResolvedProgram resolved, string primarySource)
    {
        string sourceText = resolved.CombinedSource;
        // ConvertTo-OtterTokens refuses an empty source with PowerShell's
        // parameter binding error, which Export reports as Otter's own bug.
        if (sourceText.Length == 0) throw new InvalidOperationException("Cannot bind argument to parameter 'Source' because it is an empty string.");
        ProgramNode program = Parser.Parse(Lexer.Tokenize(sourceText));
        // D-2: names the grammar would misread, and handlers that would
        // see the wrong run's names (Assert-OtterLanguageContract).
        string[] sourceLines = sourceText.Replace("\r\n", "\n").Split('\n');
        var contract = ReservedWords.Diagnostics(program, sourceLines)
            .Concat(LoopPasses.HandlerRunDiagnostics(program.Statements, sourceLines))
            .OrderBy(e => e.Line).ThenBy(e => e.Column).ToList();
        if (contract.Count > 0) throw new OtterMultipleErrors(contract);

        string html = WebCompiler.Compile(program, Path.GetFileNameWithoutExtension(primarySource));
        return WithSidecarStylesheet(html, primarySource);
    }

    // Export-OtterWebApplication's catch-all: a PowerShell error that is not
    // an Otter error, then a plain `exit 1` inside a module function, which
    // under -File leaves the process's exit code at 0 (as UI2-1).
    internal static int ReportInternalProblem(WebInternalProblem problem)
    {
        Host.Line("");
        Host.Line("Otter hit a problem inside itself, which means this is a bug in Otter.", ConsoleColor.Red);
        Host.Line($"  {problem.Message}", ConsoleColor.DarkGray);
        Host.Line("");
        return Cli.ExitSuccess;
    }

    static int ReportError(OtterError error)
    {
        Host.Line("");
        Host.Line(error.FormatDetailed(), ConsoleColor.Red);
        Host.Line("");
        return ExitCheckError;
    }

    // `<entry>.css` next to the program goes into the page, before the first
    // </head> - but only from inside the program's folder (RC3 B4): a link
    // that leads elsewhere is refused before anything is written.
    static string WithSidecarStylesheet(string html, string primarySource)
    {
        string sidecarCss = Path.ChangeExtension(primarySource, ".css");
        if (!File.Exists(sidecarCss) && !Directory.Exists(sidecarCss)) return html;
        string entryDirReal = RealPath(Path.GetDirectoryName(primarySource)!);
        string sidecarReal = RealPath(sidecarCss);
        if (!PathInside(sidecarReal, entryDirReal))
        {
            throw new OtterError($"The stylesheet '{Path.GetFileName(sidecarCss)}' is a link to '{sidecarReal}', which is outside the folder that holds '{Path.GetFileName(primarySource)}'. Otter only includes files from inside that folder, so nothing was written.", 0, "build");
        }
        string cssContent = File.ReadAllText(sidecarReal, Encoding.UTF8);
        int headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose >= 0) html = html.Insert(headClose, $"<style id=\"otter-sidecar-style\">\n{cssContent}\n</style>\n");
        return html;
    }

    // Resolve-OtterRealPath: every symbolic link or junction on the way followed.
    static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        int hops = 0;
        while (true)
        {
            string root = Path.GetPathRoot(full) ?? "";
            string[] parts = full.Substring(root.Length).Split('\\', '/').Where(p => p.Length > 0).ToArray();
            string current = root;
            bool relinked = false;
            for (int i = 0; i < parts.Length; i++)
            {
                string next = Path.Combine(current, parts[i]);
                FileSystemInfo item = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                string? linkTarget = item.Exists ? item.LinkTarget : null;
                if (!string.IsNullOrEmpty(linkTarget))
                {
                    if (++hops > 40) throw new OtterError($"The path '{path}' has too many links to follow (a link loop?).", 0, "build");
                    string resolvedTarget = Path.GetFullPath(Path.Combine(current, linkTarget));
                    full = resolvedTarget;
                    foreach (string rest in parts.Skip(i + 1)) full = Path.Combine(full, rest);
                    full = Path.GetFullPath(full);
                    relinked = true;
                    break;
                }
                current = next;
            }
            if (!relinked) return current;
        }
    }

    // Test-OtterPathInside
    static bool PathInside(string path, string folder)
    {
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string trimmed = folder.TrimEnd('\\', '/');
        return path.Equals(trimmed, comparison) || path.StartsWith(trimmed + Path.DirectorySeparatorChar, comparison);
    }

    // otter.ps1: Start-Process on Windows; on macOS and Linux the system
    // opener, given 15 seconds (some Linux openers stay attached).
    static bool OpenInBrowser(string htmlPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using Process? started = Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
                return true;
            }
            string opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var start = new ProcessStartInfo(opener) { UseShellExecute = false };
            start.ArgumentList.Add(htmlPath);
            using Process process = Process.Start(start)!;
            if (process.WaitForExit(15000) && process.ExitCode != 0) return false;
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
