using Avalonia.Controls;
using Avalonia.Input;
using VGList.Desktop.Services;

namespace VGList.Desktop.Controls;

public partial class ToastBanner : UserControl
{
    private readonly Border _pill;
    private ToastService? _subscribedService;

    public ToastBanner()
    {
        InitializeComponent();
        _pill = this.FindControl<Border>("Pill")!;
        DataContextChanged += OnDataContextChanged;
    }

    private ToastService? Service => DataContext as ToastService;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedService is not null)
        {
            _subscribedService.Shown -= OnToastShown;
        }

        _subscribedService = Service;
        if (_subscribedService is not null)
        {
            _subscribedService.Shown += OnToastShown;
        }
    }

    private void OnToastShown(object? sender, EventArgs e) =>
        MotionHelper.PlayEnter(_pill, offsetY: 12, durationMs: 160);

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (Service is { } service)
        {
            service.IsPaused = true;
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (Service is { } service)
        {
            service.IsPaused = false;
        }
    }

    private void OnActionPressed(object? sender, PointerPressedEventArgs e)
    {
        Service?.ExecuteActionCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDismissPressed(object? sender, PointerPressedEventArgs e)
    {
        Service?.Dismiss();
        e.Handled = true;
    }
}
