using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Otter.Language;
using Otter.Runtime;
using Otter.Web;

namespace Otter.Playground.Wasm;

[SupportedOSPlatform("browser")]
public static partial class PlaygroundExports
{
    public const string LanguageVersion = "1.0.1";
    private const int DefaultMaxSteps = 250_000;
    private const int DefaultTimeoutMs = 3_000;
    private const int DefaultMaxOutputBytes = 65_536;

    private static readonly HashSet<string> UiKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "window", "page", "button", "text box", "text", "row", "column", "grid",
        "image", "list", "link", "card", "checkbox", "check box",
        "dropdown", "drop down", "select", "slider", "range",
        "text area", "textarea", "badge", "tag", "canvas", "table", "scroll",
        "progress", "progress bar", "toggle", "switch", "radio", "radio button",
        "dialog", "modal", "panel"
    };

    private static readonly string[] UiVariants = { "primary", "secondary", "danger", "outline", "ghost" };

    [JSExport]
    public static string GetVersion() => $"Otter {LanguageVersion} (WebAssembly)";

    [JSExport]
    public static string CheckSource(string source)
    {
        var diagnostics = new List<PlaygroundDiag>();
        var program = CompileForPlayground(source ?? "", diagnostics);
        bool hasUi = program != null && HasUiProgram(program);
        return SerializeResult(
            ok: program != null && diagnostics.Count == 0,
            stdout: "",
            diagnostics: diagnostics,
            elapsedMs: 0,
            stepsExecuted: 0,
            truncated: false,
            hasUi: hasUi
        );
    }

    [JSExport]
    public static string CompileWeb(string source, string? title = "Otter Web App")
    {
        string normalizedSource = (source ?? "").Replace("\r\n", "\n");
        string effectiveTitle = string.IsNullOrWhiteSpace(title) ? "Otter Web App" : title;
        var diagnostics = new List<PlaygroundDiag>();
        var sw = Stopwatch.StartNew();

        ProgramNode? program = CompileForPlayground(normalizedSource, diagnostics);
        if (program == null || diagnostics.Count > 0)
        {
            sw.Stop();
            return SerializeWebResult(false, "", false, diagnostics, sw.Elapsed.TotalMilliseconds);
        }

        try
        {
            bool hasUi = HasUiProgram(program);
            string html = WebCompiler.Compile(program, effectiveTitle);
            sw.Stop();
            return SerializeWebResult(true, html, hasUi, diagnostics, sw.Elapsed.TotalMilliseconds);
        }
        catch (OtterMultipleErrors multiple)
        {
            sw.Stop();
            foreach (var err in multiple.Diagnostics) diagnostics.Add(FromOtterError(err));
            return SerializeWebResult(false, "", false, diagnostics, sw.Elapsed.TotalMilliseconds);
        }
        catch (OtterError err)
        {
            sw.Stop();
            diagnostics.Add(FromOtterError(err));
            return SerializeWebResult(false, "", false, diagnostics, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            diagnostics.Add(new PlaygroundDiag(1, 1, "error", "OTTER_WEB_COMPILE", ex.Message, null));
            return SerializeWebResult(false, "", false, diagnostics, sw.Elapsed.TotalMilliseconds);
        }
    }

    [JSExport]
    public static string RunSource(
        string source,
        string? stdinLines = "",
        int maxSteps = DefaultMaxSteps,
        int timeoutMs = DefaultTimeoutMs,
        int maxOutputBytes = DefaultMaxOutputBytes)
    {
        string normalizedSource = (source ?? "").Replace("\r\n", "\n");
        int stepCap = maxSteps > 0 ? Math.Min(maxSteps, 1_000_000) : DefaultMaxSteps;
        int timeCap = timeoutMs > 0 ? Math.Min(timeoutMs, 10_000) : DefaultTimeoutMs;
        int outputCap = maxOutputBytes > 0 ? Math.Min(maxOutputBytes, 262_144) : DefaultMaxOutputBytes;

        var diagnostics = new List<PlaygroundDiag>();
        var sw = Stopwatch.StartNew();

        ProgramNode? program = CompileForPlayground(normalizedSource, diagnostics);
        if (program == null || diagnostics.Count > 0)
        {
            sw.Stop();
            return SerializeResult(false, "", diagnostics, sw.Elapsed.TotalMilliseconds, 0, false);
        }

        var output = new BoundedStringWriter(outputCap);
        var input = new StringReader(stdinLines ?? "");
        int steps = 0;
        bool truncated = false;

        try
        {
            var interpreter = new Interpreter(output, input, normalizedSource, Array.Empty<string>(), echoInput: true)
            {
                SandboxMode = true,
                StatementHook = (stmt, _) =>
                {
                    steps++;
                    if (steps > stepCap)
                    {
                        throw new PlaygroundAbortException(
                            $"Execution stopped after {stepCap:N0} statements (playground safety limit). Check for an infinite loop.",
                            stmt.Line,
                            "OTTER_PLAYGROUND_STEP_LIMIT"
                        );
                    }
                    if ((steps & 255) == 0 && sw.ElapsedMilliseconds > timeCap)
                    {
                        throw new PlaygroundAbortException(
                            $"Execution timed out after {timeCap} ms (playground safety limit).",
                            stmt.Line,
                            "OTTER_PLAYGROUND_TIMEOUT"
                        );
                    }
                }
            };

            interpreter.Run(program);
            sw.Stop();
            return SerializeResult(true, output.GetText(), diagnostics, sw.Elapsed.TotalMilliseconds, steps, output.WasTruncated);
        }
        catch (PlaygroundAbortException abort)
        {
            sw.Stop();
            truncated = output.WasTruncated || abort.Code == "OTTER_PLAYGROUND_OUTPUT_LIMIT";
            diagnostics.Add(new PlaygroundDiag(
                abort.Line,
                1,
                "error",
                abort.Code,
                abort.Message,
                abort.Code == "OTTER_PLAYGROUND_OUTPUT_LIMIT"
                    ? "Reduce the number of 'say' statements or loop iterations."
                    : "Check loop exit conditions or click Reset to start fresh."
            ));
            return SerializeResult(false, output.GetText(), diagnostics, sw.Elapsed.TotalMilliseconds, steps, truncated);
        }
        catch (OtterMultipleErrors multiple)
        {
            sw.Stop();
            foreach (var err in multiple.Diagnostics)
            {
                diagnostics.Add(FromOtterError(err));
            }
            return SerializeResult(false, output.GetText(), diagnostics, sw.Elapsed.TotalMilliseconds, steps, output.WasTruncated);
        }
        catch (OtterError err)
        {
            sw.Stop();
            diagnostics.Add(FromOtterError(err));
            return SerializeResult(false, output.GetText(), diagnostics, sw.Elapsed.TotalMilliseconds, steps, output.WasTruncated);
        }
        catch (Exception ex)
        {
            sw.Stop();
            diagnostics.Add(new PlaygroundDiag(
                1,
                1,
                "error",
                "OTTER_PLAYGROUND_RUNTIME",
                ex.Message,
                null
            ));
            return SerializeResult(false, output.GetText(), diagnostics, sw.Elapsed.TotalMilliseconds, steps, output.WasTruncated);
        }
    }

    private static ProgramNode? CompileForPlayground(string source, List<PlaygroundDiag> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new ProgramNode(Array.Empty<Stmt>());
        }

        string normalized = source.Replace("\r\n", "\n");
        string[] sourceLines = normalized.Split('\n');

        IReadOnlyList<Token> tokens;
        try
        {
            tokens = Lexer.Tokenize(normalized);
        }
        catch (OtterMultipleErrors multiple)
        {
            foreach (var err in multiple.Diagnostics) diagnostics.Add(FromOtterError(err));
            return null;
        }
        catch (OtterError err)
        {
            diagnostics.Add(FromOtterError(err));
            return null;
        }

        ProgramNode program;
        try
        {
            program = Parser.Parse(tokens);
        }
        catch (OtterMultipleErrors multiple)
        {
            foreach (var err in multiple.Diagnostics) diagnostics.Add(FromOtterError(err));
            return null;
        }
        catch (OtterError err)
        {
            diagnostics.Add(FromOtterError(err));
            return null;
        }

        var semanticErrors = ReservedWords.Diagnostics(program, sourceLines);
        var handlerRuns = LoopPassAnalysis.Diagnostics(program, sourceLines);
        if (handlerRuns.Count > 0)
        {
            semanticErrors = semanticErrors
                .Concat(handlerRuns)
                .OrderBy(e => e.Line)
                .ThenBy(e => e.Column)
                .ToList();
        }

        if (semanticErrors.Count > 0)
        {
            foreach (var err in semanticErrors) diagnostics.Add(FromOtterError(err));
            return null;
        }

        return program;
    }

    private static PlaygroundDiag FromOtterError(OtterError err) =>
        new(
            err.Line > 0 ? err.Line : 1,
            err.Column > 0 ? err.Column : 1,
            "error",
            string.IsNullOrEmpty(err.Code) ? $"OTTER_{err.Stage.ToUpperInvariant()}" : err.Code,
            err.Message,
            err.Suggestion
        );

    private static bool HasUiProgram(ProgramNode program)
    {
        foreach (var stmt in program.Statements)
        {
            if (stmt is ShowStmt or UiElementStmt or CreateUiResourceStmt or WhenStmt)
            {
                return true;
            }
            if (stmt is ObjectDefStmt objDef)
            {
                string kind = objDef.TypeName.ToLowerInvariant();
                foreach (string v in UiVariants)
                {
                    if (kind.StartsWith(v + " ", StringComparison.Ordinal))
                    {
                        kind = kind.Substring(v.Length + 1);
                        break;
                    }
                }
                if (UiKinds.Contains(kind))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string SerializeResult(
        bool ok,
        string stdout,
        List<PlaygroundDiag> diagnostics,
        double elapsedMs,
        int stepsExecuted,
        bool truncated,
        bool hasUi = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", ok);
            writer.WriteString("stdout", stdout);
            writer.WriteBoolean("hasUi", hasUi);
            writer.WriteNumber("elapsedMs", Math.Round(elapsedMs, 2));
            writer.WriteNumber("stepsExecuted", stepsExecuted);
            writer.WriteBoolean("truncated", truncated);
            WriteDiagnosticsArray(writer, diagnostics);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SerializeWebResult(
        bool ok,
        string html,
        bool hasUi,
        List<PlaygroundDiag> diagnostics,
        double elapsedMs)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok", ok);
            writer.WriteString("html", html);
            writer.WriteBoolean("hasUi", hasUi);
            writer.WriteNumber("elapsedMs", Math.Round(elapsedMs, 2));
            WriteDiagnosticsArray(writer, diagnostics);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteDiagnosticsArray(Utf8JsonWriter writer, List<PlaygroundDiag> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var d in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteNumber("line", d.Line);
            writer.WriteNumber("column", d.Column);
            writer.WriteString("severity", d.Severity);
            writer.WriteString("code", d.Code);
            writer.WriteString("message", d.Message);
            if (!string.IsNullOrEmpty(d.Suggestion))
            {
                writer.WriteString("suggestion", d.Suggestion);
            }
            else
            {
                writer.WriteNull("suggestion");
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private sealed record PlaygroundDiag(
        int Line,
        int Column,
        string Severity,
        string Code,
        string Message,
        string? Suggestion
    );

    private sealed class BoundedStringWriter(int maxBytes) : TextWriter
    {
        private readonly StringBuilder sb = new();
        private int writtenBytes;
        private int writtenLines;
        private const int MaxLines = 2000;

        public bool WasTruncated { get; private set; }
        public override Encoding Encoding => Encoding.UTF8;

        public string GetText() => sb.ToString();

        public override void Write(char value)
        {
            if (value == '\r') return;
            if (value == '\n') writtenLines++;
            writtenBytes += value < 128 ? 1 : 3;
            CheckLimit();
            sb.Append(value);
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (char ch in value)
            {
                Write(ch);
            }
        }

        public override void WriteLine(string? value)
        {
            if (!string.IsNullOrEmpty(value)) Write(value);
            Write('\n');
        }

        private void CheckLimit()
        {
            if (writtenBytes > maxBytes || writtenLines > MaxLines)
            {
                WasTruncated = true;
                throw new PlaygroundAbortException(
                    $"Output exceeded the playground limit ({maxBytes:N0} bytes / {MaxLines:N0} lines).",
                    0,
                    "OTTER_PLAYGROUND_OUTPUT_LIMIT"
                );
            }
        }
    }
}
