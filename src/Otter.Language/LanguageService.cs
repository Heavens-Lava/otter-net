using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Otter.Language;

public record CompletionItem(string Label, string Kind, string Detail, string InsertText, string? Documentation = null);
public record DefinitionLocation(string File, int Line, int Column, string Symbol, string Kind);
public record ReferenceLocation(string File, int Line, int Column, string LineText);
public record DiagnosticItem(string File, int Line, int Column, string Severity, string Message, string? Suggestion = null, string? Code = null);
public record DocItem(string Name, string Kind, string Signature, string Description, string? Example, string Source);
public record PackOperationDoc(
    string PackId,
    string PackVersion,
    string VariablePrefix,
    string Name,
    string Description,
    string Signature,
    string ReturnType,
    string SideEffects,
    string ErrorMode,
    string EntryPath);

public static partial class LanguageService
{
    // =========================================================================
    // 1. Formatting
    // =========================================================================

    private static readonly HashSet<string> BlockOpeners = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "while", "for each", "repeat", "to", "try", "when", "on connection", "on data"
    };

    [GeneratedRegex(@"^\s*to\s+([A-Za-z][A-Za-z0-9_]*)(?:\s+([A-Za-z0-9_\s]+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex FuncHeaderRegex();

    [GeneratedRegex(@"^\s*a\s+([A-Za-z][A-Za-z0-9_]*)\s+has\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TypeDefRegex();

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s+has\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectDefRegex();

    [GeneratedRegex(@"^\s*otherwise(?:\s+if\s+.*)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex OtherwiseRegex();

    [GeneratedRegex(@"^\s*for\s+each\s+([A-Za-z][A-Za-z0-9_]*)\s+in\s+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex ForEachRegex();

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s+is\s+.*$", RegexOptions.IgnoreCase)]
    private static partial Regex AssignRegex();

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s+are\s+empty\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AreEmptyRegex();

    [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9_]*)\s+are\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex AreBlockRegex();

    [GeneratedRegex(@"^\s*use\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UseRegex();

    public static string FormatDocument(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "";

        string[] rawLines = source.Replace("\r\n", "\n").Split('\n');
        var formattedLines = new List<string>();
        int indentLevel = 0;
        const int IndentSize = 4;

        int consecutiveEmptyLines = 0;

        foreach (string rawLine in rawLines)
        {
            string trimmed = rawLine.Trim();

            if (trimmed.Length == 0)
            {
                consecutiveEmptyLines++;
                if (consecutiveEmptyLines <= 1 && formattedLines.Count > 0)
                {
                    formattedLines.Add("");
                }
                continue;
            }
            consecutiveEmptyLines = 0;

            // Comment lines
            if (trimmed.StartsWith("#"))
            {
                formattedLines.Add(new string(' ', indentLevel * IndentSize) + trimmed);
                continue;
            }

            // Standalone period terminates a block
            if (trimmed == ".")
            {
                if (indentLevel > 0) indentLevel--;
                formattedLines.Add(new string(' ', indentLevel * IndentSize) + ".");
                continue;
            }

            // `otherwise` aligns with the opening `if`
            if (OtherwiseRegex().IsMatch(trimmed))
            {
                int currentIndent = Math.Max(0, indentLevel - 1);
                formattedLines.Add(new string(' ', currentIndent * IndentSize) + trimmed);
                continue;
            }

            // Normal statement indentation
            formattedLines.Add(new string(' ', indentLevel * IndentSize) + trimmed);

            // Check if this statement opens an indented block
            bool opensBlock = false;
            if (trimmed.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("while ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("repeat ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("count from ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("each ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("try", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("when ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("on connection", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("on data", StringComparison.OrdinalIgnoreCase) ||
                ForEachRegex().IsMatch(trimmed) ||
                FuncHeaderRegex().IsMatch(trimmed) ||
                TypeDefRegex().IsMatch(trimmed) ||
                ObjectDefRegex().IsMatch(trimmed) ||
                AreBlockRegex().IsMatch(trimmed) ||
                trimmed.EndsWith(" with", StringComparison.OrdinalIgnoreCase))
            {
                opensBlock = true;
            }

            if (opensBlock)
            {
                indentLevel++;
            }
        }

        string result = string.Join("\n", formattedLines).TrimEnd() + "\n";
        return result;
    }

    // =========================================================================
    // 2. Diagnostics
    // =========================================================================

    public static List<DiagnosticItem> GetDiagnostics(string source, string filePath)
    {
        var diagnostics = new List<DiagnosticItem>();
        try
        {
            var tokens = Lexer.Tokenize(source);
            var importedFunctions = LoadImportedFunctionArities(source, filePath);
            Parser.Parse(tokens, importedFunctions);
        }
        catch (OtterMultipleErrors multiple)
        {
            foreach (var err in multiple.Diagnostics)
            {
                diagnostics.Add(new DiagnosticItem(
                    filePath,
                    err.Line > 0 ? err.Line : 1,
                    1,
                    "Error",
                    err.Message,
                    err.Suggestion
                ));
            }
        }
        catch (OtterError err)
        {
            diagnostics.Add(new DiagnosticItem(
                filePath,
                err.Line > 0 ? err.Line : 1,
                1,
                "Error",
                err.Message,
                err.Suggestion
            ));
        }
        catch (Exception ex)
        {
            diagnostics.Add(new DiagnosticItem(
                filePath,
                1,
                1,
                "Error",
                ex.Message,
                null
            ));
        }
        return diagnostics;
    }

    // Non-breaking scoping safety warnings (LSP Severity = Warning).
    // Kept separate from GetDiagnostics() so syntax-error callers remain 100% backward compatible.
    public static List<DiagnosticItem> GetScopingWarnings(string source, string filePath)
    {
        var warnings = new List<DiagnosticItem>();
        ProgramNode program;
        try
        {
            var tokens = Lexer.Tokenize(source);
            var importedFunctions = LoadImportedFunctionArities(source, filePath);
            program = Parser.Parse(tokens, importedFunctions);
        }
        catch
        {
            return warnings;
        }

        var topLevelVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stmt in program.Statements)
        {
            if (stmt is FunctionDefStmt) continue;
            CollectAssignedVarNames(stmt, topLevelVars);
        }

        if (topLevelVars.Count == 0) return warnings;

        foreach (var stmt in program.Statements)
        {
            if (stmt is not FunctionDefStmt fn) continue;
            var paramSet = new HashSet<string>(fn.Parameters, StringComparer.Ordinal);
            var bodyAssigns = new List<(string Name, int Line)>();
            foreach (var bStmt in fn.Body)
            {
                CollectAssignedVarLocations(bStmt, bodyAssigns);
            }

            var reportedInFn = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (varName, lineNum) in bodyAssigns)
            {
                if (paramSet.Contains(varName)) continue;
                if (!topLevelVars.Contains(varName)) continue;
                if (!reportedInFn.Add(varName)) continue;

                warnings.Add(new DiagnosticItem(
                    filePath,
                    lineNum > 0 ? lineNum : fn.Line,
                    1,
                    "Warning",
                    $"Function '{fn.Name}' assigns to outer-scope variable '{varName}', which mutates the caller's variable under Otter 1.0.1 dynamic scoping.",
                    $"Use a unique or prefixed local name (e.g. '_local_{varName}') if outer mutation is unintended, or pass '{varName}' as a parameter.",
                    "OTTER_OUTER_SCOPE_MUTATION"
                ));
            }
        }

        return warnings;
    }

    private static void CollectAssignedVarNames(Stmt stmt, HashSet<string> target)
    {
        var list = new List<(string Name, int Line)>();
        CollectAssignedVarLocations(stmt, list);
        foreach (var (name, _) in list) target.Add(name);
    }

    private static void CollectAssignedVarLocations(Stmt stmt, List<(string Name, int Line)> assigned)
    {
        switch (stmt)
        {
            case AssignStmt assign when assign.Target is VariableExpr v:
                assigned.Add((v.Name, stmt.Line));
                break;
            case ObjectDefStmt objDef:
                assigned.Add((objDef.Name, stmt.Line));
                break;
            case ListDefStmt listDef:
                assigned.Add((listDef.Name, stmt.Line));
                break;
            case MathIntoStmt mathInto:
                assigned.Add((mathInto.Target, stmt.Line));
                break;
            case AddToStmt addTo:
                assigned.Add((addTo.Target, stmt.Line));
                break;
            case RemoveFromStmt remFrom:
                assigned.Add((remFrom.Target, stmt.Line));
                break;
            case AskStmt ask:
                assigned.Add((ask.Name, stmt.Line));
                break;
            case CountStmt count:
                assigned.Add((count.VariableName, stmt.Line));
                foreach (var s in count.Body) CollectAssignedVarLocations(s, assigned);
                break;
            case ForEachStmt forEach:
                assigned.Add((forEach.VariableName, stmt.Line));
                foreach (var s in forEach.Body) CollectAssignedVarLocations(s, assigned);
                break;
            case IfStmt ifStmt:
                foreach (var branch in ifStmt.Branches)
                    foreach (var s in branch.Body) CollectAssignedVarLocations(s, assigned);
                if (ifStmt.ElseBody != null)
                    foreach (var s in ifStmt.ElseBody) CollectAssignedVarLocations(s, assigned);
                break;
            case WhileStmt whileStmt:
                foreach (var s in whileStmt.Body) CollectAssignedVarLocations(s, assigned);
                break;
            case RepeatStmt repeatStmt:
                foreach (var s in repeatStmt.Body) CollectAssignedVarLocations(s, assigned);
                break;
            case TryStmt tryStmt:
                if (!string.IsNullOrEmpty(tryStmt.ErrorTarget)) assigned.Add((tryStmt.ErrorTarget, stmt.Line));
                foreach (var s in tryStmt.Body) CollectAssignedVarLocations(s, assigned);
                if (tryStmt.OtherwiseBody != null)
                    foreach (var s in tryStmt.OtherwiseBody) CollectAssignedVarLocations(s, assigned);
                break;
            case CallStmt call when !string.IsNullOrEmpty(call.ResultTarget):
                assigned.Add((call.ResultTarget, stmt.Line));
                break;
            case ReplaceStmt replace when !string.IsNullOrEmpty(replace.ResultTarget):
                assigned.Add((replace.ResultTarget, stmt.Line));
                break;
            case SplitStmt split when !string.IsNullOrEmpty(split.Target):
                assigned.Add((split.Target, stmt.Line));
                break;
            case JoinStmt join when !string.IsNullOrEmpty(join.Target):
                assigned.Add((join.Target, stmt.Line));
                break;
            case FindStmt find when !string.IsNullOrEmpty(find.Target):
                assigned.Add((find.Target, stmt.Line));
                break;
            case GetKeyStmt getKey when !string.IsNullOrEmpty(getKey.ResultTarget):
                assigned.Add((getKey.ResultTarget, stmt.Line));
                break;
            case SortStmt sort when !string.IsNullOrEmpty(sort.Target):
                assigned.Add((sort.Target, stmt.Line));
                break;
        }
    }

    private static Dictionary<string, int> LoadImportedFunctionArities(string source, string? filePath)
    {
        var funcs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filePath)) return funcs;
        if (filePath.Equals("memory", StringComparison.OrdinalIgnoreCase)) return funcs;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void ScanFile(string content, string currentFilePath)
        {
            string? dir;
            try { dir = Path.GetDirectoryName(Path.GetFullPath(currentFilePath)); }
            catch { return; }
            if (string.IsNullOrEmpty(dir)) return;

            foreach (string rawLine in content.Replace("\r\n", "\n").Split('\n'))
            {
                var mUse = UseRegex().Match(rawLine);
                if (!mUse.Success) continue;

                string rel = mUse.Groups[1].Value;
                string importedPath;
                try { importedPath = Path.GetFullPath(Path.Combine(dir, rel)); }
                catch { continue; }
                if (!File.Exists(importedPath) || !visited.Add(importedPath)) continue;

                try
                {
                    string impContent = File.ReadAllText(importedPath);
                    foreach (string impLine in impContent.Replace("\r\n", "\n").Split('\n'))
                    {
                        var mFunc = FuncHeaderRegex().Match(impLine);
                        if (!mFunc.Success) continue;
                        string fnName = mFunc.Groups[1].Value;
                        string rawParams = mFunc.Groups[2].Success ? mFunc.Groups[2].Value.Trim() : "";
                        int arity = string.IsNullOrEmpty(rawParams)
                            ? 0
                            : rawParams.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        funcs[fnName] = arity;
                    }
                    ScanFile(impContent, importedPath);
                }
                catch
                {
                    // Ignore unreadable imported file during diagnostics
                }
            }
        }

        ScanFile(source, filePath);
        return funcs;
    }

    public static List<PackOperationDoc> LoadImportedPackOperations(string source, string? filePath)
    {
        var ops = new List<PackOperationDoc>();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(filePath)) return ops;

        string fileDir;
        try { fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? "."; }
        catch { return ops; }

        var seenManifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in source.Replace("\r\n", "\n").Split('\n'))
        {
            var mUse = UseRegex().Match(rawLine);
            if (!mUse.Success) continue;

            string rel = mUse.Groups[1].Value;
            string importedPath;
            try { importedPath = Path.GetFullPath(Path.Combine(fileDir, rel)); }
            catch { continue; }
            if (!File.Exists(importedPath)) continue;

            string? impDir = Path.GetDirectoryName(importedPath);
            if (string.IsNullOrEmpty(impDir)) continue;

            string[] candidateManifests =
            {
                Path.Combine(impDir, "otter.pack.json"),
                Path.Combine(Directory.GetParent(impDir)?.FullName ?? impDir, "otter.pack.json")
            };

            string? manifestPath = candidateManifests.FirstOrDefault(File.Exists);
            if (manifestPath == null || !seenManifests.Add(manifestPath)) continue;

            try
            {
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
                if (manifest == null) continue;
                int schemaVer = manifest["schemaVersion"]?.GetValue<int>() ?? 1;
                if (schemaVer < 2 || manifest["provides"]?["operations"] is not JsonArray opArray) continue;

                string pub = manifest["publisher"]?.ToString() ?? "unknown";
                string pkgName = manifest["name"]?.ToString() ?? "pack";
                string packId = $"{pub}/{pkgName}";
                string packVer = manifest["version"]?.ToString() ?? "1.0.0";
                string varPrefix = manifest["variablePrefix"]?.ToString() ?? "none";

                foreach (var node in opArray)
                {
                    if (node is not JsonObject opObj) continue;
                    string opName = opObj["name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(opName)) continue;

                    string opDesc = opObj["description"]?.ToString() ?? "";
                    var paramParts = new List<string>();
                    if (opObj["parameters"] is JsonArray pArr)
                    {
                        foreach (var pn in pArr)
                        {
                            if (pn is not JsonObject po) continue;
                            string pName = po["name"]?.ToString() ?? "arg";
                            string pType = po["type"]?.ToString() ?? "any";
                            paramParts.Add($"{pName}: {pType}");
                        }
                    }
                    string retType = opObj["returns"]?["type"]?.ToString() ?? "any";
                    string sideEffects = opObj["sideEffects"] is JsonArray seArr && seArr.Count > 0
                        ? string.Join(", ", seArr.Select(s => s?.ToString() ?? "none"))
                        : "none";
                    string errMode = opObj["errorMode"]?.ToString() ?? "returns-fallback";
                    string sig = $"{opName}({string.Join(", ", paramParts)}) -> {retType}";

                    ops.Add(new PackOperationDoc(packId, packVer, varPrefix, opName, opDesc, sig, retType, sideEffects, errMode, importedPath));
                }
            }
            catch
            {
                // Ignore malformed third-party manifest during editor completion/hover
            }
        }

        return ops;
    }

    // =========================================================================
    // 3. Completions & Suggestions
    // =========================================================================

    private static readonly List<CompletionItem> CoreKeywords = new()
    {
        new("if", "Keyword", "Conditional branch", "if ${1:condition}\n    $0\n."),
        new("otherwise", "Keyword", "Alternative conditional branch", "otherwise\n    $0"),
        new("otherwise if", "Keyword", "Chained conditional branch", "otherwise if ${1:condition}\n    $0"),
        new("while", "Keyword", "While loop", "while ${1:condition}\n    $0\n."),
        new("for each", "Keyword", "Iterate over collection", "for each ${1:item} in ${2:items}\n    $0\n."),
        new("to", "Keyword", "Function declaration", "to ${1:funcName} ${2:param}\n    $0\n    return ${3:result}\n."),
        new("return", "Keyword", "Return value from function", "return $0"),
        new("say", "Keyword", "Print to standard output", "say $0"),
        new("ask", "Keyword", "Read input from user", "ask \"${1:prompt}\" into ${2:variable}"),
        new("convert", "Keyword", "Convert data format (json/csv)", "convert ${1:data} to json into ${2:rawJson}"),
        new("try", "Keyword", "Safe error handling", "try\n    $1\notherwise\n    $0\n."),
        new("use", "Keyword", "Import external Otter file", "use \"$0\""),
        new("has", "Keyword", "Define object or type fields", "has\n    $0\n."),
        new("are empty", "Keyword", "Initialize empty list", "are empty"),
        new("add", "Keyword", "Add item to list", "add ${1:item} to ${2:list}"),
        new("remove", "Keyword", "Remove item from list", "remove ${1:item} from ${2:list}"),
        new("length of", "Keyword", "Length of collection", "length of $0"),
        new("is", "Operator", "Equality comparison or assignment", "is "),
        new("is not", "Operator", "Inequality comparison", "is not "),
        new("is at least", "Operator", "Greater than or equal comparison", "is at least "),
        new("is at most", "Operator", "Less than or equal comparison", "is at most "),
        new("is greater than", "Operator", "Strict greater comparison", "is greater than "),
        new("is less than", "Operator", "Strict less comparison", "is less than "),
        new("plus", "Operator", "Arithmetic addition or string concat", "plus "),
        new("minus", "Operator", "Arithmetic subtraction", "minus "),
        new("times", "Operator", "Arithmetic multiplication", "times "),
        new("divided by", "Operator", "Arithmetic division", "divided by "),
        new("contains", "Operator", "Collection contains check", "contains "),

        // Modern UI statements, River Slate components, and value-first properties
        new("with", "Keyword", "Attach value-first or property-first attributes", "with "),
        new("put", "Keyword", "Place UI controls into a container", "put ${1:control} in ${2:container}"),
        new("show", "Keyword", "Mount and display the root page or window", "show ${1:app}"),
        new("when", "Keyword", "Handle UI click or change event", "when ${1:control} is ${2:clicked}\n    $0\n."),
        new("page", "Type", "River Slate web page root container", "page with \"${1:My App}\" title, true scroll", "Root web page container. Supports `\"light\" theme` opt-in and `true scroll`."),
        new("card", "Type", "River Slate elevated surface card", "card with\n    \"${1:Title}\" title,\n    \"${2:Subtitle}\" subtitle\n.", "Elevated card container with optional `title`, `subtitle`, and `theme`."),
        new("grid", "Type", "River Slate responsive multi-column grid", "grid with ${1:2} columns, ${2:20} spacing", "Responsive CSS grid container (`columns`, `spacing`)."),
        new("row", "Type", "Horizontal flex layout container", "row with ${1:12} spacing, true wrap"),
        new("column", "Type", "Vertical flex layout container", "column with ${1:16} spacing"),
        new("primary button", "Type", "Primary accent button (River Slate)", "primary button with \"${1:Save}\" text"),
        new("secondary button", "Type", "Secondary surface button (River Slate)", "secondary button with \"${1:Cancel}\" text"),
        new("outline button", "Type", "Outlined button variant (River Slate)", "outline button with \"${1:Preview}\" text"),
        new("ghost button", "Type", "Borderless ghost button variant (River Slate)", "ghost button with \"${1:Dismiss}\" text"),
        new("danger button", "Type", "Destructive action button (River Slate)", "danger button with \"${1:Delete}\" text"),
        new("text box", "Type", "Labeled text input field (River Slate)", "text box with\n    \"${1:Label}\" label,\n    \"${2:Placeholder}\" placeholder\n."),
        new("text area", "Type", "Multi-line text area field (River Slate)", "text area with \"${1:Notes}\" label, ${2:3} rows"),
        new("dropdown", "Type", "Custom-styled select dropdown (River Slate)", "dropdown with \"${1:Region}\" label, \"${2:Option A, Option B}\" options"),
        new("checkbox", "Type", "Accessible checkbox control", "checkbox with \"${1:Enable option}\" text, true checked"),
        new("toggle", "Type", "Switch toggle control", "toggle with \"${1:Live updates}\" text, true checked"),
        new("radio", "Type", "Radio option button", "radio with \"${1:Standard}\" text, \"${2:plan}\" name"),
        new("slider", "Type", "Bounded range slider with live readout", "slider with\n    \"${1:Threshold}\" label,\n    ${2:0} min,\n    ${3:100} max,\n    ${4:50} value,\n    true showvalue\n."),
        new("progress", "Type", "Linear progress indicator", "progress with ${1:50} value, ${2:100} max"),
        new("badge", "Type", "Semantic status badge", "badge with \"${1:Healthy}\" text, \"${2:success}\" variant"),
        new("table", "Type", "Structured data table", "table"),
        new("theme", "Property", "River Slate theme (\"dark\" default or \"light\" opt-in)", "theme", "Set `\"light\" theme` (value-first) or `theme \"light\"` on a page, window, or card."),
        new("subtitle", "Property", "River Slate card subtitle copy", "subtitle", "Secondary descriptive line under a card `title`."),
        new("label", "Property", "Accessible field label (River Slate)", "label", "Renders a `<label>` linked to the input, dropdown, textarea, or slider."),
        new("placeholder", "Property", "Input placeholder hint text", "placeholder"),
        new("hint", "Property", "Helper text beneath a form field", "hint"),
        new("error", "Property", "Validation error message (sets aria-invalid=\"true\")", "error"),
        new("showvalue", "Property", "Show live numeric slider readout (true/false)", "showvalue", "Use `true showvalue` on a `slider` to render a live `<output>` value badge."),
        new("hideheader", "Property", "Hide default page header bar (true/false)", "hideheader"),
        new("columns", "Property", "Number of responsive grid columns", "columns"),
        new("spacing", "Property", "Flex/grid gap in pixels", "spacing"),
        new("variant", "Property", "Semantic style variant (primary, secondary, outline, ghost, danger, success, warning, muted, heading, code)", "variant"),
        new("disabled", "Property", "Disable interactive control (true/false)", "disabled"),
        new("checked", "Property", "Initial checked state for checkbox/toggle/radio", "checked"),
        new("wrap", "Property", "Allow flex items in a row to wrap (true/false)", "wrap"),
        new("scroll", "Property", "Enable document scrolling on page (true/false)", "scroll"),
        new("step", "Property", "Numeric step increment for sliders", "step")
    };

    public static List<CompletionItem> GetCompletions(string source, int line, int column, string? filePath = null, string? libraryDir = null)
    {
        var items = new List<CompletionItem>(CoreKeywords);
        var seenSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in CoreKeywords) seenSymbols.Add(k.Label);

        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];

            // Functions
            var mFunc = FuncHeaderRegex().Match(l);
            if (mFunc.Success)
            {
                string fnName = mFunc.Groups[1].Value;
                string rawParams = mFunc.Groups[2].Success ? mFunc.Groups[2].Value.Trim() : "";
                if (seenSymbols.Add(fnName))
                {
                    items.Add(new CompletionItem(fnName, "Function", $"Function ({rawParams})", $"{fnName} ", $"Defined at line {i + 1}"));
                }
                if (!string.IsNullOrEmpty(rawParams))
                {
                    foreach (var p in rawParams.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (seenSymbols.Add(p)) items.Add(new CompletionItem(p, "Parameter", "Function parameter", p));
                    }
                }
            }

            // Types
            var mType = TypeDefRegex().Match(l);
            if (mType.Success)
            {
                string typeName = mType.Groups[1].Value;
                if (seenSymbols.Add(typeName))
                {
                    items.Add(new CompletionItem(typeName, "Type", "Custom Type", typeName, $"Defined at line {i + 1}"));
                }
            }

            // Variables
            var mAssign = AssignRegex().Match(l);
            if (mAssign.Success)
            {
                string varName = mAssign.Groups[1].Value;
                if (!ReservedWords.IsReserved(varName) && seenSymbols.Add(varName))
                {
                    items.Add(new CompletionItem(varName, "Variable", "Local variable", varName, $"Assigned at line {i + 1}"));
                }
            }
            var mForEach = ForEachRegex().Match(l);
            if (mForEach.Success)
            {
                string itName = mForEach.Groups[1].Value;
                if (seenSymbols.Add(itName))
                {
                    items.Add(new CompletionItem(itName, "Variable", "Loop iteration variable", itName, $"Loop variable at line {i + 1}"));
                }
            }
        }

        // Imported Schema v2 Pack operations (`use ".../src/main.ot"` with sibling `otter.pack.json`)
        foreach (var op in LoadImportedPackOperations(source, filePath))
        {
            if (seenSymbols.Add(op.Name))
            {
                string doc = $"**Pack:** `{op.PackId}` v{op.PackVersion} (prefix `{op.VariablePrefix}`)\n\n" +
                             $"```otter\n{op.Signature}\n```\n\n" +
                             $"{op.Description}\n\n" +
                             $"- **Side Effects:** `{op.SideEffects}`\n" +
                             $"- **Error Mode:** `{op.ErrorMode}`";
                items.Add(new CompletionItem(
                    op.Name,
                    "Function",
                    $"{op.PackId} v{op.PackVersion}: {op.Signature}",
                    $"{op.Name} ",
                    doc));
            }
        }

        // Prefix filter if line is available
        if (line > 0 && line <= lines.Length)
        {
            string targetLine = lines[line - 1];
            int colIdx = Math.Min(column - 1, targetLine.Length);
            string beforeCursor = targetLine.Substring(0, colIdx);
            var wordMatch = Regex.Match(beforeCursor, @"([A-Za-z0-9_]+)$");
            if (wordMatch.Success)
            {
                string prefix = wordMatch.Value;
                items = items.Where(it => it.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        return items;
    }

    // =========================================================================
    // 4. Go-To-Definition
    // =========================================================================

    public static DefinitionLocation? GetDefinition(string source, int line, int column, string filePath, string? libraryDir = null)
    {
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        if (line <= 0 || line > lines.Length) return null;

        string targetLine = lines[line - 1];
        string? symbol = ExtractWordAtColumn(targetLine, column);
        if (string.IsNullOrEmpty(symbol)) return null;

        // 1. Search current file for function definition
        for (int i = 0; i < lines.Length; i++)
        {
            var mFunc = FuncHeaderRegex().Match(lines[i]);
            if (mFunc.Success && string.Equals(mFunc.Groups[1].Value, symbol, StringComparison.OrdinalIgnoreCase))
            {
                int col = lines[i].IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1;
                return new DefinitionLocation(filePath, i + 1, col, symbol, "Function");
            }

            var mType = TypeDefRegex().Match(lines[i]);
            if (mType.Success && string.Equals(mType.Groups[1].Value, symbol, StringComparison.OrdinalIgnoreCase))
            {
                int col = lines[i].IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1;
                return new DefinitionLocation(filePath, i + 1, col, symbol, "Type");
            }

            var mAssign = AssignRegex().Match(lines[i]);
            if (mAssign.Success && string.Equals(mAssign.Groups[1].Value, symbol, StringComparison.OrdinalIgnoreCase))
            {
                int col = lines[i].IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1;
                return new DefinitionLocation(filePath, i + 1, col, symbol, "Variable");
            }
        }

        // 2. Search imported files (`use "..."`)
        string fileDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        foreach (string l in lines)
        {
            var mUse = UseRegex().Match(l);
            if (mUse.Success)
            {
                string relPath = mUse.Groups[1].Value;
                string importedPath = Path.GetFullPath(Path.Combine(fileDir, relPath));
                if (File.Exists(importedPath))
                {
                    string impSource = File.ReadAllText(importedPath);
                    var impDef = GetDefinition(impSource, 1, 1, importedPath, libraryDir);
                    if (impDef != null && string.Equals(impDef.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        return impDef;
                    }
                    // Direct scan of imported file lines
                    string[] impLines = impSource.Replace("\r\n", "\n").Split('\n');
                    for (int j = 0; j < impLines.Length; j++)
                    {
                        var mf = FuncHeaderRegex().Match(impLines[j]);
                        if (mf.Success && string.Equals(mf.Groups[1].Value, symbol, StringComparison.OrdinalIgnoreCase))
                        {
                            int col = impLines[j].IndexOf(symbol, StringComparison.OrdinalIgnoreCase) + 1;
                            return new DefinitionLocation(importedPath, j + 1, col, symbol, "Function");
                        }
                    }
                }
            }
        }

        return null;
    }

    // =========================================================================
    // 5. Find References
    // =========================================================================

    public static List<ReferenceLocation> FindReferences(string source, int line, int column, string filePath)
    {
        var refs = new List<ReferenceLocation>();
        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        if (line <= 0 || line > lines.Length) return refs;

        string? symbol = ExtractWordAtColumn(lines[line - 1], column);
        if (string.IsNullOrEmpty(symbol)) return refs;

        string pattern = @"\b" + Regex.Escape(symbol) + @"\b";
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.Trim().StartsWith("#")) continue; // Skip comments

            var matches = rx.Matches(l);
            foreach (Match m in matches)
            {
                refs.Add(new ReferenceLocation(filePath, i + 1, m.Index + 1, l.Trim()));
            }
        }
        return refs;
    }

    // =========================================================================
    // 6. Safe Symbol Renaming
    // =========================================================================

    public static (bool Success, string NewSource, int ChangesCount, string? Error) RenameSymbol(string source, int line, int column, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return (false, source, 0, "New symbol name cannot be empty.");
        if (!Regex.IsMatch(newName, @"^[A-Za-z][A-Za-z0-9_]*$"))
        {
            return (false, source, 0, $"'{newName}' is not a valid Otter identifier.");
        }
        if (ReservedWords.IsReserved(newName))
        {
            return (false, source, 0, $"'{newName}' is a reserved Otter language keyword.");
        }

        string[] lines = source.Replace("\r\n", "\n").Split('\n');
        if (line <= 0 || line > lines.Length) return (false, source, 0, "Invalid cursor line.");

        string? oldSymbol = ExtractWordAtColumn(lines[line - 1], column);
        if (string.IsNullOrEmpty(oldSymbol)) return (false, source, 0, "No identifier found at cursor position.");
        if (string.Equals(oldSymbol, newName, StringComparison.OrdinalIgnoreCase))
        {
            return (false, source, 0, "New name is identical to current name.");
        }

        string pattern = @"\b" + Regex.Escape(oldSymbol) + @"\b";
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);

        int count = 0;
        var newLines = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i];
            if (l.Trim().StartsWith("#"))
            {
                newLines[i] = l;
                continue;
            }
            int matchesInLine = rx.Matches(l).Count;
            count += matchesInLine;
            newLines[i] = rx.Replace(l, newName);
        }

        string newSource = string.Join("\n", newLines);

        // Verify updated source parses cleanly
        var diagnostics = GetDiagnostics(newSource, "memory");
        if (diagnostics.Count > 0)
        {
            return (false, source, 0, $"Rename caused syntax error: {diagnostics[0].Message}");
        }

        return (true, newSource, count, null);
    }

    // =========================================================================
    // 7. Documentation Lookup
    // =========================================================================

    private static readonly Dictionary<string, (string Usage, string Description, string Example)> CoreDocs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["say"] = ("say <expression>...", "Prints one or more expressions to standard output, separated by spaces and terminated by a newline.", "say \"Hello, world!\"\nsay \"Result:\" 42"),
        ["ask"] = ("ask <variable> [with <prompt>]", "Prompts the user on the console and stores the entered text into the target variable.", "ask name with \"What is your name? \"\nsay \"Hello,\" name"),
        ["if"] = ("if <condition>\n    <statements>\n.", "Executes the indented block if the condition evaluates to true. Can be followed by otherwise.", "if score is greater than 100\n    say \"New high score!\"\n."),
        ["otherwise"] = ("otherwise\n    <statements>\n.", "Alternative branch in an if-statement executed when the preceding condition evaluates to false.", "if score is greater than 100\n    say \"High\"\notherwise\n    say \"Low\"\n."),
        ["while"] = ("while <condition>\n    <statements>\n.", "Repeats the indented block as long as the condition remains true.", "count is 0\nwhile count is less than 5\n    say count\n    count is count plus 1\n."),
        ["repeat"] = ("repeat <count> times\n    <statements>\n.", "Repeats the indented block a fixed number of times.", "repeat 3 times\n    say \"Otter!\"\n."),
        ["count"] = ("count from <start> to <end> [as <var>]\n    <statements>\n.", "Iterates numbers from start to end inclusive, optionally binding the counter variable.", "count from 1 to 5 as i\n    say i\n."),
        ["each"] = ("for each <item> in <collection>\n    <statements>\n.", "Iterates over each item in a list or collection.", "for each fruit in fruits\n    say fruit\n."),
        ["for each"] = ("for each <item> in <collection>\n    <statements>\n.", "Iterates over each item in a list or collection.", "for each fruit in fruits\n    say fruit\n."),
        ["make"] = ("make <type> into <target>", "Constructs an instance of a declared type or custom object.", "make Person into p\np is a Person\nname of p is \"Alice\""),
        ["use"] = ("use \"<path>\" [as <alias>]", "Imports functions, types, and definitions from another Otter source file or package.", "use \"helpers.ot\"\nuse \"@otter/json-utils\""),
        ["stop"] = ("stop [with <value>]", "Stops execution of the current function or loop, optionally returning a value.", "if done\n    stop with result\n."),
        ["return"] = ("return [with <value>]", "Returns from the enclosing function, optionally with a return value.", "return with total"),
        ["read"] = ("read <path> into <variable>", "Reads the entire contents of a file as a string.", "read \"config.json\" into content"),
        ["write"] = ("write <content> to <path>", "Writes text content into a file, overwriting existing contents.", "write data to \"output.txt\""),
        ["append"] = ("append <content> to <path>", "Appends text content to an existing file.", "append logMessage to \"app.log\""),
        ["try"] = ("try\n    <statements>\notherwise into <err>\n    <fallback>\n.", "Executes statements in a safe block, catching runtime errors in the otherwise clause.", "try\n    read \"missing.txt\" into s\notherwise into err\n    say \"Could not read file:\" message of err\n."),
        ["convert"] = ("convert <data> to <format> into <target>", "Converts structured data to/from JSON, CSV, XML, etc.", "convert jsonText to json into obj"),
        ["with"] = ("<name> is a <kind> with <value> <property>, ...", "Configures object or UI component properties using modern value-first syntax (single-line or multi-line `with ... .` block) or legacy property-first syntax.", "deployBtn is a primary button with \"Deploy\" text\napp is a page with\n    \"Dashboard\" title,\n    \"light\" theme,\n    true scroll\n."),
        ["put"] = ("put <item1>, <item2> in <container>", "Places one or more UI controls inside a page, card, grid, row, or column container.", "put header, mainGrid in app"),
        ["show"] = ("show <pageOrWindow>", "Displays the root page or window UI application.", "show app"),
        ["when"] = ("when <control> is clicked | changed\n    <statements>\n.", "Registers a reactive UI event handler for button clicks, input edits, or slider changes.", "when deployBtn is clicked\n    text of statusLabel is \"Deployed\"\n."),
        ["theme"] = ("\"dark\" theme | \"light\" theme", "Selects the River Slate design theme on a page, window, or individual card. Dark mode is the default; \"light\" is a first-class opt-in.", "settingsCard is a card with\n    \"Light Mode\" title,\n    \"light\" theme\n."),
        ["subtitle"] = ("\"<text>\" subtitle", "Renders muted supporting subtitle copy beneath a River Slate card title.", "metricsCard is a card with\n    \"System Health\" title,\n    \"Live latency and alert thresholds.\" subtitle\n."),
        ["label"] = ("\"<text>\" label", "Renders an accessible `<label>` associated with a text box, text area, dropdown, or slider.", "emailBox is a text box with \"Email\" label, \"dev@otter.dev\" placeholder"),
        ["hint"] = ("\"<text>\" hint", "Displays muted helper copy beneath a River Slate form control.", "nameBox is a text box with \"Project\" label, \"Lowercase letters only.\" hint"),
        ["error"] = ("\"<text>\" error", "Displays an inline validation error message and sets `aria-invalid=\"true\"` on the field.", "slugBox is a text box with \"Slug\" label, \"Invalid slug.\" error"),
        ["showvalue"] = ("true showvalue", "Displays a live synchronized numeric readout beside a River Slate slider.", "cpuSlider is a slider with \"CPU %\" label, 0 min, 100 max, 72 value, true showvalue"),
        ["columns"] = ("<count> columns", "Sets the responsive desktop column count on a River Slate grid container.", "mainGrid is a grid with 2 columns, 20 spacing"),
        ["variant"] = ("\"<variant>\" variant", "Selects a semantic River Slate visual variant for buttons (`primary`, `secondary`, `outline`, `ghost`, `danger`), badges (`success`, `warning`, `danger`, `outline`), or text (`title`, `heading`, `subheading`, `muted`, `code`).", "statusBadge is a badge with \"Operational\" text, \"success\" variant"),
        ["card"] = ("<name> is a card with \"<title>\" title, \"<subtitle>\" subtitle", "River Slate elevated container card with optional title, subtitle, and per-card theme override.", "summaryCard is a card with \"Summary\" title, \"Live telemetry\" subtitle"),
        ["grid"] = ("<name> is a grid with <n> columns, <px> spacing", "River Slate responsive grid layout that automatically collapses on mobile viewports.", "cardsGrid is a grid with 2 columns, 20 spacing"),
        ["slider"] = ("<name> is a slider with \"<label>\" label, <min> min, <max> max, <val> value, true showvalue", "Bounded range slider with optional live numeric readout (`true showvalue`) and `when ... is changed` handler.", "threshold is a slider with \"Alert %\" label, 10 min, 100 max, 75 value, true showvalue"),
        ["outline"] = ("<name> is a outline button with \"<text>\" text", "River Slate outline button variant with subtle border and hover fill.", "previewBtn is a outline button with \"Preview\" text"),
        ["ghost"] = ("<name> is a ghost button with \"<text>\" text", "River Slate borderless ghost button variant for low-emphasis actions.", "cancelBtn is a ghost button with \"Cancel\" text"),
    };

    public static List<DocItem> LookupDocumentation(string query, string? libraryDir = null)
    {
        var results = new List<DocItem>();
        if (string.IsNullOrWhiteSpace(query)) return results;
        query = query.Trim();

        // 1. Core language documentation
        if (CoreDocs.TryGetValue(query, out var coreDoc))
        {
            results.Add(new DocItem(
                query,
                "Language Statement",
                coreDoc.Usage,
                coreDoc.Description,
                coreDoc.Example,
                "Otter 1.0.1 Core Specification"
            ));
        }

        // 2. Built-in language functions and keywords
        if (ReservedWords.IsReserved(query))
        {
            if (ReservedWords.TryGetReason(query, out string? reason))
            {
                results.Add(new DocItem(
                    query,
                    "Language Keyword",
                    query,
                    $"Authoritative Otter 1.0.1 keyword: {string.Format(reason ?? "", query)}",
                    $"say \"Testing {query}\"",
                    "Otter Core Specification"
                ));
            }
            else
            {
                results.Add(new DocItem(
                    query,
                    "Language Keyword",
                    query,
                    $"Authoritative Otter 1.0.1 reserved word: {query}",
                    $"say \"Testing {query}\"",
                    "Otter Core Specification"
                ));
            }
        }

        // 2. Search Snippets
        if (!string.IsNullOrEmpty(libraryDir) && Directory.Exists(libraryDir))
        {
            string catalogPath = Path.Combine(libraryDir, "snippets", "catalog.json");
            if (File.Exists(catalogPath))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(catalogPath));
                    var snips = node?["snippets"]?.AsArray();
                    if (snips != null)
                    {
                        foreach (var s in snips)
                        {
                            if (s == null) continue;
                            string id = s["id"]?.ToString() ?? "";
                            string title = s["title"]?.ToString() ?? "";
                            string desc = s["description"]?.ToString() ?? "";
                            string ex = s["example"]?.ToString() ?? "";
                            if (id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                desc.Contains(query, StringComparison.OrdinalIgnoreCase))
                            {
                                results.Add(new DocItem(id, "Snippet", title, desc, ex, "Otter Snippet Catalog"));
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Search Official Packs
            string packsDir = Path.Combine(libraryDir, "packs");
            if (Directory.Exists(packsDir))
            {
                foreach (var mf in Directory.GetFiles(packsDir, "otter.pack.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        var pNode = JsonNode.Parse(File.ReadAllText(mf));
                        string pid = pNode?["id"]?.ToString() ?? "";
                        string summary = pNode?["summary"]?.ToString() ?? "";
                        if (pid.Contains(query, StringComparison.OrdinalIgnoreCase) || summary.Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new DocItem(pid, "Official Pack", pid, summary, null, "Otter Pack Registry"));
                        }
                    }
                    catch { }
                }
            }
        }

        return results;
    }

    private static string? ExtractWordAtColumn(string line, int col)
    {
        if (string.IsNullOrEmpty(line) || col <= 0) return null;
        int idx = col - 1;
        if (idx >= line.Length) idx = line.Length - 1;

        // If cursor is on whitespace or punctuation, look back one char
        if (!char.IsLetterOrDigit(line[idx]) && line[idx] != '_' && idx > 0)
        {
            idx--;
        }

        if (!char.IsLetterOrDigit(line[idx]) && line[idx] != '_') return null;

        int start = idx;
        while (start > 0 && (char.IsLetterOrDigit(line[start - 1]) || line[start - 1] == '_')) start--;

        int end = idx;
        while (end < line.Length - 1 && (char.IsLetterOrDigit(line[end + 1]) || line[end + 1] == '_')) end++;

        return line.Substring(start, end - start + 1);
    }
}
