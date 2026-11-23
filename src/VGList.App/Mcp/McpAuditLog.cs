using System.Text;
using System.Text.Json;

namespace VGList.App.Mcp;

/// <summary>
/// A7 外部 AI 访问审计日志（%LOCALAPPDATA%\VGList\mcp\audit.jsonl）：
/// - 仅记录时间、客户端名、工具、条数、结果与变更集 Id，绝不写入任何任务标题、正文或密钥；
/// - 文件超过 1 MB 自动滚动裁剪旧记录，UI 展示最近 50 条。
/// </summary>
public sealed class McpAuditLog
{
    public const long MaxFileBytes = 1024 * 1024;
    private const int KeepLinesOnRotate = 200;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();

    public McpAuditLog(string? mcpDirectory = null)
    {
        McpDirectory = string.IsNullOrWhiteSpace(mcpDirectory) ? DefaultMcpDirectory : mcpDirectory;
        FilePath = Path.Combine(McpDirectory, "audit.jsonl");
    }

    public static string DefaultMcpDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VGList",
        "mcp");

    public string McpDirectory { get; }
    public string FilePath { get; }

    public void Append(McpAuditEntry entry)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(McpDirectory);
                var line = JsonSerializer.Serialize(entry, JsonOpts);
                File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);

                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxFileBytes)
                {
                    RotateLocked();
                }
            }
            catch
            {
                // 审计写盘失败不阻断工具响应
            }
        }
    }

    public IReadOnlyList<McpAuditEntry> LoadRecent(int maxCount = 50)
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return [];
            }

            try
            {
                var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                var list = new List<McpAuditEntry>(Math.Min(lines.Length, maxCount));
                for (var i = lines.Length - 1; i >= 0 && list.Count < maxCount; i--)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }

                    try
                    {
                        var parsed = JsonSerializer.Deserialize<McpAuditEntry>(line, JsonOpts);
                        if (parsed is not null)
                        {
                            list.Add(parsed);
                        }
                    }
                    catch
                    {
                    }
                }

                return list;
            }
            catch
            {
                return [];
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch
            {
            }
        }
    }

    private void RotateLocked()
    {
        var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
        var keep = lines.Length > KeepLinesOnRotate
            ? lines[^KeepLinesOnRotate..]
            : lines;
        var tmp = FilePath + ".tmp";
        File.WriteAllLines(tmp, keep, Encoding.UTF8);
        File.Move(tmp, FilePath, overwrite: true);
    }
}
