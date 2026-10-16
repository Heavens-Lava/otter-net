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
