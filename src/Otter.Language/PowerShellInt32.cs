using System.Globalization;

namespace Otter.Language;

// Windows PowerShell 5.1's conversion of a value to [int] (System.Int32),
// where otterPS lets PowerShell convert one: otter.ps1's [int] parameters and
// casts, and the parser's `columns N on medium` (Read-OtterUiElementStatement
// casts the token's value with [int]). The messages are .NET Framework's, as
// 5.1 quotes them.
public static class PowerShellInt32
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
    public static bool TryParse(string text, out int value, out string error)
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
    public static bool TryRoundToInt32(double number, out int value)
    {
        value = 0;
        if (double.IsNaN(number) || number >= 2147483647.5 || number < -2147483648.5) return false;
        value = (int)Math.Round(number, MidpointRounding.ToEven);
        return true;
    }


    // The [int] cast of a token's value: a number rounds half to even, true
    // and false are 1 and 0, nothing is 0, text converts as above. On failure
    // `message` is PowerShell's whole PSInvalidCastException message.
    public static bool TryCast(object? value, out int result, out string message) => TryCast(value, out result, out message, out _);

    // The same, with the PSInvalidCastException's ErrorId.
    public static bool TryCast(object? value, out int result, out string message, out string errorId)
    {
        result = 0;
        message = "";
        errorId = "";
        string error;
        switch (value)
        {
            case null:
                return true;
            case bool flag:
                result = flag ? 1 : 0;
                return true;
            case double number:
                if (TryRoundToInt32(number, out result)) return true;
                // .NET Framework's Double.ToString(): 15 significant digits.
                message = Cast(number.ToString("G15", CultureInfo.CurrentCulture), "Value was either too large or too small for an Int32.");
                errorId = "InvalidCastIConvertible";
                return false;
            case string text:
                if (TryParse(text, out result, out error)) return true;
                message = Cast(text, error);
                errorId = "InvalidCastFromStringToInteger";
                return false;
            default:
                message = Cast(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "", "Specified cast is not valid.");
                errorId = "InvalidCastIConvertible";
                return false;
        }
    }

    static string Cast(string shown, string error) => $"Cannot convert value \"{shown}\" to type \"System.Int32\". Error: \"{error}\"";
}
