using System.Diagnostics;
using System.Text;
using VGList.Platform;

namespace VGList.Platform.Mac;

/// <summary>用 osascript 触发 macOS 通知中心。点击回调无法从脚本通知回传，因此事件为空实现。</summary>
public sealed class MacNotificationService : INotificationService
{
    public event Action<string?>? NotificationClicked
    {
        add { }
        remove { }
    }

    public bool Show(string title, string body, string? taskId = null)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var safeTitle = Escape(string.IsNullOrWhiteSpace(title) ? "Plana 提醒" : title, 80);
        var safeBody = Escape(string.IsNullOrWhiteSpace(body) ? safeTitle : body, 240);
        if (string.IsNullOrWhiteSpace(safeTitle) && string.IsNullOrWhiteSpace(safeBody))
        {
            return false;
        }

        try
        {
            var script = $"display notification \"{safeBody}\" with title \"{safeTitle}\"";
            var startInfo = new ProcessStartInfo("/usr/bin/osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(script);
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
    }

    private static string Escape(string value, int maxLength)
    {
        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var ch in value)
        {
            if (builder.Length >= maxLength)
            {
                break;
            }

            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(ch);
                    break;
            }
        }

        return builder.ToString();
    }
}
