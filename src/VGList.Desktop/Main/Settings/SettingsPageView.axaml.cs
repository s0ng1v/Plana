using System.ComponentModel;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using VGList.App.Models;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Settings;

/// <summary>设置页与模型配置抽屉用到的轻量文案转换器。</summary>
public static class SettingsTextConverters
{
    public static readonly IValueConverter EyeGlyph =
        new FuncValueConverter<bool, string>(visible => visible ? "隐藏" : "显示");

    public static readonly IValueConverter FetchModelsLabel =
        new FuncValueConverter<bool, string>(loading => loading ? "拉取中…" : "拉取列表");

    public static readonly IValueConverter ChevronGlyph =
        new FuncValueConverter<bool, string>(expanded => expanded ? "▾" : "▸");
}

public partial class SettingsPageView : UserControl
{
    private const double OpacityMin = 0.4;
    private const double OpacityMax = 1.0;
    private const double KnobSize = 13;

    private readonly Border _todaySlider;
    private readonly Border _todayFill;
    private readonly Border _todayKnob;
    private readonly Border _monthSlider;
    private readonly Border _monthFill;
    private readonly Border _monthKnob;
    private readonly Border _drawerScrim;
    private readonly Border _drawerCard;
    private readonly ItemsControl _providerGridControl;
    private SettingsPageViewModel? _subscribedVm;
    private bool _draggingToday;
    private bool _draggingMonth;

    public SettingsPageView()
    {
        InitializeComponent();

        _todaySlider = this.FindControl<Border>("TodayOpacitySlider")!;
        _todayFill = this.FindControl<Border>("TodayOpacityFill")!;
        _todayKnob = this.FindControl<Border>("TodayOpacityKnobBorder")!;

        _monthSlider = this.FindControl<Border>("MonthOpacitySlider")!;
        _monthFill = this.FindControl<Border>("MonthOpacityFill")!;
        _monthKnob = this.FindControl<Border>("MonthOpacityKnobBorder")!;

        _drawerScrim = this.FindControl<Border>("DrawerScrim")!;
        _drawerCard = this.FindControl<Border>("DrawerCard")!;
        _providerGridControl = this.FindControl<ItemsControl>("ProviderGridControl")!;

        _todaySlider.SizeChanged += (_, _) => SyncSliders();
        _monthSlider.SizeChanged += (_, _) => SyncSliders();
        SizeChanged += (_, _) => SyncProviderGridColumns();
        DataContextChanged += OnDataContextChanged;
    }

    private SettingsPageViewModel? ViewModel => DataContext as SettingsPageViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm.ModelServices.PropertyChanged -= OnModelServicesPropertyChanged;
        }

        _subscribedVm = ViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
            _subscribedVm.ModelServices.PropertyChanged += OnModelServicesPropertyChanged;
            SyncSliders();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsPageViewModel.TodayOpacity)
            or nameof(SettingsPageViewModel.MonthOpacity)
            or nameof(SettingsPageViewModel.ActiveSection))
        {
            SyncSliders();
            SyncProviderGridColumns();
        }
    }

    private void OnModelServicesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModelServicesViewModel.IsDrawerOpen) &&
            ViewModel?.ModelServices.IsDrawerOpen == true)
        {
            MotionHelper.PlayEnter(_drawerScrim, offsetX: 0, offsetY: 0, durationMs: 160);
            MotionHelper.PlayEnter(_drawerCard, offsetX: 32, offsetY: 0, durationMs: 180);
        }
    }

    private void SyncProviderGridColumns()
    {
        var panel = _providerGridControl.ItemsPanelRoot as UniformGrid
            ?? _providerGridControl.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault();
        if (panel is not null)
        {
            // 扣除左侧 196px 二级导航后右侧宽度 < 620 时切单列
            panel.Columns = Bounds.Width < 820 ? 1 : 2;
        }
    }

    private void SyncSliders()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        PositionSlider(_todaySlider, _todayFill, _todayKnob, vm.TodayOpacity);
        PositionSlider(_monthSlider, _monthFill, _monthKnob, vm.MonthOpacity);
    }

    private static void PositionSlider(Border slider, Border fill, Border knob, double opacity)
    {
        var width = slider.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var travel = Math.Max(1, width - KnobSize);
        var ratio = Math.Clamp((opacity - OpacityMin) / (OpacityMax - OpacityMin), 0, 1);
        var offset = ratio * travel;

        fill.Width = offset + KnobSize / 2;
        knob.Margin = new Thickness(offset, 0, 0, 0);
    }

    private static double OpacityFromPointer(Border slider, double x)
    {
        var width = slider.Bounds.Width;
        if (width <= 0)
        {
            return 1.0;
        }

        var travel = Math.Max(1, width - KnobSize);
        var ratio = Math.Clamp((x - KnobSize / 2) / travel, 0, 1);
        return Math.Clamp(OpacityMin + ratio * (OpacityMax - OpacityMin), OpacityMin, OpacityMax);
    }

    private void OnTodayOpacityPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_todaySlider).Properties.IsLeftButtonPressed) return;
        _draggingToday = true;
        e.Pointer.Capture(_todaySlider);
        ViewModel?.UpdateTodayOpacity(OpacityFromPointer(_todaySlider, e.GetPosition(_todaySlider).X));
        e.Handled = true;
    }

    private void OnTodayOpacityMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingToday) return;
        ViewModel?.UpdateTodayOpacity(OpacityFromPointer(_todaySlider, e.GetPosition(_todaySlider).X));
        e.Handled = true;
    }

    private void OnTodayOpacityReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingToday)
        {
            _draggingToday = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnMonthOpacityPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_monthSlider).Properties.IsLeftButtonPressed) return;
        _draggingMonth = true;
        e.Pointer.Capture(_monthSlider);
        ViewModel?.UpdateMonthOpacity(OpacityFromPointer(_monthSlider, e.GetPosition(_monthSlider).X));
        e.Handled = true;
    }

    private void OnMonthOpacityMoved(object? sender, PointerEventArgs e)
    {
        if (!_draggingMonth) return;
        ViewModel?.UpdateMonthOpacity(OpacityFromPointer(_monthSlider, e.GetPosition(_monthSlider).X));
        e.Handled = true;
    }

    private void OnMonthOpacityReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingMonth)
        {
            _draggingMonth = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnResetTodayPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ResetTodayWidgetCommand.Execute(null);
        e.Handled = true;
    }

    private void OnResetMonthPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ResetMonthWidgetCommand.Execute(null);
        e.Handled = true;
    }

    private void OnBeginResetSamplePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.BeginResetSampleDataCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCancelResetSamplePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.CancelResetSampleDataCommand.Execute(null);
        e.Handled = true;
    }

    private void OnConfirmResetSamplePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ResetSampleDataCommand.Execute(null);
        e.Handled = true;
    }

    private async void OnExportBackupPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            ViewModel is not { } vm ||
            (sender as Control)?.Tag is not string format)
        {
            return;
        }

        e.Handled = true;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var dateStamp = DateTime.Now.ToString("yyyyMMdd");
        var (ext, label, title, fileType) = format.ToLowerInvariant() switch
        {
            "md" => ("md", "Markdown", "导出 Markdown 任务清单", new FilePickerFileType("Markdown 文件") { Patterns = ["*.md"] }),
            "ics" => ("ics", "ICS 日历", "导出 ICS 日历文件", new FilePickerFileType("iCalendar 日历") { Patterns = ["*.ics"] }),
            _ => ("json", "JSON", "导出 Plana JSON 备份", new FilePickerFileType("JSON 备份") { Patterns = ["*.json"] }),
        };

        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = $"Plana-Backup-{dateStamp}.{ext}",
                DefaultExtension = ext,
                FileTypeChoices = [fileType],
            });
            if (file is null)
            {
                return;
            }

            var content = vm.BuildExportContent(format);
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(content);
            await writer.FlushAsync();

            vm.NotifyExportSuccess(label, file.Name);
        }
        catch (Exception ex)
        {
            vm.NotifyBackupError($"导出失败：{ex.Message}");
        }
    }

    private async void OnImportBackupPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ViewModel is not { } vm)
        {
            return;
        }

        e.Handled = true;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要导入的备份或日历文件",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("支持的备份与日历") { Patterns = ["*.json", "*.md", "*.markdown", "*.ics"] },
                    new FilePickerFileType("JSON 备份") { Patterns = ["*.json"] },
                    new FilePickerFileType("Markdown 任务清单") { Patterns = ["*.md", "*.markdown"] },
                    new FilePickerFileType("ICS 日历") { Patterns = ["*.ics"] },
                ],
            });
            var file = files.FirstOrDefault();
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync();

            vm.ImportFromContent(file.Name, content);
        }
        catch (Exception ex)
        {
            vm.NotifyBackupError($"导入失败：{ex.Message}");
        }
    }

    private void OnPlanaThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ViewModel?.SetTheme(AppTheme.Plana);
        }

        e.Handled = true;
    }

    private void OnClassicBlueThemePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ViewModel?.SetTheme(AppTheme.ClassicBlue);
        }

        e.Handled = true;
    }

    private void OnCloseActionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string action)
        {
            ViewModel?.SetCloseAction(action);
        }

        e.Handled = true;
    }

    private void OnFontPresetPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string preset)
        {
            ViewModel?.SetFontPreset(preset);
        }

        e.Handled = true;
    }

    // ===== 左侧子页切换与模型服务交互 =====

    private void OnNavSectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        switch ((sender as Control)?.Tag as string)
        {
            case "General":
                ViewModel?.SelectSection(SettingsSection.General);
                break;
            case "Models":
                ViewModel?.SelectSection(SettingsSection.Models);
                break;
            case "Assistant":
                ViewModel?.SelectSection(SettingsSection.Assistant);
                break;
            case "Widgets":
                ViewModel?.SelectSection(SettingsSection.Widgets);
                break;
        }

        e.Handled = true;
    }

    private void OnOpenCustomModelPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.OpenCustomCard();
        e.Handled = true;
    }

    private void OnProviderFilterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        switch ((sender as Control)?.Tag as string)
        {
            case "All":
                ViewModel?.ModelServices.SetFilter(ProviderFilterKind.All);
                break;
            case "Domestic":
                ViewModel?.ModelServices.SetFilter(ProviderFilterKind.Domestic);
                break;
            case "Global":
                ViewModel?.ModelServices.SetFilter(ProviderFilterKind.Global);
                break;
            case "Configured":
                ViewModel?.ModelServices.SetFilter(ProviderFilterKind.Configured);
                break;
        }

        e.Handled = true;
    }

    private void OnEditActiveModelPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.OpenActiveInDrawer();
        e.Handled = true;
    }

    private void OnProviderCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            sender is Control { DataContext: ProviderCardViewModel card })
        {
            ViewModel?.ModelServices.OpenCard(card);
            e.Handled = true;
        }
    }

    private void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.CloseDrawer();
        e.Handled = true;
    }

    private void OnCloseDrawerPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.CloseDrawer();
        e.Handled = true;
    }

    private void OnOpenApiKeyUrlPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.OpenApiKeyUrlCommand.Execute(null);
        e.Handled = true;
    }

    private void OnInstanceChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: ProfileInstanceChipViewModel chip })
        {
            ViewModel?.ModelServices.SelectInstance(chip);
        }

        e.Handled = true;
    }

    private void OnAddAnotherInstancePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.AddAnotherInstance();
        e.Handled = true;
    }

    private void OnToggleApiKeyVisPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.ToggleApiKeyVisibilityCommand.Execute(null);
        e.Handled = true;
    }

    private void OnFetchModelsPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.FetchModelsCommand.Execute(null);
        e.Handled = true;
    }

    private void OnFetchedModelChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: string model })
        {
            ViewModel?.ModelServices.PickFetchedModel(model);
        }

        e.Handled = true;
    }

    private void OnPresetModelChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PresetModelChipViewModel chip })
        {
            ViewModel?.ModelServices.PickPresetModel(chip);
        }

        e.Handled = true;
    }

    private void OnRemoveCustomModelChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PresetModelChipViewModel chip })
        {
            ViewModel?.ModelServices.RemoveCustomModel(chip);
        }

        e.Handled = true;
    }

    private void OnAddCustomModelPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.AddCustomModel();
        e.Handled = true;
    }

    private void OnNewModelInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel?.ModelServices.AddCustomModel();
            e.Handled = true;
        }
    }

    private void OnToggleAdvancedPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.ToggleAdvancedCommand.Execute(null);
        e.Handled = true;
    }

    private void OnOpenAiProtocolPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.SelectProtocol(ModelProtocol.OpenAi);
        e.Handled = true;
    }

    private void OnAnthropicProtocolPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.SelectProtocol(ModelProtocol.Anthropic);
        e.Handled = true;
    }

    private void OnTestConnectionPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.TestConnectionCommand.Execute(null);
        e.Handled = true;
    }

    private void OnSaveProfilePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.SaveProfileCommand.Execute(null);
        e.Handled = true;
    }

    private void OnActivateProfilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.ModelServices.CanActivate == true)
        {
            ViewModel.ModelServices.ActivateProfileCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnDeleteProfilePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ModelServices.DeleteProfileCommand.Execute(null);
        e.Handled = true;
    }

    // ===== 助理与记忆（A5）交互 =====

    private void OnOpenAssistantFolderPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.OpenAssistantFolderCommand.Execute(null);
        e.Handled = true;
    }

    private void OnResetRulesPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.ResetRulesCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRuleFieldLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ViewModel?.AssistantMemory.SaveRules();

    private void OnRestDayChipPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: RestDayChipViewModel chip })
        {
            chip.Toggle();
        }

        e.Handled = true;
    }

    private void OnSaveProfileMarkdownPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.SaveProfileCommand.Execute(null);
        e.Handled = true;
    }

    private void OnRestoreProfilePressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.RestorePreviousProfileCommand.Execute(null);
        e.Handled = true;
    }

    private void OnAddManualMemoryPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.AddManualMemoryCommand.Execute(null);
        e.Handled = true;
    }

    private void OnDeleteMemoryRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MemoryRowViewModel row })
        {
            ViewModel?.AssistantMemory.DeleteMemory(row);
        }

        e.Handled = true;
    }

    private void OnBeginClearMemoriesPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.BeginClearMemoriesCommand.Execute(null);
        e.Handled = true;
    }

    private void OnCancelClearMemoriesPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.CancelClearMemoriesCommand.Execute(null);
        e.Handled = true;
    }

    private void OnConfirmClearMemoriesPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.ConfirmClearMemoriesCommand.Execute(null);
        e.Handled = true;
    }

    private void OnGenerateWeeklyReviewPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.AssistantMemory.GenerateWeeklyReviewCommand.Execute(null);
        e.Handled = true;
    }

    // ===== 外部 AI 接入（A7 · MCP）交互 =====

    private void OnMcpModePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string mode)
        {
            ViewModel?.ExternalMcp.SetReadOnlyMode(string.Equals(mode, "ReadOnly", StringComparison.Ordinal));
        }

        e.Handled = true;
    }

    private void OnMcpClientPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            (sender as Control)?.Tag is string client)
        {
            ViewModel?.ExternalMcp.SelectClient(client);
        }

        e.Handled = true;
    }

    private async void OnCopyMcpConfigPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || ViewModel is not { } vm)
        {
            return;
        }

        e.Handled = true;
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(vm.ExternalMcp.ConfigSnippet);
            vm.ExternalMcp.NotifyConfigCopied();
        }
    }

    private void OnClearMcpAuditPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel?.ExternalMcp.ClearAuditCommand.Execute(null);
        e.Handled = true;
    }
}
