using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Otter.Language;
using Otter.Runtime;

namespace Otter.Desktop;

// otterPS's WPF UI provider (src/Otter.UI.psm1, D44-D48, D53, D54): the
// same controls, made the same way, with the same properties, events,
// layout and messages. Everything WPF-specific lives in this file, as it
// lives in Otter.UI.psm1 there.
//
// Where otterPS reaches a WPF member through PowerShell's loose member access
// (a property a control does not have reads as nothing; a loop over a
// missing collection does nothing), this port does what PowerShell does. A
// .NET exception PowerShell would let through to the user is given the text
// PowerShell shows for it (PowerShellError).
public sealed class WpfUiProvider : IUiProvider
{
    // ---- the tables (Otter.UI.psm1's $script:OtterWpfKinds,
    // $script:OtterUiProperties, $script:OtterUiEvents) ----

    sealed record Mapping(string? Native, string Type);

    static readonly Mapping Text = new("Text", "text"), Content = new("Content", "text"), Title = new("Title", "text"),
        Width = new("Width", "number"), Height = new("Height", "number"),
        Background = new("Background", "color"), Foreground = new("Foreground", "color"),
        Align = new(null, "align"), Spacing = new(null, "spacing"), Padding = new(null, "padding"),
        Spread = new(null, "spread"), Placeholder = new(null, "placeholder"), Round = new(null, "number");

    static Dictionary<string, Mapping> Table(params (string Name, Mapping Mapping)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Mapping, StringComparer.OrdinalIgnoreCase);

    static Dictionary<string, Mapping> ButtonProperties() => Table(("text", Content), ("width", Width), ("height", Height),
        ("background", Background), ("foreground", Foreground), ("align", Align));

    static Dictionary<string, Mapping> LayoutProperties() => Table(("width", Width), ("height", Height), ("background", Background),
        ("spacing", Spacing), ("padding", Padding), ("align", Align), ("spread", Spread));

    // PowerShell's hash tables ignore capitals, so `create Button into b`
    // makes a button and `Text of b` is its text.
    static readonly Dictionary<string, Dictionary<string, Mapping>> Properties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = ButtonProperties(),
        ["text box"] = Table(("text", Text), ("placeholder", Placeholder), ("width", Width), ("height", Height),
            ("background", Background), ("foreground", Foreground), ("align", Align)),
        ["text"] = Table(("text", Text), ("width", Width), ("height", Height), ("background", Background), ("foreground", Foreground), ("align", Align)),
        ["window"] = Table(("title", Title), ("width", Width), ("height", Height), ("background", Background), ("foreground", Foreground),
            ("spacing", Spacing), ("padding", Padding)),
        ["row"] = LayoutProperties(),
        ["column"] = LayoutProperties(),
        ["scroll"] = Table(("width", Width), ("height", Height), ("background", Background)),
        ["primary button"] = ButtonProperties(),
        ["secondary button"] = ButtonProperties(),
        ["danger button"] = ButtonProperties(),
        ["heading"] = Table(("text", Text), ("width", Width), ("height", Height), ("background", Background), ("foreground", Foreground), ("align", Align)),
        ["panel"] = LayoutProperties(),
        ["grid"] = LayoutProperties(),
        ["card"] = Table(("width", Width), ("height", Height), ("background", Background), ("spacing", Spacing), ("padding", Padding),
            ("align", Align), ("spread", Spread), ("round", Round)),
    };

    static readonly Dictionary<string, Dictionary<string, string>> Events = new(StringComparer.OrdinalIgnoreCase)
    {
        ["button"] = ClickEvents(),
        ["primary button"] = ClickEvents(),
        ["secondary button"] = ClickEvents(),
        ["danger button"] = ClickEvents(),
        ["card"] = new(StringComparer.OrdinalIgnoreCase) { ["clicked"] = "PreviewMouseDown", ["click"] = "PreviewMouseDown" },
        ["text box"] = new(StringComparer.OrdinalIgnoreCase) { ["changed"] = "TextChanged" },
        ["window"] = new(StringComparer.OrdinalIgnoreCase) { ["closed"] = "Closed" },
    };

    static Dictionary<string, string> ClickEvents() => new(StringComparer.OrdinalIgnoreCase) { ["clicked"] = "Click", ["click"] = "Click" };

    // Sort-Object of the kinds: lower-case letters and spaces sort the same
    // way ordinally and by culture.
    static readonly string[] KindNames =
    {
        "button", "card", "column", "danger button", "grid", "heading", "panel", "primary button",
        "row", "scroll", "secondary button", "text", "text box", "window",
    };

    static string Known(IEnumerable<string> names) => string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal));

    // ---- what Otter.UI.psm1 keeps about each control, by identity ----

    sealed class LayoutEntry
    {
        public string? MainAlign, CrossAlign, LastAlign;
        public bool Spread;
    }

    readonly Dictionary<object, double> spacing = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, double> padding = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, Border> wrappers = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, string> placeholders = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, Brush?> originalBackgrounds = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<(object Native, string Property)> dimensionFull = new(new FullKeyComparer());
    readonly Dictionary<object, LayoutEntry> layout = new(ReferenceEqualityComparer.Instance);
    readonly BrushConverter brushConverter = new();
    // Set-OtterUiDispatcher: the dispatcher of the last window made or shown.
    Dispatcher? dispatcher;

    sealed class FullKeyComparer : IEqualityComparer<(object Native, string Property)>
    {
        public bool Equals((object Native, string Property) x, (object Native, string Property) y) =>
            ReferenceEquals(x.Native, y.Native) && string.Equals(x.Property, y.Property, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((object Native, string Property) key) =>
            HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(key.Native), key.Property.ToLowerInvariant());
    }

    public bool OnDispatcherThread => dispatcher != null && dispatcher.CheckAccess();

    static OtterError Error(string message, int line, string? suggestion = null) => new(message, line, "runtime", 0, null, suggestion);

    static bool IsKind(string kind, params string[] names) => names.Any(n => string.Equals(kind, n, StringComparison.OrdinalIgnoreCase));

    static Brush ParseBrush(string text) => (Brush)new BrushConverter().ConvertFromString(text)!;

    // ---- create (D44): New-OtterUiResourceValue ----

    public UiResource Create(string kind, int line)
    {
        object? native = kind.ToLowerInvariant() switch
        {
            "window" => NewWindow(),
            // Controls default to content-sized widths in Otter.
            "button" => new Button { HorizontalAlignment = HorizontalAlignment.Left },
            "text" => new TextBlock { HorizontalAlignment = HorizontalAlignment.Left },
            "text box" => new TextBox { HorizontalAlignment = HorizontalAlignment.Left },
            "row" => new StackPanel { Orientation = Orientation.Horizontal },
            "column" => new StackPanel { Orientation = Orientation.Vertical },
            // D53: a scroll shows a scroll bar only when its content overflows.
            "scroll" => new ScrollViewer
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
            "card" => NewCard(),
            "heading" => new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = ParseBrush("#f8fafc"),
                Margin = new Thickness(0, 0, 0, 8),
            },
            "panel" => new StackPanel { Orientation = Orientation.Vertical },
            "grid" => new Grid(),
            "primary button" => new Button
            {
                Background = ParseBrush("#2563eb"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(18, 10, 18, 10),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            "secondary button" => new Button
            {
                Background = ParseBrush("#334155"),
                Foreground = ParseBrush("#f8fafc"),
                BorderBrush = ParseBrush("#475569"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 10, 18, 10),
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            "danger button" => new Button
            {
                Background = ParseBrush("#dc2626"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(18, 10, 18, 10),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            _ => null,
        };
        if (native == null)
        {
            throw Error($"I don't know how to create a {kind} yet.", line, $"Otter currently knows: {string.Join(", ", KindNames)}");
        }
        return new UiResource(kind, native);
    }

    Window NewWindow()
    {
        var window = new Window();
        dispatcher = window.Dispatcher;
        return window;
    }

    static Border NewCard()
    {
        var border = new Border
        {
            Background = ParseBrush("#1e293b"),
            BorderBrush = ParseBrush("#334155"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24),
            Effect = new DropShadowEffect { BlurRadius = 24, Opacity = 0.35, ShadowDepth = 8, Direction = 270 },
        };
        border.Child = new StackPanel { Orientation = Orientation.Vertical };
        return border;
    }

    // ---- properties (D45, D48, D53, D54) ----

    static Mapping PropertyMapping(string kind, string property, int line)
    {
        if (!Properties.TryGetValue(kind, out var forKind))
        {
            throw Error($"A {kind} has no properties Otter knows about yet.", line);
        }
        if (!forKind.TryGetValue(property, out var mapping))
        {
            throw Error($"A {kind} has no property called \"{property}\".", line, $"A {kind} has: {Known(forKind.Keys)}");
        }
        return mapping;
    }

    public object? GetProperty(UiResource resource, string property, int line)
    {
        Mapping mapping = PropertyMapping(resource.Kind, property, line);
        object native = resource.Native;
        switch (mapping.Type)
        {
            // Never set: gone (D22), not 0.
            case "spacing": return spacing.TryGetValue(native, out double s) ? s : null;
            case "padding": return padding.TryGetValue(native, out double p) ? p : null;
            case "placeholder": return placeholders.TryGetValue(native, out string? text) ? text : null;
            case "spread": return layout.TryGetValue(native, out var spreadEntry) && spreadEntry.Spread;
            // The direction set most recently.
            case "align": return layout.TryGetValue(native, out var alignEntry) ? alignEntry.LastAlign : null;
        }
        if (dimensionFull.Contains((native, property))) return "full";
        object? raw = ReadNative(native, mapping.Native);
        // A colour always reads back as "#AARRGGBB".
        if (mapping.Type == "color") return raw is SolidColorBrush brush ? brush.Color.ToString() : null;
        return raw;
    }

    public void SetProperty(UiResource resource, string property, object? value, int line)
    {
        Mapping mapping = PropertyMapping(resource.Kind, property, line);
        object native = resource.Native;
        string kind = resource.Kind;
        switch (mapping.Type)
        {
            case "spacing": SetSpacing(resource, value, line); return;
            case "padding": SetPadding(resource, value, line); return;
            case "placeholder": SetPlaceholder(resource, value); return;
            case "align": SetAlign(resource, value, line); return;
            case "spread":
            {
                LayoutEntry entry = LayoutOf(native);
                if (entry.MainAlign != null)
                {
                    throw Error($"spread conflicts with the earlier align \"{entry.MainAlign}\" - they both control this {kind}'s main-axis position.", line);
                }
                entry.Spread = true;
                UpdateSpread(resource);
                return;
            }
            case "number":
            {
                bool isWidth = string.Equals(property, "width", StringComparison.OrdinalIgnoreCase);
                bool isHeight = string.Equals(property, "height", StringComparison.OrdinalIgnoreCase);
                wrappers.TryGetValue(native, out Border? wrapper);
                var element = (FrameworkElement)native;
                if ((isWidth || isHeight) && PowerShellEqualsFull(value))
                {
                    dimensionFull.Add((native, property));
                    if (isWidth)
                    {
                        if (IsKind(kind, "window")) SetNative("Width", () => element.Width = SystemParameters.WorkArea.Width);
                        else
                        {
                            element.HorizontalAlignment = HorizontalAlignment.Stretch;
                            element.Width = double.NaN;
                            if (wrapper != null)
                            {
                                wrapper.HorizontalAlignment = HorizontalAlignment.Stretch;
                                wrapper.Width = double.NaN;
                            }
                        }
                    }
                    else
                    {
                        if (IsKind(kind, "window")) SetNative("Height", () => element.Height = SystemParameters.WorkArea.Height);
                        else
                        {
                            element.VerticalAlignment = VerticalAlignment.Stretch;
                            element.Height = double.NaN;
                            if (wrapper != null)
                            {
                                wrapper.VerticalAlignment = VerticalAlignment.Stretch;
                                wrapper.Height = double.NaN;
                            }
                        }
                    }
                    return;
                }
                double number = AssertNumber(value, property, kind, line);
                dimensionFull.Remove((native, property));
                if (isWidth && !IsKind(kind, "window"))
                {
                    element.HorizontalAlignment = HorizontalAlignment.Left;
                    if (wrapper != null)
                    {
                        wrapper.HorizontalAlignment = HorizontalAlignment.Left;
                        SetNative("Width", () => wrapper.Width = number);
                    }
                }
                else if (isHeight && !IsKind(kind, "window"))
                {
                    element.VerticalAlignment = VerticalAlignment.Top;
                    if (wrapper != null)
                    {
                        wrapper.VerticalAlignment = VerticalAlignment.Top;
                        SetNative("Height", () => wrapper.Height = number);
                    }
                }
                WriteNative(native, mapping.Native, number);
                return;
            }
            case "color":
            {
                Brush brush = AssertColor(value, line);
                WriteNative(native, mapping.Native, brush);
                if (wrappers.TryGetValue(native, out Border? wrapper)) wrapper.Background = brush;
                if (originalBackgrounds.ContainsKey(native))
                {
                    originalBackgrounds[native] = brush;
                    UpdatePlaceholderWatermark((TextBox)native);
                }
                return;
            }
        }
        // text: the same conversion `say` applies.
        WriteNative(native, mapping.Native, Values.Format(value));
    }

    // Test-OtterNumeric, ConvertTo-OtterNumber
    static bool IsNumeric(object? value, out double number)
    {
        number = 0;
        return value is not bool && Values.TryNumber(value, out number);
    }

    // Assert-OtterUiNumber: checked before WPF is touched.
    static double AssertNumber(object? value, string property, string kind, int line)
    {
        if (!IsNumeric(value, out double number))
        {
            string shown = Values.Format(value);
            if (value is string text) shown = "\"" + text + "\"";
            throw Error($"I expected a number for the {property} of this {kind} but got {shown}.", line);
        }
        if (number < 0)
        {
            throw Error($"The {property} of a {kind} can't be negative, but I got {Values.PowerShellText(number)}.", line);
        }
        return number;
    }

    // Assert-OtterUiColor: a colour name or "#rrggbb", as WPF's BrushConverter reads it.
    Brush AssertColor(object? value, int line)
    {
        string text = Values.Format(value);
        try
        {
            return (Brush)brushConverter.ConvertFromString(text)!;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw Error($"I don't understand the color \"{text}\".", line);
        }
    }

    // `$Value -eq 'full'`: PowerShell's comparison of any Otter value with the
    // text "full" (a list is a filter: true when an item matches).
    static bool PowerShellEqualsFull(object? value) => value switch
    {
        string text => string.Equals(text, "full", StringComparison.OrdinalIgnoreCase),
        bool b => b,
        OtterList list => list.Any(item => item is bool or string && PowerShellEqualsFull(item)),
        _ => false,
    };

    // PowerShell's [string] of an Otter value.
    static string PowerShellString(object? value) => value switch
    {
        null => "",
        bool b => b ? "True" : "False",
        double d => Values.PowerShellText(d),
        string s => s,
        OtterList list => string.Join(" ", list.Select(PowerShellString)),
        OtterThing => "OtterObject",
        _ => value.ToString() ?? "",
    };

    // $Resource.Native.(name): a member the control does not have reads as nothing.
    static object? ReadNative(object native, string? name) => name switch
    {
        "Content" => native is ContentControl content ? content.Content : null,
        "Text" => native switch { TextBlock block => block.Text, TextBox box => box.Text, _ => null },
        "Title" => native is Window window ? window.Title : null,
        "Width" => native is FrameworkElement element ? element.Width : null,
        "Height" => native is FrameworkElement element ? element.Height : null,
        "Background" => native switch
        {
            Control control => control.Background,
            Panel panel => panel.Background,
            Border border => border.Background,
            TextBlock block => block.Background,
            _ => null,
        },
        "Foreground" => native switch { Control control => control.Foreground, TextBlock block => block.Foreground, _ => null },
        _ => null,
    };

    // $Resource.Native.(name) = value
    static void WriteNative(object native, string? name, object value)
    {
        switch (name)
        {
            case "Content": SetNative(name, () => ((ContentControl)native).Content = value); return;
            case "Text":
                SetNative(name, () =>
                {
                    if (native is TextBlock block) block.Text = (string)value;
                    else ((TextBox)native).Text = (string)value;
                });
                return;
            case "Title": SetNative(name, () => ((Window)native).Title = (string)value); return;
            case "Width": SetNative(name, () => ((FrameworkElement)native).Width = (double)value); return;
            case "Height": SetNative(name, () => ((FrameworkElement)native).Height = (double)value); return;
            case "Background":
                SetNative(name, () =>
                {
                    switch (native)
                    {
                        case Control control: control.Background = (Brush)value; break;
                        case Panel panel: panel.Background = (Brush)value; break;
                        case Border border: border.Background = (Brush)value; break;
                        case TextBlock block: block.Background = (Brush)value; break;
                    }
                });
                return;
            case "Foreground":
                SetNative(name, () =>
                {
                    if (native is Control control) control.Foreground = (Brush)value;
                    else if (native is TextBlock block) block.Foreground = (Brush)value;
                });
                return;
            default:
                // A table entry with no native property (a card's round):
                // PowerShell cannot set a property with no name.
                throw new PowerShellError("The property '' cannot be found on this object. Verify that the property exists and can be set.");
        }
    }

    // A property set that fails inside WPF (a handler of the change failing,
    // a value WPF refuses): PowerShell's SetValueInvocationException.
    static void SetNative(string name, Action set)
    {
        try
        {
            set();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw new PowerShellError($"Exception setting \"{name}\": \"{e.Message}\"");
        }
    }

    // ---- align and spread (D54) ----

    static string AlignAxis(string direction, string kind, int line) => direction switch
    {
        "top" or "middle" or "bottom" => "vertical",
        "left" or "center" or "right" => "horizontal",
        _ => throw Error($"A {kind} has no alignment called \"{direction}\".", line, "Otter accepts: top, middle, bottom, left, center, right"),
    };

    static VerticalAlignment VerticalOf(string direction) => direction switch
    {
        "top" => VerticalAlignment.Top,
        "middle" => VerticalAlignment.Center,
        _ => VerticalAlignment.Bottom,
    };

    static HorizontalAlignment HorizontalOf(string direction) => direction switch
    {
        "left" => HorizontalAlignment.Left,
        "center" => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Right,
    };

    LayoutEntry LayoutOf(object native)
    {
        if (!layout.TryGetValue(native, out var entry)) layout[native] = entry = new LayoutEntry();
        return entry;
    }

    void SetAlign(UiResource resource, object? value, int line)
    {
        string direction = PowerShellString(value).ToLowerInvariant();
        string kind = resource.Kind;
        string axis = AlignAxis(direction, kind, line);
        object native = resource.Native;
        if (!IsKind(kind, "row", "column"))
        {
            if (axis != "horizontal") throw Error($"A {kind} can only align left, center, or right.", line);
            if (native is Button button) button.HorizontalContentAlignment = HorizontalOf(direction);
            else
            {
                TextAlignment textAlignment = direction switch { "left" => TextAlignment.Left, "center" => TextAlignment.Center, _ => TextAlignment.Right };
                if (native is TextBlock block) block.TextAlignment = textAlignment;
                else if (native is TextBox box) box.TextAlignment = textAlignment;
            }
            return;
        }
        LayoutEntry entry = LayoutOf(native);
        string mainAxis = IsKind(kind, "row") ? "horizontal" : "vertical";
        entry.LastAlign = direction;
        if (axis == mainAxis)
        {
            if (entry.Spread)
            {
                throw Error($"align \"{direction}\" conflicts with spread - they both control this {kind}'s main-axis position.", line);
            }
            if (entry.MainAlign != null && entry.MainAlign != direction)
            {
                throw Error($"align \"{direction}\" conflicts with the earlier align \"{entry.MainAlign}\" - only one alignment is allowed per axis.", line);
            }
            entry.MainAlign = direction;
            MainAxisAlign(resource, direction);
            return;
        }
        if (entry.CrossAlign != null && entry.CrossAlign != direction)
        {
            throw Error($"align \"{direction}\" conflicts with the earlier align \"{entry.CrossAlign}\" - only one alignment is allowed per axis.", line);
        }
        entry.CrossAlign = direction;
        if (ContainerPanel(resource) is Panel panel)
        {
            foreach (UIElement child in panel.Children)
            {
                if (child is not FrameworkElement element) continue;
                if (axis == "vertical") element.VerticalAlignment = VerticalOf(direction);
                else element.HorizontalAlignment = HorizontalOf(direction);
            }
        }
    }

    // Set-OtterUiMainAxisAlign: the leftover space, measured once, becomes a
    // margin before the first child.
    void MainAxisAlign(UiResource resource, string direction)
    {
        if (ContainerPanel(resource) is not StackPanel panel) return;
        int count = panel.Children.Count;
        if (count == 0) return;
        bool horizontal = panel.Orientation == Orientation.Horizontal;
        double available = horizontal
            ? (!double.IsNaN(panel.Width) && panel.Width > 0 ? panel.Width : panel.ActualWidth)
            : (!double.IsNaN(panel.Height) && panel.Height > 0 ? panel.Height : panel.ActualHeight);
        double total = 0;
        foreach (UIElement child in panel.Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            total += horizontal ? child.DesiredSize.Width : child.DesiredSize.Height;
        }
        double leftover = available - total;
        if (leftover <= 0) return;
        var first = (FrameworkElement)panel.Children[0];
        double before = direction is "right" or "bottom" ? leftover : direction is "center" or "middle" ? leftover / 2 : 0;
        Thickness margin = first.Margin;
        first.Margin = horizontal
            ? new Thickness(before, margin.Top, margin.Right, margin.Bottom)
            : new Thickness(margin.Left, before, margin.Right, margin.Bottom);
    }

    // Update-OtterUiSpread
    void UpdateSpread(UiResource resource)
    {
        if (!layout.TryGetValue(resource.Native, out var entry) || !entry.Spread) return;
        if (ContainerPanel(resource) is not Panel panel) return;
        int count = panel.Children.Count;
        if (count <= 1) return;
        bool horizontal = panel is StackPanel stack && stack.Orientation == Orientation.Horizontal;
        double available = horizontal
            ? (!double.IsNaN(panel.Width) && panel.Width > 0 ? panel.Width : panel.ActualWidth > 0 ? panel.ActualWidth : 0)
            : (!double.IsNaN(panel.Height) && panel.Height > 0 ? panel.Height : panel.ActualHeight > 0 ? panel.ActualHeight : 0);
        double total = 0;
        foreach (UIElement child in panel.Children)
        {
            var element = (FrameworkElement)child;
            double size = horizontal ? element.Width : element.Height;
            double desired = horizontal ? element.DesiredSize.Width : element.DesiredSize.Height;
            if (!double.IsNaN(size) && size > 0) total += size;
            else if (desired > 0) total += desired;
            else
            {
                element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                total += horizontal ? element.DesiredSize.Width : element.DesiredSize.Height;
            }
        }
        if (available <= total) return;
        double gap = (available - total) / (count - 1);
        for (int i = 0; i < count - 1; i++)
        {
            var element = (FrameworkElement)panel.Children[i];
            Thickness m = element.Margin;
            element.Margin = horizontal ? new Thickness(m.Left, m.Top, gap, m.Bottom) : new Thickness(m.Left, m.Top, m.Right, gap);
        }
        var last = (FrameworkElement)panel.Children[count - 1];
        Thickness lm = last.Margin;
        last.Margin = horizontal ? new Thickness(lm.Left, lm.Top, 0, lm.Bottom) : new Thickness(lm.Left, lm.Top, lm.Right, 0);
    }

    // ---- spacing, padding, placeholder (D48, D53) ----

    // Set-OtterUiSpacing: every child, now and later, gets the same margin
    // after it.
    void SetSpacing(UiResource resource, object? value, int line)
    {
        double number = AssertNumber(value, "spacing", resource.Kind, line);
        object panel = ContainerPanel(resource);
        spacing[resource.Native] = number;
        if (panel is not Panel children) return;
        bool horizontal = panel is StackPanel stack && stack.Orientation == Orientation.Horizontal;
        foreach (UIElement child in children.Children)
        {
            if (child is FrameworkElement element) element.Margin = horizontal ? new Thickness(0, 0, number, 0) : new Thickness(0, 0, 0, number);
        }
    }

    // Set-OtterUiPadding: a window's content, or the control itself, goes
    // inside a Border that carries the padding.
    void SetPadding(UiResource resource, object? value, int line)
    {
        double number = AssertNumber(value, "padding", resource.Kind, line);
        object native = resource.Native;
        padding[native] = number;
        if (IsKind(resource.Kind, "window"))
        {
            var window = (Window)native;
            if (window.Content == null)
            {
                var border = new Border { Padding = new Thickness(number), Child = new StackPanel() };
                window.Content = border;
                wrappers[native] = border;
            }
            else if (window.Content is Border existing)
            {
                existing.Padding = new Thickness(number);
            }
            else
            {
                var inner = (UIElement)window.Content;
                window.Content = null;
                var border = new Border { Padding = new Thickness(number), Child = inner };
                window.Content = border;
                wrappers[native] = border;
            }
            return;
        }
        if (wrappers.TryGetValue(native, out Border? wrapper))
        {
            wrapper.Padding = new Thickness(number);
            return;
        }
        var element = (FrameworkElement)native;
        var made = new Border
        {
            Padding = new Thickness(number),
            Width = element.Width,
            Height = element.Height,
            HorizontalAlignment = element.HorizontalAlignment,
            VerticalAlignment = element.VerticalAlignment,
            Margin = element.Margin,
        };
        element.Margin = new Thickness(0, 0, 0, 0);
        Brush? background = ReadNative(native, "Background") as Brush;
        if (background != null) made.Background = background;
        object parent = element.Parent;
        if (parent != null)
        {
            if (parent is Panel parentPanel)
            {
                int index = parentPanel.Children.IndexOf(element);
                if (index >= 0)
                {
                    parentPanel.Children.RemoveAt(index);
                    made.Child = element;
                    parentPanel.Children.Insert(index, made);
                }
            }
            else if (parent is ContentControl content)
            {
                content.Content = null;
                made.Child = element;
                content.Content = made;
            }
        }
        else
        {
            made.Child = element;
        }
        wrappers[native] = made;
    }

    static VisualBrush NewWatermarkBrush(string text, Brush? background)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = Brushes.DarkGray,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(4, 2, 0, 0),
        };
        var grid = new Grid { Background = background ?? Brushes.White };
        grid.Children.Add(block);
        return new VisualBrush(grid)
        {
            Stretch = Stretch.None,
            TileMode = TileMode.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
        };
    }

    void UpdatePlaceholderWatermark(TextBox box)
    {
        if (!placeholders.TryGetValue(box, out string? text)) return;
        originalBackgrounds.TryGetValue(box, out Brush? original);
        if (string.IsNullOrEmpty(box.Text)) box.Background = NewWatermarkBrush(text, original);
        else if (original != null) box.Background = original;
        else box.ClearValue(Control.BackgroundProperty);
    }

    void SetPlaceholder(UiResource resource, object? value)
    {
        string text = Values.Format(value);
        placeholders[resource.Native] = text;
        var box = (TextBox)resource.Native;
        if (!originalBackgrounds.ContainsKey(box))
        {
            originalBackgrounds[box] = box.Background;
            box.TextChanged += (sender, _) => UpdatePlaceholderWatermark((TextBox)sender);
        }
        UpdatePlaceholderWatermark(box);
    }

    // ---- events (D46) ----

    public void AddEventHandler(UiResource resource, string eventName, Action handler, int line)
    {
        if (!Events.TryGetValue(resource.Kind, out var forKind))
        {
            throw Error($"A {resource.Kind} has no events Otter knows about yet.", line);
        }
        if (!forKind.TryGetValue(eventName, out string? clrName))
        {
            throw Error($"A {resource.Kind} has no event called \"{eventName}\".", line, $"A {resource.Kind} has: {Known(forKind.Keys)}");
        }
        switch (clrName)
        {
            case "Click": ((ButtonBase)resource.Native).Click += (_, _) => handler(); break;
            case "PreviewMouseDown": ((UIElement)resource.Native).PreviewMouseDown += (_, _) => handler(); break;
            case "TextChanged": ((TextBox)resource.Native).TextChanged += (_, _) => handler(); break;
            case "Closed": ((Window)resource.Native).Closed += (_, _) => handler(); break;
        }
    }

    // ---- layout and show (D47, D53) ----

    // Get-OtterUiContainerPanel: a window's children live in a panel it makes
    // the first time one is needed.
    object ContainerPanel(UiResource container)
    {
        if (IsKind(container.Kind, "window"))
        {
            var window = (Window)container.Native;
            if (window.Content == null)
            {
                var panel = new StackPanel();
                if (padding.TryGetValue(window, out double number))
                {
                    var border = new Border { Padding = new Thickness(number), Child = panel };
                    window.Content = border;
                    wrappers[window] = border;
                }
                else
                {
                    window.Content = panel;
                }
            }
            return window.Content is Border wrapped ? wrapped.Child : window.Content!;
        }
        return container.Native;
    }

    FrameworkElement ElementForParent(UiResource resource) =>
        wrappers.TryGetValue(resource.Native, out Border? wrapper) ? wrapper : (FrameworkElement)resource.Native;

    public void Put(UiResource container, UiResource item, int line)
    {
        if (IsKind(container.Kind, "scroll"))
        {
            // A scroll holds one thing, and WPF would quietly replace or steal it.
            var scroll = (ScrollViewer)container.Native;
            if (scroll.Content != null)
            {
                throw Error("A scroll can only hold one thing. Put a column or row in it first if you need more than one.", line);
            }
            FrameworkElement only = ElementForParent(item);
            if (only.Parent != null)
            {
                throw Error($"A {item.Kind} can only be in one place at a time, and this one is already somewhere else.", line);
            }
            scroll.Content = only;
            return;
        }
        if (!IsKind(container.Kind, "window", "row", "column"))
        {
            throw Error($"I can only put things in a window, row, column, or scroll, not a {container.Kind}.", line);
        }
        var panel = (Panel)ContainerPanel(container);
        FrameworkElement element = ElementForParent(item);
        try
        {
            panel.Children.Add(element);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw Error($"A {item.Kind} can only be in one place at a time, and this one is already somewhere else.", line);
        }
        bool horizontal = panel is StackPanel stack && stack.Orientation == Orientation.Horizontal;
        // D48: a child put in after spacing was set gets it too.
        if (spacing.TryGetValue(container.Native, out double space))
        {
            element.Margin = horizontal ? new Thickness(0, 0, space, 0) : new Thickness(0, 0, 0, space);
        }
        // D54: and the alignment.
        if (layout.TryGetValue(container.Native, out var entry))
        {
            if (entry.CrossAlign != null)
            {
                if (horizontal) element.VerticalAlignment = VerticalOf(entry.CrossAlign);
                else element.HorizontalAlignment = HorizontalOf(entry.CrossAlign);
            }
            if (entry.Spread) UpdateSpread(container);
            else if (entry.MainAlign != null) MainAxisAlign(container, entry.MainAlign);
        }
    }

    // Show-OtterUiResource: modal; the window's dispatcher runs the handlers
    // until it closes. Anything that stops it (a handler's failure included)
    // is otterPS's one message.
    public void Show(UiResource resource, int line)
    {
        if (!IsKind(resource.Kind, "window"))
        {
            throw Error($"I can only show a window right now, not a {resource.Kind}.", line);
        }
        try
        {
            var window = (Window)resource.Native;
            dispatcher = window.Dispatcher;
            window.ShowDialog();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            throw Error("This window has already been closed, so it can't be shown again.", line);
        }
    }
}

// An exception that reaches the user as PowerShell would show it from
// otterPS: "Otter hit a problem inside itself" with this text.
public sealed class PowerShellError(string message) : Exception(message);
