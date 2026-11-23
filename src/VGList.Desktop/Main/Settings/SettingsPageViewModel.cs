using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Models;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Settings;

/// <summary>设置页左侧分类导航子页。</summary>
public enum SettingsSection
{
    General,
    Models,
    Assistant,
    Widgets,
}

/// <summary>主窗口设置页视图模型：常规与外观、独立模型服务页（A2）、助理与记忆（A5）、两个桌面小组件联动。</summary>
public sealed partial class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly ITaskRepository _repository;
    private readonly WidgetCoordinator _coordinator;
    private readonly ToastService _toast;
    private bool _syncing;
    private bool _disposed;

    public SettingsPageViewModel(
        ITaskRepository repository,
        WidgetCoordinator coordinator,
        ToastService toast,
        ModelProfileStore modelStore,
        IModelConnectionTester connectionTester)
    {
        _repository = repository;
        _coordinator = coordinator;
        _toast = toast;
        ModelServices = new ModelServicesViewModel(modelStore, connectionTester, toast);
        AssistantMemory = new AssistantMemorySettingsViewModel(repository, toast);
        ExternalMcp = new ExternalMcpSettingsViewModel(coordinator, toast);
        _coordinator.StateChanged += OnCoordinatorStateChanged;
        LoadFromCoordinator();
    }

    public ModelServicesViewModel ModelServices { get; }

    public AssistantMemorySettingsViewModel AssistantMemory { get; }

    public ExternalMcpSettingsViewModel ExternalMcp { get; }

    [ObservableProperty]
    private SettingsSection _activeSection = SettingsSection.General;

    public bool IsGeneralSection => ActiveSection == SettingsSection.General;
    public bool IsModelsSection => ActiveSection == SettingsSection.Models;
    public bool IsAssistantSection => ActiveSection == SettingsSection.Assistant;
    public bool IsWidgetsSection => ActiveSection == SettingsSection.Widgets;

    public void SelectSection(SettingsSection section) => ActiveSection = section;

    partial void OnActiveSectionChanged(SettingsSection value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsModelsSection));
        OnPropertyChanged(nameof(IsAssistantSection));
        OnPropertyChanged(nameof(IsWidgetsSection));
        if (value == SettingsSection.Assistant)
        {
            AssistantMemory.EnsureLoaded();
            ExternalMcp.RefreshAudit();
        }
    }

    [ObservableProperty]
    private bool _reduceMotion;

    [ObservableProperty]
    private bool _autoDetectDate = true;

    [ObservableProperty]
    private bool _allowAssistantReadTasks = true;

    [ObservableProperty]
    private bool _morningBriefEnabled = true;

    [ObservableProperty]
    private bool _launchAtStartup;

    [ObservableProperty]
    private bool _startMinimizedToTray = true;

    [ObservableProperty]
    private bool _isConfirmingResetSample;

    [ObservableProperty]
    private bool _showTodayWidget;

    [ObservableProperty]
    private bool _todayTopmost;

    [ObservableProperty]
    private bool _todayLocked;

    [ObservableProperty]
    private bool _todayClickThrough;

    [ObservableProperty]
    private double _todayOpacity;

    [ObservableProperty]
    private bool _todayShowInput;

    [ObservableProperty]
    private bool _todayShowCompleted;

    [ObservableProperty]
    private bool _showMonthWidget;

    [ObservableProperty]
    private bool _monthTopmost;

    [ObservableProperty]
    private bool _monthLocked;

    [ObservableProperty]
    private bool _monthClickThrough;

    [ObservableProperty]
    private double _monthOpacity;

    [ObservableProperty]
    private bool _monthShowLunar;

    [ObservableProperty]
    private bool _monthShowCompleted;

    public string TodayOpacityText => $"{Math.Round(TodayOpacity * 100)}%";
    public string MonthOpacityText => $"{Math.Round(MonthOpacity * 100)}%";

    public bool IsPlanaTheme => AppTheme.Normalize(_coordinator.MainSettings.Theme) == AppTheme.Plana;

    public bool IsClassicBlueTheme => !IsPlanaTheme;

    public bool IsFontAnthropicSerif => FontPresetMode.Normalize(_coordinator.MainSettings.FontPreset) == FontPresetMode.AnthropicSerif;

    public bool IsFontAnthropicClassic => FontPresetMode.Normalize(_coordinator.MainSettings.FontPreset) == FontPresetMode.AnthropicClassic;

    public bool IsFontCleanSans => FontPresetMode.Normalize(_coordinator.MainSettings.FontPreset) == FontPresetMode.CleanSans;

    public bool IsFontSystem => FontPresetMode.Normalize(_coordinator.MainSettings.FontPreset) == FontPresetMode.System;

    public bool IsCloseActionAsk => WindowCloseAction.Normalize(_coordinator.MainSettings.CloseAction) == WindowCloseAction.Ask;

    public bool IsCloseActionMinimize => WindowCloseAction.Normalize(_coordinator.MainSettings.CloseAction) == WindowCloseAction.Minimize;

    public bool IsCloseActionExit => WindowCloseAction.Normalize(_coordinator.MainSettings.CloseAction) == WindowCloseAction.Exit;

    public void SetTheme(string theme)
    {
        _coordinator.SetTheme(theme);
        OnPropertyChanged(nameof(IsPlanaTheme));
        OnPropertyChanged(nameof(IsClassicBlueTheme));
    }

    public void SetFontPreset(string fontPreset)
    {
        _coordinator.SetFontPreset(fontPreset);
        NotifyFontPresetProperties();
    }

    private void NotifyFontPresetProperties()
    {
        OnPropertyChanged(nameof(IsFontAnthropicSerif));
        OnPropertyChanged(nameof(IsFontAnthropicClassic));
        OnPropertyChanged(nameof(IsFontCleanSans));
        OnPropertyChanged(nameof(IsFontSystem));
    }

    public void SetCloseAction(string closeAction)
    {
        _coordinator.SetCloseAction(closeAction);
        OnPropertyChanged(nameof(IsCloseActionAsk));
        OnPropertyChanged(nameof(IsCloseActionMinimize));
        OnPropertyChanged(nameof(IsCloseActionExit));
    }

    public bool HandleEscape()
    {
        if (ModelServices.IsDrawerOpen)
        {
            ModelServices.CloseDrawer();
            return true;
        }

        if (IsConfirmingResetSample)
        {
            IsConfirmingResetSample = false;
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void ResetTodayWidget()
    {
        _coordinator.ResetTodayPosition();
        _toast.Show("已重置今日日程小组件位置与默认尺寸");
    }

    [RelayCommand]
    private void ResetMonthWidget()
    {
        _coordinator.ResetMonthPosition();
        _toast.Show("已重置月历小组件位置与默认尺寸");
    }

    [RelayCommand]
    private void BeginResetSampleData() => IsConfirmingResetSample = true;

    [RelayCommand]
    private void CancelResetSampleData() => IsConfirmingResetSample = false;

    [RelayCommand]
    private void ResetSampleData()
    {
        IsConfirmingResetSample = false;
        var changeSet = _coordinator.ResetSampleTasks();
        if (changeSet is not null)
        {
            _toast.Show("已重置为示例数据", () => _coordinator.UndoChangeSet(changeSet.Id));
        }
        else
        {
            _toast.Show("已重置为示例数据");
        }
    }

    public void UpdateTodayOpacity(double opacity)
    {
        TodayOpacity = Math.Clamp(opacity, 0.4, 1.0);
        _coordinator.SetTodayOpacity(TodayOpacity);
    }

    public void UpdateMonthOpacity(double opacity)
    {
        MonthOpacity = Math.Clamp(opacity, 0.4, 1.0);
        _coordinator.SetMonthOpacity(MonthOpacity);
    }

    /// <summary>P1：按格式生成本地数据导出内容（json / md / ics）。</summary>
    public string BuildExportContent(string format) => format.ToLowerInvariant() switch
    {
        "md" or "markdown" => TaskBackupService.ExportMarkdown(_repository),
        "ics" => TaskBackupService.ExportIcs(_repository),
        _ => TaskBackupService.ExportJson(_repository),
    };

    /// <summary>P1：自动识别 JSON / Markdown / ICS 并原子导入（支持一键撤销）。</summary>
    public BackupImportResult ImportFromContent(string filePath, string content)
    {
        var result = TaskBackupService.ImportAuto(
            filePath,
            content,
            DateOnly.FromDateTime(DateTime.Today),
            _repository);

        if (result.ImportedCount == 0)
        {
            _toast.Show(result.SkippedCount > 0
                ? $"未新增任务（已跳过 {result.SkippedCount} 条重复项）"
                : "文件中没有可导入的任务");
            return result;
        }

        var summary = result.SkippedCount > 0
            ? $"已从 {result.FormatLabel} 导入 {result.ImportedCount} 项（跳过重复 {result.SkippedCount} 项）"
            : $"已从 {result.FormatLabel} 导入 {result.ImportedCount} 项任务";

        if (!string.IsNullOrEmpty(result.ChangeSetId))
        {
            _toast.Show(summary, () => _coordinator.UndoChangeSet(result.ChangeSetId), durationMs: 6000);
        }
        else
        {
            _toast.Show(summary);
        }

        return result;
    }

    public void NotifyExportSuccess(string formatLabel, string fileName) =>
        _toast.Show($"已导出 {formatLabel} 备份：{fileName}");

    public void NotifyBackupError(string message) =>
        _toast.Show(message, durationMs: 5000);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        ModelServices.Dispose();
        ExternalMcp.Dispose();
    }

    partial void OnReduceMotionChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetReduceMotion(value);
        }
    }

    partial void OnAutoDetectDateChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetAutoDetectDate(value);
        }
    }

    partial void OnAllowAssistantReadTasksChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetAllowAssistantReadTasks(value);
        }
    }

    partial void OnMorningBriefEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMorningBriefEnabled(value);
        }
    }

    partial void OnLaunchAtStartupChanged(bool value)
    {
        if (_syncing)
        {
            return;
        }

        if (!_coordinator.SetLaunchAtStartup(value))
        {
            _syncing = true;
            LaunchAtStartup = !value;
            _syncing = false;
            _toast.Show("无法写入 Windows 开机自启动项");
        }
        else
        {
            _toast.Show(value ? "已开启开机自动启动 Plana" : "已关闭开机自动启动");
        }
    }

    partial void OnStartMinimizedToTrayChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetStartMinimizedToTray(value);
        }
    }

    partial void OnShowTodayWidgetChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayVisible(value);
        }
    }

    partial void OnTodayTopmostChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayTopmost(value);
        }
    }

    partial void OnTodayLockedChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayLocked(value);
        }
    }

    partial void OnTodayClickThroughChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayClickThrough(value);
            if (value)
            {
                _toast.Show("今日小组件已开启点击穿透，可在本设置页或系统托盘菜单随时解除", durationMs: 5000);
            }
        }
    }

    partial void OnTodayOpacityChanged(double value) =>
        OnPropertyChanged(nameof(TodayOpacityText));

    partial void OnTodayShowInputChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayShowInput(value);
        }
    }

    partial void OnTodayShowCompletedChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetTodayShowCompleted(value);
        }
    }

    partial void OnShowMonthWidgetChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthVisible(value);
        }
    }

    partial void OnMonthTopmostChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthTopmost(value);
        }
    }

    partial void OnMonthLockedChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthLocked(value);
        }
    }

    partial void OnMonthClickThroughChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthClickThrough(value);
            if (value)
            {
                _toast.Show("月历小组件已开启点击穿透，可在本设置页或系统托盘菜单随时解除", durationMs: 5000);
            }
        }
    }

    partial void OnMonthOpacityChanged(double value) =>
        OnPropertyChanged(nameof(MonthOpacityText));

    partial void OnMonthShowLunarChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthShowLunar(value);
        }
    }

    partial void OnMonthShowCompletedChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMonthShowCompleted(value);
        }
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (!_disposed)
        {
            LoadFromCoordinator();
        }
    });

    private void LoadFromCoordinator()
    {
        _syncing = true;
        try
        {
            var main = _coordinator.MainSettings;
            var today = _coordinator.TodaySettings;
            var month = _coordinator.MonthSettings;

            ReduceMotion = main.ReduceMotion;
            AutoDetectDate = main.AutoDetectDate;
            AllowAssistantReadTasks = main.AllowAssistantReadTasks;
            MorningBriefEnabled = main.MorningBriefEnabled;
            LaunchAtStartup = main.LaunchAtStartup;
            StartMinimizedToTray = main.StartMinimizedToTray;
            ShowTodayWidget = main.ShowTodayWidget;
            TodayTopmost = today.IsTopmost;
            TodayLocked = today.IsLocked;
            TodayClickThrough = today.IsClickThrough;
            TodayOpacity = Math.Clamp(today.Opacity, 0.4, 1.0);
            TodayShowInput = today.ShowInput;
            TodayShowCompleted = today.ShowCompleted;

            ShowMonthWidget = main.ShowMonthWidget;
            MonthTopmost = month.IsTopmost;
            MonthLocked = month.IsLocked;
            MonthClickThrough = month.IsClickThrough;
            MonthOpacity = Math.Clamp(month.Opacity, 0.4, 1.0);
            MonthShowLunar = month.ShowLunar;
            MonthShowCompleted = month.ShowCompleted;

            OnPropertyChanged(nameof(IsPlanaTheme));
            OnPropertyChanged(nameof(IsClassicBlueTheme));
            NotifyFontPresetProperties();
            OnPropertyChanged(nameof(IsCloseActionAsk));
            OnPropertyChanged(nameof(IsCloseActionMinimize));
            OnPropertyChanged(nameof(IsCloseActionExit));
        }
        finally
        {
            _syncing = false;
        }
    }
}
