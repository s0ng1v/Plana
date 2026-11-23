using Microsoft.Win32;

namespace VGList.Platform.Win.Win32;

/// <summary>
/// Windows 当前用户级开机自启动管理器（HKCU\...\Run，无需管理员 UAC 提权）。
/// </summary>
public static class WindowsStartupManager
{
    public const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultAppName = "Plana";

    public static string BuildCommand(string exePath, bool startMinimized)
    {
        var quoted = $"\"{exePath.Trim()}\"";
        return startMinimized ? $"{quoted} --minimized" : quoted;
    }

    public static bool IsEnabled(string appName = DefaultAppName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            var value = key?.GetValue(appName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static bool TrySetEnabled(
        bool enabled,
        bool startMinimized,
        string? exePath = null,
        string appName = DefaultAppName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                if (key.GetValue(appName) is not null)
                {
                    key.DeleteValue(appName, throwOnMissingValue: false);
                }

                return true;
            }

            var resolvedPath = string.IsNullOrWhiteSpace(exePath)
                ? Environment.ProcessPath
                : exePath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                return false;
            }

            key.SetValue(appName, BuildCommand(resolvedPath, startMinimized), RegistryValueKind.String);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
