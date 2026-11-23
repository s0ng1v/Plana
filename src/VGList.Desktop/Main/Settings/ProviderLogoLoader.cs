using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VGList.Desktop.Main.Settings;

/// <summary>
/// 厂商真实官方 Logo 加载器：从内嵌资源 Assets/Providers/{id}.png 缓存加载；
/// 单元测试无 Avalonia 运行时或自定义服务无图标时静默返回 null，由 UI 自动回退首字母徽标。
/// </summary>
public static class ProviderLogoLoader
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? TryLoad(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) || providerId == "__custom__")
        {
            return null;
        }

        return Cache.GetOrAdd(providerId, static id =>
        {
            try
            {
                var uri = new Uri($"avares://VGList/Assets/Providers/{id}.png");
                if (!AssetLoader.Exists(uri))
                {
                    return null;
                }

                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        });
    }
}
