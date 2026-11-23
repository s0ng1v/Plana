using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace VGList.Widgets.Themes;

/// <summary>
/// 内嵌应用字体集合（ADR-0050 / ADR-0063）：
/// 加载随包打包的 AnthropicSerifWebText / AnthropicSansWebText / AnthropicMonoVariable，
/// 以及中文思源 NotoSansSC / NotoSerifSC（可变字体，字重轴已重定基为 400..900、默认 400，见 ADR-0067），
/// 配合 FontManagerOptions.FontFamilyMappings 让 XAML 中的 "Anthropic Serif" / "Anthropic Sans" / "Anthropic Mono"
/// / "Noto Sans SC" / "Noto Serif SC" 自动解析到真实内嵌字体，
/// 保证未安装系统字体的机器上中文渲染一致（不再回退微软雅黑），
/// 同时保留完整系统回退链（如中文 SimSun 与彩色 Emoji）。
/// </summary>
public sealed class AnthropicFontCollection : EmbeddedFontCollection
{
    public static readonly Uri CollectionKey = new("fonts:Anthropic", UriKind.Absolute);
    public static readonly Uri AssetsUri = new("avares://VGList.Widgets/Assets/Fonts", UriKind.Absolute);

    public AnthropicFontCollection()
        : base(CollectionKey, AssetsUri)
    {
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
            // 中文思源字体同样内嵌（ADR-0063）：未安装思源的机器不再回退微软雅黑
            ["Noto Sans SC"] = new FontFamily("fonts:Anthropic#Noto Sans SC"),
            ["Noto Serif SC"] = new FontFamily("fonts:Anthropic#Noto Serif SC"),
        };
}
