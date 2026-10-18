using System.Globalization;

namespace Otter.Cli;

// Windows PowerShell 5.1's conversions of a command-line string, where
// otter.ps1 lets PowerShell convert one: a [int] parameter (-Port, the
// common parameter -OutBuffer), the cast [int]($_.Trim()) of each
// `otter debug -Breakpoints` part, and the [ActionPreference] common
// parameters. The messages are .NET Framework's, as 5.1 quotes them.
internal static class PowerShellConvert
{
    const string FormatMessage = "Input string was not in a correct format.";
    const string Int32Overflow = "Value was either too large or too small for an Int32.";

    // LanguagePrimitives.ConvertStringToInteger for System.Int32: "" is 0;
    // otherwise Int32Converter (trims; "#..", "0x..", "&h.." are hexadecimal;
    // anything else Int32.Parse with NumberStyles.Integer), and when that
    // says "not in a correct format" a second try through System.Double
    // (Double.Parse with Float | AllowThousands, then Convert.ToInt32, which
    // rounds half to even). On failure `error` is the first attempt's .NET
    // message, the one PowerShell quotes.
    public static bool TryToInt32(string text, out int value, out string error)
    {
        value = 0;
        error = "";
        if (text.Length == 0) return true;
        string trimmed = text.Trim();
        bool formatError;
        if (trimmed.Length == 0)
        {
            // BaseNumberConverter reads text[0] of the trimmed text.
            error = "Index was outside the bounds of the array.";
            return false;
        }
        if (trimmed[0] == '#')
        {
            if (TryHex(trimmed.Substring(1), out value, out error, out formatError)) return true;
        }
        else if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
        {
            if (TryHex(trimmed.Substring(2), out value, out error, out formatError)) return true;
        }
        else
        {
            if (TryDecimal(trimmed, out value, out error, out formatError)) return true;
        }
        if (!formatError) return false;
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double number)
            && TryRoundToInt32(number, out int rounded))
        {
            value = rounded;
            error = "";
            return true;
        }
        return false;
    }

    // Int32.Parse(text, NumberStyles.Integer): ASCII digits after an optional sign.
    static bool TryDecimal(string text, out int value, out string error, out bool formatError)
    {
        value = 0;
        error = "";
        formatError = false;
        int start = text.Length > 0 && (text[0] == '-' || text[0] == '+') ? 1 : 0;
        if (start == text.Length || !text.Skip(start).All(c => c is >= '0' and <= '9'))
        {
            error = FormatMessage;
            formatError = true;
            return false;
        }
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = Int32Overflow;
            return false;
        }
        return true;
    }

    // Convert.ToInt32(text, 16) on .NET Framework (ParseNumbers.StringToInt).
    static bool TryHex(string text, out int value, out string error, out bool formatError)
    {
        value = 0;
        error = "";
        formatError = false;
        if (text.Length == 0)
        {
            error = "Index was out of range. Must be non-negative and less than the size of the collection." + Environment.NewLine + "Parameter name: startIndex";
            return false;
        }
        int i = 0;
        if (text[0] == '-')
        {
            error = "String cannot contain a minus sign if the base is not 10.";
            return false;
        }
        if (text[0] == '+') i++;
        if (i + 1 < text.Length && text[i] == '0' && (text[i + 1] == 'x' || text[i + 1] == 'X')) i += 2;
        int digitsStart = i;
        ulong result = 0;
        bool overflow = false;
        while (i < text.Length && Uri.IsHexDigit(text[i]))
        {
            result = result * 16 + (ulong)Convert.ToInt32(text[i].ToString(), 16);
            if (result > uint.MaxValue) overflow = true;
            i++;
        }
        if (i == digitsStart)
        {
            error = "Could not find any recognizable digits.";
            formatError = true;
            return false;
        }
        if (overflow)
        {
            error = "Value was either too large or too small for a UInt32.";
            return false;
        }
        if (i < text.Length)
        {
            error = "Additional non-parsable characters are at the end of the string.";
            formatError = true;
            return false;
        }
        value = unchecked((int)(uint)result);
        return true;
    }

    // Convert.ToInt32(double): half to even, an overflow beyond Int32.
    static bool TryRoundToInt32(double number, out int value)
    {
        value = 0;
        if (double.IsNaN(number) || number >= 2147483647.5 || number < -2147483648.5) return false;
        value = (int)Math.Round(number, MidpointRounding.ToEven);
        return true;
    }

    // System.Management.Automation.ActionPreference in Windows PowerShell 5.1.
    public static readonly string[] ActionPreferenceNames = { "SilentlyContinue", "Stop", "Continue", "Inquire", "Ignore", "Suspend" };

    // PowerShell's string to enum conversion (EnumSingleTypeConverter): each
    // comma-separated part, trimmed, is a name (any capitals) or a decimal
    // number; the parts are combined with OR, and the result must be a
    // defined value. On failure `error` is the whole PSInvalidCastException message.
    public static bool TryToActionPreference(string text, out int value, out string error)
    {
        value = 0;
        error = "";
        const string Type = "System.Management.Automation.ActionPreference";
        foreach (string rawPart in text.Split(','))
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
            {
                error = $"Cannot convert value \"{text}\" to type \"{Type}\". Error: \"The identifier name {part} cannot be processed because it is either too similar or identical to the following enumerator names: {string.Join(", ", ActionPreferenceNames)}. Use a more specific identifier name.\"";
                return false;
            }
            int index = Array.FindIndex(ActionPreferenceNames, n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) { value |= index; continue; }
            if (long.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long number) && number is >= int.MinValue and <= int.MaxValue)
            {
                value |= (int)number;
                continue;
            }
            error = $"Cannot convert value \"{text}\" to type \"{Type}\". Error: \"Unable to match the identifier name {part} to a valid enumerator name. Specify one of the following enumerator names and try again:{Environment.NewLine}{string.Join(", ", ActionPreferenceNames)}\"";
            return false;
        }
        if (value < 0 || value >= ActionPreferenceNames.Length)
        {
            error = $"Cannot convert value \"{text}\" to type \"{Type}\" due to enumeration values that are not valid. Specify one of the following enumeration values and try again. The possible enumeration values are \"{string.Join(",", ActionPreferenceNames)}\".";
            return false;
        }
        return true;
    }

    // [ValidateVariableName] (the -ErrorVariable, -OutVariable, ... common
    // parameters): a plain or scope-qualified variable name, not a drive path.
    public static bool IsVariableName(string text)
    {
        int colon = text.IndexOf(':');
        if (colon < 0) return true;
        string qualifier = text.Substring(0, colon);
        return qualifier.Equals("global", StringComparison.OrdinalIgnoreCase)
            || qualifier.Equals("local", StringComparison.OrdinalIgnoreCase)
            || qualifier.Equals("script", StringComparison.OrdinalIgnoreCase)
            || qualifier.Equals("private", StringComparison.OrdinalIgnoreCase)
            || qualifier.Equals("variable", StringComparison.OrdinalIgnoreCase);
    }
}
