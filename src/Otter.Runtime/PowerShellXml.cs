using System.Xml;

namespace Otter.Runtime;

// otterPS reads XML through PowerShell's XML adapter, where $node.Name,
// $node.InnerText, $node.ChildNodes and the rest are FIRST looked up as an
// attribute or child element of that name (ignoring case), and only then as
// the .NET property. So in otterPS `name of book` on <book name="x"> is "x",
// and `elements "item" in list` does not find <item name="apple"/>, whose
// Name reads as "apple". This class reproduces that lookup for every
// property otterPS reads.
public static class PowerShellXml
{
    // $node.<property>: matching attributes, then matching child elements;
    // one match is its value, several an array; none is the .NET property.
    public static object? Property(XmlNode node, string property, Func<object?> dotnetProperty)
    {
        var found = new List<object?>();
        if (node is XmlElement element)
        {
            foreach (XmlAttribute attribute in element.Attributes)
            {
                if (string.Equals(attribute.LocalName, property, StringComparison.OrdinalIgnoreCase)) found.Add(attribute.Value);
            }
        }
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child is XmlElement childElement && string.Equals(childElement.LocalName, property, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(NodeValue(childElement));
            }
        }
        if (found.Count == 0) return dotnetProperty();
        return found.Count == 1 ? found[0] : new PowerShellArray(found);
    }

    // An element with no attributes and only text is read as that text.
    static object NodeValue(XmlElement element)
    {
        bool simple = element.Attributes.Count == 0 &&
            element.ChildNodes.Cast<XmlNode>().All(n => n.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace);
        return simple ? element.InnerText : element;
    }

    public static object? Name(XmlNode node) => Property(node, "Name", () => node.Name);

    // A whole document's InnerText reads as $null through the adapter.
    public static object? InnerText(XmlNode node) => Property(node, "InnerText", () => node is XmlDocument ? null : node.InnerText);

    public static object? OuterXml(XmlNode node) => Property(node, "OuterXml", () => node.OuterXml);

    public static object? DocumentElement(XmlDocument document) => Property(document, "DocumentElement", () => document.DocumentElement);

    // What PowerShell's foreach walks for a value: a string or a single
    // object once, an array item by item, an XML node its child nodes.
    public static IEnumerable<object?> Enumerate(object? value) => value switch
    {
        null => Array.Empty<object?>(),
        string text => new object?[] { text },
        PowerShellArray array => array.Items,
        XmlAttributeCollection attributes => attributes.Cast<object?>(),
        XmlNode node => node.ChildNodes.Cast<object?>(),
        _ => new[] { value },
    };

    // $root.ChildNodes, filtered to elements, as Get-OtterXmlChildElements does.
    public static List<XmlElement> ChildElements(XmlNode node)
    {
        object? root = node is XmlDocument document ? DocumentElement(document) : node;
        if (root is not XmlNode rootNode) return new List<XmlElement>();
        object? children = Property(rootNode, "ChildNodes", () => rootNode.ChildNodes);
        IEnumerable<object?> items = children is XmlNodeList list ? list.Cast<object?>() : Enumerate(children);
        return items.OfType<XmlElement>().ToList();
    }

    // $e.Name -eq $Name: text compares ignoring case; an array matches when
    // any of its items does.
    public static bool NameIs(XmlElement element, string name) => Name(element) switch
    {
        string text => string.Equals(text, name, StringComparison.OrdinalIgnoreCase),
        PowerShellArray array => array.Items.Any(i => i is string s && string.Equals(s, name, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    // Turning a value PowerShell handed back into text, as [string] does.
    public static string Text(object? value) => value switch
    {
        null => "",
        string text => text,
        PowerShellArray array => string.Join(" ", array.Items.Select(Text)),
        XmlElement => "System.Xml.XmlElement",
        _ => value.ToString() ?? "",
    };
}

// An array PowerShell produced (several attributes or elements of one name).
// Otter prints it the way PowerShell turns it into text: items joined by spaces.
public sealed class PowerShellArray(List<object?> items)
{
    public List<object?> Items { get; } = items;
    public override string ToString() => PowerShellXml.Text(this);
}
