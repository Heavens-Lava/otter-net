using System.Buffers;
using System.Text;

namespace Otter.Runtime;

// Shared strict application/x-www-form-urlencoded decoder for query strings
// and form bodies. Decode percent-escaped bytes and literal Unicode as one
// UTF-8 stream, so supplementary characters survive without replacement.
public static class OtterWebForms
{
    static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryParse(string encoded, out List<KeyValuePair<string, string>> fields)
    {
        fields = new();
        foreach (string pair in encoded.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key = separator < 0 ? pair : pair[..separator];
            string value = separator < 0 ? "" : pair[(separator + 1)..];
            if (!TryDecode(key, out string name) || !TryDecode(value, out string decoded))
            {
                fields.Clear();
                return false;
            }
            if (name.Length != 0) fields.Add(new(name, decoded));
        }
        return true;
    }

    public static bool TryDecode(string source, out string decoded)
    {
        decoded = "";
        using var stream = new MemoryStream(source.Length);
        for (int i = 0; i < source.Length;)
        {
            char c = source[i];
            if (c == '+')
            {
                stream.WriteByte((byte)' ');
                i++;
            }
            else if (c == '%')
            {
                if (i + 2 >= source.Length || !Uri.IsHexDigit(source[i + 1]) || !Uri.IsHexDigit(source[i + 2]))
                    return false;
                int high = Hex(source[i + 1]);
                int low = Hex(source[i + 2]);
                stream.WriteByte((byte)((high << 4) | low));
                i += 3;
            }
            else
            {
                var status = Rune.DecodeFromUtf16(source.AsSpan(i), out Rune rune, out int consumed);
                if (status != OperationStatus.Done) return false;
                Span<byte> buffer = stackalloc byte[4];
                int length = rune.EncodeToUtf8(buffer);
                stream.Write(buffer[..length]);
                i += consumed;
            }
        }
        try
        {
            decoded = StrictUtf8.GetString(stream.ToArray());
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    static int Hex(char c) => c <= '9' ? c - '0' : c <= 'F' ? c - 'A' + 10 : c - 'a' + 10;
}
