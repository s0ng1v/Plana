using System.Security.Cryptography;
using System.Text;
using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>
/// macOS 本地凭据保护：32 字节随机密钥保存在
/// ~/Library/Application Support/VGList/secret.key（仅当前用户可读），
/// 使用 AES-GCM 加密，避免把 API Key 明文写进 models.json。
/// </summary>
public sealed class MacSecretProtector : ISecretProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly Lazy<byte[]> Key = new(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Protect(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(Key.Value, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[1 + NonceSize + TagSize + cipher.Length];
        payload[0] = FormatVersion;
        Buffer.BlockCopy(nonce, 0, payload, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, 1 + NonceSize + TagSize, cipher.Length);
        return Convert.ToBase64String(payload);
    }

    public bool TryUnprotect(string cipherText, out string plainText)
    {
        plainText = string.Empty;
        if (string.IsNullOrWhiteSpace(cipherText))
        {
            return false;
        }

        try
        {
            var payload = Convert.FromBase64String(cipherText);
            if (payload.Length < 1 + NonceSize + TagSize || payload[0] != FormatVersion)
            {
                return false;
            }

            var nonce = payload.AsSpan(1, NonceSize);
            var tag = payload.AsSpan(1 + NonceSize, TagSize);
            var cipher = payload.AsSpan(1 + NonceSize + TagSize);
            var plainBytes = new byte[cipher.Length];

            using var aes = new AesGcm(Key.Value, TagSize);
            aes.Decrypt(nonce, cipher, tag, plainBytes);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] LoadOrCreateKey()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Application Support",
            "VGList");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "secret.key");
        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == KeySize)
                {
                    return existing;
                }
            }
        }
        catch
        {
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(path, key);
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }

        return key;
    }
}
