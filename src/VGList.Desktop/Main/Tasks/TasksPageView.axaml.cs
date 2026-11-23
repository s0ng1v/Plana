using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VGList.Desktop.Controls;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Tasks;

public partial class TasksPageView : UserControl
{
    private readonly Panel _tasksHost;
    private readonly TextBox _addBox;
    private readonly Border _addBoxBorder;
    private readonly Grid _addBoxLayout;
    private readonly StackPanel _draftActionsPanel;
    private readonly Border _draftListButton;
    private readonly Panel _listContentHost;
    private readonly Border _sortButton;
    private readonly Avalonia.Controls.Shapes.Path _plusIcon;
    private readonly Border _draftDateChip;
    private readonly Border _sortOverlay;
    private readonly Border _draftListOverlay;
    private readonly DatePickerPopover _draftDateOverlay;
    private bool _chipInline = true;
    private bool? _verticalSingle;
    private double _lastLayoutWidth;
    private TasksPageViewModel? _subscribedVm;
    private Window? _attachedWindow;

    public TasksPageView()
    {
        InitializeComponent();

        _tasksHost = this.FindControl<Panel>("TasksHost")!;
        _addBox = this.FindControl<TextBox>("AddBoxInput")!;
        _addBoxBorder = this.FindControl<Border>("AddBoxBorder")!;
        _addBoxLayout = this.FindControl<Grid>("AddBoxLayout")!;
        _draftActionsPanel = this.FindControl<StackPanel>("DraftActionsPanel")!;
        _draftListButton = this.FindControl<Border>("DraftListButton")!;
        _listContentHost = this.FindControl<Panel>("ListContentHost")!;
        _sortButton = this.FindControl<Border>("SortButton")!;
        _plusIcon = this.FindControl<Avalonia.Controls.Shapes.Path>("PlusIcon")!;
        _draftDateChip = this.FindControl<Border>("DraftDateChip")!;
        _sortOverlay = this.FindControl<Border>("SortOverlay")!;
        _draftListOverlay = this.FindControl<Border>("DraftListOverlay")!;
        _draftDateOverlay = this.FindControl<DatePickerPopover>("DraftDateOverlay")!;

        _addBox.AddHandler(KeyDownEvent, OnAddBoxKeyDown, RoutingStrategies.Tunnel);
        _addBox.GotFocus += (_, _) =>
        {
            _addBoxBorder.Classes.Set("focused", true);
            if (ViewModel is { } vm)
            {
                vm.IsDraftFocused = true;
            }

            Dispatcher.UIThread.Post(UpdateChipPlacement, DispatcherPriority.Background);
        };
        _addBox.LostFocus += (_, _) =>
        {
            _addBoxBorder.Classes.Set("focused", false);
            if (ViewModel is { } vm && !vm.IsDraftCalendarOpen && !vm.IsDraftListMenuOpen)
            {
                vm.IsDraftFocused = false;
            }
        };

        _addBox.TextChanged += (_, _) =>
            Dispatcher.UIThread.Post(UpdateChipPlacement, DispatcherPriority.Background);
        _addBoxLayout.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - _lastLayoutWidth) >= 2)
            {
                _lastLayoutWidth = e.NewSize.Width;
                UpdateChipPlacement();
            }
        };

        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// 捕获整个窗口的点击：点左侧清单栏、导航栏等任务页之外的地方也要让录入框失焦。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _attachedWindow = window;
            window.AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_attachedWindow is { } window)
        {
            window.RemoveHandler(PointerPressedEvent, OnPagePointerPressed);
            _attachedWindow = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private TasksPageViewModel? ViewModel => DataContext as TasksPageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.ViewAnimatedRefresh -= OnViewAnimatedRefresh;
            _subscribedVm.FocusAddRequested -= OnFocusAddRequested;
        }

        _subscribedVm = ViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.ViewAnimatedRefresh += OnViewAnimatedRefresh;
            _subscribedVm.FocusAddRequested += OnFocusAddRequested;
        }
    }

    private void OnViewAnimatedRefresh(object? sender, EventArgs e) =>
        MotionHelper.PlayEnter(_listContentHost, offsetY: 6, durationMs: 180);

    private void OnFocusAddRequested(object? sender, EventArgs e)
    {
        if (ViewModel is { IsInputVisible: true })
        {
            _addBox.Focus();
            _addBox.CaretIndex = _addBox.Text?.Length ?? 0;
        }
    }

    private void OnAddBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel is { IsDraftCalendarOpen: true } vm)
            {
                vm.IsDraftCalendarOpen = false;
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Tab)
        {
            ViewModel?.CycleDraftPriorityCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ViewModel?.SubmitDraft();
            e.Handled = true;
        }
    }

    private void OnAddButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.FocusAddCommand.Execute(null);
        e.Handled = true;
    }

    private void OnSortButtonPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsSortMenuOpen;
            vm.CloseOverlays();
            if (next)
            {
                vm.IsSortMenuOpen = true;
                OverlayPositioner.PlaceAndShow(_tasksHost, _sortOverlay, _sortButton, OverlayPlacement.BelowRightAligned);
            }
        }

        e.Handled = true;
    }

    private void OnSortDefaultPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetSortModeCommand.Execute(TaskSortMode.Default);
        e.Handled = true;
    }

    private void OnSortPriorityPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetSortModeCommand.Execute(TaskSortMode.Priority);
        e.Handled = true;
    }

    private void OnSortDatePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.SetSortModeCommand.Execute(TaskSortMode.Date);
        e.Handled = true;
    }

    private void OnDraftDatePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsDraftCalendarOpen;
            vm.CloseOverlays();
            if (next)
            {
                vm.ToggleDraftCalendarCommand.Execute(null);
                if (vm.IsDraftCalendarOpen)
                {
                    OverlayPositioner.PlaceAndShow(_tasksHost, _draftDateOverlay, _draftDateChip, OverlayPlacement.BelowLeftAligned);
                }
            }
        }

        e.Handled = true;
    }

    private void OnOverlayDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        // 点击位置落在录入框内：关闭月历/清单浮层并直接聚焦输入框，无需再点第二次！
        var posInAddBox = e.GetPosition(_addBoxBorder);
        var clickedAddBox = posInAddBox.X >= 0 && posInAddBox.Y >= 0 &&
                            posInAddBox.X <= _addBoxBorder.Bounds.Width &&
                            posInAddBox.Y <= _addBoxBorder.Bounds.Height;

        ViewModel?.CloseOverlays();
        if (clickedAddBox)
        {
            _addBox.Focus();
            _addBox.CaretIndex = _addBox.Text?.Length ?? 0;
        }
        else
        {
            ClearInputFocus();
        }

        e.Handled = true;
    }

    private void OnOverlayCardPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnDraftListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            var next = !vm.IsDraftListMenuOpen;
            vm.CloseOverlays();
            if (next)
            {
                vm.ToggleDraftListMenuCommand.Execute(null);
                OverlayPositioner.PlaceAndShow(_tasksHost, _draftListOverlay, _draftListButton, OverlayPlacement.BelowRightAligned);
            }
        }

        e.Handled = true;
    }

    private void OnDraftListPickPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: string listId })
        {
            ViewModel?.PickDraftList(listId);
            _addBox.Focus();
        }

        e.Handled = true;
    }

    private void OnGroupHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MainTaskGroupViewModel group })
        {
            ViewModel?.ToggleGroup(group);
        }

        e.Handled = true;
    }

    private void OnPostponeOverduePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.PostponeOverdue();
        e.Handled = true;
    }

    private void OnToggleCompleted(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleCompletedGroup();
        e.Handled = true;
    }

    private void OnListBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is { HasSelection: true } vm)
        {
            vm.ClearSelection();
        }
    }

    private void OnTaskRowSelected(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.SelectTask(row);

    private void OnTaskCheckToggled(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.ToggleTask(row);

    private void OnQuickReschedule(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.QuickReschedule(row);

    private void OnQuickPriority(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.QuickCyclePriority(row);

    private void OnQuickDelete(object? sender, MainTaskRowViewModel row) =>
        ViewModel?.DeleteTaskWithUndo(row.Task);

    // ---------- 录入框：点空白失焦 + 日期按钮换行下放 ----------

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source &&
            !IsWithin(source, _addBoxBorder) &&
            !IsWithin(source, _draftDateOverlay) &&
            !IsWithin(source, _draftListOverlay))
        {
            ClearInputFocus();
        }
    }

    private void ClearInputFocus()
    {
        if (!_addBox.IsFocused)
        {
            return;
        }

        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
    }

    /// <summary>离开任务页 / 切换分组时收起录入态与浮层。</summary>
    public void DismissTransient()
    {
        ViewModel?.CloseOverlays();
        ClearInputFocus();
    }

    /// <summary>
    /// 零重挂载换行切换：以恒定外层 _addBoxLayout.Bounds.Width 作为基准并加 16px 迟滞死区，
    /// 彻底杜绝 SizeChanged 振荡死循环与 TextLayout 句柄泄漏。
    /// </summary>
    private void UpdateChipPlacement()
    {
        var outerWidth = _addBoxLayout.Bounds.Width;
        if (outerWidth <= 80)
        {
            return;
        }

        var text = _addBox.Text ?? string.Empty;
        if (text.Contains('\n') || text.Contains('\r'))
        {
            ApplyChipPlacement(inline: false);
            ApplyVerticalLayout(single: false);
            return;
        }

        var actionsWidth = Math.Max(32, _draftActionsPanel.Bounds.Width > 0 ? _draftActionsPanel.Bounds.Width : _draftActionsPanel.DesiredSize.Width);
        // 迟滞死区：当前处于多行态时需比单行宽度余出 18px 才切回单行，杜绝临界抖动
        var hysteresis = _chipInline ? 0 : 18;
        var inlineTextWidth = Math.Max(48, outerWidth - 12 - 16 - actionsWidth - 12 - hysteresis);
        var single = MeasureLineCount(text, inlineTextWidth) <= 1;

        ApplyChipPlacement(single);
        ApplyVerticalLayout(single);
    }

    private void ApplyChipPlacement(bool inline)
    {
        if (_chipInline == inline)
        {
            return;
        }

        _chipInline = inline;
        if (inline)
        {
            Grid.SetColumnSpan(_addBox, 1);
            Grid.SetRow(_draftActionsPanel, 0);
            Grid.SetColumn(_draftActionsPanel, 2);
            Grid.SetColumnSpan(_draftActionsPanel, 1);
            _draftActionsPanel.Margin = new Thickness(0);
        }
        else
        {
            Grid.SetColumnSpan(_addBox, 2);
            Grid.SetRow(_draftActionsPanel, 1);
            Grid.SetColumn(_draftActionsPanel, 1);
            Grid.SetColumnSpan(_draftActionsPanel, 2);
            _draftActionsPanel.Margin = new Thickness(0, 6, 0, 0);
        }
    }

    private void ApplyVerticalLayout(bool single)
    {
        if (_verticalSingle == single)
        {
            return;
        }

        _verticalSingle = single;
        _addBox.VerticalContentAlignment = single
            ? Avalonia.Layout.VerticalAlignment.Center
            : Avalonia.Layout.VerticalAlignment.Top;
        _plusIcon.VerticalAlignment = single
            ? Avalonia.Layout.VerticalAlignment.Center
            : Avalonia.Layout.VerticalAlignment.Top;
        _plusIcon.Margin = single ? new Thickness(0) : new Thickness(0, 5, 0, 0);
    }

    private int MeasureLineCount(string text, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            return 1;
        }

        var typeface = new Typeface(_addBox.FontFamily, _addBox.FontStyle, _addBox.FontWeight);
        using var layout = new TextLayout(
            text,
            typeface,
            _addBox.FontSize,
            Brushes.Black,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            maxWidth: maxWidth);

        return Math.Max(1, layout.TextLines.Count);
    }

    private void OnMorningBriefPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ViewModel?.OpenMorningBrief();
        }

        e.Handled = true;
    }

    private void OnDismissMorningBriefPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ViewModel?.DismissMorningBrief();
        }

        e.Handled = true;
    }

    private void OnClearFiltersPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ClearFilters();
        e.Handled = true;
    }

    private void OnToggleRecurringFilterPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ToggleRecurringFilter();
        e.Handled = true;
    }

    private void OnTagFilterChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag)
        {
            ViewModel?.ToggleTagFilter(tag);
        }

        e.Handled = true;
    }

    private void OnRestoreTrashRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MainTaskRowViewModel row })
        {
            ViewModel?.RestoreTrashedTask(row);
        }

        e.Handled = true;
    }

    private void OnPermanentlyDeleteTrashRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MainTaskRowViewModel row })
        {
            ViewModel?.PermanentlyDeleteTrashedTask(row);
        }

        e.Handled = true;
    }

    private void OnBeginEmptyTrashPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.BeginEmptyTrash();
        e.Handled = true;
    }

    private void OnCancelEmptyTrashPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CancelEmptyTrash();
        e.Handled = true;
    }

    private void OnConfirmEmptyTrashPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ConfirmEmptyTrash();
        e.Handled = true;
    }

    private static bool IsWithin(Visual? source, Visual container)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (ReferenceEquals(visual, container))
            {
                return true;
            }
        }

        return false;
    }
}
