using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VGList.App.Assistant;

/// <summary>结构化用户画像字段（设置 UI 展示与编辑不含原始 Markdown 符号，磁盘仍保存可读 profile.md）。</summary>
public sealed record UserProfileFields(
    string Nickname,
    string SleepAndRoutine,
    string FrequentActivities,
    string PeakHours,
    string ProcrastinationTypes,
    string OtherNotes);

/// <summary>
/// 本地可读用户画像（%LOCALAPPDATA%\VGList\assistant\profile.md）：
/// - 固定 6 个可读可改小节（我的称呼 / 作息习惯 / 常做的事 / 高效时段 / 容易拖延的类型 / 其他）；
/// - 小节级手改保护：自动更新画像时若检测到某小节被用户手工修改过，绝不覆盖，改为追加至「## 助理更新建议」；
/// - 每次更新前保留最近 10 版历史快照，支持一键「恢复上一版」；
/// - 记录「是否已首次询问称呼」，确保只在刚认识时温柔询问一次，绝不每轮追问。
/// </summary>
public sealed class UserProfileStore
{
    public const int MaxHistoryVersions = 10;
    public const int MaxInjectedChars = 1200;

    public const string SectionNickname = "我的称呼";
    public const string SectionRoutine = "作息习惯";
    public const string SectionActivities = "常做的事";
    public const string SectionPeakHours = "高效时段";
    public const string SectionProcrastination = "容易拖延的类型";
    public const string SectionOther = "其他";

    public static readonly string[] StandardSections =
    [
        SectionNickname,
        SectionRoutine,
        SectionActivities,
        SectionPeakHours,
        SectionProcrastination,
        SectionOther,
    ];

    public UserProfileStore(string? directory = null)
    {
        AssistantDirectory = string.IsNullOrWhiteSpace(directory) ? RulesStore.DefaultAssistantDirectory : directory;
        FilePath = Path.Combine(AssistantDirectory, "profile.md");
        MetaPath = Path.Combine(AssistantDirectory, "profile.meta.json");
        HistoryDirectory = Path.Combine(AssistantDirectory, "profile-history");
    }

    public string AssistantDirectory { get; }
    public string FilePath { get; }
    public string MetaPath { get; }
    public string HistoryDirectory { get; }

    public string LoadMarkdown()
    {
        if (!File.Exists(FilePath))
        {
            return BuildDefaultTemplate();
        }

        try
        {
            return File.ReadAllText(FilePath, Encoding.UTF8);
        }
        catch
        {
            return BuildDefaultTemplate();
        }
    }

    /// <summary>读取结构化画像字段（剥离 Markdown 标题和默认占位提示）。</summary>
    public UserProfileFields LoadFields()
    {
        var sections = ParseSections(LoadMarkdown());
        return new UserProfileFields(
            Nickname: sections.GetValueOrDefault(SectionNickname, string.Empty),
            SleepAndRoutine: sections.GetValueOrDefault(SectionRoutine, string.Empty),
            FrequentActivities: sections.GetValueOrDefault(SectionActivities, string.Empty),
            PeakHours: sections.GetValueOrDefault(SectionPeakHours, string.Empty),
            ProcrastinationTypes: sections.GetValueOrDefault(SectionProcrastination, string.Empty),
            OtherNotes: sections.GetValueOrDefault(SectionOther, string.Empty));
    }

    /// <summary>读取用户称呼（未设置时返回空串）。</summary>
    public string GetNickname()
    {
        if (!File.Exists(FilePath))
        {
            return string.Empty;
        }

        return ParseSections(LoadMarkdown()).GetValueOrDefault(SectionNickname, string.Empty).Trim();
    }

    /// <summary>更新用户称呼（同时标记不再重复询问，并保留其他小节原样）。</summary>
    public void SetNickname(string nickname)
    {
        var clean = (nickname ?? string.Empty).Trim();
        var fields = LoadFields() with { Nickname = clean };
        SaveUserEditedFields(fields);

        if (!string.IsNullOrEmpty(clean))
        {
            MarkNicknameAsked();
        }
    }

    /// <summary>用户通过设置页结构化表单保存画像（不覆盖基准哈希以保护手改小节）。</summary>
    public void SaveUserEditedFields(UserProfileFields fields)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SectionNickname] = fields.Nickname?.Trim() ?? string.Empty,
            [SectionRoutine] = fields.SleepAndRoutine?.Trim() ?? string.Empty,
            [SectionActivities] = fields.FrequentActivities?.Trim() ?? string.Empty,
            [SectionPeakHours] = fields.PeakHours?.Trim() ?? string.Empty,
            [SectionProcrastination] = fields.ProcrastinationTypes?.Trim() ?? string.Empty,
            [SectionOther] = fields.OtherNotes?.Trim() ?? string.Empty,
        };

        var md = RenderMarkdown(dict, protectedSuggestions: null, updatedAt: DateTime.Now);
        SaveUserEdited(md);

        if (!string.IsNullOrWhiteSpace(fields.Nickname))
        {
            MarkNicknameAsked();
        }
    }

    public bool HasAskedNickname() => LoadMeta().NicknameAsked;

    public void MarkNicknameAsked()
    {
        Directory.CreateDirectory(AssistantDirectory);
        var meta = LoadMeta();
        if (meta.NicknameAsked)
        {
            return;
        }

        meta.NicknameAsked = true;
        SaveMeta(meta);
    }

    /// <summary>用户手动保存画像 Markdown：不覆写系统记录基准哈希，使后续自动更新自动识别并保护手改小节。</summary>
    public void SaveUserEdited(string markdown)
    {
        Directory.CreateDirectory(AssistantDirectory);
        BackupCurrentVersion();
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, markdown ?? string.Empty, Encoding.UTF8);
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>
    /// 自动更新画像（保护所有被用户手工修改过的小节，不被覆盖；改放末尾「助理更新建议」）。
    /// </summary>
    public string ApplyAutoUpdate(IReadOnlyDictionary<string, string> proposedSections)
    {
        Directory.CreateDirectory(AssistantDirectory);
        var current = LoadMarkdown();
        BackupCurrentVersion();

        var existingSections = ParseSections(current);
        var meta = LoadMeta();
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        var protectedSuggestions = new List<string>();

        foreach (var sec in StandardSections)
        {
            existingSections.TryGetValue(sec, out var curBody);
            curBody ??= string.Empty;

            if (!proposedSections.TryGetValue(sec, out var newBody) || string.IsNullOrWhiteSpace(newBody))
            {
                merged[sec] = curBody;
                continue;
            }

            var isHandEdited = !string.IsNullOrWhiteSpace(curBody) &&
                (!meta.Hashes.TryGetValue(sec, out var savedHash) || ComputeHash(curBody) != savedHash);

            if (isHandEdited)
            {
                merged[sec] = curBody;
                protectedSuggestions.Add($"- **{sec}**：{newBody.Trim()}");
            }
            else
            {
                merged[sec] = newBody.Trim();
                meta.Hashes[sec] = ComputeHash(merged[sec]);
            }
        }

        var output = RenderMarkdown(merged, protectedSuggestions, DateTime.Now);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, output, Encoding.UTF8);
        File.Move(tmp, FilePath, overwrite: true);
        SaveMeta(meta);
        return output;
    }

    public IReadOnlyList<string> ListHistoryFiles()
    {
        if (!Directory.Exists(HistoryDirectory))
        {
            return [];
        }

        return Directory.GetFiles(HistoryDirectory, "*.md")
            .OrderByDescending(Path.GetFileName)
            .ToList();
    }

    public bool RestorePreviousVersion()
    {
        var versions = ListHistoryFiles();
        if (versions.Count == 0)
        {
            return false;
        }

        var latest = versions[0];
        var content = File.ReadAllText(latest, Encoding.UTF8);
        File.Delete(latest);
        Directory.CreateDirectory(AssistantDirectory);
        File.WriteAllText(FilePath, content, Encoding.UTF8);
        return true;
    }

    public void ClearAll(bool includeHistory = true)
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
        if (File.Exists(MetaPath)) File.Delete(MetaPath);
        if (includeHistory && Directory.Exists(HistoryDirectory))
        {
            Directory.Delete(HistoryDirectory, recursive: true);
        }
    }

    private void BackupCurrentVersion()
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        Directory.CreateDirectory(HistoryDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        File.Copy(FilePath, Path.Combine(HistoryDirectory, $"{stamp}.md"), overwrite: true);

        var files = ListHistoryFiles();
        for (var i = MaxHistoryVersions; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch
            {
            }
        }
    }

    private ProfileMeta LoadMeta()
    {
        if (!File.Exists(MetaPath))
        {
            return new ProfileMeta();
        }

        try
        {
            var raw = File.ReadAllText(MetaPath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("hashes", out _))
            {
                return JsonSerializer.Deserialize<ProfileMeta>(raw) ?? new ProfileMeta();
            }

            // 兼容旧版纯 Dictionary<string, string> 格式
            var legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(raw)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);
            return new ProfileMeta { Hashes = legacy };
        }
        catch
        {
            return new ProfileMeta();
        }
    }

    private void SaveMeta(ProfileMeta meta)
    {
        Directory.CreateDirectory(AssistantDirectory);
        File.WriteAllText(MetaPath, JsonSerializer.Serialize(meta), Encoding.UTF8);
    }

    public static Dictionary<string, string> ParseSections(string markdown)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        string? currentHeading = null;
        var buf = new StringBuilder();

        void Flush()
        {
            if (currentHeading is not null)
            {
                var body = buf.ToString().Trim();
                if (body.StartsWith("（暂无记录", StringComparison.Ordinal))
                {
                    body = string.Empty;
                }

                dict[currentHeading] = body;
            }

            buf.Clear();
        }

        foreach (var rawLine in (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.StartsWith("## ", StringComparison.Ordinal))
            {
                Flush();
                currentHeading = rawLine[3..].Trim();
            }
            else if (currentHeading is not null)
            {
                buf.AppendLine(rawLine);
            }
        }

        Flush();
        return dict;
    }

    private static string RenderMarkdown(
        IReadOnlyDictionary<string, string> sections,
        List<string>? protectedSuggestions,
        DateTime updatedAt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 我的用户画像");
        sb.AppendLine();
        sb.AppendLine($"> 更新于 {updatedAt:yyyy-MM-dd HH:mm} · 本机可读可编辑；你手改过的小节不会被自动覆盖。");
        sb.AppendLine();

        foreach (var sec in StandardSections)
        {
            sb.AppendLine($"## {sec}");
            var body = sections.GetValueOrDefault(sec);
            sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "（暂无记录，可自行补充或随使用自动归纳）" : body);
            sb.AppendLine();
        }

        if (protectedSuggestions is { Count: > 0 })
        {
            sb.AppendLine("## 助理更新建议");
            foreach (var s in protectedSuggestions)
            {
                sb.AppendLine(s);
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string BuildDefaultTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 我的用户画像");
        sb.AppendLine();
        sb.AppendLine("> 本机 Markdown 画像文件，可直接编辑；你手改过的小节在自动更新时会被保护。");
        sb.AppendLine();
        foreach (var sec in StandardSections)
        {
            sb.AppendLine($"## {sec}");
            sb.AppendLine("（暂无记录，可自行补充或随使用自动归纳）");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim()));
        return Convert.ToHexString(bytes)[..16];
    }

    private sealed class ProfileMeta
    {
        [System.Text.Json.Serialization.JsonPropertyName("hashes")]
        public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.Ordinal);

        [System.Text.Json.Serialization.JsonPropertyName("nicknameAsked")]
        public bool NicknameAsked { get; set; }
    }
}

/// <summary>向系统提示注入「我的画像」与首次称呼引导（≤ 1200 字，绝不每轮重复追问称呼）。</summary>
public sealed class ProfileContextContributor(UserProfileStore store, Func<bool> isEnabled) : IContextContributor
{
    public string SectionName => "用户画像与称呼";

    public string? BuildContext(string userMessage, DateOnly today)
    {
        var sb = new StringBuilder();
        var nickname = store.GetNickname();

        if (!string.IsNullOrWhiteSpace(nickname))
        {
            sb.AppendLine($"- 用户的称呼：「{nickname}」（请在日常回复中自然、亲切地用这个称呼和对方聊天，不用每句都刻意重复）");
        }
        else if (!store.HasAskedNickname())
        {
            // 只触发这一次，立即落盘标记，后续对话不再追问
            store.MarkNicknameAsked();
            sb.AppendLine("- 初次见面小互动：用户目前还没留称呼。请先认真帮对方完成这次对话的正事，然后在回复最后用轻松可爱的一句话顺带问一句「对了，以后我可以怎么称呼你呀？」；如果用户之后告诉你称呼，请立刻调用 `set_user_nickname` 记进用户画像里。");
        }

        if (isEnabled() && File.Exists(store.FilePath))
        {
            var sections = UserProfileStore.ParseSections(store.LoadMarkdown());
            foreach (var sec in UserProfileStore.StandardSections)
            {
                if (sec == UserProfileStore.SectionNickname)
                {
                    continue;
                }

                if (sections.TryGetValue(sec, out var body) && !string.IsNullOrWhiteSpace(body))
                {
                    var block = $"- {sec}：{body.Replace('\n', ' ')}";
                    if (sb.Length + block.Length + 2 > UserProfileStore.MaxInjectedChars)
                    {
                        break;
                    }

                    sb.AppendLine(block);
                }
            }
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
