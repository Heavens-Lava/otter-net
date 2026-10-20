using System.Text.RegularExpressions;
using Otter.Language;
using static Otter.Web.PowerShellText;

namespace Otter.Web;

// otterPS ConvertTo-OtterWeb (src/Otter.Web.psm1, D50): a whole program as
// one standalone HTML page - the top-level UI resources rendered to HTML,
// declarative elements, event handlers, reactive state, routing, and the
// rest of the program compiled to JavaScript.
public sealed class WebCompiler
{
    sealed class Resource(string kind, string name)
    {
        public string Kind { get; } = kind;
        public string Name { get; } = name;
        public OrderedMap<object?> Properties { get; } = new();
    }

    static readonly string[] KnownKinds =
    {
        "window", "page", "button", "text box", "text", "row", "column",
        "image", "list", "link", "card", "checkbox", "check box",
        "dropdown", "drop down", "select", "slider", "range",
        "text area", "textarea", "badge", "tag", "canvas", "table", "scroll",
        "progress", "progress bar", "toggle", "switch", "radio", "radio button",
        "dialog", "modal", "panel",
    };

    static readonly string[] Variants = { "primary", "secondary", "danger" };

    readonly JsCompiler js = new();
    readonly OrderedMap<Resource> resources = new();
    readonly OrderedMap<List<string>> containers = new();
    readonly List<(string Id, string Js)> runnableSamples = new();
    string appTitle = "";
    int renderDepth;
    readonly HashSet<string> rendering = new(StringComparer.Ordinal);

    WebCompiler() { }

    // ConvertTo-OtterWeb
    public static string Compile(ProgramNode program, string title = "Otter Web App") => new WebCompiler().Run(program, title);

    static bool IsTrue(object? value) => Eq(value, true) || Eq(value, "true");

    static bool In(string? text, params string[] words) => text != null && words.Any(w => string.Equals(text, w, StringComparison.OrdinalIgnoreCase));

    static string PxOrRaw(object? value) => IsNumber(value) ? $"{Of(value)}px" : Of(value);

    string Run(ProgramNode program, string title)
    {
        var whenHandlers = new List<WhenStmt>();
        var topLevelStatements = new List<Stmt>();
        var declarativeRoots = new List<UiElementStmt>();
        var stateDefs = new OrderedMap<StateDefStmt>();
        var deriveDefs = new OrderedMap<DeriveDefStmt>();
        var watchStmts = new List<WatchStmt>();
        var functions = new OrderedMap<FunctionDefStmt>();

        RuntimeUi.Info runtimeUi = RuntimeUi.Of(program.Statements);
        js.SetRuntimeUiNames(runtimeUi.Names, Array.Empty<string>());
        js.SetLoopPassNames(program.Statements);

        // Pass 1: resources and configuration.
        foreach (Stmt stmt in program.Statements)
        {
            if (stmt is StateDefStmt state) { stateDefs[state.Name] = state; continue; }
            if (stmt is DeriveDefStmt derive) { deriveDefs[derive.Name] = derive; continue; }
            if (stmt is WatchStmt watch) { watchStmts.Add(watch); continue; }
            if (stmt is FunctionDefStmt function) functions[function.Name] = function;
            if (stmt is UiElementStmt element) { declarativeRoots.Add(element); continue; }
            if (stmt is CreateUiResourceStmt create)
            {
                resources[create.Target] = new Resource(create.TypeName.ToLowerInvariant(), create.Target);
                continue;
            }
            if (stmt is ObjectDefStmt objectDef)
            {
                if (resources.TryGetValue(objectDef.Name, out Resource? existing))
                {
                    AddProperties(existing, objectDef, topLevelStatements);
                    continue;
                }
                string kind = objectDef.TypeName.ToLowerInvariant();
                string? variant = null;
                foreach (string v in Variants)
                {
                    if (kind.StartsWith(v + " ", StringComparison.Ordinal)) { variant = v; kind = kind.Substring(v.Length + 1); break; }
                }
                if (In(kind, KnownKinds))
                {
                    var resource = new Resource(kind, objectDef.Name);
                    if (variant != null) resource.Properties["variant"] = variant;
                    AddProperties(resource, objectDef, topLevelStatements);
                    resources[objectDef.Name] = resource;
                    continue;
                }
            }
            if (stmt is AssignStmt { Target: PropertyAccessExpr access } assign)
            {
                if (access.Target is VariableExpr owner && resources.TryGetValue(owner.Name, out Resource? target) && assign.Value is LiteralExpr literal)
                {
                    target.Properties[access.Property.ToLowerInvariant()] = literal.Value;
                    continue;
                }
            }
            if (stmt is PutInStmt runtimePut &&
                ((runtimePut.Item is VariableExpr runtimeItem && runtimeUi.Names.Contains(runtimeItem.Name)) ||
                 (runtimePut.Container is VariableExpr runtimeContainer && runtimeUi.Names.Contains(runtimeContainer.Name))))
            {
                topLevelStatements.Add(runtimePut);
                continue;
            }
            if (stmt is PutInStmt put)
            {
                if (put.Item is VariableExpr item && put.Container is VariableExpr container)
                {
                    if (!containers.Contains(container.Name)) containers[container.Name] = new List<string>();
                    containers[container.Name].Add(item.Name);
                }
                continue;
            }
            if (stmt is WhenStmt runtimeWhen && runtimeWhen.Target is VariableExpr whenTarget && runtimeUi.Names.Contains(whenTarget.Name) &&
                !In(runtimeWhen.EventName, "drag", "drop", "files dropped"))
            {
                topLevelStatements.Add(runtimeWhen);
                continue;
            }
            if (stmt is WhenStmt when) { whenHandlers.Add(when); continue; }
            if (stmt is ShowStmt) continue;
            topLevelStatements.Add(stmt);
        }

        // Page UI addressed by DOM id: the top-level resources and named
        // declarative elements.
        var staticUiNames = new List<string>(resources.Keys);
        var declStack = new Stack<UiElementStmt>();
        foreach (UiElementStmt d in declarativeRoots) declStack.Push(d);
        while (declStack.Count > 0)
        {
            UiElementStmt d = declStack.Pop();
            if (!string.IsNullOrEmpty(d.Name)) staticUiNames.Add(d.Name);
            foreach (Stmt c in d.Children) if (c is UiElementStmt child) declStack.Push(child);
        }
        js.SetRuntimeUiNames(runtimeUi.Names, staticUiNames);

        // The root container: the first page or window, or the first resource.
        string? rootName = resources.Keys.FirstOrDefault(n => resources[n].Kind is "window" or "page");
        if (rootName == null && resources.Count > 0) rootName = resources.Keys.First();

        appTitle = title;
        string rootBg = "#0f172a";
        string rootFg = "#f8fafc";
        if (rootName != null)
        {
            var rootProps = resources[rootName].Properties;
            if (rootProps.Contains("title")) appTitle = Of(rootProps["title"]);
            if (rootProps.Contains("background")) rootBg = Of(rootProps["background"]);
            if (rootProps.Contains("foreground")) rootFg = Of(rootProps["foreground"]);
        }

        bool isPage = rootName != null && resources[rootName].Kind == "page";
        bool pageScrolls = isPage && resources[rootName!].Properties.Contains("scroll") && IsTrueOrText(resources[rootName!].Properties["scroll"]);
        string bodyClass = pageScrolls ? " class=\"otter-has-page otter-doc-scroll\"" : isPage ? " class=\"otter-has-page\"" : "";

        // D103: every page a route names is in the page; the router shows one.
        var routePageNames = new List<string>();
        var routePageSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Stmt stmt in program.Statements)
        {
            if (stmt is RouteStmt route && !string.IsNullOrEmpty(route.PageName) && routePageSeen.Add(route.PageName)) routePageNames.Add(route.PageName);
        }

        string elementsHtml = "";
        if (routePageNames.Count > 0)
        {
            elementsHtml = string.Join("\n", routePageNames.Select((page, index) => RenderElement(page, index > 0)));
        }
        else if (rootName != null)
        {
            elementsHtml = RenderElement(rootName);
        }
        else
        {
            elementsHtml = string.Join("\n", resources.Keys.ToList().Select(name => RenderElement(name)));
        }

        // Functions that declare UI, called at the top level.
        foreach (Stmt stmt in topLevelStatements)
        {
            if (stmt is CallStmt call && functions.TryGetValue(call.Call.Name, out FunctionDefStmt? fnDef))
            {
                foreach (Stmt bodyStmt in fnDef.Body) if (bodyStmt is UiElementStmt uiBody) declarativeRoots.Add(uiBody);
            }
        }

        var declarativeCssRules = new List<string>();
        var declarativeJsListeners = new List<string>();
        var declarativeHtmlParts = new List<string>();
        var declarative = new DeclarativeRenderer(js, declarativeCssRules, declarativeJsListeners);
        foreach (UiElementStmt rootEl in declarativeRoots)
        {
            string rendered = declarative.Render(rootEl, 2);
            if (rendered.Length > 0) declarativeHtmlParts.Add(rendered);
        }
        string declarativeHtml = string.Join("\n", declarativeHtmlParts);
        if (declarativeHtml.Length > 0)
        {
            elementsHtml = elementsHtml.Length > 0 ? $"{elementsHtml}\n{declarativeHtml}" : declarativeHtml;
        }

        if (string.Equals(appTitle, "Otter Web App", StringComparison.OrdinalIgnoreCase))
        {
            foreach (UiElementStmt r in declarativeRoots)
            {
                if (In(r.Tag, "window", "page") && r.Label is LiteralExpr rootLabel)
                {
                    appTitle = Of(rootLabel.Value);
                    break;
                }
                foreach (Stmt c in r.Children)
                {
                    if (c is UiElementStmt { Label: LiteralExpr headingLabel } heading && string.Equals(heading.Tag, "heading", StringComparison.OrdinalIgnoreCase))
                    {
                        appTitle = Of(headingLabel.Value);
                        break;
                    }
                }
            }
        }

        string declarativeCssJoined = string.Join("\n", declarativeCssRules);
        string declarativeListenersJoined = string.Join("\n", declarativeJsListeners);

        var stateInitJs = new List<string>();
        foreach (string k in stateDefs.Keys)
        {
            string valJs = js.Expression(stateDefs[k].InitialValue);
            stateInitJs.Add($"    otterState['{k}'] = {valJs};\n    window['{k}'] = {valJs};");
        }
        string stateInitJoined = string.Join("\n", stateInitJs);

        var deriveInitJs = new List<string>();
        foreach (string k in deriveDefs.Keys) deriveInitJs.Add(Templates.DeriveInit(k, js.Expression(deriveDefs[k].Expression)));
        string deriveInitJoined = string.Join("\n", deriveInitJs);

        var watcherInitJs = new List<string>();
        foreach (WatchStmt w in watchStmts)
        {
            string body = string.Join("\n", w.Body.Select(s => js.Statement(s, 3)));
            watcherInitJs.Add(Templates.WatcherInit(w.TargetName, body));
        }
        string watcherInitJoined = string.Join("\n", watcherInitJs);

        // Runtime UI: one template per kind the program creates while running.
        string runtimeUiJs = "";
        if (runtimeUi.Uses)
        {
            var templates = new List<(string Kind, string Html)>();
            const string templateName = "__otter_rt__";
            foreach (string kind in runtimeUi.Kinds)
            {
                resources[templateName] = new Resource(kind, templateName);
                try
                {
                    string html = RenderElementCore(templateName).Trim();
                    int at = templates.FindIndex(t => t.Kind == kind);
                    if (at >= 0) templates[at] = (kind, html); else templates.Add((kind, html));
                }
                finally { resources.Remove(templateName); }
            }
            string templatesJson = templates.Count > 0
                ? "{" + string.Join(",", templates.Select(t => JsonString(t.Kind) + ":" + JsonString(t.Html))) + "}"
                : "{}";
            runtimeUiJs = $"    const otterUiTemplates = {templatesJson};\n" + Templates.WebRuntimeUiJs;
        }

        var jsHandlers = new List<string>();
        foreach (WhenStmt when in whenHandlers)
        {
            string targetName = when.Target is VariableExpr whenVariable ? whenVariable.Name : "target";
            if (In(when.EventName, "drag", "drop", "files dropped"))
            {
                var dragBodyLines = new List<string>();
                bool enteredDrag = js.EnterHandlerRun(when);
                try { foreach (Stmt s in when.Body) dragBodyLines.Add(js.Statement(s, 5)); }
                finally { js.ExitHandlerRun(enteredDrag); }
                string domEvent = In(when.EventName, "drag") ? "dragstart" : "drop";
                string dragGuard = when.EventName.ToLowerInvariant() switch
                {
                    "drag" => "",
                    "drop" => "        if (!otterIsItemDrop(event)) { return; }\n",
                    _ => "        if (!otterIsFileDrop(event)) { return; }\n",
                };
                jsHandlers.Add(Templates.DragHandler(targetName, domEvent, dragGuard, when.EventName, string.Join("\n", dragBodyLines)));
                continue;
            }
            string eventName = when.EventName.ToLowerInvariant() switch
            {
                "clicked" => "click",
                "changed" => "input",
                var other => other,
            };
            var bodyJs = new List<string>();
            bool entered = js.EnterHandlerRun(when);
            try { foreach (Stmt s in when.Body) bodyJs.Add(js.Statement(s, 3)); }
            finally { js.ExitHandlerRun(entered); }
            jsHandlers.Add(Templates.WhenHandler(targetName, eventName, string.Join("\n", bodyJs)));
        }

        HashSet<string> topLevelGlobals = Bindings.TopLevelGlobals(topLevelStatements);
        string topLevelJoined = string.Join("\n", topLevelStatements.Select(s => js.Statement(s, 2, knownGlobals: topLevelGlobals)));
        string handlersJoined = string.Join("\n", jsHandlers);

        bool usesDragDrop = false;
        foreach (Resource resource in resources.Values)
        {
            foreach (string dragKey in new[] { "draggable", "accepts drops" })
            {
                if (resource.Properties.Contains(dragKey) && IsTrueOrText(resource.Properties[dragKey])) usesDragDrop = true;
            }
        }
        foreach (WhenStmt w in whenHandlers) if (In(w.EventName, "drag", "drop", "files dropped")) usesDragDrop = true;

        string runnableRuntimeJs = "";
        string runnableCss = "";
        if (runnableSamples.Count > 0)
        {
            runnableCss = Templates.RunnableCss;
            string sampleRegistry = string.Join("\n", runnableSamples.Select(s => $"    window.otterRunnables[{JsonString(s.Id)}] = {s.Js};"));
            string orderJson = "[" + string.Join(",", runnableSamples.Select(s => JsonString(s.Id))) + "]";
            runnableRuntimeJs = Templates.RunnableRuntimeJs.Replace("@@ORDER@@", orderJson) + "\n" + sampleRegistry;
        }
        string dragDropRuntimeJs = usesDragDrop ? Templates.DragDropRuntimeJs : "";

        return Templates.Page(HtmlText(appTitle), rootBg, rootFg, runnableCss, declarativeCssJoined, bodyClass, elementsHtml,
            dragDropRuntimeJs, runnableRuntimeJs, Templates.JsCryptoRuntime, runtimeUiJs, stateInitJoined, deriveInitJoined,
            watcherInitJoined, topLevelJoined, handlersJoined, declarativeListenersJoined);
    }

    // `$value -eq $true -or "$value" -eq 'true'`
    static bool IsTrueOrText(object? value) => Eq(value, true) || Eq(Of(value), "true");

    // The literal properties of `x has ...` / `x is a button with ...` go on
    // the resource; any other value is set when the page runs, on the name as
    // this statement writes it (New-OtterWebRuntimePropertyAssign -ResourceName
    // $stmt.Name): the resources table ignores case, the page's names do not.
    static void AddProperties(Resource resource, ObjectDefStmt objectDef, List<Stmt> topLevelStatements)
    {
        foreach (AssignStmt p in objectDef.Properties)
        {
            string propKey = p.Target is VariableExpr name ? name.Name.ToLowerInvariant() : "";
            if (p.Value is LiteralExpr literal) resource.Properties[propKey] = literal.Value;
            else topLevelStatements.Add(new AssignStmt(new PropertyAccessExpr(propKey, new VariableExpr(objectDef.Name, p.Line), p.Line), p.Value, p.Line));
        }
    }

    // Render-OtterElement: the resource, with D110's drag and drop attributes.
    string RenderElement(string resName, bool hidden = false)
    {
        string html = RenderElementCore(resName, hidden);
        if (!resources.TryGetValue(resName, out Resource? resource)) return html;
        var dragProps = resource.Properties;
        string dragAttrs = "";
        if (dragProps.Contains("draggable") && IsTrueOrText(dragProps["draggable"])) dragAttrs += " draggable=\"true\"";
        if (dragProps.Contains("accepts drops") && IsTrueOrText(dragProps["accepts drops"])) dragAttrs += " data-otter-accepts-drops=\"true\"";
        if (dragAttrs.Length > 0)
        {
            string idMarker = $"id=\"{resName}\"";
            int at = html.IndexOf(idMarker, StringComparison.Ordinal);
            if (at >= 0) html = html.Insert(at + idMarker.Length, dragAttrs);
        }
        return html;
    }

    static readonly Regex ExternalUrl = new("^(?i)https?://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // The deepest nesting rendered natively: otterPS renders 1,609 levels
    // (and sometimes 1,610) on Windows PowerShell 5.1 before "The script
    // failed due to call depth overflow." (measured in CI, `otter web` and
    // `otter build` alike); 1,500 leaves a margin for its other callers.
    public const int MaxNativeRenderDepth = 1500;

    // Render-OtterElementCore
    string RenderElementCore(string resName, bool hidden = false)
    {
        if (!resources.TryGetValue(resName, out Resource? r)) return "";
        // A resource put inside itself (directly or through others): otterPS
        // renders it again and again until PowerShell stops it, an error the
        // web compiler's catch-all reports.
        if (rendering.Contains(resName)) throw new WebInternalProblem("The script failed due to call depth overflow.");
        if (++renderDepth > MaxNativeRenderDepth)
        {
            // Nested deeper without a cycle: on Windows PowerShell 5.1 otterPS
            // runs out of stack at about 1,610 levels, but not at a fixed one
            // (SPEC-QUESTIONS F5-6), so otter-net cannot know the outcome.
            throw new NotSupportedYet($"UI resources nested more than {MaxNativeRenderDepth} deep", 0);
        }
        rendering.Add(resName);
        try { return RenderResource(resName, r, hidden); }
        finally { renderDepth--; rendering.Remove(resName); }
    }

    string RenderResource(string resName, Resource r, bool hidden)
    {
        string kind = r.Kind;
        var props = r.Properties;
        object? Prop(string key) => props[key];
        bool Has(string key) => props.Contains(key);

        var styles = new List<string>();
        if (hidden) styles.Add("display: none;");
        if (Has("width"))
        {
            object? w = Prop("width");
            styles.Add($"width: {(Eq(w, "full") ? "100%; max-width: 100%" : PxOrRaw(w))};");
        }
        if (Has("height"))
        {
            object? h = Prop("height");
            styles.Add($"height: {(Eq(h, "full") ? "100%" : PxOrRaw(h))};");
        }
        if (Has("maxwidth")) styles.Add($"max-width: {PxOrRaw(Prop("maxwidth"))};");
        if (Has("minwidth")) styles.Add($"min-width: {PxOrRaw(Prop("minwidth"))};");
        if (Has("minheight")) styles.Add($"min-height: {PxOrRaw(Prop("minheight"))};");
        if (Has("background")) styles.Add($"background: {Of(Prop("background"))};");
        if (Has("foreground")) styles.Add($"color: {Of(Prop("foreground"))};");
        if (Has("round") && IsTrue(Prop("round")))
        {
            styles.Add("border-radius: 9999px;");
        }
        else if (Has("radius"))
        {
            object? rad = Prop("radius");
            styles.Add($"border-radius: {(Eq(rad, "round") ? "9999px" : PxOrRaw(rad))};");
        }
        if (Has("border")) styles.Add($"border: {Of(Prop("border"))};");
        if (Has("shadow")) styles.Add($"box-shadow: {Of(Prop("shadow"))};");
        if (Has("padding")) styles.Add($"padding: {PxOrRaw(Prop("padding"))};");
        if (Has("margin")) styles.Add($"margin: {PxOrRaw(Prop("margin"))};");
        if (Has("size") || Has("fontsize")) styles.Add($"font-size: {PxOrRaw(Has("fontsize") ? Prop("fontsize") : Prop("size"))};");
        if (Has("weight") || Has("fontweight")) styles.Add($"font-weight: {Of(Has("fontweight") ? Prop("fontweight") : Prop("weight"))};");
        if (Has("fontstyle")) styles.Add($"font-style: {Of(Prop("fontstyle"))};");
        else if (Has("style") && (Eq(Prop("style"), "italic") || Eq(Prop("style"), "normal"))) styles.Add($"font-style: {Of(Prop("style"))};");
        if (Has("family") || Has("fontfamily")) styles.Add($"font-family: {Of(Has("fontfamily") ? Prop("fontfamily") : Prop("family"))};");
        if (Has("lineheight")) styles.Add($"line-height: {Of(Prop("lineheight"))};");
        if (Has("letterspacing")) styles.Add($"letter-spacing: {Of(Prop("letterspacing"))};");
        if (Has("whitespace")) styles.Add($"white-space: {Of(Prop("whitespace"))};");
        if (Has("overflow")) styles.Add($"overflow: {Of(Prop("overflow"))};");
        if (Has("align")) styles.Add($"text-align: {Of(Prop("align"))};");
        else if (!In(kind, "row", "column") && Has("align_h")) styles.Add($"text-align: {Of(Prop("align_h"))};");
        if (Has("flex")) styles.Add($"flex: {Of(Prop("flex"))}; min-width: 0;");
        if (Has("grow")) styles.Add($"flex-grow: {Of(Prop("grow"))};");
        if (Has("cursor")) styles.Add($"cursor: {Of(Prop("cursor"))};");
        if (Has("position")) styles.Add($"position: {Of(Prop("position"))};");
        if (Has("bottom")) styles.Add($"bottom: {PxOrRaw(Prop("bottom"))};");
        if (Has("top")) styles.Add($"top: {PxOrRaw(Prop("top"))};");
        if (Has("left")) styles.Add($"left: {PxOrRaw(Prop("left"))};");
        if (Has("right")) styles.Add($"right: {PxOrRaw(Prop("right"))};");
        if (Has("zindex")) styles.Add($"z-index: {Of(Prop("zindex"))};");
        if (Has("customstyle")) styles.Add($"{Of(Prop("customstyle"))};");
        string StyleAttr() => styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
        string styleAttr = StyleAttr();

        string childHtml = "";
        if (containers.TryGetValue(resName, out List<string>? children))
        {
            childHtml = "\n" + string.Join("\n", children.ToList().Select(child => RenderElement(child))) + "\n";
        }

        string Text(string key, string fallback) => Has(key) ? Of(Prop(key)) : fallback;

        switch (kind)
        {
            case "window":
            {
                string spacing = Of(Has("spacing") ? Prop("spacing") : 12);
                if (!Has("gap")) styles.Add($"gap: {spacing}px;");
                return Templates.WindowElement(resName, StyleAttr(), HtmlText(appTitle), spacing, childHtml);
            }
            case "page":
            {
                string spacing = Of(Has("spacing") ? Prop("spacing") : 12);
                if (!Has("gap")) styles.Add($"gap: {spacing}px;");
                bool showHeader = !(Has("hideheader") && Truthy(Prop("hideheader")));
                string headerHtml = showHeader && appTitle.Length > 0 ? $"<header class=\"otter-page-header\"><h1 class=\"otter-title\">{HtmlText(appTitle)}</h1></header>" : "";
                return Templates.PageElement(resName, StyleAttr(), headerHtml, spacing, childHtml);
            }
            case "card":
            {
                string spacing = Of(Has("spacing") ? Prop("spacing") : 12);
                if (!Has("gap")) styles.Add($"gap: {spacing}px;");
                string cardTitle = Has("title") ? $"<h3 class=\"otter-card-title\">{HtmlAttr(Of(Prop("title")))}</h3>" : "";
                return Templates.CardElement(resName, StyleAttr(), cardTitle, childHtml);
            }
            case "button":
            {
                string text = HtmlAttr(Text("text", "Button"));
                string variantClass = Has("variant") ? $" otter-button-{Of(Prop("variant"))}" : "";
                return $"      <button id=\"{resName}\" class=\"otter-button{variantClass}\"{styleAttr}>{text}</button>";
            }
            case "text box":
            {
                string val = HtmlAttr(Has("text") ? Of(Prop("text")) : Text("value", ""));
                string ph = Has("placeholder") ? $" placeholder=\"{HtmlAttr(Of(Prop("placeholder")))}\"" : "";
                return $"      <input type=\"text\" id=\"{resName}\" class=\"otter-text-box\" value=\"{val}\"{ph}{styleAttr} />";
            }
            case "text":
            {
                string rawVal = Has("text") ? Of(Prop("text")) : Text("value", "");
                string val = HtmlAttr(rawVal);
                if (Has("runnable") && IsTrueOrText(Prop("runnable")))
                {
                    string? sampleJs = RunnableSamples.Js(rawVal, js);
                    if (sampleJs != null)
                    {
                        runnableSamples.Add((resName, sampleJs));
                        return Templates.RunnableTextElement(resName, styleAttr, val);
                    }
                }
                return $"      <div id=\"{resName}\" class=\"otter-text\"{styleAttr}>{val}</div>";
            }
            case "image":
            {
                string src = HtmlAttr(Has("source") ? Of(Prop("source")) : Text("src", ""));
                string alt = HtmlAttr(Text("alt", resName));
                return $"      <img id=\"{resName}\" class=\"otter-image\" src=\"{src}\" alt=\"{alt}\"{styleAttr} />";
            }
            case "link":
            {
                string rawHref = Has("url") ? Of(Prop("url")) : Text("href", "#");
                string rawText = Text("text", rawHref);
                string href = HtmlAttr(rawHref);
                string text = HtmlAttr(rawText);
                string linkAttrs = ExternalUrl.IsMatch(rawHref) ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "";
                return $"      <a id=\"{resName}\" class=\"otter-link\" href=\"{href}\"{styleAttr}{linkAttrs}>{text}</a>";
            }
            case "checkbox":
            case "check box":
            {
                string text = HtmlAttr(Text("text", ""));
                string isChecked = Has("checked") && Truthy(Prop("checked")) ? " checked" : "";
                return $"      <label class=\"otter-checkbox-label\"{styleAttr}><input type=\"checkbox\" id=\"{resName}\" class=\"otter-checkbox\"{isChecked} /> <span>{text}</span></label>";
            }
            case "dropdown":
            case "drop down":
            case "select":
            {
                var opts = new List<string>();
                if (Has("options")) opts = Of(Prop("options")).Split(',').Select(o => o.Trim()).ToList();
                string optHtml = string.Join("\n", opts.Select(o =>
                {
                    string escOpt = HtmlAttr(o);
                    return $"        <option value=\"{escOpt}\">{escOpt}</option>";
                }));
                return Templates.SelectElement(resName, styleAttr, optHtml);
            }
            case "slider":
            case "range":
            {
                string min = Of(Has("min") ? Prop("min") : 0);
                string max = Of(Has("max") ? Prop("max") : 100);
                string val = Of(Has("value") ? Prop("value") : 50);
                return $"      <input type=\"range\" id=\"{resName}\" class=\"otter-slider\" min=\"{min}\" max=\"{max}\" value=\"{val}\"{styleAttr} />";
            }
            case "text area":
            case "textarea":
            {
                string val = HtmlAttr(Has("text") ? Of(Prop("text")) : Text("value", ""));
                string ph = Has("placeholder") ? $" placeholder=\"{HtmlAttr(Of(Prop("placeholder")))}\"" : "";
                string rows = Of(Has("rows") ? Prop("rows") : 3);
                return $"      <textarea id=\"{resName}\" class=\"otter-text-area\" rows=\"{rows}\"{ph}{styleAttr}>{val}</textarea>";
            }
            case "badge":
            case "tag":
            {
                string text = HtmlAttr(Text("text", ""));
                return $"      <span id=\"{resName}\" class=\"otter-badge\"{styleAttr}>{text}</span>";
            }
            case "canvas":
            {
                string w = Of(Has("width") ? Prop("width") : 400);
                string h = Of(Has("height") ? Prop("height") : 300);
                string anim = Has("animation") ? HtmlAttr(Of(Prop("animation"))) : "";
                string mode = Has("mode") ? HtmlAttr(Of(Prop("mode"))) : "2d";
                return $"      <canvas id=\"{resName}\" class=\"otter-canvas\" width=\"{w}\" height=\"{h}\" data-animation=\"{anim}\" data-mode=\"{mode}\"{styleAttr}></canvas>";
            }
            case "progress":
            case "progress bar":
            {
                string val = Of(Has("value") ? Prop("value") : 0);
                string max = Of(Has("max") ? Prop("max") : 100);
                return $"      <progress id=\"{resName}\" class=\"otter-progress\" value=\"{val}\" max=\"{max}\"{styleAttr}></progress>";
            }
            case "toggle":
            case "switch":
            {
                string text = HtmlAttr(Text("text", ""));
                string isChecked = Has("checked") && IsTrue(Prop("checked")) ? " checked" : "";
                return $"      <label class=\"otter-toggle-label\"{styleAttr}><input type=\"checkbox\" id=\"{resName}\" class=\"otter-toggle\" role=\"switch\"{isChecked} /><span class=\"otter-toggle-track\"><span class=\"otter-toggle-thumb\"></span></span><span class=\"otter-toggle-text\">{text}</span></label>";
            }
            case "radio":
            case "radio button":
            {
                string text = HtmlAttr(Text("text", ""));
                string group = HtmlAttr(Has("group") ? Of(Prop("group")) : Text("name", "default-group"));
                string isChecked = Has("checked") && IsTrue(Prop("checked")) ? " checked" : "";
                return $"      <label class=\"otter-radio-label\"{styleAttr}><input type=\"radio\" id=\"{resName}\" name=\"{group}\" class=\"otter-radio\"{isChecked} /> <span>{text}</span></label>";
            }
            case "row":
            {
                string spacing = Of(Has("spacing") ? Prop("spacing") : 8);
                string wrap = Has("wrap") && (IsTrue(Prop("wrap")) || Eq(Prop("wrap"), "wrap")) ? "wrap" : "nowrap";
                string? direction = Has("align") ? Of(Prop("align")) : null;
                bool spread = Has("spread") && IsTrue(Prop("spread"));
                if (spread && In(direction, "left", "center", "right"))
                {
                    throw new OtterError($"Horizontal alignment 'align {direction}' conflicts with 'spread' on a row.", 0, "runtime");
                }
                string justify = spread ? "space-between"
                    : In(direction, "left", "center", "right") ? direction!.ToLowerInvariant() switch { "left" => "flex-start", "center" => "center", "right" => "flex-end", _ => "flex-start" }
                    : Has("justify") ? Of(Prop("justify"))
                    : "flex-start";
                string align = In(direction, "top", "middle", "bottom") ? direction!.ToLowerInvariant() switch { "top" => "flex-start", "middle" => "center", "bottom" => "flex-end", _ => "center" }
                    : Has("alignitems") ? Of(Prop("alignitems"))
                    : Has("items") ? Of(Prop("items"))
                    : "center";
                styles.Add("display: flex; flex-direction: row;");
                styles.Add($"gap: {spacing}px;");
                styles.Add($"align-items: {align};");
                styles.Add($"justify-content: {justify};");
                styles.Add($"flex-wrap: {wrap};");
                return Templates.RowElement(resName, StyleAttr(), childHtml);
            }
            case "column":
            {
                string spacing = Of(Has("spacing") ? Prop("spacing") : 8);
                string? direction = Has("align") ? Of(Prop("align")) : null;
                bool spread = Has("spread") && IsTrue(Prop("spread"));
                if (spread && In(direction, "top", "middle", "bottom"))
                {
                    throw new OtterError($"Vertical alignment 'align {direction}' conflicts with 'spread' on a column.", 0, "runtime");
                }
                string justify = spread ? "space-between"
                    : In(direction, "top", "middle", "bottom") ? direction!.ToLowerInvariant() switch { "top" => "flex-start", "middle" => "center", "bottom" => "flex-end", _ => "flex-start" }
                    : Has("justify") ? Of(Prop("justify"))
                    : "flex-start";
                string align = In(direction, "left", "center", "right") ? direction!.ToLowerInvariant() switch { "left" => "flex-start", "center" => "center", "right" => "flex-end", _ => "stretch" }
                    : Has("alignitems") ? Of(Prop("alignitems"))
                    : Has("items") ? Of(Prop("items"))
                    : "stretch";
                styles.Add("display: flex; flex-direction: column;");
                styles.Add($"gap: {spacing}px;");
                styles.Add($"align-items: {align};");
                styles.Add($"justify-content: {justify};");
                return Templates.ColumnElement(resName, StyleAttr(), childHtml);
            }
            case "scroll":
                styles.Add("overflow-y: auto; overflow-x: hidden; display: flex; flex-direction: column;");
                return Templates.ScrollElement(resName, StyleAttr(), childHtml);
            default:
                return $"      <div id=\"{resName}\" class=\"otter-resource otter-{kind}\"{styleAttr}>{childHtml}</div>";
        }
    }
}
