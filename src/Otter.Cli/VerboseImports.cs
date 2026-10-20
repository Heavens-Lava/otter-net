namespace Otter.Cli;

// -Verbose on otter.ps1 (an advanced script; docs/SPEC-QUESTIONS.md C2-5):
// PowerShell's VERBOSE stream is shown, on standard output. otterPS writes
// to it in two ways, both measured on Windows PowerShell 5.1:
//
//   - each Import-Module it makes while a command runs lists the modules it
//     loads and the functions it imports and exports (VerboseImports.g.cs),
//     with the paths of the PowerShell Otter's own files: the compiled
//     engine's module before a plain run, the profiler, the debugger, the web
//     compiler (web, browse, build, publish), the web server and the desktop
//     module. The modules otter.ps1 loads with `using module` show nothing;
//   - Write-Verbose: "Otter Web App compiled to: <page>" when a page is
//     written, and "Otter web server listening on http://<host>:<port>/".
internal static partial class VerboseImports
{
    public static bool Enabled { get; set; }

    static void Write(string[] block)
    {
        if (!Enabled) return;
        foreach (string line in block)
        {
            Host.Verbose(line.StartsWith("path:", StringComparison.Ordinal)
                ? $"Loading module from path '{PowerShellError.OtterPsFile(line[5..])}'."
                : line);
        }
    }

    public static void CompilerNative() => Write(CompilerNativeLines);
    public static void Profiler() => Write(ProfilerLines);
    public static void Debugger() => Write(DebuggerLines);
    // Imported once: later imports find Export-OtterWebApplication and skip it.
    static bool webImported;
    public static void Web()
    {
        if (webImported) return;
        webImported = true;
        Write(WebLines);
    }
    public static void Server() => Write(ServerLines);
    public static void Desktop() => Write(DesktopLines);

    // Export-OtterWebApplication's Write-Verbose, after the page is written.
    public static void PageWritten(string path)
    {
        if (Enabled) Host.Verbose($"Otter Web App compiled to: {path}");
    }

    // Start-OtterServer's Write-Verbose, once the server listens.
    public static void ServerListening(string host, int port)
    {
        if (Enabled) Host.Verbose($"Otter web server listening on http://{host}:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/");
    }
}
