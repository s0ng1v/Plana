using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using VGList.Desktop.Main.Tasks;
using VGList.Desktop.Services;

namespace VGList.Desktop.Controls;

public partial class TaskRowControl : UserControl
{
    public TaskRowControl()
    {
        InitializeComponent();
    }

    public event EventHandler<MainTaskRowViewModel>? RowSelected;
    public event EventHandler<MainTaskRowViewModel>? CheckToggled;
    public event EventHandler<MainTaskRowViewModel>? QuickRescheduleRequested;
    public event EventHandler<MainTaskRowViewModel>? QuickPriorityRequested;
    public event EventHandler<MainTaskRowViewModel>? QuickDeleteRequested;

    /// <summary>供支持拖拽的宿主（如四象限）启用：按下时不立即触发选中，交由宿主按拖动距离区分点击或拖拽。</summary>
    public bool DeferSelectionUntilRelease { get; set; }

    private MainTaskRowViewModel? Row => DataContext as MainTaskRowViewModel;

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DeferSelectionUntilRelease)
        {
            return;
        }

        if (Row is { } row && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            RowSelected?.Invoke(this, row);
            e.Handled = true;
        }
    }

    private void OnCheckPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Row is not { } row)
        {
            return;
        }

        e.Handled = true;

        if (row.IsCompleted || MotionHelper.ReduceMotion)
        {
            CheckToggled?.Invoke(this, row);
            return;
        }

        if (row.IsCompleting)
        {
            return;
        }

        row.IsCompleting = true;
        DispatcherTimer.RunOnce(
            () =>
            {
                CheckToggled?.Invoke(this, row);
                row.IsCompleting = false;
            },
            TimeSpan.FromMilliseconds(320));
    }

    private void OnQuickReschedulePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Row is { } row)
        {
            QuickRescheduleRequested?.Invoke(this, row);
        }

        e.Handled = true;
    }

    private void OnQuickPriorityPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Row is { } row)
        {
            QuickPriorityRequested?.Invoke(this, row);
        }

        e.Handled = true;
    }

    private void OnQuickDeletePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Row is { } row)
        {
            QuickDeleteRequested?.Invoke(this, row);
        }

        e.Handled = true;
    }
}
