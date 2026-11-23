#if WINDOWS
using VGList.Platform;
using VGList.Platform.Win;

namespace VGList.Desktop.Services;

/// <summary>将 Windows DPAPI 适配为统一的 ISecretProtector 抽象。</summary>
public sealed class WindowsSecretProtector : ISecretProtector
{
    public string Protect(string plainText) => DpapiSecretProtector.Protect(plainText);

    public bool TryUnprotect(string cipherText, out string plainText) =>
        DpapiSecretProtector.TryUnprotect(cipherText, out plainText);
}
#endif
