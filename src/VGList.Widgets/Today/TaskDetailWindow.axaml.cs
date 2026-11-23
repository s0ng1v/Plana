using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace VGList.Widgets.Today;

/// <summary>任务详情面板：标题与 Markdown 内容就地编辑，失焦或 ESC 关闭。</summary>
public partial class TaskDetailWindow : Window
{
    private const double ContentLineHeight = 22;

    private readonly TaskDetailViewModel _vm;
    private readonly TextBox _contentBox;
    private readonly Popup _slashPopup;
    private bool _activated;
    private bool _insertingSlash;

    public TaskDetailWindow(TaskDetailViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = vm;

        _contentBox = this.FindControl<TextBox>("ContentBox")
                      ?? throw new InvalidOperationException("ContentBox 未在 XAML 中定义。");
        _slashPopup = this.FindControl<Popup>("SlashPopup")
                      ?? throw new InvalidOperationException("SlashPopup 未在 XAML 中定义。");

        // 宿主窗口刚打开时可能抢走激活焦点，等激活稳定后再取回，避免一出现就因失焦关闭。
        Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(Activate, DispatcherPriority.Input);

            var titleBox = this.FindControl<TextBox>("TitleBox");
            if (titleBox is not null)
            {
                titleBox.Focus();
                titleBox.CaretIndex = titleBox.Text?.Length ?? 0;
            }
        };
        Activated += (_, _) => _activated = true;
        Deactivated += (_, _) =>
        {
            if (!_activated)
            {
                return;
            }

            // 斜杠菜单是独立弹层，打开期间窗口必然失活，不能因此关掉详情面板。
            if (_slashPopup.IsOpen)
            {
                return;
            }

            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!IsActive && !_slashPopup.IsOpen)
                    {
                        Close();
                    }
                },
                DispatcherPriority.Background);
        };

        // 菜单被点外部轻量关闭后，若窗口仍失活才真正关闭面板。
        _slashPopup.Closed += (_, _) => Dispatcher.UIThread.Post(
            () =>
            {
                if (_activated && !_insertingSlash && !IsActive)
                {
                    Close();
                }
            },
            DispatcherPriority.Background);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDragAreaPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    /// <summary>斜杠菜单出现在光标右侧，而不是压在文字左侧。</summary>
    private void ShowSlashMenu(int slashIndex)
    {
        var caret = GetCaretRect(slashIndex);
        _slashPopup.HorizontalOffset = caret.Right + 6;
        _slashPopup.VerticalOffset = caret.Bottom + 4;
        _slashPopup.IsOpen = true;
    }

    private Rect GetCaretRect(int index)
    {
        var text = _contentBox.Text ?? string.Empty;
        var width = _contentBox.Bounds.Width;
        if (width <= 0)
        {
            return default;
        }

        var before = text[..Math.Clamp(index, 0, text.Length)];
        var typeface = new Typeface(_contentBox.FontFamily, _contentBox.FontStyle, _contentBox.FontWeight);
        var layout = new TextLayout(
            before,
            typeface,
            _contentBox.FontSize,
            Brushes.White,
            TextAlignment.Left,
            TextWrapping.Wrap,
            TextTrimming.None,
            maxWidth: width,
            lineHeight: ContentLineHeight);

        return layout.HitTestTextPosition(before.Length);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_slashPopup.IsOpen)
        {
            _slashPopup.IsOpen = false;
        }
        else
        {
            Close();
        }

        e.Handled = true;
    }

    private void OnToggleCompleted(object? sender, PointerPressedEventArgs e)
    {
        _vm.ToggleCompleted();
        e.Handled = true;
    }

    private void OnCyclePriority(object? sender, PointerPressedEventArgs e)
    {
        _vm.CyclePriority();
        e.Handled = true;
    }

    private void OnContentTextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = _contentBox.Text ?? string.Empty;
        var caret = _contentBox.CaretIndex;

        if (caret > 0 && caret <= text.Length && text[caret - 1] == '/')
        {
            ShowSlashMenu(caret - 1);
        }
        else
        {
            _slashPopup.IsOpen = false;
        }
    }

    private void OnSlashItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: SlashMenuItem item })
        {
            InsertSlash(item);
            e.Handled = true;
        }
    }

    private void InsertSlash(SlashMenuItem item)
    {
        _insertingSlash = true;
        _slashPopup.IsOpen = false;

        var text = _contentBox.Text ?? string.Empty;
        var caret = Math.Clamp(_contentBox.CaretIndex, 0, text.Length);

        if (caret == 0 || text[caret - 1] != '/')
        {
            var slash = text.LastIndexOf('/');
            if (slash < 0)
            {
                return;
            }

            caret = slash + 1;
        }

        text = text.Remove(caret - 1, 1);

        var prefix = item.IsBlock && caret - 1 > 0 && text[caret - 2] != '\n' ? "\n" : string.Empty;
        var snippet = prefix + item.Snippet;
        text = text.Insert(caret - 1, snippet);

        _contentBox.Text = text;
        _contentBox.CaretIndex = caret - 1 + snippet.Length;
        Activate();
        _contentBox.Focus();

        // 菜单关闭引发的失活检查排在前，这里延后一拍解除保护。
        Dispatcher.UIThread.Post(() => _insertingSlash = false, DispatcherPriority.Background);
    }
}
