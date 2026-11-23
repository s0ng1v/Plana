using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Assistant;
using VGList.Core.Abstractions;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Settings;

public sealed partial class RestDayChipViewModel(DayOfWeek day, string label, bool isSelected, Action onChanged) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    public void Toggle()
    {
        IsSelected = !IsSelected;
        onChanged();
    }
}

public sealed class MemoryRowViewModel(MemoryItem item)
{
    public string Id { get; } = item.Id;
    public string Category { get; } = item.Category;
    public string Content { get; } = item.Content;
    public string MetaLabel { get; } = $"{item.Source} · {item.UpdatedAt:yyyy-MM-dd}";
}

/// <summary>
/// 设置页「助理与记忆」独立子页视图模型（A5 / ADR-0048）：
/// 管理我的日程规则、Lana 读写权限（默认仅删除需确认，均可按需更改）、结构化用户画像（零原始 Markdown 符号）、长期记忆与离线习惯复盘。
/// </summary>
public sealed partial class AssistantMemorySettingsViewModel : ObservableObject
{
    private readonly ITaskRepository _repository;
    private readonly ToastService _toast;
    private readonly RulesStore _rulesStore;
    private readonly MemoryStore _memoryStore;
    private readonly UserProfileStore _profileStore;
    private readonly WeeklyReviewStore _reviewStore;
    private bool _loaded;
    private bool _syncing;

    public AssistantMemorySettingsViewModel(
        ITaskRepository repository,
        ToastService toast,
        string? assistantDir = null)
    {
        _repository = repository;
        _toast = toast;
        _rulesStore = new RulesStore(assistantDir);
        _memoryStore = new MemoryStore(assistantDir);
        _profileStore = new UserProfileStore(assistantDir);
        _reviewStore = new WeeklyReviewStore(assistantDir);

        (DayOfWeek Day, string Label)[] week =
        [
            (DayOfWeek.Monday, "周一"),
            (DayOfWeek.Tuesday, "周二"),
            (DayOfWeek.Wednesday, "周三"),
            (DayOfWeek.Thursday, "周四"),
            (DayOfWeek.Friday, "周五"),
            (DayOfWeek.Saturday, "周六"),
            (DayOfWeek.Sunday, "周日"),
        ];

        foreach (var (d, lbl) in week)
        {
            RestDays.Add(new RestDayChipViewModel(d, lbl, false, SaveRules));
        }
    }

    public ObservableCollection<RestDayChipViewModel> RestDays { get; } = [];
    public ObservableCollection<MemoryRowViewModel> Memories { get; } = [];

    [ObservableProperty] private string _preferenceText = string.Empty;
    [ObservableProperty] private bool _autoCreateTask = true;
    [ObservableProperty] private bool _autoUpdateTask = true;
    [ObservableProperty] private bool _autoAppendSubtasks = true;
    [ObservableProperty] private bool _autoCategorizeList = true;
    [ObservableProperty] private bool _autoCompleteTask = true;
    [ObservableProperty] private bool _autoCreateList = true;
    [ObservableProperty] private bool _autoDeleteTask;

    [ObservableProperty] private bool _memoryEnabled = true;
    [ObservableProperty] private string _newMemoryInput = string.Empty;
    [ObservableProperty] private string? _memoryError;
    [ObservableProperty] private bool _isConfirmingClearMemories;

    // 结构化用户画像字段（界面干净友好，不含 # / > 等原始 Markdown 符号）
    [ObservableProperty] private bool _useProfileInChat = true;
    [ObservableProperty] private bool _autoUpdateProfile = true;
    [ObservableProperty] private string _profileNickname = string.Empty;
    [ObservableProperty] private string _profileRoutine = string.Empty;
    [ObservableProperty] private string _profileActivities = string.Empty;
    [ObservableProperty] private string _profilePeakHours = string.Empty;
    [ObservableProperty] private string _profileProcrastination = string.Empty;
    [ObservableProperty] private string _profileOther = string.Empty;
    [ObservableProperty] private int _profileHistoryCount;

    [ObservableProperty] private string _habitSummaryText = "打开本页时自动统计本地任务记录";
    [ObservableProperty] private string _weeklyReviewPreview = string.Empty;

    public bool HasMemories => Memories.Count > 0;
    public bool HasMemoryError => !string.IsNullOrEmpty(MemoryError);
    public bool CanRestoreProfile => ProfileHistoryCount > 0;
    public bool HasWeeklyReviewPreview => !string.IsNullOrEmpty(WeeklyReviewPreview);
    public string MemoryCountLabel => $"{Memories.Count} / {MemoryStore.MaxMemories} 条";

    /// <summary>懒加载：仅当用户切到「助理与记忆」子页时才读取本地规则、记忆与画像文件。</summary>
    public void EnsureLoaded()
    {
        if (!_loaded)
        {
            _loaded = true;
            ReloadAll();
        }
        else
        {
            ReloadProfileFields();
            RefreshStatsAndMemories();
        }
    }

    public void ReloadAll()
    {
        _syncing = true;
        try
        {
            var rules = _rulesStore.Load();
            PreferenceText = rules.PreferenceText;
            AutoCreateTask = rules.Permissions.AutoCreateTask;
            AutoUpdateTask = rules.Permissions.AutoUpdateTask;
            AutoAppendSubtasks = rules.Permissions.AutoAppendSubtasks;
            AutoCategorizeList = rules.Permissions.AutoCategorizeList;
            AutoCompleteTask = rules.Permissions.AutoCompleteTask;
            AutoCreateList = rules.Permissions.AutoCreateList;
            AutoDeleteTask = rules.Permissions.AutoDeleteTask;
            MemoryEnabled = rules.MemoryEnabled;
            UseProfileInChat = rules.UseProfileInChat;
            AutoUpdateProfile = rules.AutoUpdateProfile;

            var restSet = new HashSet<DayOfWeek>(rules.RestDays);
            foreach (var chip in RestDays)
            {
                chip.IsSelected = restSet.Contains(chip.Day);
            }

            ReloadProfileFields();
            RefreshStatsAndMemories();
        }
        finally
        {
            _syncing = false;
        }
    }

    private void ReloadProfileFields()
    {
        var fields = _profileStore.LoadFields();
        ProfileNickname = fields.Nickname;
        ProfileRoutine = fields.SleepAndRoutine;
        ProfileActivities = fields.FrequentActivities;
        ProfilePeakHours = fields.PeakHours;
        ProfileProcrastination = fields.ProcrastinationTypes;
        ProfileOther = fields.OtherNotes;
        ProfileHistoryCount = _profileStore.ListHistoryFiles().Count;
    }

    [RelayCommand]
    public void SaveRules()
    {
        if (_syncing)
        {
            return;
        }

        var doc = new UserRulesDocument
        {
            RestDays = RestDays.Where(c => c.IsSelected).Select(c => c.Day).ToList(),
            PreferenceText = PreferenceText ?? string.Empty,
            MemoryEnabled = MemoryEnabled,
            UseProfileInChat = UseProfileInChat,
            AutoUpdateProfile = AutoUpdateProfile,
            Permissions = new AssistantPermissionConfig
            {
                AutoCreateTask = AutoCreateTask,
                AutoUpdateTask = AutoUpdateTask,
                AutoAppendSubtasks = AutoAppendSubtasks,
                AutoCategorizeList = AutoCategorizeList,
                AutoCompleteTask = AutoCompleteTask,
                AutoCreateList = AutoCreateList,
                AutoDeleteTask = AutoDeleteTask,
            },
        };

        _rulesStore.Save(doc);
    }

    [RelayCommand]
    public void ResetRules()
    {
        _rulesStore.ResetToDefault();
        ReloadAll();
        _toast.Show("已恢复默认日程规则与权限");
    }

    [RelayCommand]
    public void AddManualMemory()
    {
        MemoryError = null;
        var text = NewMemoryInput.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var (ok, _, err) = _memoryStore.Add(text, "偏好", "手动");
        if (!ok)
        {
            MemoryError = err;
            return;
        }

        NewMemoryInput = string.Empty;
        RefreshStatsAndMemories();
        _toast.Show("已添加到本地记忆");
    }

    public void DeleteMemory(MemoryRowViewModel row)
    {
        if (_memoryStore.Remove(row.Id))
        {
            RefreshStatsAndMemories();
            _toast.Show("已删除该条记忆");
        }
    }

    [RelayCommand]
    public void BeginClearMemories() => IsConfirmingClearMemories = true;

    [RelayCommand]
    public void CancelClearMemories() => IsConfirmingClearMemories = false;

    [RelayCommand]
    public void ConfirmClearMemories()
    {
        IsConfirmingClearMemories = false;
        _memoryStore.ClearAll();
        _profileStore.ClearAll(includeHistory: false);
        ReloadProfileFields();
        RefreshStatsAndMemories();
        _toast.Show("已清空助理记忆与当前画像");
    }

    [RelayCommand]
    public void SaveProfile()
    {
        var fields = new UserProfileFields(
            Nickname: ProfileNickname ?? string.Empty,
            SleepAndRoutine: ProfileRoutine ?? string.Empty,
            FrequentActivities: ProfileActivities ?? string.Empty,
            PeakHours: ProfilePeakHours ?? string.Empty,
            ProcrastinationTypes: ProfileProcrastination ?? string.Empty,
            OtherNotes: ProfileOther ?? string.Empty);

        _profileStore.SaveUserEditedFields(fields);
        ProfileHistoryCount = _profileStore.ListHistoryFiles().Count;
        _toast.Show("已保存用户画像 ✨");
    }

    [RelayCommand]
    public void RestorePreviousProfile()
    {
        if (_profileStore.RestorePreviousVersion())
        {
            ReloadProfileFields();
            _toast.Show("已恢复上一版用户画像");
        }
    }

    [RelayCommand]
    public void GenerateWeeklyReview()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        WeeklyReviewPreview = _reviewStore.GenerateOfflineReview(_repository, today);
        _toast.Show($"已生成并保存 {WeeklyReviewStore.GetIsoWeekKey(today)} 本周复盘");
    }

    [RelayCommand]
    public void OpenAssistantFolder()
    {
        try
        {
            Directory.CreateDirectory(_rulesStore.AssistantDirectory);
            Process.Start(new ProcessStartInfo(_rulesStore.AssistantDirectory) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void RefreshStatsAndMemories()
    {
        Memories.Clear();
        foreach (var item in _memoryStore.LoadAll())
        {
            Memories.Add(new MemoryRowViewModel(item));
        }

        OnPropertyChanged(nameof(HasMemories));
        OnPropertyChanged(nameof(MemoryCountLabel));

        var today = DateOnly.FromDateTime(DateTime.Today);
        var stats = LocalHabitStats.Compute(_repository, today);
        HabitSummaryText = stats.SummaryNote;

        var reviewPath = _reviewStore.GetReviewFilePath(today);
        if (File.Exists(reviewPath))
        {
            WeeklyReviewPreview = File.ReadAllText(reviewPath);
        }
    }

    partial void OnAutoCreateTaskChanged(bool value)
    {
        AutoCreateList = value;
        SaveRules();
    }

    partial void OnAutoUpdateTaskChanged(bool value) => SaveRules();

    partial void OnAutoAppendSubtasksChanged(bool value)
    {
        AutoCategorizeList = value;
        SaveRules();
    }

    partial void OnAutoCategorizeListChanged(bool value) => SaveRules();
    partial void OnAutoCompleteTaskChanged(bool value) => SaveRules();
    partial void OnAutoCreateListChanged(bool value) => SaveRules();
    partial void OnAutoDeleteTaskChanged(bool value) => SaveRules();
    partial void OnMemoryEnabledChanged(bool value) => SaveRules();
    partial void OnUseProfileInChatChanged(bool value) => SaveRules();
    partial void OnAutoUpdateProfileChanged(bool value) => SaveRules();
    partial void OnMemoryErrorChanged(string? value) => OnPropertyChanged(nameof(HasMemoryError));
    partial void OnProfileHistoryCountChanged(int value) => OnPropertyChanged(nameof(CanRestoreProfile));
    partial void OnWeeklyReviewPreviewChanged(string value) => OnPropertyChanged(nameof(HasWeeklyReviewPreview));
}
