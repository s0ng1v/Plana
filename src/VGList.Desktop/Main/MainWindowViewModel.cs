using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Assistant;
using VGList.App.Mcp;
using VGList.App.Models;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Core.Models;
using VGList.Desktop.Controls;
using VGList.Desktop.Main.Assistant;
using VGList.Desktop.Main.Calendar;
using VGList.Desktop.Main.Eisenhower;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main;

/// <summary>
/// 主程序壳层视图模型：管理四页导航、可折叠清单栏、键盘操作与浮动 Toast。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly WidgetCoordinator _coordinator;
    private readonly ITaskRepository _repository;
    private readonly DateOnly _today;
    private bool _disposed;

    public MainWindowViewModel(
        ITaskRepository repository,
        WidgetCoordinator coordinator,
        ModelProfileStore? modelStore = null,
        IModelConnectionTester? connectionTester = null)
    {
        _coordinator = coordinator;
        _repository = repository;
        _today = DateOnly.FromDateTime(DateTime.Today);

        var resolvedStore = modelStore ?? new ModelProfileStore(PlatformServices.Secrets);
        var resolvedTester = connectionTester ?? new ModelConnectionTester();

        Toast = new ToastService();
        TasksPage = new TasksPageViewModel(
            repository,
            Toast,
            _today,
            briefService: coordinator.BriefService,
            shouldShowFallbackBrief: () => !coordinator.MainSettings.ShowTodayWidget && coordinator.MainSettings.MorningBriefEnabled,
            onOpenMorningBrief: OpenMorningBriefAssistant);
        EisenhowerPage = new EisenhowerViewModel(repository, Toast, _today);
        CalendarPage = new CalendarPageViewModel(repository, Toast, coordinator, _today);
        Func<VGList.App.Assistant.IAssistantHost>? assistantHostFactory = repository is IScheduleService sched
            ? () => AssistantHostFactory.Create(repository, sched, resolvedStore, coordinator, _today)
            : null;
        AssistantPage = new AssistantPageViewModel(repository, resolvedStore, Toast, ShowModelSettings, _today, hostFactory: assistantHostFactory);
        SettingsPage = new SettingsPageViewModel(repository, coordinator, Toast, resolvedStore, resolvedTester);

        _isSidebarCollapsed = coordinator.MainSettings.IsSidebarCollapsed;

        _repository.Changed += OnRepositoryChanged;
        _repository.ListsChanged += OnListsChanged;
        _coordinator.StateChanged += OnCoordinatorStateChanged;
        _coordinator.MorningBriefRequested += OnMorningBriefRequested;
        _coordinator.McpApplied += OnMcpApplied;
        _coordinator.McpConfirmationAdded += OnMcpConfirmationAdded;
        _coordinator.McpConfirmationsChanged += OnMcpConfirmationsChanged;
        TasksPage.PropertyChanged += OnTasksPagePropertyChanged;
        AssistantPage.PropertyChanged += OnAssistantPagePropertyChanged;
        RefreshLists();
        SyncPendingMcpRequests();

        var lunar = LunarCalendar.Describe(_today);
        TodayDateSummary = $"{_today.Month}月{_today.Day}日 {DateLabels.Weekday(_today)} · {lunar.Text}";
    }

    public ObservableCollection<ListSidebarItem> Lists { get; } = [];
    public ObservableCollection<McpPendingConfirmation> PendingMcpRequests { get; } = [];

    public ToastService Toast { get; }
    public TasksPageViewModel TasksPage { get; }
    public EisenhowerViewModel EisenhowerPage { get; }
    public CalendarPageViewModel CalendarPage { get; }
    public AssistantPageViewModel AssistantPage { get; }
    public SettingsPageViewModel SettingsPage { get; }

    public string TodayDateSummary { get; }

    [ObservableProperty]
    private MainPage _page = MainPage.Tasks;

    /// <summary>左侧导航 Lana 头像的视觉状态（与对话舞台共源：Sleep / Idle / Listening / Thinking / Happy）。</summary>
    [ObservableProperty]
    private LanaVisualState _lanaNavState = LanaVisualState.Sleep;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private bool _isClosePromptOpen;

    [ObservableProperty]
    private bool _rememberCloseChoice;

    [ObservableProperty]
    private bool _isMcpConfirmOverlayOpen;

    public bool HasPendingMcpRequests => PendingMcpRequests.Count > 0;
    public string PendingMcpBadgeText => $"外部请求 {PendingMcpRequests.Count}";

    public bool IsTasksPage => Page == MainPage.Tasks;
    public bool IsEisenhowerPage => Page == MainPage.Eisenhower;
    public bool IsCalendarPage => Page == MainPage.Calendar;
    public bool IsAssistantPage => Page == MainPage.Assistant;
    public bool IsSettingsPage => Page == MainPage.Settings;

    public bool IsSidebarVisible => IsTasksPage && !IsSidebarCollapsed;

    public event EventHandler<MainPage>? PageSwitched;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? ExitConfirmed;
    public event EventHandler? ActivateWindowRequested;
    public event EventHandler? McpAttentionRequested;

    [RelayCommand]
    private void ToggleMcpConfirmOverlay()
    {
        if (HasPendingMcpRequests)
        {
            IsMcpConfirmOverlayOpen = !IsMcpConfirmOverlayOpen;
        }
        else
        {
            IsMcpConfirmOverlayOpen = false;
        }
    }

    [RelayCommand]
    private void CloseMcpConfirmOverlay() => IsMcpConfirmOverlayOpen = false;

    public void ApproveMcpRequest(string requestId)
    {
        _coordinator.ResolveMcpConfirmation(requestId, approved: true);
        SyncPendingMcpRequests();
    }

    public void DenyMcpRequest(string requestId)
    {
        _coordinator.ResolveMcpConfirmation(requestId, approved: false);
        SyncPendingMcpRequests();
    }

    public void RefreshDateRollover(DateOnly newToday) =>
        _coordinator.RefreshMorningBriefs(newToday);

    private void OpenMorningBriefAssistant(MorningBriefInfo info)
    {
        ActivateWindowRequested?.Invoke(this, EventArgs.Empty);
        OpenAssistant(info.PromptText, autoSend: true);
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e) =>
        RunOnUi(() => TasksPage.RefreshMorningBrief());

    private void OnMorningBriefRequested(object? sender, MorningBriefInfo info) =>
        RunOnUi(() => OpenMorningBriefAssistant(info));

    private void OnMcpApplied(object? sender, McpAppliedEventArgs e) =>
        RunOnUi(() => Toast.Show(e.Label, () => _coordinator.UndoChangeSet(e.ChangeSetId), durationMs: 6000));

    private void OnMcpConfirmationAdded(object? sender, McpPendingConfirmation req) =>
        RunOnUi(() =>
        {
            SyncPendingMcpRequests();
            Toast.Show($"外部 AI（{req.ClientName}）{req.Summary}", durationMs: 6000);
            McpAttentionRequested?.Invoke(this, EventArgs.Empty);
        });

    private void OnMcpConfirmationsChanged(object? sender, EventArgs e) =>
        RunOnUi(SyncPendingMcpRequests);

    private void SyncPendingMcpRequests()
    {
        var snapshot = _coordinator.McpConfirmations.GetSnapshot();
        PendingMcpRequests.Clear();
        foreach (var item in snapshot)
        {
            PendingMcpRequests.Add(item);
        }

        OnPropertyChanged(nameof(HasPendingMcpRequests));
        OnPropertyChanged(nameof(PendingMcpBadgeText));
        if (!HasPendingMcpRequests)
        {
            IsMcpConfirmOverlayOpen = false;
        }
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    /// <summary>
    /// 响应主窗口关闭请求：返回 true 表示允许直接关闭退出；返回 false 表示拦截（转为最小化或打开确认卡）。
    /// </summary>
    public bool RequestWindowClose()
    {
        var action = WindowCloseAction.Normalize(_coordinator.MainSettings.CloseAction);
        if (string.Equals(action, WindowCloseAction.Exit, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(action, WindowCloseAction.Minimize, StringComparison.Ordinal))
        {
            MinimizeRequested?.Invoke(this, EventArgs.Empty);
            return false;
        }

        RememberCloseChoice = false;
        IsClosePromptOpen = true;
        return false;
    }

    [RelayCommand]
    private void ToggleRememberCloseChoice() => RememberCloseChoice = !RememberCloseChoice;

    [RelayCommand]
    private void CancelClosePrompt() => IsClosePromptOpen = false;

    [RelayCommand]
    private void ConfirmMinimizeFromPrompt()
    {
        if (RememberCloseChoice)
        {
            _coordinator.SetCloseAction(WindowCloseAction.Minimize);
        }

        IsClosePromptOpen = false;
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ConfirmExitFromPrompt()
    {
        if (RememberCloseChoice)
        {
            _coordinator.SetCloseAction(WindowCloseAction.Exit);
        }

        IsClosePromptOpen = false;
        ExitConfirmed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowTasks() => Page = MainPage.Tasks;

    [RelayCommand]
    private void ShowEisenhower() => Page = MainPage.Eisenhower;

    [RelayCommand]
    private void ShowCalendarPage() => Page = MainPage.Calendar;

    [RelayCommand]
    private void ShowAssistant() => Page = MainPage.Assistant;

    [RelayCommand]
    private void ShowSettings() => Page = MainPage.Settings;

    private void ShowModelSettings()
    {
        SettingsPage.SelectSection(SettingsSection.Models);
        Page = MainPage.Settings;
    }

    /// <summary>全局快捷键 Ctrl+Z（焦点不在输入框时）：撤销最近 30 分钟内最近一条未撤销的助理/外部变更集。</summary>
    public bool TryUndoLastAssistantChange()
    {
        if (_repository is not IScheduleService schedule)
        {
            return false;
        }

        var cutoff = DateTimeOffset.Now - TimeSpan.FromMinutes(30);
        var candidate = schedule.Recent(20)
            .FirstOrDefault(cs => cs.UndoneAt is null &&
                                  (cs.Source == ChangeSource.Assistant || cs.Source == ChangeSource.Mcp) &&
                                  cs.At >= cutoff);
        if (candidate is null)
        {
            return false;
        }

        var res = schedule.Undo(candidate.Id);
        if (!res.Success)
        {
            return false;
        }

        var msg = res.Skipped.Count > 0
            ? $"已撤销助理改动 {res.RevertedCount} 项（{res.Skipped.Count} 项因之后被修改而保留）"
            : $"已整批撤销助理改动 {res.RevertedCount} 项";
        Toast.Show(msg);
        return true;
    }

    /// <summary>供任务卡片 / 场景入口拉起助理并预填或自动发送（A4/A6）。</summary>
    public void OpenAssistant(string? prefill = null, bool autoSend = false)
    {
        Page = MainPage.Assistant;
        if (!string.IsNullOrWhiteSpace(prefill))
        {
            AssistantPage.DraftInput = prefill;
            if (autoSend && AssistantPage.CanSend)
            {
                _ = AssistantPage.SendAsync();
            }
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        if (!IsTasksPage)
        {
            Page = MainPage.Tasks;
            IsSidebarCollapsed = false;
        }
        else
        {
            IsSidebarCollapsed = !IsSidebarCollapsed;
        }

        _coordinator.SetSidebarCollapsed(IsSidebarCollapsed);
    }

    [RelayCommand]
    private void SelectToday() => SelectTasksSection(MainSection.Today);

    [RelayCommand]
    private void SelectNext7Days() => SelectTasksSection(MainSection.Next7Days);

    [RelayCommand]
    private void SelectInbox() => SelectTasksSection(MainSection.Inbox);

    [RelayCommand]
    private void SelectCompleted() => SelectTasksSection(MainSection.Completed);

    [RelayCommand]
    private void SelectTrash() => SelectTasksSection(MainSection.Trash);

    public void FocusQuickAdd()
    {
        if (Page != MainPage.Tasks)
        {
            Page = MainPage.Tasks;
        }

        if (TasksPage.Section is MainSection.Completed or MainSection.Trash)
        {
            TasksPage.SelectSection(MainSection.Today);
        }

        TasksPage.FocusAddCommand.Execute(null);
    }

    public bool HandleEscape()
    {
        if (IsClosePromptOpen)
        {
            IsClosePromptOpen = false;
            return true;
        }

        if (IsMcpConfirmOverlayOpen)
        {
            IsMcpConfirmOverlayOpen = false;
            return true;
        }

        switch (Page)
        {
            case MainPage.Tasks:
                if (TasksPage.IsDraftCalendarOpen)
                {
                    TasksPage.IsDraftCalendarOpen = false;
                    return true;
                }

                if (TasksPage.IsDraftListMenuOpen)
                {
                    TasksPage.IsDraftListMenuOpen = false;
                    return true;
                }

                if (TasksPage.IsSortMenuOpen)
                {
                    TasksPage.IsSortMenuOpen = false;
                    return true;
                }

                if (TasksPage.Detail is { } taskDetail)
                {
                    if (taskDetail.CloseOpenPopups())
                    {
                        return true;
                    }

                    TasksPage.ClearSelection();
                    return true;
                }

                return false;

            case MainPage.Eisenhower:
                if (EisenhowerPage.IsQuickAddOpen)
                {
                    EisenhowerPage.CloseQuickCreate();
                    return true;
                }

                return false;

            case MainPage.Calendar:
                if (CalendarPage.IsQuickAddOpen)
                {
                    CalendarPage.IsQuickAddOpen = false;
                    return true;
                }

                if (CalendarPage.IsOverflowOpen)
                {
                    CalendarPage.IsOverflowOpen = false;
                    return true;
                }

                if (CalendarPage.Detail is { } calDetail)
                {
                    if (calDetail.CloseOpenPopups())
                    {
                        return true;
                    }

                    CalendarPage.ClearSelection();
                    return true;
                }

                return false;

            case MainPage.Assistant:
                if (AssistantPage.ClosePopups())
                {
                    return true;
                }

                if (AssistantPage.IsGenerating)
                {
                    AssistantPage.StopGeneration();
                    return true;
                }

                return false;

            case MainPage.Settings:
                if (SettingsPage.HandleEscape())
                {
                    return true;
                }

                Page = MainPage.Tasks;
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _repository.Changed -= OnRepositoryChanged;
        _repository.ListsChanged -= OnListsChanged;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        _coordinator.MorningBriefRequested -= OnMorningBriefRequested;
        _coordinator.McpApplied -= OnMcpApplied;
        _coordinator.McpConfirmationAdded -= OnMcpConfirmationAdded;
        _coordinator.McpConfirmationsChanged -= OnMcpConfirmationsChanged;
        TasksPage.PropertyChanged -= OnTasksPagePropertyChanged;
        AssistantPage.PropertyChanged -= OnAssistantPagePropertyChanged;
        TasksPage.Dispose();
        EisenhowerPage.Dispose();
        CalendarPage.Dispose();
        AssistantPage.Dispose();
        SettingsPage.Dispose();
    }

    /// <summary>选中侧栏中的某个自建清单。</summary>
    public void SelectList(string listId)
    {
        if (Page != MainPage.Tasks)
        {
            Page = MainPage.Tasks;
        }

        TasksPage.SelectList(listId);
        SyncListSelection();
    }

    public ListSidebarItem? FindList(string id) => Lists.FirstOrDefault(l => l.Id == id);

    [RelayCommand]
    private void AddList()
    {
        var list = _repository.CreateList("新建清单");
        SelectList(list.Id);
        FindList(list.Id)?.BeginEdit();
    }

    public void BeginRenameList(string id) => FindList(id)?.BeginEdit();

    public void CommitRenameList(ListSidebarItem item)
    {
        if (!item.IsEditing)
        {
            return;
        }

        item.EndEdit();
        _repository.RenameList(item.Id, item.EditName);
    }

    public void CancelRenameList(ListSidebarItem item) => item.EndEdit();

    public void DeleteList(ListSidebarItem item)
    {
        _repository.DeleteList(item.Id);
        Toast.Show($"已删除清单「{item.Name}」，其中的任务已移至收集箱");
    }

    private void OnRepositoryChanged(object? sender, TaskChangedEventArgs e) =>
        RunOnUi(() =>
        {
            if (!_disposed) RefreshLists();
        });

    private void OnListsChanged(object? sender, EventArgs e) =>
        RunOnUi(() =>
        {
            if (!_disposed) RefreshLists();
        });

    private void OnTasksPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TasksPageViewModel.Section) or nameof(TasksPageViewModel.CurrentListId))
        {
            SyncListSelection();
        }
    }

    private void RefreshLists()
    {
        var editingId = Lists.FirstOrDefault(l => l.IsEditing)?.Id;
        var counts = _repository.Query(new TaskQuery())
            .Where(t => !t.IsCompleted)
            .GroupBy(t => t.ListId)
            .ToDictionary(g => g.Key, g => g.Count());

        Lists.Clear();
        foreach (var list in _repository.Lists.Where(l => l.Id != TaskList.InboxId))
        {
            var item = new ListSidebarItem(
                list.Id,
                list.Name,
                list.Color,
                counts.TryGetValue(list.Id, out var count) ? count : 0);

            if (editingId == list.Id)
            {
                item.BeginEdit();
            }

            Lists.Add(item);
        }

        SyncListSelection();
    }

    private void SyncListSelection()
    {
        var inList = TasksPage.Section == MainSection.List;
        foreach (var item in Lists)
        {
            item.IsSelected = inList && string.Equals(item.Id, TasksPage.CurrentListId, StringComparison.Ordinal);
        }
    }

    partial void OnPageChanged(MainPage value)
    {
        OnPropertyChanged(nameof(IsTasksPage));
        OnPropertyChanged(nameof(IsEisenhowerPage));
        OnPropertyChanged(nameof(IsCalendarPage));
        OnPropertyChanged(nameof(IsAssistantPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsSidebarVisible));
        if (value == MainPage.Assistant)
        {
            AssistantPage.RefreshGreeting(pickNewVariant: true);
        }

        AssistantPage.IsPageActive = value == MainPage.Assistant;
        SyncLanaNavState();
        PageSwitched?.Invoke(this, value);
    }

    private void OnAssistantPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantPageViewModel.StageLanaState))
        {
            SyncLanaNavState();
        }
    }

    private void SyncLanaNavState() =>
        LanaNavState = IsAssistantPage ? AssistantPage.StageLanaState : LanaVisualState.Sleep;

    /// <summary>控件完成 Happy 庆祝后回落到 Idle / Listening（由主窗口或对话页事件转发）。</summary>
    public void CompleteLanaHappy() => AssistantPage.CompleteLanaHappy();

    partial void OnIsSidebarCollapsedChanged(bool value) =>
        OnPropertyChanged(nameof(IsSidebarVisible));

    private void SelectTasksSection(MainSection section)
    {
        if (Page != MainPage.Tasks)
        {
            Page = MainPage.Tasks;
        }

        TasksPage.SelectSection(section);
    }
}
