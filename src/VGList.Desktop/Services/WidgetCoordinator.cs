using VGList.App.Assistant;
using VGList.App.Mcp;
using VGList.App.Services;
using VGList.App.Settings;
using VGList.Core.Abstractions;
using VGList.Platform;
using VGList.Widgets.Month;
using VGList.Widgets.Today;

namespace VGList.Desktop.Services;

/// <summary>协调主窗口与两个桌面小组件之间的显隐、设置同步、早安提示、动效偏好、外部 MCP 桥与生命周期关闭。</summary>
public sealed class WidgetCoordinator
{
    private readonly ITaskRepository _repository;
    private readonly SettingsStore _settingsStore;
    private readonly MainSettings _mainSettings;
    private readonly McpAuditLog _mcpAuditLog;
    private readonly McpPendingConfirmationQueue _mcpConfirmations = new();
    private readonly Func<bool, bool, bool> _startupRegistrySync;
    private TodayWidgetWindow? _todayWindow;
    private MonthWidgetWindow? _monthWindow;
    private ExternalAccessBridge? _mcpBridge;
    private bool _shuttingDown;

    public WidgetCoordinator(
        ITaskRepository repository,
        SettingsStore settingsStore,
        string? assistantDir = null,
        string? mcpDir = null,
        Func<bool, bool, bool>? startupRegistrySync = null)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _mainSettings = settingsStore.LoadMain();
        _startupRegistrySync = startupRegistrySync ?? ((enabled, minimized) => PlatformServices.Startup.TrySetEnabled(enabled, minimized));
        BriefService = new MorningBriefService(repository, assistantDir);
        _mcpAuditLog = new McpAuditLog(mcpDir);
        _mcpConfirmations.RequestAdded += (_, req) => McpConfirmationAdded?.Invoke(this, req);
        _mcpConfirmations.Changed += (_, _) => McpConfirmationsChanged?.Invoke(this, EventArgs.Empty);
        MotionHelper.ReduceMotion = _mainSettings.ReduceMotion;
        AutoDetectDatePreference.IsEnabled = _mainSettings.AutoDetectDate;
    }

    public event EventHandler? StateChanged;
    public event EventHandler<MorningBriefInfo>? MorningBriefRequested;
    public event EventHandler<McpAppliedEventArgs>? McpApplied;
    public event EventHandler<McpPendingConfirmation>? McpConfirmationAdded;
    public event EventHandler? McpConfirmationsChanged;
    public event EventHandler? McpAuditUpdated;

    public MainSettings MainSettings => _mainSettings;

    public MorningBriefService BriefService { get; }

    public McpPendingConfirmationQueue McpConfirmations => _mcpConfirmations;

    public bool IsMcpListening => _mcpBridge is { IsListening: true };

    public string McpPipeName => _mcpBridge?.PipeName ?? PlatformServices.Mcp.GetCurrentUserPipeName();

    public bool IsTodayVisible => _todayWindow is { IsVisible: true };

    public bool IsMonthVisible => _monthWindow is { IsVisible: true };

    public WidgetSettings TodaySettings => _todayWindow?.CurrentSettings ?? _settingsStore.LoadWidget();

    public WidgetSettings MonthSettings => _monthWindow?.CurrentSettings ?? _settingsStore.LoadMonth();

    public void InitializeWidgets()
    {
        if (_mainSettings.ShowTodayWidget)
        {
            EnsureTodayWindow().Show();
        }

        if (_mainSettings.ShowMonthWidget)
        {
            EnsureMonthWindow().Show();
        }
    }

    public void SetTodayVisible(bool visible)
    {
        _mainSettings.ShowTodayWidget = visible;
        _settingsStore.SaveMain(_mainSettings);

        if (visible)
        {
            var window = EnsureTodayWindow();
            window.Show();
            window.Activate();
        }
        else
        {
            _todayWindow?.Hide();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthVisible(bool visible)
    {
        _mainSettings.ShowMonthWidget = visible;
        _settingsStore.SaveMain(_mainSettings);

        if (visible)
        {
            var window = EnsureMonthWindow();
            window.Show();
            window.Activate();
        }
        else
        {
            _monthWindow?.Hide();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayOpacity(double opacity)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetBackdropOpacity(opacity, persist: true);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.Opacity = Math.Clamp(opacity, 0.4, 1.0);
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayTopmost(bool topmost)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetWidgetTopmost(topmost);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.IsTopmost = topmost;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayShowInput(bool showInput)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetShowInput(showInput);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.ShowInput = showInput;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayShowCompleted(bool showCompleted)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetShowCompleted(showCompleted);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.ShowCompleted = showCompleted;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayLocked(bool locked)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetLocked(locked);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.IsLocked = locked;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTodayClickThrough(bool clickThrough)
    {
        if (_todayWindow is not null)
        {
            _todayWindow.SetClickThrough(clickThrough);
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.IsClickThrough = clickThrough;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetTodayPosition()
    {
        if (_todayWindow is not null)
        {
            _todayWindow.ResetPosition();
        }
        else
        {
            var s = _settingsStore.LoadWidget();
            s.X = null;
            s.Y = null;
            s.Width = null;
            s.Height = null;
            _settingsStore.SaveWidget(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthOpacity(double opacity)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetBackdropOpacity(opacity, persist: true);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.Opacity = Math.Clamp(opacity, 0.4, 1.0);
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthTopmost(bool topmost)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetWidgetTopmost(topmost);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.IsTopmost = topmost;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthShowCompleted(bool showCompleted)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetShowCompleted(showCompleted);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.ShowCompleted = showCompleted;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthShowLunar(bool showLunar)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetShowLunar(showLunar);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.ShowLunar = showLunar;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthLocked(bool locked)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetLocked(locked);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.IsLocked = locked;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMonthClickThrough(bool clickThrough)
    {
        if (_monthWindow is not null)
        {
            _monthWindow.SetClickThrough(clickThrough);
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.IsClickThrough = clickThrough;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DisableAllClickThrough()
    {
        SetTodayClickThrough(false);
        SetMonthClickThrough(false);
    }

    public void SetKeepWidgetsOnShowDesktop(bool enabled)
    {
        _mainSettings.KeepWidgetsOnShowDesktop = enabled;
        _settingsStore.SaveMain(_mainSettings);
        _todayWindow?.SetKeepOnDesktop(enabled);
        _monthWindow?.SetKeepOnDesktop(enabled);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SetLaunchAtStartup(bool enabled)
    {
        var ok = _startupRegistrySync(enabled, _mainSettings.StartMinimizedToTray);
        if (!ok)
        {
            return false;
        }

        _mainSettings.LaunchAtStartup = enabled;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetStartMinimizedToTray(bool minimized)
    {
        _mainSettings.StartMinimizedToTray = minimized;
        _settingsStore.SaveMain(_mainSettings);
        if (_mainSettings.LaunchAtStartup)
        {
            _startupRegistrySync(true, minimized);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetMonthPosition()
    {
        if (_monthWindow is not null)
        {
            _monthWindow.ResetPosition();
        }
        else
        {
            var s = _settingsStore.LoadMonth();
            s.X = null;
            s.Y = null;
            s.Width = null;
            s.Height = null;
            _settingsStore.SaveMonth(s);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetReduceMotion(bool reduceMotion)
    {
        _mainSettings.ReduceMotion = reduceMotion;
        MotionHelper.ReduceMotion = reduceMotion;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetTheme(string theme)
    {
        var normalized = AppTheme.Normalize(theme);
        if (string.Equals(_mainSettings.Theme, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _mainSettings.Theme = normalized;
        _settingsStore.SaveMain(_mainSettings);
        AppTheme.Apply(normalized, _mainSettings.FontPreset);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetFontPreset(string fontPreset)
    {
        var normalized = FontPresetMode.Normalize(fontPreset);
        if (string.Equals(_mainSettings.FontPreset, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _mainSettings.FontPreset = normalized;
        _settingsStore.SaveMain(_mainSettings);
        AppTheme.ApplyFontPreset(normalized);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSidebarCollapsed(bool collapsed)
    {
        _mainSettings.IsSidebarCollapsed = collapsed;
        _settingsStore.SaveMain(_mainSettings);
    }

    public void SetCalendarShowCompleted(bool showCompleted)
    {
        _mainSettings.CalendarShowCompleted = showCompleted;
        _settingsStore.SaveMain(_mainSettings);
    }

    public void SetAutoDetectDate(bool enabled)
    {
        _mainSettings.AutoDetectDate = enabled;
        AutoDetectDatePreference.IsEnabled = enabled;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAllowAssistantReadTasks(bool allowed)
    {
        _mainSettings.AllowAssistantReadTasks = allowed;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetCloseAction(string closeAction)
    {
        var normalized = WindowCloseAction.Normalize(closeAction);
        if (string.Equals(_mainSettings.CloseAction, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _mainSettings.CloseAction = normalized;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMorningBriefEnabled(bool enabled)
    {
        _mainSettings.MorningBriefEnabled = enabled;
        _settingsStore.SaveMain(_mainSettings);
        _todayWindow?.RefreshMorningBrief();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshMorningBriefs(DateOnly? currentDate = null)
    {
        _todayWindow?.RefreshMorningBrief(currentDate);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>仅当用户开启了外部 AI 接入（默认关）时才启动本机命名管道监听。</summary>
    public void InitializeMcpIfEnabled()
    {
        if (_mainSettings.McpEnabled)
        {
            EnsureMcpBridgeRunning();
        }
    }

    public void SetMcpEnabled(bool enabled)
    {
        if (_mainSettings.McpEnabled == enabled && (enabled == IsMcpListening))
        {
            return;
        }

        _mainSettings.McpEnabled = enabled;
        _settingsStore.SaveMain(_mainSettings);

        if (enabled)
        {
            EnsureMcpBridgeRunning();
        }
        else
        {
            StopMcpBridge();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetMcpReadOnly(bool readOnly)
    {
        if (_mainSettings.McpReadOnly == readOnly)
        {
            return;
        }

        _mainSettings.McpReadOnly = readOnly;
        _settingsStore.SaveMain(_mainSettings);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool ResolveMcpConfirmation(string requestId, bool approved) =>
        _mcpConfirmations.Resolve(requestId, approved);

    public IReadOnlyList<McpAuditEntry> LoadMcpAuditEntries(int maxCount = 50) =>
        _mcpAuditLog.LoadRecent(maxCount);

    public void ClearMcpAuditEntries()
    {
        _mcpAuditLog.Clear();
        McpAuditUpdated?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureMcpBridgeRunning()
    {
        if (_repository is not IScheduleService schedule)
        {
            return;
        }

        if (_mcpBridge is null)
        {
            var pipeName = PlatformServices.Mcp.GetCurrentUserPipeName();
            _mcpBridge = new ExternalAccessBridge(
                repository: _repository,
                schedule: schedule,
                isReadOnly: () => _mainSettings.McpReadOnly,
                pipeName: pipeName,
                serverPipeFactory: name => PlatformServices.Mcp.CreateCurrentUserOnlyServerPipe(name),
                auditLog: _mcpAuditLog,
                confirmationQueue: _mcpConfirmations);
            _mcpBridge.Applied += (_, args) => McpApplied?.Invoke(this, args);
            _mcpBridge.AuditUpdated += (_, _) => McpAuditUpdated?.Invoke(this, EventArgs.Empty);
        }

        _mcpBridge.Start();
    }

    private void StopMcpBridge()
    {
        if (_mcpBridge is null)
        {
            return;
        }

        _mcpBridge.Dispose();
        _mcpBridge = null;
    }

    public VGList.Core.Models.ChangeSet? ResetSampleTasks()
    {
        if (_repository is IScheduleService schedule)
        {
            return schedule.ReplaceAllTasks(SampleData.CreateSeedTasks(), "重置为示例数据");
        }

        if (_repository is InMemoryTaskRepository memory)
        {
            memory.ReplaceAll(SampleData.CreateSeedTasks());
        }

        return null;
    }

    public bool UndoChangeSet(string changeSetId)
    {
        if (_repository is IScheduleService schedule)
        {
            return schedule.Undo(changeSetId).Success;
        }

        return false;
    }

    public void CloseAll()
    {
        _shuttingDown = true;
        StopMcpBridge();
        _todayWindow?.Close();
        _monthWindow?.Close();
    }

    private TodayWidgetWindow EnsureTodayWindow()
    {
        if (_todayWindow is not null)
        {
            return _todayWindow;
        }

        var vm = new TodayWidgetViewModel(
            _repository,
            briefService: BriefService,
            morningBriefEnabled: () => _mainSettings.MorningBriefEnabled);
        var window = new TodayWidgetWindow(vm, _settingsStore);
        window.MorningBriefActivated += (_, info) => MorningBriefRequested?.Invoke(this, info);
        window.SettingsUpdated += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_todayWindow, window))
            {
                _todayWindow = null;
            }

            if (!_shuttingDown)
            {
                _mainSettings.ShowTodayWidget = false;
                _settingsStore.SaveMain(_mainSettings);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        _todayWindow = window;
        return window;
    }

    private MonthWidgetWindow EnsureMonthWindow()
    {
        if (_monthWindow is not null)
        {
            return _monthWindow;
        }

        var vm = new MonthWidgetViewModel(_repository);
        var window = new MonthWidgetWindow(vm, _settingsStore);
        window.SettingsUpdated += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_monthWindow, window))
            {
                _monthWindow = null;
            }

            if (!_shuttingDown)
            {
                _mainSettings.ShowMonthWidget = false;
                _settingsStore.SaveMain(_mainSettings);
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        _monthWindow = window;
        return window;
    }
}
