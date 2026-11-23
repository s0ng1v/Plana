using System.Text;
using System.Text.Json;
using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Assistant;

/// <summary>用户个人日程规则配置（存为可读 JSON：%LOCALAPPDATA%\VGList\assistant\rules.json）。</summary>
public sealed class UserRulesDocument
{
    public const int MaxPreferenceChars = 2000;
    public const int CurrentPermissionsSchemaVersion = 2;

    /// <summary>权限结构版本：旧版（&lt;2）加载时自动迁移到「除删除外默认直写、均可配置」的新默认策略。</summary>
    public int PermissionsSchemaVersion { get; set; }

    public List<DayOfWeek> RestDays { get; set; } = [];
    public List<string> NoWeekendOrHolidayListIds { get; set; } = [];
    public string PreferenceText { get; set; } = string.Empty;
    public AssistantPermissionConfig Permissions { get; set; } = new();
    public bool MemoryEnabled { get; set; } = true;
    public bool UseProfileInChat { get; set; } = true;
    public bool AutoUpdateProfile { get; set; } = true;
    public bool WeeklyReviewEnabled { get; set; } = true;

    public static UserRulesDocument CreateDefault() => new()
    {
        PermissionsSchemaVersion = CurrentPermissionsSchemaVersion,
    };
}

/// <summary>规则文件读写仓储（懒加载，仅在助理或设置页「助理与记忆」访问时读取磁盘）。</summary>
public sealed class RulesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RulesStore(string? directory = null)
    {
        AssistantDirectory = string.IsNullOrWhiteSpace(directory) ? DefaultAssistantDirectory : directory;
        FilePath = Path.Combine(AssistantDirectory, "rules.json");
    }

    public static string DefaultAssistantDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VGList",
        "assistant");

    public string AssistantDirectory { get; }
    public string FilePath { get; }

    public UserRulesDocument Load()
    {
        if (!File.Exists(FilePath))
        {
            return UserRulesDocument.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            var doc = JsonSerializer.Deserialize<UserRulesDocument>(json, JsonOpts) ?? UserRulesDocument.CreateDefault();
            doc.Permissions ??= new AssistantPermissionConfig();

            if (doc.PermissionsSchemaVersion < UserRulesDocument.CurrentPermissionsSchemaVersion)
            {
                // 一次性迁移旧版默认配置（ADR-0048 / ADR-0050）：除删除外均默认允许直接执行，之后完全尊重用户修改
                doc.Permissions = new AssistantPermissionConfig();
                doc.PermissionsSchemaVersion = UserRulesDocument.CurrentPermissionsSchemaVersion;
                try
                {
                    Save(doc);
                }
                catch
                {
                }
            }

            return doc;
        }
        catch
        {
            return UserRulesDocument.CreateDefault();
        }
    }

    public void Save(UserRulesDocument doc)
    {
        Directory.CreateDirectory(AssistantDirectory);
        doc.Permissions ??= new AssistantPermissionConfig();
        doc.PermissionsSchemaVersion = UserRulesDocument.CurrentPermissionsSchemaVersion;
        if (doc.PreferenceText.Length > UserRulesDocument.MaxPreferenceChars)
        {
            doc.PreferenceText = doc.PreferenceText[..UserRulesDocument.MaxPreferenceChars];
        }

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOpts), Encoding.UTF8);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public UserRulesDocument ResetToDefault()
    {
        var def = UserRulesDocument.CreateDefault();
        Save(def);
        return def;
    }
}

/// <summary>
/// 基于「我的规则」的提案校验器（IProposalValidator）：
/// 仅校验休息日与周末/法定节假日清单保护（调休补班日视为工作日），不对每日任务数量做硬性限制。
/// </summary>
public sealed class RuleProposalValidator : IProposalValidator
{
    private readonly ITaskRepository _repository;
    private readonly Func<UserRulesDocument> _rulesProvider;
    private readonly Func<DateOnly, (bool IsHoliday, bool IsMakeupWorkday, string? HolidayName)>? _holidayLookup;

    public RuleProposalValidator(
        ITaskRepository repository,
        Func<UserRulesDocument> rulesProvider,
        Func<DateOnly, (bool IsHoliday, bool IsMakeupWorkday, string? HolidayName)>? holidayLookup = null)
    {
        _repository = repository;
        _rulesProvider = rulesProvider;
        _holidayLookup = holidayLookup;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<ProposalViolation>> Validate(IReadOnlyList<ProposalItem> items)
    {
        var result = new Dictionary<string, IReadOnlyList<ProposalViolation>>();
        if (items.Count == 0)
        {
            return result;
        }

        var rules = _rulesProvider();
        var restSet = new HashSet<DayOfWeek>(rules.RestDays);
        var noHolidayLists = new HashSet<string>(rules.NoWeekendOrHolidayListIds, StringComparer.Ordinal);
        if (restSet.Count == 0 && noHolidayLists.Count == 0)
        {
            return result;
        }

        var currentTasks = _repository.Query(new TaskQuery { IncludeCompleted = false })
            .ToDictionary(t => t.Id, t => t.Clone());

        foreach (var item in items)
        {
            DateOnly? targetDate = null;
            string? targetListId = null;

            switch (item.Operation)
            {
                case CreateTaskOp create:
                    targetDate = create.Draft.Date;
                    targetListId = string.IsNullOrWhiteSpace(create.Draft.ListId) ? TaskList.InboxId : create.Draft.ListId;
                    break;

                case UpdateTaskOp update when currentTasks.TryGetValue(update.TaskId, out var existing):
                    var newDate = update.Patch.Date ?? existing.Date;
                    var newList = update.Patch.ListId ?? existing.ListId;
                    if (newDate != existing.Date || newList != existing.ListId)
                    {
                        targetDate = newDate;
                        targetListId = newList;
                    }

                    break;
            }

            if (targetDate is not { } d)
            {
                continue;
            }

            var violations = new List<ProposalViolation>();

            if (restSet.Contains(d.DayOfWeek))
            {
                violations.Add(new ProposalViolation(
                    "rest-day",
                    $"{d:M月d日}（{WeekdayCn(d.DayOfWeek)}）是你设定的休息日"));
            }

            if (targetListId is not null && noHolidayLists.Contains(targetListId))
            {
                var (isHoliday, isMakeup, holidayName) = _holidayLookup is not null
                    ? _holidayLookup(d)
                    : (false, false, null);
                var isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

                if (isHoliday)
                {
                    violations.Add(new ProposalViolation(
                        "holiday-list-guard",
                        $"{d:M月d日}为法定节假日（{holidayName ?? "假期"}），该清单已设为节假日不排"));
                }
                else if (isWeekend && !isMakeup)
                {
                    violations.Add(new ProposalViolation(
                        "weekend-list-guard",
                        $"{d:M月d日}为周末，该清单已设为周末与节假日不排"));
                }
            }

            if (violations.Count > 0)
            {
                result[item.Id] = violations;
            }
        }

        return result;
    }

    private static string WeekdayCn(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日",
    };
}

/// <summary>向系统提示注入「我的规则」摘要与自然语言偏好（不包含任何每日负荷上限）。</summary>
public sealed class RulesContextContributor(Func<UserRulesDocument> rulesProvider) : IContextContributor
{
    public string SectionName => "用户的日程规则与偏好";

    public string? BuildContext(string userMessage, DateOnly today)
    {
        var r = rulesProvider();
        var sb = new StringBuilder();
        if (r.RestDays.Count > 0)
        {
            sb.AppendLine($"- 休息日（尽量不排任务）：{string.Join("、", r.RestDays)}");
        }

        if (!string.IsNullOrWhiteSpace(r.PreferenceText))
        {
            var pref = r.PreferenceText.Trim();
            if (pref.Length > 1200)
            {
                pref = pref[..1200] + "…";
            }

            sb.AppendLine($"- 个人偏好：{pref}");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
