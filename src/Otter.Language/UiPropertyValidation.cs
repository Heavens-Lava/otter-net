namespace Otter.Language;

// Shared validation for UI component property declarations (`is a <uiKind> with ...`
// and `is a <uiKind>` blocks). Checks duplicate properties across all objects, and
// unknown properties + invalid literal value types on known UI component kinds.
internal static class UiPropertyValidation
{
    private static readonly string[] Variants = { "primary ", "secondary ", "danger ", "outline ", "ghost " };

    private static readonly HashSet<string> KnownUiKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "window", "page", "button", "text box", "text", "row", "column",
        "image", "list", "link", "card", "checkbox", "check box",
        "dropdown", "drop down", "select", "slider", "range",
        "text area", "textarea", "badge", "tag", "canvas", "table", "scroll",
        "progress", "progress bar", "toggle", "switch", "radio", "radio button",
        "dialog", "modal", "panel", "heading", "section", "sidebar", "main",
        "input", "grid",
    };

    private static readonly HashSet<string> KnownUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        // Layout & box model
        "width", "height", "maxwidth", "minwidth", "minheight",
        "padding", "margin", "spacing", "gap", "flex", "grow",
        "wrap", "overflow", "position", "top", "right", "bottom", "left", "zindex",
        "align", "align_h", "alignitems", "justify", "spread", "scroll", "centered",
        // Visual & typography
        "background", "foreground", "border", "radius", "round", "shadow",
        "size", "fontsize", "weight", "fontweight", "fontstyle", "style",
        "family", "fontfamily", "lineheight", "letterspacing", "whitespace",
        "cursor", "customstyle", "variant", "animation", "mode", "theme",
        // Content, controls & interaction
        "text", "value", "title", "subtitle", "description", "url", "href", "source", "src", "alt",
        "label", "hint", "error", "placeholder", "options", "items", "rows", "min", "max", "step",
        "showvalue", "checked", "group", "name", "hideheader", "runnable", "draggable", "accepts drops",
        "columns", "headers", "data", "selected", "enabled", "disabled",
        "visible", "hidden", "id", "tooltip", "icon",
    };

    private static readonly HashSet<string> BooleanUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "hideheader", "scroll", "runnable", "round", "spread", "checked",
        "draggable", "accepts drops", "wrap", "centered", "enabled", "disabled",
        "visible", "hidden", "selected", "showvalue",
    };

    private static readonly HashSet<string> NumericUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "spacing", "flex", "grow", "rows", "min", "max", "zindex", "columns", "step",
    };

    private static readonly HashSet<string> SizeOrWeightUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "size", "fontsize", "weight", "fontweight",
    };

    private static readonly HashSet<string> DimensionUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "width", "height", "maxwidth", "minwidth", "minheight",
        "padding", "margin", "gap", "radius", "top", "right", "bottom", "left",
    };

    private static readonly HashSet<string> TextOrStyleUiProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "href", "source", "src", "background", "foreground", "customstyle",
        "fontfamily", "family", "whitespace", "cursor", "overflow", "position",
        "theme", "label", "hint", "error", "subtitle", "description",
    };

    private static readonly HashSet<string> AlignDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "top", "middle", "bottom", "left", "center", "right",
    };

    public static bool IsKnownUiKind(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        string kind = typeName.Trim();
        foreach (string v in Variants)
        {
            if (kind.StartsWith(v, StringComparison.OrdinalIgnoreCase))
            {
                kind = kind.Substring(v.Length).Trim();
                break;
            }
        }
        return KnownUiKinds.Contains(kind);
    }

    public static OtterError? ValidateProperties(
        string objectName,
        string typeName,
        IReadOnlyList<AssignStmt> properties,
        Func<string, int, string?, string, OtterError> makeError)
    {
        if (properties.Count == 0) return null;

        // 1. Duplicate property check (applies to all object & component declarations)
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in properties)
        {
            if (prop.Target is not VariableExpr v) continue;
            if (seen.TryGetValue(v.Name, out int firstLine))
            {
                return makeError(
                    $"Duplicate property '{v.Name}' in '{objectName}' declaration (already set on line {firstLine}).",
                    prop.Line,
                    $"Remove the duplicate '{v.Name}' property so each property is declared once.",
                    "DuplicateProperty"
                );
            }
            seen[v.Name] = prop.Line;
        }

        // 2. UI component schema checks (only for known UI component kinds)
        if (!IsKnownUiKind(typeName)) return null;

        foreach (var prop in properties)
        {
            if (prop.Target is not VariableExpr v) continue;
            string name = v.Name;

            if (!KnownUiProperties.Contains(name))
            {
                return makeError(
                    $"Unknown property '{name}' for UI component '{typeName}'.",
                    prop.Line,
                    "Use a supported UI property such as text, url, size, weight, background, foreground, padding, spacing, align, width, height, radius, or customstyle.",
                    "UnknownUiProperty"
                );
            }

            if (prop.Value is not LiteralExpr lit) continue;
            object? val = lit.Value;

            if (string.Equals(name, "align", StringComparison.OrdinalIgnoreCase))
            {
                if (val is not string dir || !AlignDirections.Contains(dir))
                {
                    return makeError(
                        $"Invalid value for 'align': expected top, middle, bottom, left, center, or right.",
                        prop.Line,
                        "Write: middle align (or top/bottom/left/center/right align).",
                        "InvalidUiPropertyType"
                    );
                }
            }
            else if (BooleanUiProperties.Contains(name))
            {
                if (val is not bool)
                {
                    return makeError(
                        $"Property '{name}' expects a true/false value, not '{val}'.",
                        prop.Line,
                        $"Write: true {name} (or false {name}, or bare '{name}').",
                        "InvalidUiPropertyType"
                    );
                }
            }
            else if (NumericUiProperties.Contains(name))
            {
                if (val is not double)
                {
                    return makeError(
                        $"Property '{name}' expects a number, not '{val}'.",
                        prop.Line,
                        $"Write a numeric value such as: 16 {name}",
                        "InvalidUiPropertyType"
                    );
                }
            }
            else if (SizeOrWeightUiProperties.Contains(name))
            {
                if (val is bool || val is null)
                {
                    return makeError(
                        $"Property '{name}' expects a number (or CSS font value), not '{val}'.",
                        prop.Line,
                        $"Write a numeric value such as: 14 {name}",
                        "InvalidUiPropertyType"
                    );
                }
            }
            else if (DimensionUiProperties.Contains(name))
            {
                if (val is bool || val is null)
                {
                    return makeError(
                        $"Property '{name}' expects a number or size string (such as \"100%\" or full), not '{val}'.",
                        prop.Line,
                        $"Write: 64 {name} or \"100%\" {name}",
                        "InvalidUiPropertyType"
                    );
                }
            }
            else if (TextOrStyleUiProperties.Contains(name))
            {
                if (val is not string)
                {
                    return makeError(
                        $"Property '{name}' expects text (a quoted string), not '{val}'.",
                        prop.Line,
                        $"Write a quoted text value such as: \"...\" {name}",
                        "InvalidUiPropertyType"
                    );
                }
            }
        }

        return null;
    }
}
