using System.Security.Cryptography;
using System.Text;

namespace MaarifPlatform.Infrastructure.Configuration;

public sealed class SettingsSecretProtector
{
    private const string Prefix = "enc:v1:";
    private readonly byte[] _key;

    public SettingsSecretProtector(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32) throw new InvalidOperationException("Security:SettingsEncryptionKey must be a base64-encoded 32-byte key.");
    }

    public static bool IsSecret(string key) => key.EndsWith(":ApiKey", StringComparison.OrdinalIgnoreCase);
    public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string key, string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, cipher, tag, Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        CryptographicOperations.ZeroMemory(plain);
        return Prefix + Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Unprotect(string key, string value)
    {
        var bytes = Convert.FromBase64String(value[Prefix.Length..]);
        if (bytes.Length < 28) throw new CryptographicException("Invalid encrypted setting.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain, Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        var result = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return result;
    }
}
