using System.Globalization;
using System.Runtime.InteropServices;

namespace Otter.Language;

// The few places where Windows PowerShell 5.1 writes a value with the user's
// regional format (Region > Formats), and otterPS shows it: a number inside
// a .NET conversion message ("Cannot convert value "2147483647,5" to type
// "System.Int32"" with German formats), the time separator in task and event
// times, WPF's refusal of an infinite width. otter-net runs with invariant
// globalization (no culture data in the native executable), so this builds
// the current culture from the invariant one and the user's own settings as
// Windows' National Language Support gives them (GetLocaleInfoEx, user
// overrides included) - the source .NET Framework itself reads. Only these
// symbols are taken; comparisons, casing and every invariant formatting stay
// as they are, and the process keeps the invariant culture: the few places
// use this one by name. With English (United States) formats nothing
// changes.
// Measured on a Windows CI runner under German formats
// (SPEC-QUESTIONS W5-4). Elsewhere PowerShell 7 reads ICU's data, which
// otter-net does not have: the invariant culture stays.
public static unsafe partial class UserFormats
{
    const uint LocaleSDecimal = 0x0E;
    const uint LocaleSThousand = 0x0F;
    const uint LocaleSTime = 0x1E;
    const uint LocaleSPositiveSign = 0x50;
    const uint LocaleSNegativeSign = 0x51;
    const uint LocaleSNan = 0x69;
    const uint LocaleSPositiveInfinity = 0x6A;
    const uint LocaleSNegativeInfinity = 0x6B;

    [LibraryImport("kernel32.dll", EntryPoint = "GetLocaleInfoEx", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetLocaleInfoEx(string? localeName, uint type, char* data, int length);

    static CultureInfo? culture;

    // The user's formats as a culture (the invariant one off Windows).
    public static CultureInfo Culture => culture ??= Build();

    // A number as .NET Framework's Double.ToString() writes it with the
    // user's formats (15 significant digits), as PowerShell shows the value
    // in "Cannot convert value "..." to type ..." messages; never a
    // negative zero.
    public static string Number(double value) => value == 0 ? "0" : value.ToString("G15", Culture);

    static CultureInfo Build()
    {
        if (!OperatingSystem.IsWindows()) return CultureInfo.InvariantCulture;
        var user = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        NumberFormatInfo number = user.NumberFormat;
        // .NET Framework's CultureData: an empty sign is "+" or "-".
        Read(LocaleSDecimal, text => { number.NumberDecimalSeparator = text; number.PercentDecimalSeparator = text; });
        Read(LocaleSThousand, text => { number.NumberGroupSeparator = text; number.PercentGroupSeparator = text; }, allowEmpty: true);
        Read(LocaleSPositiveSign, text => number.PositiveSign = text);
        Read(LocaleSNegativeSign, text => number.NegativeSign = text);
        Read(LocaleSNan, text => number.NaNSymbol = text);
        Read(LocaleSPositiveInfinity, text => number.PositiveInfinitySymbol = text);
        Read(LocaleSNegativeInfinity, text => number.NegativeInfinitySymbol = text);
        Read(LocaleSTime, text => user.DateTimeFormat.TimeSeparator = text);
        return user;
    }

    static void Read(uint type, Action<string> set, bool allowEmpty = false)
    {
        try
        {
            char* buffer = stackalloc char[128];
            int length = GetLocaleInfoEx(null, type, buffer, 128);
            if (length <= 0) return;
            string text = new(buffer, 0, length - 1);
            if (text.Length == 0 && !allowEmpty) return;
            set(text);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or ArgumentException or InvalidOperationException)
        {
            // keep the invariant symbol
        }
    }
}
