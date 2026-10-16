using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Otter.Runtime;

// JSON exactly as otterPS reads and writes it on Windows PowerShell 5.1
// (D29): ConvertTo-Json is .NET Framework's JavaScriptSerializer followed by
// PowerShell's own indenting pass, and ConvertFrom-Json is JavaScriptSerializer's
// lenient reader followed by PowerShell turning dictionaries into objects.
// Both are reproduced here character for character, quirks included, because
// what a program prints must not change between the two implementations.
public sealed class JsonProblem() : Exception("not valid JSON");

public static class OtterJson
{
    // ---- writing ---------------------------------------------------------

    // ConvertTo-Json -Depth 32 of otterPS's ConvertTo-OtterJsonShape; gone
    // gives back nothing at all, as ConvertTo-Json does for $null.
    public static string? Write(object? value)
    {
        if (value is null) return null;
        var compact = new StringBuilder();
        Serialize(value, compact);
        string json = compact.ToString();
        if (!json.StartsWith('{') && !json.StartsWith('[')) return json;
        var pretty = new StringBuilder();
        pretty.Append(json[0]);
        if (json[0] == '{') ConvertDictionary(json, 1, pretty, "", 0);
        else ConvertList(json, 1, pretty, "", 0);
        return pretty.ToString();
    }

    // Raised when the value holds something that is not data.
    public sealed class NotData() : Exception("not data");

    static void Serialize(object? value, StringBuilder sb)
    {
        switch (value)
        {
            case null: sb.Append("null"); return;
            case bool b: sb.Append(b ? "true" : "false"); return;
            case double d: sb.Append(RoundTrip(d)); return;
            case string s: SerializeString(s, sb); return;
            case OtterThing thing:
            {
                sb.Append('{');
                bool first = true;
                foreach (string name in thing.PropertyNames)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    SerializeString(name, sb);
                    sb.Append(':');
                    Serialize(thing.ReadProperty(name), sb);
                }
                sb.Append('}');
                return;
            }
            case OtterList list:
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Serialize(list[i], sb);
                }
                sb.Append(']');
                return;
            }
            // An OtterDate is a PowerShell class: ConvertTo-Json writes its two
            // properties, the DateTime in JavaScriptSerializer's own form.
            case OtterDate date:
            {
                long milliseconds = (date.Value.ToUniversalTime().Ticks - UnixEpochTicks) / 10000;
                sb.Append("{\"Value\":\"\\/Date(").Append(milliseconds.ToString(CultureInfo.InvariantCulture)).Append(")\\/\",\"HasTime\":");
                sb.Append(date.HasTime ? "true" : "false").Append('}');
                return;
            }
            // OtterBytes is a PowerShell class too: its one property, the bytes
            // as numbers.
            case OtterBytes bytes:
                sb.Append("{\"Value\":[").Append(string.Join(",", bytes.Value.Select(b => b.ToString(CultureInfo.InvariantCulture)))).Append("]}");
                return;
            case OtterFunction:
            case OtterType:
                throw new NotData();
            default:
                SerializeString(value.ToString() ?? "", sb);
                return;
        }
    }

    const long UnixEpochTicks = 621355968000000000;

    // .NET Framework's "R": 15 significant digits when they read back as the
    // same number, otherwise 17. (.NET 10's "R" is the shortest round trip,
    // which prints pi as 3.141592653589793 where otterPS prints ...931.)
    // Framework never prints a negative zero.
    public static string RoundTrip(double value)
    {
        if (value == 0) return "0";
        string text = value.ToString("G15", CultureInfo.InvariantCulture);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double back) && back == value) return text;
        return value.ToString("G17", CultureInfo.InvariantCulture);
    }

    // HttpUtility.JavaScriptStringEncode, which JavaScriptSerializer uses.
    static void SerializeString(string text, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char c in text)
        {
            switch (c)
            {
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < ' ' || c is '\'' or '<' or '>' or '&' or '\u0085' or (char)0x2028 or (char)0x2029)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // PowerShell 5.1's ConvertToJsonCommand indenting: four spaces a level,
    // two after each colon, and a value that opens a block indented past its
    // own key.
    static int ConvertList(string json, int index, StringBuilder result, string padString, int numberOfSpaces)
    {
        result.Append("\r\n");
        string newPad = padString + new string(' ', numberOfSpaces) + "    ";
        bool headChar = true;
        for (int i = index; i < json.Length; i++)
        {
            switch (json[i])
            {
                case '{':
                    result.Append(newPad).Append('{');
                    i = ConvertDictionary(json, i + 1, result, newPad, 0);
                    headChar = false;
                    break;
                case '[':
                    result.Append(newPad).Append('[');
                    i = ConvertList(json, i + 1, result, newPad, 0);
                    headChar = false;
                    break;
                case ']':
                    result.Append("\r\n").Append(padString).Append(' ', numberOfSpaces).Append(']');
                    return i;
                case '"':
                    if (headChar) result.Append(newPad);
                    result.Append('"');
                    i = ConvertQuotedString(json, i + 1, result);
                    headChar = false;
                    break;
                case ',':
                    result.Append(",\r\n");
                    headChar = true;
                    break;
                default:
                    if (headChar) result.Append(newPad);
                    result.Append(json[i]);
                    headChar = false;
                    break;
            }
        }
        return json.Length - 1;
    }

    static int ConvertDictionary(string json, int index, StringBuilder result, string padString, int numberOfSpaces)
    {
        result.Append("\r\n");
        string newPad = padString + new string(' ', numberOfSpaces) + "    ";
        bool beforeQuote = true;
        int spacesBeforeValue = 0;
        for (int i = index; i < json.Length; i++)
        {
            switch (json[i])
            {
                case '{':
                    result.Append('{');
                    i = ConvertDictionary(json, i + 1, result, newPad, spacesBeforeValue);
                    break;
                case '[':
                    result.Append('[');
                    i = ConvertList(json, i + 1, result, newPad, spacesBeforeValue);
                    break;
                case '}':
                    result.Append("\r\n").Append(padString).Append(' ', numberOfSpaces).Append('}');
                    return i;
                case '"':
                {
                    if (beforeQuote) result.Append(newPad);
                    result.Append('"');
                    int end = ConvertQuotedString(json, i + 1, result);
                    // The key, its quotes, the colon and the two spaces.
                    if (beforeQuote) spacesBeforeValue = end - i + 4;
                    i = end;
                    beforeQuote = false;
                    break;
                }
                case ':':
                    result.Append(":  ");
                    break;
                case ',':
                    result.Append(",\r\n");
                    beforeQuote = true;
                    break;
                default:
                    result.Append(json[i]);
                    break;
            }
        }
        return json.Length - 1;
    }

    static int ConvertQuotedString(string json, int index, StringBuilder result)
    {
        for (int i = index; i < json.Length; i++)
        {
            result.Append(json[i]);
            if (json[i] != '"') continue;
            int j = i;
            bool escaped = false;
            while (j > 0 && json[--j] == '\\') escaped = !escaped;
            if (!escaped) return i;
        }
        return json.Length - 1;
    }

    // ---- reading ---------------------------------------------------------

    // ConvertFrom-Json on Windows PowerShell 5.1, then otterPS's
    // ConvertFrom-OtterJsonValue: objects become things, arrays lists, every
    // number a double. Throws JsonProblem for anything it would refuse.
    public static object? Read(string text)
    {
        var reader = new Reader(text);
        object? parsed = reader.ReadDocument();
        return ToOtter(parsed);
    }

    // What JavaScriptSerializer hands back for an object: its members in the
    // order first seen, a repeated name keeping its first place, last value.
    sealed class Members
    {
        public readonly List<string> Names = new();
        public readonly Dictionary<string, object?> Values = new(StringComparer.Ordinal);

        public void Set(string name, object? value)
        {
            if (!Values.ContainsKey(name)) Names.Add(name);
            Values[name] = value;
        }
    }

    static object? ToOtter(object? value)
    {
        switch (value)
        {
            case null: return null;
            case Members members:
            {
                // PowerShell's own step: a property needs a name, and names
                // differing only in capital letters collide.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var thing = new OtterThing("thing");
                foreach (string name in members.Names)
                {
                    if (name.Length == 0 || !seen.Add(name)) throw new JsonProblem();
                    thing.WriteProperty(name, ToOtter(members.Values[name]));
                }
                return thing;
            }
            case List<object?> items: return new OtterList(items.Select(ToOtter));
            case bool b: return b;
            case int n: return (double)n;
            case long n: return (double)n;
            case decimal n: return DecimalToDouble(n);
            case double n: return n;
            // [string] of a DateTime in PowerShell is the invariant culture.
            case DateTime date: return date.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            default: return value.ToString();
        }
    }

    // .NET Framework's decimal-to-double (oleaut32's VarR8FromDec).
    static double DecimalToDouble(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        ulong low64 = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);
        double result = ((double)low64 + (double)(uint)bits[2] * 18446744073709551616.0) / PowersOfTen[(bits[3] >> 16) & 0xFF];
        return (bits[3] < 0) ? -result : result;
    }

    static readonly double[] PowersOfTen = Enumerable.Range(0, 29).Select(p => double.Parse("1e" + p, CultureInfo.InvariantCulture)).ToArray();

    // JavaScriptObjectDeserializer, as Windows PowerShell 5.1 configures it.
    sealed class Reader(string text)
    {
        const int DepthLimit = 1020;
        static readonly Regex DatePattern = new(@"^""\\/Date\((?<ticks>-?[0-9]+)(?:[a-zA-Z]|(?:\+|-)[0-9]{4})?\)\\/""", RegexOptions.CultureInvariant);
        int position;

        char? Next() => position < text.Length ? text[position++] : null;
        void Back() => position--;

        char? NextNonEmpty()
        {
            while (position < text.Length)
            {
                char c = text[position++];
                if (!char.IsWhiteSpace(c)) return c;
            }
            return null;
        }

        public object? ReadDocument()
        {
            object? value = ReadValue(0);
            if (NextNonEmpty() != null) throw new JsonProblem();
            return value;
        }

        object? ReadValue(int depth)
        {
            if (++depth > DepthLimit) throw new JsonProblem();
            char? c = NextNonEmpty();
            if (c == null) return null;
            Back();
            if (string.CompareOrdinal(text, position, "\"\\/Date(", 0, 8) == 0 && position + 8 <= text.Length)
            {
                Match date = DatePattern.Match(text.Substring(position));
                if (date.Success)
                {
                    if (!long.TryParse(date.Groups["ticks"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms)) throw new JsonProblem();
                    position += date.Length;
                    try { return new DateTime(checked(ms * 10000 + UnixEpochTicks), DateTimeKind.Utc); }
                    catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException) { throw new JsonProblem(); }
                }
            }
            if (c == '{') return ReadObject(depth);
            if (c == '[') return ReadArray(depth);
            if (c is '"' or '\'') return ReadString();
            return ReadPrimitive();
        }

        List<object?> ReadArray(int depth)
        {
            var list = new List<object?>();
            Next();
            bool expectMore = false;
            char? c;
            while ((c = NextNonEmpty()) != null && c != ']')
            {
                Back();
                list.Add(ReadValue(depth));
                expectMore = false;
                c = NextNonEmpty();
                if (c == ']') break;
                expectMore = true;
                if (c != ',') throw new JsonProblem();
            }
            if (expectMore || c != ']') throw new JsonProblem();
            return list;
        }

        Members ReadObject(int depth)
        {
            Members? members = null;
            Next();
            char? c;
            while ((c = NextNonEmpty()) != null)
            {
                Back();
                if (c == ':') throw new JsonProblem();
                string? name = null;
                if (c != '}')
                {
                    name = c is '"' or '\'' ? ReadString() : ReadToken();
                    if (string.IsNullOrEmpty(name)) throw new JsonProblem();
                    if (NextNonEmpty() != ':') throw new JsonProblem();
                }
                if (members == null)
                {
                    members = new Members();
                    if (string.IsNullOrEmpty(name))
                    {
                        c = NextNonEmpty();
                        break;
                    }
                }
                object? value = ReadValue(depth);
                members.Set(name!, value);
                c = NextNonEmpty();
                if (c == '}') break;
                if (c != ',') throw new JsonProblem();
            }
            if (c != '}' || members == null) throw new JsonProblem();
            return members;
        }

        string ReadString()
        {
            var sb = new StringBuilder();
            char quote = Next()!.Value;
            bool escaped = false;
            char? c;
            while ((c = Next()) != null)
            {
                if (c == '\\')
                {
                    if (escaped) { sb.Append('\\'); escaped = false; }
                    else escaped = true;
                    continue;
                }
                if (escaped)
                {
                    switch (c)
                    {
                        case '"' or '\'' or '/': sb.Append(c.Value); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length ||
                                !int.TryParse(text.AsSpan(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw new JsonProblem();
                            }
                            position += 4;
                            sb.Append((char)code);
                            break;
                        default: throw new JsonProblem();
                    }
                    escaped = false;
                    continue;
                }
                if (c == quote) return ReplaceLoneSurrogates(sb.ToString());
                sb.Append(c.Value);
            }
            throw new JsonProblem();
        }

        static string ReplaceLoneSurrogates(string s)
        {
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1])) { i++; continue; }
                if (char.IsSurrogate(chars[i])) chars[i] = '�';
            }
            return new string(chars);
        }

        string ReadToken()
        {
            var sb = new StringBuilder();
            char? c;
            while ((c = Next()) != null)
            {
                if (char.IsLetterOrDigit(c.Value) || c is '.' or '-' or '_' or '+') sb.Append(c.Value);
                else { Back(); break; }
            }
            return sb.ToString();
        }

        object? ReadPrimitive()
        {
            string token = ReadToken();
            if (token == "null") return null;
            if (token == "true") return true;
            if (token == "false") return false;
            bool hasPoint = token.Contains('.');
            if (token.IndexOf("e", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (!hasPoint)
                {
                    if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
                    if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                }
                if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal m)) return m;
            }
            // .NET Framework reads only these spellings of the special values,
            // and refuses a number too large for a double.
            if (token is "NaN") return double.NaN;
            if (token is "Infinity" or "+Infinity") return double.PositiveInfinity;
            if (token is "-Infinity") return double.NegativeInfinity;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
            throw new JsonProblem();
        }
    }
}
