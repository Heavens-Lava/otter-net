using Otter.Language;
using Otter.Runtime;

namespace Otter.Cli;

// otter.ps1's Invoke-OtterFile and Invoke-OtterSource: check the file, expand
// its `use` lines, lex, parse, check, then run (or stop after checking).
internal static class FileMode
{
    public enum Kind { Run, Check }

    // Assert-OtterSourceFileReadable (RC3 B12): the path exists, is a file,
    // and can be opened. Returns the resolved path; exits with 1 otherwise.
    public static string AssertReadable(string scriptPath)
    {
        string? resolved = Project.PsResolvePath(scriptPath);
        if (resolved == null)
        {
            Host.Line($"Otter: I cannot find a file called \"{scriptPath}\".", ConsoleColor.Red, PsSite.Otter(471));
            throw new CliExit(Cli.ExitUsageError);
        }
        if (Directory.Exists(resolved))
        {
            Host.Line($"Otter: \"{scriptPath}\" is a folder, not an Otter file.", ConsoleColor.Red, PsSite.Otter(475));
            throw new CliExit(Cli.ExitUsageError);
        }
        try
        {
            using var probe = File.OpenRead(resolved);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Host.Line($"Otter: I could not read \"{scriptPath}\": {Modules.FileAccessReason(e)}.", ConsoleColor.Red, PsSite.Otter(483));
            throw new CliExit(Cli.ExitUsageError);
        }
        return resolved;
    }

    public static int Invoke(CommandLine line, string scriptPath, Kind kind, string[] arguments,
                             string? displayTarget = null, Debugger? debugger = null, Profiler? profiler = null)
    {
        if (!scriptPath.ToLowerInvariant().EndsWith(".ot", StringComparison.Ordinal))
        {
            Host.Line($"Otter: \"{scriptPath}\" is not an Otter file - expected a .ot file.", ConsoleColor.Red, PsSite.Otter(514));
            throw new CliExit(Cli.ExitUsageError);
        }
        string resolvedPath = AssertReadable(scriptPath);

        ResolvedProgram resolved;
        try
        {
            resolved = Modules.Resolve(resolvedPath);
        }
        catch (OtterError error)
        {
            Program.Trace("native: use error");
            debugger?.Finished();
            ShowFailure(error, line.DebugErrors);
            throw new CliExit(Cli.ExitCheckError);
        }
        string source = resolved.CombinedSource;
        string[] sourceLines = source.Replace("\r\n", "\n").Split('\n');
        bool checkOnly = kind == Kind.Check;

        // Lex, parse and check before anything is shown: a program using
        // something otter-net does not have yet goes to the PowerShell Otter
        // whole, before anything has run or been printed.
        var compiled = Compile(source, sourceLines, line.DebugTokens);
        if (compiled.Failure != null)
        {
            Program.Trace("native: check failed");
            WriteTokensInTry(compiled, debugger, profiler, sourceLines, line.DebugErrors);
            debugger?.Finished();
            profiler?.Report(sourceLines);
            ShowFailure(compiled.Failure is OtterError otterError ? resolved.Remap(otterError) : compiled.Failure, line.DebugErrors);
            throw new CliExit(Cli.ExitCheckError);
        }
        // Desktop windows: otterPS opens WPF for `create`, which Native AOT
        // cannot host (docs/port/desktop.md). On Windows such a program runs
        // whole in the otter-desktop host, which has the WPF provider;
        // elsewhere WPF cannot start in otterPS either, and the program runs
        // here until it says so.
        if (!line.ParseOnly && !checkOnly && compiled.CreatesUiResources && OperatingSystem.IsWindows() && UiProviders.Current == null)
        {
            throw new NeedsDesktop("a desktop window (create, WPF)");
        }
        // -DebugAst: the tree, after the tokens, when the program will run.
        List<string>? ast = line.DebugAst && !line.ParseOnly && !checkOnly ? AstDump.Lines(compiled.Program!) : null;
        WriteTokensInTry(compiled, debugger, profiler, sourceLines, line.DebugErrors);
        if (ast != null)
        {
            try { WriteAst(ast); }
            catch (WriteHostFailure failure) when (!failure.Site.HasPosition) { InvokeFileCatch(failure, debugger, profiler, sourceLines, line.DebugErrors); }
        }
        // $script:OtterCompiledRunAllowed: Invoke-OtterCompiledProgram imports
        // the compiled engine's module before a plain run (-Verbose shows it).
        if (!line.ParseOnly && !checkOnly && !line.DebugAst && debugger == null && profiler == null
            && !string.Equals(Environment.GetEnvironmentVariable("OTTER_ENGINE"), "interpreter", StringComparison.Ordinal))
        {
            VerboseImports.CompilerNative();
        }

        if (!line.ParseOnly && !checkOnly)
        {
            Program.Trace("native: run");
            // Under -InformationAction each `say` is a Write-Host that fails.
            TextWriter programOutput = InformationAction.Active ? new InformationWriter(Host.Out) : Host.Out;
            var interpreter = new Interpreter(programOutput, Host.In, source, arguments, echoInput: Console.IsInputRedirected)
            {
                WriteColoredLine = Console.IsOutputRedirected ? null : (text, color) =>
                {
                    InformationAction.Before(text, PsSite.Handled);
                    Program.WriteInColor(Host.Out, text, color);
                    InformationAction.After(text, PsSite.Handled);
                },
                ReadSecretLine = Console.IsInputRedirected ? null : () => Program.ReadSecretLine(Host.Out),
                MoveCursor = Console.IsOutputRedirected ? null : (column, row) => { Host.Out.Flush(); Console.SetCursorPosition(column, row); },
                StatementHook = debugger?.Hook ?? profiler?.Hook,
                // Set-OtterApplicationId: secrets are named after the program (D111).
                ApplicationPath = resolvedPath,
                // $script:OtterCompiledRunAllowed: a plain run goes to otterPS's
                // compiled engine when it can compile the program (CompiledEngine.cs).
                CompiledEngine = debugger == null && profiler == null && !line.DebugAst
                    && !string.Equals(Environment.GetEnvironmentVariable("OTTER_ENGINE"), "interpreter", StringComparison.Ordinal)
                    && OtterPsCompiledEngine.Accepts(compiled.Program!),
            };
            debugger?.Attach(interpreter);
            profiler?.Attach(interpreter);
            Host.MarkWritten();
            try
            {
                interpreter.Run(compiled.Program!);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                debugger?.Finished();
                profiler?.Report(sourceLines);
                // Invoke-OtterCompiledProgram: a failure that is not an Otter
                // error is the compiled engine's internal problem.
                if (interpreter.CompiledEngine && failure is not OtterError) failure = OtterPsCompiledEngine.InternalProblem(failure);
                ShowFailure(failure is OtterError error ? resolved.Remap(error) : failure, line.DebugErrors);
                throw new CliExit(Cli.ExitRuntimeError);
            }
        }
        else
        {
            Program.Trace(checkOnly ? "native: check" : "native: parse only");
        }

        debugger?.Finished();
        profiler?.Report(sourceLines);
        if (checkOnly)
        {
            Host.Line($"Otter: {displayTarget ?? scriptPath} is valid.", ConsoleColor.Green, PsSite.Otter(575));
        }
        else if (line.ParseOnly)
        {
            Host.Line($"ok: {scriptPath}", site: PsSite.Otter(579));
        }
        return Cli.ExitSuccess;
    }

    // -DebugTokens inside Invoke-OtterSource's try: a Write-Host that fails
    // there (-InformationAction) goes to Invoke-OtterFile's catch.
    static void WriteTokensInTry(Compiled compiled, Debugger? debugger, Profiler? profiler, string[] sourceLines, bool debugErrors)
    {
        try { compiled.WriteTokens(); }
        catch (WriteHostFailure failure) when (!failure.Site.HasPosition) { InvokeFileCatch(failure, debugger, profiler, sourceLines, debugErrors); }
    }

    // Invoke-OtterFile's catch for a failing Write-Host: the debugger's last
    // event, the profile, then Show-OtterFailure, whose own first Write-Host
    // fails again and ends otter.ps1.
    static void InvokeFileCatch(WriteHostFailure failure, Debugger? debugger, Profiler? profiler, string[] sourceLines, bool debugErrors)
    {
        debugger?.Finished();
        profiler?.Report(sourceLines);
        ShowFailure(failure, debugErrors);
        throw new CliExit(Cli.ExitRuntimeError);
    }

    // Invoke-OtterSource's -DebugAst block.
    public static void WriteAst(List<string> lines)
    {
        Host.Line("--- ast ---", ConsoleColor.DarkCyan, PsSite.Handled);
        foreach (string text in lines) Host.Line(text, ConsoleColor.DarkGray);
        Host.Line("");
    }

    public sealed class Compiled
    {
        public ProgramNode? Program;
        public Exception? Failure;
        public IReadOnlyList<Token>? Tokens;
        public bool ShowTokens;
        public bool CreatesUiResources;

        // -DebugTokens: the tokens, as Invoke-OtterSource lists them.
        public void WriteTokens()
        {
            if (!ShowTokens || Tokens == null) return;
            Host.Line("--- tokens ---", ConsoleColor.DarkCyan, PsSite.Handled);
            foreach (Token token in Tokens) Host.Line($"  {token}", ConsoleColor.DarkGray);
            Host.Line("");
            ShowTokens = false;
        }
    }

    // Lex, parse and the D-2 reserved-name check. A failure is returned (an
    // OtterError, or an unexpected exception: a bug in Otter); something not
    // native yet throws NeedsPowerShell.
    public static Compiled Compile(string source, string[] sourceLines, bool showTokens)
    {
        var result = new Compiled { ShowTokens = showTokens };
        // otterPS's ConvertTo-OtterTokens has a mandatory -Source, so an empty
        // program (no lines, or one empty line) stops PowerShell's parameter
        // binding: Otter's own bug, reported as one (docs/SPEC-QUESTIONS.md Q12).
        if (source.Length == 0)
        {
            result.Failure = new InvalidOperationException("Cannot bind argument to parameter 'Source' because it is an empty string.");
            return result;
        }
        try
        {
            result.Tokens = Lexer.Tokenize(source);
        }
        catch (NotSupportedYet unsupported)
        {
            throw new NeedsPowerShell($"{unsupported.Feature} (line {unsupported.Line}) is not native yet");
        }
        catch (OtterError error)
        {
            result.Failure = error;
            return result;
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            throw new NeedsPowerShell($"the lexer stopped unexpectedly ({unexpected.GetType().Name})");
        }
        try
        {
            result.Program = Parser.Parse(result.Tokens, out result.CreatesUiResources);
        }
        catch (NotSupportedYet unsupported)
        {
            throw new NeedsPowerShell($"{unsupported.Feature} (line {unsupported.Line}) is not native yet");
        }
        catch (OtterError error)
        {
            result.Failure = error;
            return result;
        }
        catch (Exception unexpected) when (unexpected is not OutOfMemoryException)
        {
            // A parser bug must not change what the user sees.
            throw new NeedsPowerShell($"the parser stopped unexpectedly ({unexpected.GetType().Name})");
        }
        // D-2: names the grammar would misread are refused before anything runs.
        List<OtterError> reserved = ReservedWords.Diagnostics(result.Program, sourceLines);
        // ... and a handler inside another handler's run that uses that run's
        // names (D133; Assert-OtterLanguageContract, LoopPasses.cs).
        List<OtterError> handlerRuns = LoopPassAnalysis.Diagnostics(result.Program, sourceLines);
        if (handlerRuns.Count > 0) reserved = reserved.Concat(handlerRuns).OrderBy(e => e.Line).ThenBy(e => e.Column).ToList();
        if (reserved.Count > 0)
        {
            result.Failure = new OtterMultipleErrors(reserved);
            result.Program = null;
        }
        return result;
    }

    // Show-OtterFailure: an Otter error in full, or Otter's own bug said
    // honestly (D14).
    public static void ShowFailure(Exception failure, bool debugErrors)
    {
        if (failure is OtterError error)
        {
            Host.Line("", site: PsSite.Otter(221));
            Host.Line(error.FormatDetailed(), ConsoleColor.Red);
            Host.Line("");
            return;
        }
        Host.Line("", site: PsSite.Otter(230));
        Host.Line("Otter hit a problem inside itself, which means this is a bug in Otter.", ConsoleColor.Red);
        Host.Line($"  {failure.Message}", ConsoleColor.DarkGray);
        if (debugErrors)
        {
            // otterPS shows PowerShell's script stack here; otter-net has its own.
            Host.Line("");
            Host.Line(failure.StackTrace ?? "", ConsoleColor.DarkGray);
        }
        else
        {
            Host.Line("  Run again with -DebugErrors to see where.", ConsoleColor.DarkGray);
        }
        Host.Line("");
    }
}
