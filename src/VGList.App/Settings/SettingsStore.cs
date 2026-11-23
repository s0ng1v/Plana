using System.Text.Json;
using System.Text.Json.Serialization;

namespace VGList.App.Settings;

/// <summary>
/// 极简设置存储：%LOCALAPPDATA%\VGList\settings.json。
/// 读写失败一律回退到默认值，不弹窗、不阻塞启动。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VGList",
            "settings.json");
    }

    public string FilePath => _path;

    public WidgetSettings LoadWidget() => LoadFile().Widget ?? new WidgetSettings();

    public WidgetSettings LoadMonth() => LoadFile().Month ?? new WidgetSettings();

    public MainSettings LoadMain() => LoadFile().Main ?? new MainSettings();

    public void SaveWidget(WidgetSettings settings)
    {
        var file = LoadFile();
        file.Widget = settings;
        SaveFile(file);
    }

    public void SaveMonth(WidgetSettings settings)
    {
        var file = LoadFile();
        file.Month = settings;
        SaveFile(file);
    }

    public void SaveMain(MainSettings settings)
    {
        var file = LoadFile();
        file.Main = settings;
        SaveFile(file);
    }

    private SettingsFile LoadFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SettingsFile();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SettingsFile>(json, Options) ?? new SettingsFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SettingsFile();
        }
    }

    private void SaveFile(SettingsFile file)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(file, Options);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // P0 允许丢失设置，不打断用户操作。
        }
    }

    private sealed class SettingsFile
    {
        public WidgetSettings? Widget { get; set; }

        public WidgetSettings? Month { get; set; }

        public MainSettings? Main { get; set; }
    }
}
