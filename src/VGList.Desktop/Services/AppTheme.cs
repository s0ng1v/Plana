using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using VGList.App.Settings;

namespace VGList.Desktop.Services;

/// <summary>运行期主题与字体预设切换：逐键写入 Application.Resources，DynamicResource 会即时刷新所有已打开窗口。</summary>
public static class AppTheme
{
    public const string Plana = "Plana";
    public const string ClassicBlue = "ClassicBlue";

    private const string PlanaUri = "avares://VGList.Widgets/Themes/PlanaTokens.axaml";
    private const string BlueUri = "avares://VGList.Widgets/Themes/BlueTokens.axaml";

    public static string Normalize(string? theme) =>
        string.Equals(theme, Plana, StringComparison.OrdinalIgnoreCase) ? Plana : ClassicBlue;

    public static void Apply(string? theme, string? fontPreset = null)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var uri = Normalize(theme) == ClassicBlue ? BlueUri : PlanaUri;
        if (AvaloniaXamlLoader.Load(new Uri(uri)) is not ResourceDictionary dictionary)
        {
            return;
        }

        foreach (var entry in dictionary)
        {
            app.Resources[entry.Key] = entry.Value;
        }

        if (fontPreset is not null)
        {
            ApplyFontPreset(fontPreset);
        }
    }

    private static readonly FontFamily UiAnthropic = new(
        "fonts:Anthropic#Anthropic Sans Web, fonts:NotoSansSC#Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

    private static readonly FontFamily DisplayAnthropicSerif = new(
        "fonts:Anthropic#Anthropic Serif Web, fonts:NotoSerifSC#Noto Serif SC, Songti SC, STSong, Georgia, Microsoft YaHei UI, Segoe UI Emoji, serif");

    private static readonly FontFamily DisplayAnthropicClassic = new(
        "fonts:Anthropic#Anthropic Serif Web, SimSun, STSong, fonts:NotoSerifSC#Noto Serif SC, Songti SC, Microsoft YaHei UI, Georgia, Segoe UI Emoji, serif");

    private static readonly FontFamily UiCleanSans = new(
        "fonts:NotoSansSC#Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

    private static readonly FontFamily MonoAnthropic = new(
        "fonts:Anthropic#Anthropic Mono Variable, Cascadia Mono, Menlo, Consolas, Courier New, fonts:NotoSansSC#Noto Sans SC, monospace");

    private static readonly FontFamily UiSystem = new(
        "PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");

    private static readonly FontFamily MonoSystem = new(
        "Cascadia Mono, Menlo, Consolas, Courier New, monospace");

    public static (FontFamily Ui, FontFamily Display, FontFamily Mono) ResolveFonts(string? fontPreset) =>
        FontPresetMode.Normalize(fontPreset) switch
        {
            FontPresetMode.AnthropicClassic => (UiAnthropic, DisplayAnthropicClassic, MonoAnthropic),
            FontPresetMode.CleanSans => (UiCleanSans, UiCleanSans, MonoAnthropic),
            FontPresetMode.System => (UiSystem, UiSystem, MonoSystem),
            _ => (UiAnthropic, DisplayAnthropicSerif, MonoAnthropic),
        };

    public static void ApplyFontPreset(string? fontPreset)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var (ui, display, mono) = ResolveFonts(fontPreset);
        app.Resources["Font.Ui"] = ui;
        app.Resources["Font.Display"] = display;
        app.Resources["Font.Mono"] = mono;
    }
}
