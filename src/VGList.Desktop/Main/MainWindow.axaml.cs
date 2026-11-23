using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Main.Assistant;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;
using VGList.Platform;
using PathShape = Avalonia.Controls.Shapes.Path;

namespace VGList.Desktop.Main;

/// <summary>
/// 主程序主窗口壳层：扩展标题栏与自绘三键、全局快捷键、侧栏过渡与页面切换动画。
/// </summary>
public partial class MainWindow : Window
{
    private const double DragStripHeight = 40;
    private const double SidebarExpandedWidth = 248;

    private readonly MainWindowViewModel _viewModel;
    private readonly TasksPageView _tasksPageView;
    private readonly AssistantPageView _assistantPageView;
    private readonly Border _sidebarHost;
    private readonly Border _tasksPageContainer;
    private readonly Border _eisenhowerPageContainer;
    private readonly Border _calendarPageContainer;
    private readonly Border _assistantPageContainer;
    private readonly Border _settingsPageContainer;
    private readonly PathShape _maximizeGlyph;
    private readonly PathShape _restoreGlyph;
    private readonly Border _closePromptScrim;
    private readonly Border _closePromptCard;
    private readonly Border _mcpConfirmScrim;
    private readonly Border _mcpConfirmCard;
    private readonly DispatcherTimer _dateRolloverTimer;
    private DateOnly _lastKnownDate = DateOnly.FromDateTime(DateTime.Today);
    private bool _allowRealClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        _sidebarHost = Find<Border>("SidebarHost");
        _tasksPageView = Find<TasksPageView>("TasksPageView");
        _assistantPageView = Find<AssistantPageView>("AssistantPageView");
        _tasksPageContainer = Find<Border>("TasksPageContainer");
        _eisenhowerPageContainer = Find<Border>("EisenhowerPageContainer");
        _calendarPageContainer = Find<Border>("CalendarPageContainer");
        _assistantPageContainer = Find<Border>("AssistantPageContainer");
        _settingsPageContainer = Find<Border>("SettingsPageContainer");
        _maximizeGlyph = Find<PathShape>("MaximizeGlyph");
        _restoreGlyph = Find<PathShape>("RestoreGlyph");
        _closePromptScrim = Find<Border>("ClosePromptScrim");
        _closePromptCard = Find<Border>("ClosePromptCard");
        _mcpConfirmScrim = Find<Border>("McpConfirmScrim");
        _mcpConfirmCard = Find<Border>("McpConfirmCard");

        SyncSidebarVisual();
        SyncMaximizeGlyph();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.PageSwitched += OnPageSwitched;
        _viewModel.MinimizeRequested += OnMinimizeRequested;
        _viewModel.ExitConfirmed += OnExitConfirmed;
        _viewModel.ActivateWindowRequested += OnActivateWindowRequested;
        _viewModel.McpAttentionRequested += OnMcpAttentionRequested;
        HookListItems();
        PropertyChanged += OnWindowPropertyChanged;

        // A6③：轻量跨天检查定时器（每分钟仅比较日期，凌晨 4 点后刷新早安提示）
        _dateRolloverTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _dateRolloverTimer.Tick += OnDateRolloverTick;
        _dateRolloverTimer.Start();
        Closed += (_, _) => _dateRolloverTimer.Stop();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
    }

    private T Find<T>(string name) where T : Control =>
        this.FindControl<T>(name) ?? throw new InvalidOperationException($"{name} 未在 XAML 中定义。");

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            SyncMaximizeGlyph();
        }
    }

    private void SyncMaximizeGlyph()
    {
        var isMax = WindowState == WindowState.Maximized;
        _maximizeGlyph.IsVisible = !isMax;
        _restoreGlyph.IsVisible = isMax;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarVisible))
        {
            SyncSidebarVisual();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsClosePromptOpen) && _viewModel.IsClosePromptOpen)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }

            MotionHelper.PlayEnter(_closePromptScrim, offsetX: 0, offsetY: 0, durationMs: 140);
            MotionHelper.PlayEnter(_closePromptCard, offsetX: 0, offsetY: 10, durationMs: 180);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsMcpConfirmOverlayOpen) && _viewModel.IsMcpConfirmOverlayOpen)
        {
            MotionHelper.PlayEnter(_mcpConfirmScrim, offsetX: 0, offsetY: 0, durationMs: 140);
            MotionHelper.PlayEnter(_mcpConfirmCard, offsetX: 0, offsetY: 10, durationMs: 180);
        }
    }

    private void OnDateRolloverTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (today != _lastKnownDate && now.Hour >= 4)
        {
            _lastKnownDate = today;
            _viewModel.RefreshDateRollover(today);
        }
    }

    private void OnActivateWindowRequested(object? sender, EventArgs e) =>
        ShowFromTray();

    private void OnMcpAttentionRequested(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            ShowFromTray();
            _viewModel.IsMcpConfirmOverlayOpen = true;
            return;
        }

        if (!IsActive && TryGetPlatformHandle() is { Handle: var hwnd } && hwnd != IntPtr.Zero)
        {
            PlatformServices.Mcp.FlashTaskbarOnce(hwnd);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_allowRealClose ||
            e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown)
        {
            return;
        }

        if (!_viewModel.RequestWindowClose())
        {
            e.Cancel = true;
        }
    }

    private void OnMinimizeRequested(object? sender, EventArgs e) =>
        Hide();

    private void OnExitConfirmed(object? sender, EventArgs e) =>
        ForceExit();

    /// <summary>从系统托盘或二次启动唤起主窗口并置前激活。</summary>
    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>跳过关闭询问直接退出 Plana（用于托盘菜单或确认卡退出）。</summary>
    public void ForceExit()
    {
        _allowRealClose = true;
        Close();
    }

    private void SyncSidebarVisual()
    {
        var visible = _viewModel.IsSidebarVisible;
        _sidebarHost.Width = visible ? SidebarExpandedWidth : 0;
        _sidebarHost.Opacity = visible ? 1 : 0;
        _sidebarHost.IsHitTestVisible = visible;
    }

    private void OnPageSwitched(object? sender, MainPage page)
    {
        Border target = page switch
        {
            MainPage.Eisenhower => _eisenhowerPageContainer,
            MainPage.Calendar => _calendarPageContainer,
            MainPage.Assistant => _assistantPageContainer,
            MainPage.Settings => _settingsPageContainer,
            _ => _tasksPageContainer,
        };

        MotionHelper.PlayEnter(target, offsetX: 0, offsetY: 8, durationMs: 180);
        if (page == MainPage.Assistant)
        {
            _assistantPageView.FocusComposer();
        }
    }

    /// <summary>全局点空白失焦 + 顶部 40px 空白区域拖动窗口、双击最大化/还原；交互控件不拦截。</summary>
    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 点别的地方让当前 TextBox 自动失焦（如设置页自然语言偏好、画像、搜索框、Lana 输入框等）
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            FocusManager?.GetFocusedElement() is TextBox focused &&
            focused.Name != "AddBoxInput" &&
            e.Source is Visual clickSource &&
            !IsInsideTextBox(clickSource))
        {
            FocusManager.Focus(null);
        }

        if (_viewModel.IsClosePromptOpen || _viewModel.IsMcpConfirmOverlayOpen)
        {
            return;
        }

        if (e.GetPosition(this).Y >= DragStripHeight)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Visual source && IsInteractive(source))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnMinimizePressed(object? sender, PointerPressedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        e.Handled = true;
    }

    private void OnMaximizeRestorePressed(object? sender, PointerPressedEventArgs e)
    {
        ToggleMaximizeRestore();
        e.Handled = true;
    }

    private void OnClosePressed(object? sender, PointerPressedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnClosePromptScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CancelClosePromptCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCancelClosePromptPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CancelClosePromptCommand.Execute(null);
        e.Handled = true;
    }

    private void OnToggleRememberCloseChoicePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel.ToggleRememberCloseChoiceCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnConfirmMinimizeFromPromptPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel.ConfirmMinimizeFromPromptCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnConfirmExitFromPromptPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel.ConfirmExitFromPromptCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnMcpBadgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel.ToggleMcpConfirmOverlayCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnMcpConfirmScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CloseMcpConfirmOverlayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCloseMcpConfirmPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.CloseMcpConfirmOverlayCommand.Execute(null);
        e.Handled = true;
    }

    private void OnApproveMcpRequestPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string id)
        {
            _viewModel.ApproveMcpRequest(id);
        }

        e.Handled = true;
    }

    private void OnDenyMcpRequestPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string id)
        {
            _viewModel.DenyMcpRequest(id);
        }

        e.Handled = true;
    }

    private bool IsInsideTextBox(Visual source)
    {
        for (Visual? visual = source; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInteractive(Visual source)
    {
        for (Visual? visual = source; visual is not null && visual != this; visual = visual.GetVisualParent())
        {
            if (visual is TextBox or Button or ToggleButton or ScrollBar or Controls.PlanaBrandLogo)
            {
                return true;
            }

            if (visual is Control { Tag: "Interactive" })
            {
                return true;
            }
        }

        return false;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_viewModel.HandleEscape())
            {
                e.Handled = true;
            }

            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.D1:
                _viewModel.ShowTasksCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D2:
                _viewModel.ShowEisenhowerCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D3:
                _viewModel.ShowCalendarPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D4:
            case Key.J:
                _viewModel.ShowAssistantCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemComma:
                _viewModel.ShowSettingsCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.B:
                _viewModel.ToggleSidebarCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N:
                _viewModel.FocusQuickAdd();
                e.Handled = true;
                break;
            case Key.Z:
                if (FocusManager?.GetFocusedElement() is not TextBox &&
                    _viewModel.TryUndoLastAssistantChange())
                {
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnLanaHappyCompleted(object? sender, EventArgs e) => _viewModel.CompleteLanaHappy();

    private void OnBrandLogoPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel.ShowTasksCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRailItemPressed(object? sender, PointerPressedEventArgs e)
    {
        _tasksPageView.DismissTransient();

        switch ((sender as Control)?.Tag as string)
        {
            case "Tasks":
                _viewModel.ShowTasksCommand.Execute(null);
                break;
            case "Eisenhower":
                _viewModel.ShowEisenhowerCommand.Execute(null);
                break;
            case "Calendar":
                _viewModel.ShowCalendarPageCommand.Execute(null);
                break;
            case "Assistant":
                _viewModel.ShowAssistantCommand.Execute(null);
                break;
            case "Settings":
                _viewModel.ShowSettingsCommand.Execute(null);
                break;
        }

        e.Handled = true;
    }

    private void OnSideItemPressed(object? sender, PointerPressedEventArgs e)
    {
        _tasksPageView.DismissTransient();

        switch ((sender as Control)?.Tag as string)
        {
            case "Today":
                _viewModel.SelectTodayCommand.Execute(null);
                break;
            case "Next7Days":
                _viewModel.SelectNext7DaysCommand.Execute(null);
                break;
            case "Inbox":
                _viewModel.SelectInboxCommand.Execute(null);
                break;
            case "Completed":
                _viewModel.SelectCompletedCommand.Execute(null);
                break;
            case "Trash":
                _viewModel.SelectTrashCommand.Execute(null);
                break;
        }

        e.Handled = true;
    }

    private void OnToggleSidebarPressed(object? sender, PointerPressedEventArgs e)
    {
        _viewModel.ToggleSidebarCommand.Execute(null);
        e.Handled = true;
    }

    private void HookListItems()
    {
        _viewModel.Lists.CollectionChanged += OnListsCollectionChanged;
        foreach (var item in _viewModel.Lists)
        {
            item.PropertyChanged += OnListSidebarItemPropertyChanged;
        }
    }

    private void OnListsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ListSidebarItem item in e.OldItems)
            {
                item.PropertyChanged -= OnListSidebarItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ListSidebarItem item in e.NewItems)
            {
                item.PropertyChanged += OnListSidebarItemPropertyChanged;
            }
        }
    }

    /// <summary>清单进入重命名状态后，把焦点交给对应输入框并全选。</summary>
    private void OnListSidebarItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ListSidebarItem.IsEditing) ||
            sender is not ListSidebarItem { IsEditing: true } item)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var box = this.GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(t => ReferenceEquals(t.DataContext, item));
                box?.Focus();
                box?.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private void OnListSideItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if ((sender as Control)?.Tag is string id)
        {
            _tasksPageView.DismissTransient();
            _viewModel.SelectList(id);
        }

        e.Handled = true;
    }

    private void OnAddListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _tasksPageView.DismissTransient();
        _viewModel.AddListCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRenameListMenuClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string id)
        {
            _viewModel.BeginRenameList(id);
        }
    }

    private void OnDeleteListMenuClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string id && _viewModel.FindList(id) is { } item)
        {
            _viewModel.DeleteList(item);
        }
    }

    private void OnListNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ListSidebarItem item })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.CommitRenameList(item);
                e.Handled = true;
                break;
            case Key.Escape:
                _viewModel.CancelRenameList(item);
                e.Handled = true;
                break;
        }
    }

    private void OnListNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ListSidebarItem { IsEditing: true } item })
        {
            _viewModel.CommitRenameList(item);
        }
    }
}
