using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace VGList.Widgets.Themes;

/// <summary>
/// 按嵌入式字体资源名加载的轻量 FontCollectionBase（ADR-0071）：
/// 只注册指定的字体文件，避免不同汉字回退字体（Noto Sans SC / Noto Serif SC）混在同一集合中时，
/// 因西文字体缺字引发集合内遍历而误命中另一种中文字体。
/// </summary>
public abstract class BundledFontCollection : FontCollectionBase
{
    public override Uri Key { get; }

    protected BundledFontCollection(Uri key, params string[] fontAssetNames)
    {
        Key = key;
        foreach (var fileName in fontAssetNames)
        {
            using var stream = AssetLoader.Open(new Uri($"avares://VGList.Widgets/Assets/Fonts/{fileName}", UriKind.Absolute));
            TryAddGlyphTypeface(stream, out _);
        }
    }
}

/// <summary>
/// 内嵌应用字体集合（ADR-0050 / ADR-0063 / ADR-0071）：
/// 仅加载 AnthropicSerifWebText / AnthropicSansWebText / AnthropicMonoVariable，
/// 与中文思源字体集合（NotoSansSCFontCollection / NotoSerifSCFontCollection）严格隔离，
/// 使 FontFamily 链表中的显式集合 URI（fonts:NotoSansSC#... / fonts:NotoSerifSC#...）能在未安装系统思源的机器上精确生效，
/// 同时保留系统字体与彩色 Emoji（Segoe UI Emoji）尾部回退。
/// </summary>
public sealed class AnthropicFontCollection : BundledFontCollection
{
    public static readonly Uri CollectionKey = new("fonts:Anthropic", UriKind.Absolute);
    public static readonly Uri NotoSansSCKey = new("fonts:NotoSansSC", UriKind.Absolute);
    public static readonly Uri NotoSerifSCKey = new("fonts:NotoSerifSC", UriKind.Absolute);
    public static readonly Uri AssetsUri = new("avares://VGList.Widgets/Assets/Fonts", UriKind.Absolute);

    public AnthropicFontCollection()
        : base(
            CollectionKey,
            "AnthropicSerifWebText.ttf",
            "AnthropicSansWebText.ttf",
            "AnthropicMonoVariable.ttf")
    {
    }

    /// <summary>
    /// 向 Avalonia FontManager 注册全部三个隔离的内嵌字体集合。
    /// </summary>
    public static void RegisterAll(FontManager fontManager)
    {
        fontManager.AddFontCollection(new AnthropicFontCollection());
        fontManager.AddFontCollection(new NotoSansSCFontCollection());
        fontManager.AddFontCollection(new NotoSerifSCFontCollection());
    }

    public static IReadOnlyDictionary<string, FontFamily> CreateMappings() =>
        new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase)
        {
            ["Anthropic Serif"] = new FontFamily("fonts:Anthropic#Anthropic Serif Web"),
            ["Anthropic Serif Web"] = new FontFamily("fonts:Anthropic#Anthropic Serif Web"),
            // 字体文件内 name 表同时声明了排版家族名（ID16）与旧家族名（ID1，带 Text 后缀），
            // 两个名字都指向嵌入集合，避免不同平台按不同 name 记录解析时落到系统回退。
            ["Anthropic Serif Web Text"] = new FontFamily("fonts:Anthropic#Anthropic Serif Web"),
            ["Anthropic Sans"] = new FontFamily("fonts:Anthropic#Anthropic Sans Web"),
            ["Anthropic Sans Web"] = new FontFamily("fonts:Anthropic#Anthropic Sans Web"),
            ["Anthropic Sans Web Text"] = new FontFamily("fonts:Anthropic#Anthropic Sans Web"),
            ["Anthropic Mono"] = new FontFamily("fonts:Anthropic#Anthropic Mono Variable"),
            ["Anthropic Mono Variable"] = new FontFamily("fonts:Anthropic#Anthropic Mono Variable"),
            // 中文思源字体隔离于各自集合（ADR-0071），单独按名查找时也指向正确集合
            ["Noto Sans SC"] = new FontFamily("fonts:NotoSansSC#Noto Sans SC"),
            ["Noto Serif SC"] = new FontFamily("fonts:NotoSerifSC#Noto Serif SC"),
        };
}

/// <summary>
/// 独立内嵌思源黑体集合（字重轴重定基 400..900，见 ADR-0067 / ADR-0071）。
/// </summary>
public sealed class NotoSansSCFontCollection : BundledFontCollection
{
    public NotoSansSCFontCollection()
        : base(AnthropicFontCollection.NotoSansSCKey, "NotoSansSC-VF.ttf")
    {
    }
}

/// <summary>
/// 独立内嵌思源宋体集合（字重轴重定基 400..900，见 ADR-0067 / ADR-0071）。
/// </summary>
public sealed class NotoSerifSCFontCollection : BundledFontCollection
{
    public NotoSerifSCFontCollection()
        : base(AnthropicFontCollection.NotoSerifSCKey, "NotoSerifSC-VF.ttf")
    {
    }
}
