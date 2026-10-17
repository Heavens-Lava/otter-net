using System.Security.Cryptography;
using System.Text;

namespace Otter.Runtime;

// hash "text" as "sha256" [with key "k"] into digest (D91) and
// encrypt/decrypt "text" with key "secret" into result (D92), ported from
// otterPS's Get-OtterHash, Protect-OtterText and Unprotect-OtterText. The
// encrypted form is the same - base64(salt, IV, AES-256-CBC ciphertext,
// HMAC-SHA256 tag) with PBKDF2-SHA256 keys - so either implementation can
// decrypt what the other encrypted. Only .NET's own primitives are used.
public sealed class CryptoProblem(string message) : Exception(message);

public static class OtterCrypto
{
    const string ValidNames = "\"md5\", \"sha1\", \"sha256\", \"sha384\", or \"sha512\"";

    // Lowercase hex; a key makes it an HMAC.
    public static string Hash(string text, string algorithm, string? key)
    {
        string normalized = algorithm.ToLowerInvariant().Replace("-", "").Replace(" ", "");
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] digest;
        if (key != null)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            digest = normalized switch
            {
                "md5" => HMACMD5.HashData(keyBytes, data),
                "sha1" => HMACSHA1.HashData(keyBytes, data),
                "sha256" => HMACSHA256.HashData(keyBytes, data),
                "sha384" => HMACSHA384.HashData(keyBytes, data),
                "sha512" => HMACSHA512.HashData(keyBytes, data),
                _ => throw new CryptoProblem($"I don't know a hash algorithm called \"{algorithm}\". Try {ValidNames}."),
            };
        }
        else
        {
            digest = normalized switch
            {
                "md5" => MD5.HashData(data),
                "sha1" => SHA1.HashData(data),
                "sha256" => SHA256.HashData(data),
                "sha384" => SHA384.HashData(data),
                "sha512" => SHA512.HashData(data),
                _ => throw new CryptoProblem($"I don't know a hash algorithm called \"{algorithm}\". Try {ValidNames}."),
            };
        }
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    // ---- over bytes (D109) ----

    public const int KeyLength = 32;
    const int PasswordIterations = 600000;

    public static byte[] Digest(string algorithm, byte[] data) => algorithm.ToLowerInvariant() switch
    {
        "sha384" => SHA384.HashData(data),
        "sha512" => SHA512.HashData(data),
        _ => SHA256.HashData(data),
    };

    public static byte[] Hmac(string algorithm, byte[] key, byte[] data) => algorithm.ToLowerInvariant() switch
    {
        "sha384" => HMACSHA384.HashData(key, data),
        "sha512" => HMACSHA512.HashData(key, data),
        _ => HMACSHA256.HashData(key, data),
    };

    static readonly byte[] EncryptionLabel = Encoding.ASCII.GetBytes("otter-aead-v1-enc");
    static readonly byte[] MacLabel = Encoding.ASCII.GetBytes("otter-aead-v1-mac");

    // 0x01 | iv(16) | AES-256-CBC ciphertext | HMAC-SHA256 tag(32), with the
    // two subkeys derived from the one 32-byte key - otterPS's Protect-OtterBytes.
    public static byte[] SealBytes(byte[] data, byte[] key)
    {
        byte[] encryptionKey = HMACSHA256.HashData(key, EncryptionLabel);
        byte[] macKey = HMACSHA256.HashData(key, MacLabel);
        byte[] iv = RandomNumberGenerator.GetBytes(16);
        using Aes aes = Aes.Create();
        aes.Key = encryptionKey;
        byte[] cipher = aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
        byte[] body = [1, .. iv, .. cipher];
        byte[] tag = HMACSHA256.HashData(macKey, body);
        return [.. body, .. tag];
    }

    // The plaintext, or null when the payload is malformed, altered or made
    // with another key - one answer for all three, on purpose.
    public static byte[]? OpenBytes(byte[] payload, byte[] key)
    {
        if (payload.Length < 65 || payload[0] != 1) return null;
        byte[] encryptionKey = HMACSHA256.HashData(key, EncryptionLabel);
        byte[] macKey = HMACSHA256.HashData(key, MacLabel);
        byte[] body = payload[..^32];
        byte[] expected = HMACSHA256.HashData(macKey, body);
        if (!CryptographicOperations.FixedTimeEquals(expected, payload[^32..])) return null;
        int cipherLength = body.Length - 17;
        if (cipherLength % 16 != 0) return null;
        using Aes aes = Aes.Create();
        aes.Key = encryptionKey;
        try { return aes.DecryptCbc(body[17..], body[1..17], PaddingMode.PKCS7); }
        catch (CryptographicException) { return null; }
    }

    // otter-pbkdf2-sha256$<iterations>$<salt base64>$<hash base64>
    public static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
        return $"otter-pbkdf2-sha256${PasswordIterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}";
    }

    // true/false, or null when the text is not such a hash.
    public static bool? PasswordMatches(string password, string storedHash)
    {
        string[] parts = storedHash.Split('$');
        if (parts.Length != 4 || !string.Equals(parts[0], "otter-pbkdf2-sha256", StringComparison.OrdinalIgnoreCase)) return null;
        if (!int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.CurrentCulture, out int iterations) || iterations < 1000) return null;
        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException) { return null; }
        if (salt.Length < 8 || expected.Length != 32) return null;
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }

    const int SaltSize = 16, IvSize = 16, TagSize = 32, Iterations = 100000;

    // PBKDF2-HMACSHA256 gives 64 bytes: the AES key, then a separate HMAC key.
    static (byte[] EncryptionKey, byte[] MacKey) Keys(string key, byte[] salt)
    {
        byte[] material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(key), salt, Iterations, HashAlgorithmName.SHA256, 64);
        return (material[..32], material[32..]);
    }

    public static string Encrypt(string text, string key)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (encryptionKey, macKey) = Keys(key, salt);
        using Aes aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.GenerateIV();
        byte[] iv = aes.IV;
        byte[] cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), iv, PaddingMode.PKCS7);
        byte[] signed = [.. salt, .. iv, .. cipher];
        byte[] tag = HMACSHA256.HashData(macKey, signed);
        return Convert.ToBase64String([.. signed, .. tag]);
    }

    // One message for every failure, so none reveals what was wrong.
    const string CannotDecrypt = "I could not decrypt this - the key is wrong, or the data is corrupted.";

    public static string Decrypt(string cipherText, string key)
    {
        byte[] payload;
        try { payload = Convert.FromBase64String(cipherText); }
        catch (FormatException) { throw new CryptoProblem(CannotDecrypt); }
        int cipherStart = SaltSize + IvSize;
        if (payload.Length < cipherStart + TagSize) throw new CryptoProblem(CannotDecrypt);
        byte[] salt = payload[..SaltSize];
        byte[] iv = payload[SaltSize..cipherStart];
        int tagStart = payload.Length - TagSize;
        byte[] cipher = tagStart > cipherStart ? payload[cipherStart..tagStart] : [];
        byte[] tag = payload[tagStart..];

        var (encryptionKey, macKey) = Keys(key, salt);
        byte[] signed = [.. salt, .. iv, .. cipher];
        byte[] expected = HMACSHA256.HashData(macKey, signed);
        // The tag is checked before anything is decrypted, in constant time.
        if (!CryptographicOperations.FixedTimeEquals(tag, expected)) throw new CryptoProblem(CannotDecrypt);
        using Aes aes = Aes.Create();
        aes.Key = encryptionKey;
        try { return Encoding.UTF8.GetString(aes.DecryptCbc(cipher, iv, PaddingMode.PKCS7)); }
        catch (CryptographicException) { throw new CryptoProblem(CannotDecrypt); }
    }
}
