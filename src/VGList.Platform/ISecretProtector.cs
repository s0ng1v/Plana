namespace VGList.Platform;

/// <summary>本机敏感凭据加解密抽象（Windows 用 DPAPI，macOS 用本地密钥 + AES-GCM）。</summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    bool TryUnprotect(string cipherText, out string plainText);
}
