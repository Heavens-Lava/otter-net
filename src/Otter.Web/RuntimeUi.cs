using System.Collections;
using System.Text.RegularExpressions;
using Otter.Language;

namespace Otter.Web;

// otterPS Get-OtterWebRuntimeUiInfo: the UI resources a page creates while
// it runs (a `create` anywhere but the top level), the kinds they use, and
// whether the page uses runtime UI at all.
static class RuntimeUi
{
    public sealed class Info
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        public List<string> Kinds { get; } = new();
        public bool Uses { get; set; }
    }

    static readonly string[] Variants = { "primary", "secondary", "danger" };

    public static Info Of(IReadOnlyList<Stmt> statements)
    {
        var info = new Info();
        foreach (Stmt statement in statements) Visit(statement, nested: false, info);
        if (info.Names.Count > 0) info.Uses = true;
        return info;
    }

    static void Visit(object? value, bool nested, Info info)
    {
        if (value is null or string or Enum or ValueType) return;
        if (value is IEnumerable items)
        {
            foreach (object? item in items) Visit(item, nested, info);
            return;
        }
        if (!NodeFields.IsContractObject(value)) return;
        if (value is Node node)
        {
            // ObjectDef: a nested `X has ...` may set a UI resource's properties.
            if (nested && node is CreateUiResourceStmt or PutInStmt or ShowStmt or UiActionStmt or WhenStmt or ObjectDefStmt) info.Uses = true;
            if (!nested && node is UiActionStmt) info.Uses = true;
            if (nested && node is CreateUiResourceStmt create)
            {
                info.Names.Add(create.Target);
                string kind = create.TypeName.ToLowerInvariant();
                foreach (string v in Variants)
                {
                    if (kind.StartsWith(v + " ", StringComparison.Ordinal)) { kind = kind.Substring(v.Length + 1); break; }
                }
                if (!info.Kinds.Contains(kind)) info.Kinds.Add(kind);
            }
        }
        foreach (var (_, field) in NodeFields.Of(value)) Visit(field, nested: true, info);
    }

    // Test-OtterWebSampleUsesUi: whether a code sample declares or drives UI.
    static readonly string[] UiKinds =
    {
        "window", "page", "button", "text box", "text", "row", "column", "image", "list", "link", "card", "checkbox", "check box",
        "dropdown", "drop down", "select", "slider", "range", "text area", "textarea", "badge", "tag", "canvas", "table", "scroll",
        "progress", "progress bar", "toggle", "switch", "radio", "radio button", "dialog", "modal", "panel",
    };

    public static bool SampleUsesUi(object? value)
    {
        if (value is null or string or Enum or ValueType) return false;
        if (value is IEnumerable items)
        {
            foreach (object? item in items) if (SampleUsesUi(item)) return true;
            return false;
        }
        if (!NodeFields.IsContractObject(value)) return false;
        if (value is CreateUiResourceStmt or PutInStmt or ShowStmt or UiActionStmt or UiElementStmt or WhenStmt) return true;
        if (value is ObjectDefStmt { TypeName: { Length: > 0 } typeName } && !string.Equals(typeName, "thing", StringComparison.OrdinalIgnoreCase))
        {
            string kind = typeName.ToLowerInvariant();
            foreach (string v in new[] { "primary ", "secondary ", "danger " })
            {
                if (kind.StartsWith(v, StringComparison.Ordinal)) kind = kind.Substring(v.Length);
            }
            if (UiKinds.Contains(kind)) return true;
        }
        foreach (var (_, field) in NodeFields.Of(value)) if (SampleUsesUi(field)) return true;
        return false;
    }
}

// otterPS Get-OtterRunnableJs: `runnable true` on a text resource makes its
// text a live code sample with a Run button, when the web can run it.
static class RunnableSamples
{
    static readonly Regex AskLine = new(@"(?m)^\s*ask\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ArgumentsWord = new(@"\barguments\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex NeedsHost = new(@"\botter[A-Za-z]*(File|Folder|Directory|Command|Registry|Clipboard|Environment)[A-Za-z]*\s*\(|__OTTER_DESKTOP_BRIDGE__|\bfetch\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex WindowVariable = new(@"window\.([A-Za-z_]\w*)\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? Js(string source, JsCompiler js)
    {
        if (AskLine.IsMatch(source) || ArgumentsWord.IsMatch(source)) return null;
        var pageUiNames = js.GetRuntimeUiNames();
        js.SetRuntimeUiNames(Array.Empty<string>(), Array.Empty<string>());
        try
        {
            ProgramNode sample = Parser.Parse(Lexer.Tokenize(source), webTarget: true);
            var statements = sample.Statements;
            if (statements.Count == 0) return null;
            if (RuntimeUi.SampleUsesUi(statements)) return null;
            HashSet<string> globals = Bindings.TopLevelGlobals(statements);
            string code = string.Join("\n", statements.Select(s => js.Statement(s, 2, knownGlobals: globals)));
            if (NeedsHost.IsMatch(code)) return null;
            var vars = new List<string>();
            foreach (Match match in WindowVariable.Matches(code))
            {
                string name = match.Groups[1].Value;
                if (!vars.Contains(name, StringComparer.Ordinal)) vars.Add(name);
            }
            string varsJson = "[" + string.Join(",", vars.Select(PowerShellText.JsonString)) + "]";
            return $"{{ vars: {varsJson}, run: async (otterSay) => {{\n{code}\n}} }}";
        }
        catch (OtterError)
        {
            return null;
        }
        finally
        {
            js.SetRuntimeUiNames(pageUiNames.Names, pageUiNames.StaticNames);
        }
    }
}
