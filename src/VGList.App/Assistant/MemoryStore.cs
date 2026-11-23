using System.Text;
using System.Text.RegularExpressions;

namespace VGList.App.Assistant;

public sealed record MemoryItem(
    string Id,
    string Category,
    string Content,
    string Source,
    DateOnly UpdatedAt,
    bool Pinned = false);

/// <summary>
/// 本地 Markdown 记忆库（%LOCALAPPDATA%\VGList\assistant\memories.md）：
/// - 人类可直接用记事本/VSCode 阅改；容错解析手工修改/乱序/缺字段行；
/// - 内置 SensitiveInfoFilter 拒绝敏感信息；上限 200 条；
/// - 零向量模型本地相关度召回（字符二元 n-gram 重叠 + 近期加权 + 置顶优先，最多 10 条 / ≤ 1500 字）。
/// </summary>
public sealed class MemoryStore
{
    public const int MaxMemories = 200;
    public const int MaxInjectedItems = 10;
    public const int MaxInjectedChars = 1500;

    private static readonly Regex StructuredLineRegex = new(
        @"^-\s+(?:\[(?<id>[a-zA-Z0-9_-]+)\]\s+)?(?:\((?<meta>[^)]+)\)\s+)?(?<content>.+)$",
        RegexOptions.Compiled);

    public MemoryStore(string? directory = null)
    {
        AssistantDirectory = string.IsNullOrWhiteSpace(directory) ? RulesStore.DefaultAssistantDirectory : directory;
        FilePath = Path.Combine(AssistantDirectory, "memories.md");
    }

    public string AssistantDirectory { get; }
    public string FilePath { get; }

    public IReadOnlyList<MemoryItem> LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        var items = new List<MemoryItem>();
        foreach (var rawLine in File.ReadAllLines(FilePath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var m = StructuredLineRegex.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var content = m.Groups["content"].Value.Trim();
            if (string.IsNullOrEmpty(content))
            {
                continue;
            }

            var id = m.Groups["id"].Success ? m.Groups["id"].Value.Trim() : $"m{Math.Abs(content.GetHashCode()):x8}";
            var category = "偏好";
            var source = "手动";
            var updated = DateOnly.FromDateTime(DateTime.Today);
            var pinned = false;

            if (m.Groups["meta"].Success)
            {
                var parts = m.Groups["meta"].Value.Split('|', StringSplitOptions.TrimEntries);
                if (parts.Length >= 1 && !string.IsNullOrEmpty(parts[0]))
                {
                    if (parts[0].StartsWith("★", StringComparison.Ordinal))
                    {
                        pinned = true;
                        category = parts[0].TrimStart('★').Trim();
                    }
                    else
                    {
                        category = parts[0];
                    }
                }

                if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
                {
                    source = parts[1];
                }

                if (parts.Length >= 3 && DateOnly.TryParse(parts[2], out var d))
                {
                    updated = d;
                }
            }

            items.Add(new MemoryItem(id, category, content, source, updated, pinned));
        }

        return items;
    }

    public (bool Success, MemoryItem? Item, string? Error) Add(
        string content,
        string category = "偏好",
        string source = "对话",
        bool pinned = false)
    {
        var clean = (content ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return (false, null, "记忆内容不能为空");
        }

        if (SensitiveInfoFilter.ContainsSensitiveInfo(clean, out var kind))
        {
            return (false, null, $"检测到敏感信息（{kind}），出于隐私安全不会写入记忆。");
        }

        var list = LoadAll().ToList();
        if (list.Count >= MaxMemories)
        {
            return (false, null, $"记忆条数已达上限（{MaxMemories} 条），请前往「设置 → 助理与记忆」清理旧条目。");
        }

        // 避免重复记住高度相同内容
        var dup = list.FirstOrDefault(x => string.Equals(x.Content, clean, StringComparison.OrdinalIgnoreCase));
        if (dup is not null)
        {
            return (true, dup, null);
        }

        var item = new MemoryItem(
            Id: "m" + Guid.NewGuid().ToString("N")[..8],
            Category: NormalizeCategory(category),
            Content: clean,
            Source: string.IsNullOrWhiteSpace(source) ? "对话" : source.Trim(),
            UpdatedAt: DateOnly.FromDateTime(DateTime.Today),
            Pinned: pinned);

        list.Insert(0, item);
        SaveAll(list);
        return (true, item, null);
    }

    public (bool Success, MemoryItem? Item, string? Error) Update(string id, string newContent, string? newCategory = null)
    {
        var clean = (newContent ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(clean))
        {
            return (false, null, "更新内容不能为空");
        }

        if (SensitiveInfoFilter.ContainsSensitiveInfo(clean, out var kind))
        {
            return (false, null, $"检测到敏感信息（{kind}），已拒绝更新。");
        }

        var list = LoadAll().ToList();
        var idx = list.FindIndex(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            return (false, null, $"未找到记忆条目 {id}");
        }

        var updated = list[idx] with
        {
            Content = clean,
            Category = string.IsNullOrWhiteSpace(newCategory) ? list[idx].Category : NormalizeCategory(newCategory),
            UpdatedAt = DateOnly.FromDateTime(DateTime.Today),
        };
        list[idx] = updated;
        SaveAll(list);
        return (true, updated, null);
    }

    public bool Remove(string id)
    {
        var list = LoadAll().ToList();
        var removed = list.RemoveAll(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            SaveAll(list);
        }

        return removed;
    }

    public void ClearAll()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }

    public IReadOnlyList<MemoryItem> QueryRelevant(string query, DateOnly today, int limit = MaxInjectedItems)
    {
        var all = LoadAll();
        if (all.Count == 0)
        {
            return [];
        }

        var queryBigrams = ExtractBigrams(query);
        var scored = all.Select(m =>
        {
            var memBigrams = ExtractBigrams(m.Content + " " + m.Category);
            var overlap = queryBigrams.Count == 0 ? 0 : queryBigrams.Intersect(memBigrams).Count();
            var ageDays = Math.Max(0, today.DayNumber - m.UpdatedAt.DayNumber);
            var recencyScore = 1.0 / (1.0 + ageDays / 30.0);
            var score = (m.Pinned ? 100.0 : 0.0) + overlap * 3.0 + recencyScore;
            return (Item: m, Score: score);
        })
        .OrderByDescending(x => x.Score)
        .Take(Math.Max(1, limit))
        .Select(x => x.Item)
        .ToList();

        return scored;
    }

    private void SaveAll(IReadOnlyList<MemoryItem> items)
    {
        Directory.CreateDirectory(AssistantDirectory);
        var sb = new StringBuilder();
        sb.AppendLine("# Plana 助理记忆");
        sb.AppendLine();
        sb.AppendLine("> 本文件保存在你的本机，可随时用任何文本编辑器直接增删改。");
        sb.AppendLine();
        foreach (var m in items)
        {
            var cat = m.Pinned ? $"★{m.Category}" : m.Category;
            sb.AppendLine($"- [{m.Id}] ({cat} | {m.Source} | {m.UpdatedAt:yyyy-MM-dd}) {m.Content}");
        }

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
        File.Move(tmp, FilePath, overwrite: true);
    }

    private static string NormalizeCategory(string? raw) => (raw ?? string.Empty).Trim() switch
    {
        "习惯" => "习惯",
        "目标" => "目标",
        "事实" => "事实",
        _ => "偏好",
    };

    private static HashSet<string> ExtractBigrams(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return set;
        }

        var cleaned = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                cleaned.Append(char.ToLowerInvariant(ch));
            }
        }

        var s = cleaned.ToString();
        for (var i = 0; i < s.Length - 1; i++)
        {
            set.Add(s.Substring(i, 2));
        }

        return set;
    }
}

/// <summary>向系统提示按本地相关度注入最多 10 条记忆（≤ 1500 字，关闭开关时不注入）。</summary>
public sealed class MemoryContextContributor(MemoryStore store, Func<bool> isEnabled) : IContextContributor
{
    public string SectionName => "关于用户的长期记忆";

    public string? BuildContext(string userMessage, DateOnly today)
    {
        if (!isEnabled())
        {
            return null;
        }

        var relevant = store.QueryRelevant(userMessage, today);
        if (relevant.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var m in relevant)
        {
            var line = $"- [{m.Id}][{m.Category}] {m.Content}";
            if (sb.Length + line.Length + 2 > MemoryStore.MaxInjectedChars)
            {
                break;
            }

            sb.AppendLine(line);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
