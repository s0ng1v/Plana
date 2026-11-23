using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.Widgets.Support;

namespace VGList.Desktop.Services;

/// <summary>底部浮动提示与单步撤销服务。</summary>
public sealed partial class ToastService : ObservableObject
{
    private CancellationTokenSource? _dismissCts;
    private Action? _undoAction;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _actionLabel = "撤销";

    [ObservableProperty]
    private bool _hasAction;

    [ObservableProperty]
    private bool _isPaused;

    public event EventHandler? Shown;

    [RelayCommand]
    private void ExecuteAction()
    {
        var action = _undoAction;
        Dismiss();
        action?.Invoke();
    }

    [RelayCommand]
    public void Dismiss() => UiDispatcher.PostIfNeeded(() =>
    {
        _dismissCts?.Cancel();
        _dismissCts = null;
        _undoAction = null;
        IsVisible = false;
    });

    public void Show(string message, Action? undoAction = null, string actionLabel = "撤销", int durationMs = 4000) =>
        UiDispatcher.PostIfNeeded(() =>
        {
            _dismissCts?.Cancel();
            _undoAction = undoAction;
            Message = message;
            ActionLabel = actionLabel;
            HasAction = undoAction is not null;
            IsVisible = true;
            Shown?.Invoke(this, EventArgs.Empty);

            var cts = new CancellationTokenSource();
            _dismissCts = cts;
            _ = AutoDismissAsync(cts.Token, durationMs);
        });

    private async Task AutoDismissAsync(CancellationToken token, int durationMs)
    {
        try
        {
            var elapsed = 0;
            const int step = 200;
            while (elapsed < durationMs)
            {
                await Task.Delay(step, token);
                if (!IsPaused)
                {
                    elapsed += step;
                }
            }

            if (!token.IsCancellationRequested)
            {
                Dismiss();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }
}
