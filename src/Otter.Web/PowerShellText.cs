using System.Globalization;
using System.Text;

namespace Otter.Web;

// The few places where otterPS's web compiler leans on PowerShell itself
// to turn values into text, reproduced as Windows PowerShell 5.1 (the
// certified reference, on .NET Framework) does them.
static class PowerShellText
{
    // "$value" / [string]$value. A double is written with .NET Framework's
    // default "G" format, which is 15 significant digits (PowerShell 7 on
    // .NET Core writes the shortest round-trip form instead: 0.1 plus 0.2
    // is "0.3" on 5.1 and "0.30000000000000004" on 7).
    public static string Of(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "True" : "False",
        double number => Number(number),
        int number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public static string Number(double number)
    {
        if (double.IsNaN(number)) return "NaN";
        if (double.IsPositiveInfinity(number)) return "Infinity";
        if (double.IsNegativeInfinity(number)) return "-Infinity";
        return number.ToString("G15", CultureInfo.InvariantCulture);
    }

    // PowerShell's `$left -eq $right`: the right side is converted to the
    // left side's type; text compares ignoring case; a failed conversion is
    // simply not equal.
    public static bool Eq(object? left, object? right)
    {
        switch (left)
        {
            case null:
                return right is null;
            case string text:
                return string.Equals(text, Of(right), StringComparison.OrdinalIgnoreCase);
            case bool flag:
                return flag == Truthy(right);
            case double number:
                return ToDouble(right) is double other && number == other;
            default:
                return Equals(left, right);
        }
    }

    // [bool]$value: PowerShell's truthiness.
    public static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0,
        double number => number != 0,
        _ => true,
    };

    static double? ToDouble(object? value)
    {
        switch (value)
        {
            case double number: return number;
            case bool flag: return flag ? 1 : 0;
            case string text:
                if (text.Trim().Length == 0) return 0;
                return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;
            default: return null;
        }
    }

    // `-is [int] -or -is [double]` on an Otter literal: numbers are doubles.
    public static bool IsNumber(object? value) => value is double;

    // Escape-OtterHtmlAttr: & first, then " < >.
    public static string HtmlAttr(string? text)
    {
        if (text == null) return "";
        return text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // Escape-OtterHtmlText: the attribute escapes and '.
    public static string HtmlText(string? text) => HtmlAttr(text).Replace("'", "&#39;");

    // ConvertTo-Json -Compress of a string on Windows PowerShell 5.1
    // (JavaScriptSerializer): besides " and \, it writes < > & ' as <
    // > & ' and other control characters as \u00xx.
    // PowerShell 7 (Newtonsoft) leaves < > & ' as they are.
    public static string JsonString(string text)
    {
        var json = new StringBuilder(text.Length + 2);
        json.Append('"');
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                case '<': case '>': case '&': case '\'':
                    json.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (ch < ' ') json.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else json.Append(ch);
                    break;
            }
        }
        json.Append('"');
        return json.ToString();
    }
}
