using System.Globalization;
using Otter.Language;

namespace Otter.Cli;

// Windows PowerShell 5.1's conversions of a command-line string, where
// otter.ps1 lets PowerShell convert one: a [int] parameter (-Port, the
// common parameter -OutBuffer), the cast [int]($_.Trim()) of each
// `otter debug -Breakpoints` part, and the [ActionPreference] common
// parameters. The messages are .NET Framework's, as 5.1 quotes them.
internal static class PowerShellConvert
{
    // LanguagePrimitives.ConvertStringToInteger for System.Int32
    // (Otter.Language's PowerShellInt32, shared with the parser).
    public static bool TryToInt32(string text, out int value, out string error) => PowerShellInt32.TryParse(text, out value, out error);

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
