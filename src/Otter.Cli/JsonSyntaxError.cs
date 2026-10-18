using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Otter.Cli;

// What ConvertFrom-Json says about text it refuses, on Windows PowerShell
// 5.1: the message of .NET Framework's JavaScriptObjectDeserializer
// (System.Web.Extensions), most with its debug suffix " (position): rest of
// the input", or of PowerShell's own step that turns dictionaries into
// objects. otterPS quotes it after "invalid JSON syntax: ".
//
// Otter.Runtime's OtterJson reads JSON the same way and only says whether it
// can; this walks the text again, the way JavaScriptObjectDeserializer does,
// to find the message. Returns null when the text is valid.
internal static class JsonSyntaxError
{
    sealed class Problem(string message) : Exception(message);

    public static string? Describe(string input)
    {
        try
        {
            var reader = new Reader(input);
            object? result = reader.ReadInternal(0);
            if (reader.NextNonEmpty() != null)
            {
                throw new Problem($"Invalid JSON primitive: {reader.Rest}.");
            }
            Populate(result);
            return null;
        }
        catch (Problem problem)
        {
            return problem.Message;
        }
    }

    // JsonObject.PopulateFromDictionary / PopulateFromList: a PSObject per
    // dictionary, whose property names ignore capitals.
    static void Populate(object? value)
    {
        if (value is Members members)
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in members.Names)
            {
                if (name.Length == 0)
                {
                    throw new Problem("Cannot process argument because the value of argument \"name\" is not valid. Change the value of the \"name\" argument and run the operation again.");
                }
                if (seen.TryGetValue(name, out string? first))
                {
                    throw new Problem($"Cannot convert the JSON string because a dictionary that was converted from the string contains the duplicated keys '{first}' and '{name}'.");
                }
                seen[name] = name;
                Populate(members.Values[name]);
            }
        }
        else if (value is List<object?> list)
        {
            foreach (object? item in list) Populate(item);
        }
    }

    sealed class Members
    {
        public readonly List<string> Names = new();
        public readonly Dictionary<string, object?> Values = new(StringComparer.Ordinal);
    }

    // JavaScriptObjectDeserializer over JavaScriptString.
    sealed class Reader(string s)
    {
        const int DepthLimit = 1020;
        const string DateTimePrefix = "\"\\/Date(";
        int index;

        public string Rest => s.Length > index ? s.Substring(index) : "";

        // Measured on Windows PowerShell 5.1 (CI): the text after the
        // position is the whole JSON text, not what follows the position.
        string Debug(string message) => $"{message} ({index}): {s}";

        public char? NextNonEmpty()
        {
            while (s.Length > index)
            {
                char c = s[index++];
                if (!char.IsWhiteSpace(c)) return c;
            }
            return null;
        }

        char? MoveNext() => s.Length > index ? s[index++] : null;

        string? MoveNext(int count)
        {
            if (s.Length >= index + count)
            {
                string result = s.Substring(index, count);
                index += count;
                return result;
            }
            return null;
        }

        void MovePrev() { if (index > 0) index--; }

        void MovePrev(int count)
        {
            while (index > 0 && count > 0) { index--; count--; }
        }

        public object? ReadInternal(int depth)
        {
            if (++depth > DepthLimit) throw new Problem(Debug("RecursionLimit exceeded."));
            char? c = NextNonEmpty();
            if (c == null) return null;
            MovePrev();
            string? next = MoveNext(DateTimePrefix.Length);
            if (next != null)
            {
                MovePrev(DateTimePrefix.Length);
                if (next == DateTimePrefix) return ReadDate();
            }
            if (c == '{') return ReadDictionary(depth);
            if (c == '[') return ReadList(depth);
            if (c is '"' or '\'') return ReadString();
            return ReadPrimitive();
        }

        object? ReadDate()
        {
            Match match = Regex.Match(Rest, @"^""\\/Date\((?<ticks>-?[0-9]+)(?:[a-zA-Z]|(?:\+|-)[0-9]{4})?\)\\/""", RegexOptions.CultureInvariant);
            if (long.TryParse(match.Groups["ticks"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
            {
                MoveNext(match.Length);
                long value = unchecked(ticks * 10000 + 621355968000000000);
                if (value < DateTime.MinValue.Ticks || value > DateTime.MaxValue.Ticks)
                {
                    throw new Problem("Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks." + Environment.NewLine + "Parameter name: ticks");
                }
                return value;
            }
            return ReadString();
        }

        List<object?> ReadList(int depth)
        {
            var list = new List<object?>();
            char? c = MoveNext();
            if (c != '[') throw new Problem(Debug("Invalid array passed in, '[' expected."));
            bool expectMore = false;
            while ((c = NextNonEmpty()) != null && c != ']')
            {
                MovePrev();
                list.Add(ReadInternal(depth));
                expectMore = false;
                c = NextNonEmpty();
                if (c == ']') break;
                expectMore = true;
                if (c != ',') throw new Problem(Debug("Invalid array passed in, ',' expected."));
            }
            if (expectMore) throw new Problem(Debug("Invalid array passed in, extra trailing ','."));
            if (c != ']') throw new Problem(Debug("Invalid array passed in, ']' expected."));
            return list;
        }

        Members ReadDictionary(int depth)
        {
            Members? dictionary = null;
            char? c = MoveNext();
            if (c != '{') throw new Problem(Debug("Invalid object passed in, '{' expected."));
            while ((c = NextNonEmpty()) != null)
            {
                MovePrev();
                if (c == ':') throw new Problem(Debug("Invalid object passed in, member name expected."));
                string? memberName = null;
                if (c != '}')
                {
                    memberName = ReadMemberName();
                    c = NextNonEmpty();
                    if (c != ':') throw new Problem(Debug("Invalid object passed in, ':' or '}' expected."));
                }
                if (dictionary == null)
                {
                    dictionary = new Members();
                    if (string.IsNullOrEmpty(memberName))
                    {
                        c = NextNonEmpty();
                        break;
                    }
                }
                if (dictionary.Names.Count >= 1000)
                {
                    throw new Problem("The JSON request was too large to be deserialized.");
                }
                object? value = ReadInternal(depth);
                if (!dictionary.Values.ContainsKey(memberName!)) dictionary.Names.Add(memberName!);
                dictionary.Values[memberName!] = value;
                c = NextNonEmpty();
                if (c == '}') break;
                if (c != ',') throw new Problem(Debug("Invalid object passed in, ':' or '}' expected."));
            }
            if (c != '}') throw new Problem(Debug("Invalid object passed in, ':' or '}' expected."));
            return dictionary!;
        }

        string? ReadMemberName()
        {
            char? c = NextNonEmpty();
            if (c == null) return null;
            MovePrev();
            if (c is '"' or '\'') return ReadString();
            return ReadToken();
        }

        object? ReadPrimitive()
        {
            string input = ReadToken();
            if (input == "null" || input == "true" || input == "false") return input;
            bool hasDecimalPoint = input.Contains('.') || input.Contains('e', StringComparison.OrdinalIgnoreCase);
            if (!hasDecimalPoint && long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return input;
            if (decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return input;
            if (input is "NaN" or "Infinity" or "+Infinity" or "-Infinity") return input;
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !double.IsInfinity(d)) return input;
            throw new Problem($"Invalid JSON primitive: {input}.");
        }

        string ReadToken()
        {
            var sb = new StringBuilder();
            char? c;
            while ((c = MoveNext()) != null)
            {
                if (char.IsLetterOrDigit(c.Value) || c is '.' or '-' or '_' or '+') sb.Append(c.Value);
                else { MovePrev(); break; }
            }
            return sb.ToString();
        }

        string ReadString()
        {
            var sb = new StringBuilder();
            bool escaped = false;
            char? c = MoveNext();
            char quote = '"';
            if (c == '\'') quote = '\'';
            else if (c != '"') throw new Problem(Debug("Invalid string passed in, '\"' expected."));
            while ((c = MoveNext()) != null)
            {
                if (c == '\\')
                {
                    if (escaped) { sb.Append('\\'); escaped = false; }
                    else escaped = true;
                    continue;
                }
                if (escaped)
                {
                    AppendEscaped(c.Value, sb);
                    escaped = false;
                }
                else
                {
                    if (c == quote) return sb.ToString();
                    sb.Append(c.Value);
                }
            }
            throw new Problem(Debug("Unterminated string passed in."));
        }

        void AppendEscaped(char c, StringBuilder sb)
        {
            switch (c)
            {
                case '"' or '\'' or '/': sb.Append(c); return;
                case 'b': sb.Append('\b'); return;
                case 'f': sb.Append('\f'); return;
                case 'n': sb.Append('\n'); return;
                case 'r': sb.Append('\r'); return;
                case 't': sb.Append('\t'); return;
                case 'u':
                {
                    // int.Parse(MoveNext(4), NumberStyles.HexNumber)
                    string? digits = MoveNext(4);
                    if (digits == null) throw new Problem("Value cannot be null." + Environment.NewLine + "Parameter name: String");
                    if (!int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                    {
                        throw new Problem("Input string was not in a correct format.");
                    }
                    sb.Append((char)code);
                    return;
                }
                default:
                    throw new Problem(Debug("Unrecognized escape sequence."));
            }
        }
    }
}
