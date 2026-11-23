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

    public static (FontFamily Ui, FontFamily Display, FontFamily Mono) ResolveFonts(string? fontPreset) =>
        FontPresetMode.Normalize(fontPreset) switch
        {
            FontPresetMode.AnthropicSerif => (
                new FontFamily("Anthropic Sans, Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Anthropic Serif, Noto Serif SC, Songti SC, STSong, Georgia, Microsoft YaHei UI, serif"),
                new FontFamily("Anthropic Mono, Cascadia Mono, Menlo, Consolas, Courier New, monospace")),
            FontPresetMode.AnthropicClassic => (
                new FontFamily("Anthropic Sans, Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Anthropic Serif, SimSun, STSong, Noto Serif SC, Songti SC, STSong, Georgia, serif"),
                new FontFamily("Anthropic Mono, Cascadia Mono, Menlo, Consolas, Courier New, monospace")),
            FontPresetMode.CleanSans => (
                new FontFamily("Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Anthropic Mono, Cascadia Mono, Menlo, Consolas, Courier New, monospace")),
            FontPresetMode.System => (
                new FontFamily("PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Cascadia Mono, Menlo, Consolas, Courier New, monospace")),
            _ => (
                new FontFamily("Anthropic Sans, Noto Sans SC, PingFang SC, Hiragino Sans GB, Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI"),
                new FontFamily("Anthropic Serif, Noto Serif SC, Songti SC, STSong, Georgia, Microsoft YaHei UI, serif"),
                new FontFamily("Anthropic Mono, Cascadia Mono, Menlo, Consolas, Courier New, monospace")),
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
