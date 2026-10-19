using Otter.Language;
using static Otter.Web.PowerShellText;

namespace Otter.Web;

// otterPS Render-OtterDeclarativeElementWeb (Otter.Web.psm1): a D56
// declarative element (`card "Title"` with its layout, properties,
// animations, events and children) as HTML, plus the CSS rules and
// event-listener scripts it needs.
sealed class DeclarativeRenderer(JsCompiler js, List<string> cssRules, List<string> jsListeners)
{
    int idCounter;

    public int IdCounter => idCounter;

    static string Lower(string? text) => (text ?? "").ToLowerInvariant();

    static string CssEasing(string? easing) => easing switch
    {
        "ease-out" => "cubic-bezier(0.16, 1, 0.3, 1)",
        "ease-in" => "cubic-bezier(0.7, 0, 0.84, 0)",
        "spring" => "cubic-bezier(0.34, 1.56, 0.64, 1)",
        "linear" => "linear",
        _ => "ease",
    };

    static string? AlignStyle(string? align) => Lower(align) switch
    {
        "center" => "align-items: center; text-align: center; justify-content: center;",
        "left" => "align-items: flex-start; text-align: left; justify-content: flex-start;",
        "right" => "align-items: flex-end; text-align: right; justify-content: flex-end;",
        "top" => "align-items: flex-start;",
        "middle" => "align-items: center;",
        "bottom" => "align-items: flex-end;",
        _ => null,
    };

    public string Render(Stmt node, int depth = 1)
    {
        if (node is not UiElementStmt element) return "";
        string id;
        if (!string.IsNullOrEmpty(element.Name)) id = element.Name;
        else id = $"otter_el_{idCounter++}";
        string tag = !string.IsNullOrEmpty(element.Tag) ? element.Tag.ToLowerInvariant() : "panel";
        string? variant = !string.IsNullOrEmpty(element.Variant) ? element.Variant.ToLowerInvariant() : null;

        var styles = new List<string>();
        var classes = new List<string> { $"otter-{tag}" };
        if (variant != null)
        {
            classes.Add($"otter-{tag}-{variant}");
            classes.Add($"otter-btn-{variant}");
        }

        UiLayoutSpec layout = element.Layout;
        string? mode = !string.IsNullOrEmpty(layout.Mode) ? layout.Mode.ToLowerInvariant() : null;
        switch (mode)
        {
            case "centered": styles.Add("display: flex; flex-direction: column; align-items: center; justify-content: center; min-height: 100%;"); break;
            case "split": styles.Add("display: flex; flex-direction: row; justify-content: space-between; align-items: center; width: 100%;"); break;
            case "sidebar": styles.Add("display: grid; grid-template-columns: 240px 1fr; width: 100%; min-height: 100%;"); break;
            case "stack": styles.Add("display: flex; flex-direction: column; width: 100%;"); break;
            case "cards": styles.Add("display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); width: 100%;"); break;
            case "navbar": styles.Add("display: flex; flex-direction: row; align-items: center; justify-content: space-between; width: 100%; padding: 12px 24px;"); break;
            case "form": styles.Add("display: flex; flex-direction: column; width: 100%; max-width: 480px; margin: 0 auto;"); break;
            case "row": styles.Add("display: flex; flex-direction: row;"); break;
            case "column": styles.Add("display: flex; flex-direction: column;"); break;
            case "grid":
                styles.Add("display: grid;");
                if (layout.Columns != null)
                {
                    object? colCount = layout.Columns is LiteralExpr columnsLiteral ? columnsLiteral.Value : 2;
                    styles.Add($"grid-template-columns: repeat({Of(colCount)}, minmax(0, 1fr));");
                }
                break;
        }
        if (layout.Gap != null)
        {
            object? gapVal = layout.Gap is LiteralExpr gapLiteral ? gapLiteral.Value : 12;
            styles.Add($"gap: {Of(gapVal)}px;");
        }
        if (layout.Spread) styles.Add("justify-content: space-between;");
        if (!string.IsNullOrEmpty(layout.Align) && AlignStyle(layout.Align) is { } alignStyle) styles.Add(alignStyle);
        foreach (ResponsiveRule rule in layout.Responsive)
        {
            string bpPx = Lower(rule.Breakpoint) switch { "small" => "640px", "medium" => "768px", "large" => "1024px", _ => "768px" };
            if (rule.Stack) cssRules.Add($"@media (max-width: {bpPx}) {{ #{id} {{ flex-direction: column !important; }} }}");
            if (rule.Columns > 0) cssRules.Add($"@media (max-width: {bpPx}) {{ #{id} {{ grid-template-columns: repeat({rule.Columns}, 1fr) !important; }} }}");
        }

        foreach (Stmt property in element.Properties)
        {
            if (property is not AssignStmt assign) continue;
            string pName = assign.Target is VariableExpr target ? target.Name.ToLowerInvariant() : "";
            object? pVal = assign.Value is LiteralExpr literal ? literal.Value : null;
            switch (pName)
            {
                case "round":
                    styles.Add($"border-radius: {(pVal != null && !Eq(pVal, true) ? $"{Of(pVal)}px" : "12px")};");
                    break;
                case "gap":
                    styles.Add($"gap: {Of(pVal)}px;");
                    break;
                case "align":
                    if (AlignStyle(Of(pVal)) is { } propertyAlign) styles.Add(propertyAlign);
                    break;
                case "spread":
                    if (Eq(pVal, true) || Eq(pVal, "true")) styles.Add("justify-content: space-between;");
                    break;
                case "padding":
                    styles.Add($"padding: {Of(pVal)}px;");
                    break;
                case "width":
                    styles.Add($"width: {(Eq(pVal, "full") ? "100%" : IsNumber(pVal) ? $"{Of(pVal)}px" : Of(pVal))};");
                    break;
                case "height":
                    styles.Add($"height: {(Eq(pVal, "full") ? "100%" : IsNumber(pVal) ? $"{Of(pVal)}px" : Of(pVal))};");
                    break;
                case "background":
                    styles.Add($"background: {Of(pVal)};");
                    break;
                case "foreground":
                    styles.Add($"color: {Of(pVal)};");
                    break;
            }
        }

        foreach (Stmt animationNode in element.Animations)
        {
            if (animationNode is not UiAnimationBlock animation) continue;
            string dur = Of(animation.DurationMs > 0 ? animation.DurationMs : 200);
            string easingCss = CssEasing(animation.Easing);
            switch (Lower(animation.Trigger))
            {
                case "hover":
                {
                    object scale = 1.05;
                    foreach (UiAnimationStep step in animation.Steps)
                    {
                        if (step.Operation is "grow" or "scale" && step.Amount is LiteralExpr amount) scale = amount.Value!;
                    }
                    styles.Add($"transition: transform {dur}ms {easingCss}, box-shadow {dur}ms {easingCss};");
                    cssRules.Add($"#{id}:hover {{ transform: scale({Of(scale)}); }}");
                    break;
                }
                case "press":
                    styles.Add("transition: transform 100ms ease;");
                    cssRules.Add($"#{id}:active {{ transform: scale(0.96); }}");
                    break;
                case "enter":
                {
                    var fromTransforms = new List<string>();
                    string? fromOpacity = null;
                    foreach (UiAnimationStep step in animation.Steps)
                    {
                        if (step.Operation == "fade" && step.Direction == "in") fromOpacity = "0";
                        if (step.Operation == "move")
                        {
                            string amt = Of(step.Amount is LiteralExpr moveAmount ? moveAmount.Value : 20);
                            switch (Lower(step.Direction))
                            {
                                case "up": fromTransforms.Add($"translateY({amt}px)"); break;
                                case "down": fromTransforms.Add($"translateY(-{amt}px)"); break;
                                case "left": fromTransforms.Add($"translateX({amt}px)"); break;
                                case "right": fromTransforms.Add($"translateX(-{amt}px)"); break;
                            }
                        }
                        if (step.Operation == "slide")
                        {
                            fromOpacity = "0";
                            fromTransforms.Add(Lower(step.Direction) switch
                            {
                                "left" => "translateX(-30px)",
                                "right" => "translateX(30px)",
                                "top" => "translateY(-30px)",
                                "bottom" => "translateY(30px)",
                                _ => "translateX(-30px)",
                            });
                        }
                    }
                    string fromCss = "";
                    if (fromOpacity != null) fromCss += $"opacity: {fromOpacity}; ";
                    if (fromTransforms.Count > 0) fromCss += $"transform: {string.Join(" ", fromTransforms)}; ";
                    const string toCss = "opacity: 1; transform: translate(0, 0) scale(1);";
                    string animName = $"otter_enter_{id}";
                    cssRules.Add($"@keyframes {animName} {{ from {{ {fromCss} }} to {{ {toCss} }} }}");
                    styles.Add($"animation: {animName} {dur}ms {easingCss} forwards;");
                    break;
                }
                case "leave":
                    cssRules.Add($"@keyframes otter_leave_{id} {{ from {{ opacity: 1; transform: scale(1); }} to {{ opacity: 0; transform: scale(0.95); }} }}");
                    break;
            }
        }

        foreach (Stmt eventNode in element.Events)
        {
            if (eventNode is not UiEventStmt uiEvent) continue;
            string eventName = uiEvent.EventName.ToLowerInvariant();
            string evtName = eventName switch
            {
                "hover" => "mouseenter",
                "press" => "mousedown",
                _ => eventName,
            };
            string bodyCode = string.Join("\n", uiEvent.Body.Select(s => js.Statement(s, 3)));
            jsListeners.Add(Templates.DeclarativeListener(id, evtName, bodyCode));
        }

        string labelAttr = "";
        string labelText = "";
        if (element.Label != null)
        {
            if (element.Label is LiteralExpr labelLiteral) labelText = HtmlAttr(Of(labelLiteral.Value));
            else labelAttr = $" data-otter-bind=\"{HtmlAttr(js.Expression(element.Label))}\"";
        }

        var childrenHtml = new List<string>();
        foreach (Stmt child in element.Children)
        {
            if (child is UiElementStmt)
            {
                childrenHtml.Add(Render(child, depth + 1));
            }
            else if (child is IfStmt { Branches.Count: > 0 } conditional)
            {
                IfBranch b0 = conditional.Branches[0];
                string escapedCond = HtmlAttr(js.Expression(b0.Condition));
                var subHtml = new List<string>();
                foreach (Stmt sub in b0.Body)
                {
                    if (sub is UiElementStmt) subHtml.Add(Render(sub, depth + 2));
                }
                string subJoined = string.Join("\n", subHtml);
                string ifId = $"otter_if_{idCounter++}";
                childrenHtml.Add($"      <div id=\"{ifId}\" class=\"otter-conditional\" data-otter-if=\"{escapedCond}\" style=\"display: none;\">\n{subJoined}\n      </div>");
            }
        }

        string styleAttr = styles.Count > 0 ? $" style=\"{string.Join(" ", styles)}\"" : "";
        string classAttr = $" class=\"{string.Join(" ", classes)}\"";
        string content = childrenHtml.Count > 0
            ? (labelText.Length > 0 ? $"{labelText}\n" + string.Join("\n", childrenHtml) : "\n" + string.Join("\n", childrenHtml) + "\n    ")
            : labelText;
        string pad = new(' ', 2 * depth);
        return tag switch
        {
            "heading" => $"{pad}<h2 id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</h2>",
            "button" => $"{pad}<button id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</button>",
            "text" => $"{pad}<div id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</div>",
            "input" => $"{pad}<input type=\"text\" id=\"{id}\"{classAttr}{styleAttr}{labelAttr} value=\"{labelText}\" />",
            "section" => $"{pad}<section id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</section>",
            "page" => $"{pad}<main id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</main>",
            _ => $"{pad}<div id=\"{id}\"{classAttr}{styleAttr}{labelAttr}>{content}</div>",
        };
    }
}
