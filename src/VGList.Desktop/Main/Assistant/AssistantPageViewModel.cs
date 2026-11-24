using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.Core.Abstractions;
using VGList.Core.Dates;
using VGList.Core.Models;
using VGList.Data;
using VGList.Desktop.Controls;
using VGList.Desktop.Main.Settings;
using VGList.Desktop.Services;
using VGList.Widgets.Support;

namespace VGList.Desktop.Main.Assistant;

/// <summary>对话草稿与消息中的文件 / 图片引用胶囊（支持文件类型彩色角标与图片悬停缩略图预览）。</summary>
public sealed class ChatAttachmentItem
{
    public ChatAttachmentItem(string name, string fullPath, string sizeLabel, string kindTag, Bitmap? thumbnail = null)
    {
        Id = Guid.NewGuid().ToString("N");
        Name = name;
        FullPath = fullPath;
        SizeLabel = sizeLabel;
        KindTag = kindTag;
        IsImage = string.Equals(kindTag, "图片", StringComparison.Ordinal);
        ThumbnailBitmap = thumbnail;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        (FileBadgeText, FileBadgeBrush) = ResolveFileVisual(ext);
    }

    public string Id { get; }
    public string Name { get; }
    public string FullPath { get; }
    public string SizeLabel { get; }
    public string KindTag { get; }
    public bool IsImage { get; }
    public Bitmap? ThumbnailBitmap { get; }
    public bool HasThumbnail => ThumbnailBitmap is not null;
    public string FileBadgeText { get; }
    public IBrush FileBadgeBrush { get; }

    public ChatAttachmentItem CloneForEdit() => new(Name, FullPath, SizeLabel, KindTag, ThumbnailBitmap);

    public static (string Badge, IBrush Brush) ResolveFileVisual(string ext) => AttachmentFormats.Classify(ext) switch
    {
        AttachmentCategory.Image => ("IMG", ProviderCardViewModel.CreateBrush("#E46B92")),
        AttachmentCategory.Pdf => ("PDF", ProviderCardViewModel.CreateBrush("#E5484D")),
        AttachmentCategory.Word => ("W", ProviderCardViewModel.CreateBrush("#2B6CB0")),
        AttachmentCategory.Excel => ("XLS", ProviderCardViewModel.CreateBrush("#1F8A5B")),
        AttachmentCategory.PowerPoint => ("PPT", ProviderCardViewModel.CreateBrush("#D95D24")),
        AttachmentCategory.Epub => ("EPUB", ProviderCardViewModel.CreateBrush("#7C4DFF")),
        AttachmentCategory.Rtf => ("RTF", ProviderCardViewModel.CreateBrush("#4A6FA5")),
        AttachmentCategory.Html => ("HTML", ProviderCardViewModel.CreateBrush("#E34F26")),
        AttachmentCategory.Calendar => ("ICS", ProviderCardViewModel.CreateBrush("#3B82F6")),
        AttachmentCategory.Data when ext is ".json" or ".jsonl" => ("{}", ProviderCardViewModel.CreateBrush("#D97706")),
        AttachmentCategory.Data when ext is ".csv" or ".tsv" => ("CSV", ProviderCardViewModel.CreateBrush("#1F8A5B")),
        AttachmentCategory.Data => ("DATA", ProviderCardViewModel.CreateBrush("#D97706")),
        AttachmentCategory.Code => ("CODE", ProviderCardViewModel.CreateBrush("#6366F1")),
        AttachmentCategory.Text when ext is ".md" or ".markdown" => ("M", ProviderCardViewModel.CreateBrush("#0EA5B7")),
        _ => ("TXT", ProviderCardViewModel.CreateBrush("#64748B")),
    };
}

/// <summary>@ 引用输入框内已添加图片/文件的候选条目。</summary>
public sealed partial class MentionFileCandidate : ObservableObject
{
    public MentionFileCandidate(
        string name,
        string fullPath,
        string locationLabel,
        string fileBadgeText,
        IBrush fileBadgeBrush,
        bool isBrowseAction = false,
        Bitmap? thumbnailBitmap = null)
    {
        Name = name;
        FullPath = fullPath;
        LocationLabel = locationLabel;
        FileBadgeText = fileBadgeText;
        FileBadgeBrush = fileBadgeBrush;
        IsBrowseAction = isBrowseAction;
        ThumbnailBitmap = thumbnailBitmap;
    }

    public string Name { get; }
    public string FullPath { get; }
    public string LocationLabel { get; }
    public string FileBadgeText { get; }
    public IBrush FileBadgeBrush { get; }
    public bool IsBrowseAction { get; }
    public Bitmap? ThumbnailBitmap { get; }
    public bool HasThumbnail => ThumbnailBitmap is not null;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>顶栏模型下拉菜单条目。</summary>
public sealed class ModelSwitcherItemViewModel
{
    public ModelSwitcherItemViewModel(
        string profileId,
        string name,
        string model,
        Bitmap? logoBitmap,
        string monogram,
        IBrush brandBrush,
        bool supportsImages,
        bool isActive)
    {
        ProfileId = profileId;
        Name = name;
        Model = model;
        LogoBitmap = logoBitmap;
        Monogram = monogram;
        BrandBrush = brandBrush;
        SupportsImages = supportsImages;
        IsActive = isActive;
    }

    public string ProfileId { get; }
    public string Name { get; }
    public string Model { get; }
    public Bitmap? LogoBitmap { get; }
    public bool HasLogo => LogoBitmap is not null;
    public string Monogram { get; }
    public IBrush BrandBrush { get; }
    public bool SupportsImages { get; }
    public bool IsActive { get; }
    public string Subtitle => SupportsImages ? $"{Model} · 支持图片" : Model;
}

/// <summary>助理消息内的按时间顺序交错内容块（正文块 / 工具步骤块）。</summary>
public abstract class ChatBlockViewModel : ObservableObject;

/// <summary>按时间序出现的 Markdown 正文块（支持表格、代码块与流式渐入）。</summary>
public sealed partial class ChatTextBlockViewModel : ChatBlockViewModel
{
    private readonly StringBuilder _buffer = new();

    public ChatTextBlockViewModel(string initialText = "", bool isStreaming = false)
    {
        if (!string.IsNullOrEmpty(initialText))
        {
            _buffer.Append(initialText);
        }

        _text = initialText;
        _isStreaming = isStreaming;
    }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isStreaming;

    public void Append(string delta) => _buffer.Append(delta);

    public string FlushBuffer()
    {
        var current = _buffer.ToString();
        Text = current;
        return current;
    }

    public string RawText => _buffer.Length > 0 ? _buffer.ToString() : Text;
}

/// <summary>助理消息里的单步工具进度胶囊（执行中显示转圈圆圈与流光文字，完成后切为 ✓）。</summary>
public sealed partial class ChatToolStepViewModel : ChatBlockViewModel
{
    public ChatToolStepViewModel(string label, bool isRunning = true)
    {
        _label = label;
        _isRunning = isRunning;
        StartedTickMs = Environment.TickCount64;
    }

    public long StartedTickMs { get; }
    public string? PendingCompletedLabel { get; set; }
    public bool PendingSucceeded { get; set; } = true;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _succeeded = true;

    public bool IsCompletedSuccess => !IsRunning && Succeeded;
    public bool IsCompletedFailed => !IsRunning && !Succeeded;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCompletedSuccess));
        OnPropertyChanged(nameof(IsCompletedFailed));
    }

    partial void OnSucceededChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCompletedSuccess));
        OnPropertyChanged(nameof(IsCompletedFailed));
    }
}

/// <summary>提案 / 已应用日程改动清单中的单条记录（精简两行 + 行内即时改优先级/提醒 + 单条/多选撤销）。</summary>
public sealed partial class ChatProposalItemViewModel : ObservableObject
{
    public ChatProposalItemViewModel(
        string actionTag,
        string actionKind,
        string title,
        string changeSummary,
        string reason,
        ScheduleOp? operation,
        string? violationWarning = null,
        bool isSelected = true,
        string? proposalItemId = null,
        string? dateBadgeText = null,
        string? priorityBadgeText = null,
        TaskPriority? priorityTier = null,
        string? extraBadgeText = null,
        DateOnly? taskDate = null,
        TimeOnly? dueTime = null,
        TaskReminder? reminder = null,
        string? reminderBadgeText = null)
    {
        ProposalItemId = proposalItemId ?? Guid.NewGuid().ToString("N")[..12];
        ActionTag = actionTag;
        ActionKind = actionKind; // "add" | "move" | "edit" | "delete"
        Title = title;
        ChangeSummary = changeSummary;
        Reason = reason;
        Operation = operation;
        ViolationWarning = violationWarning;
        TaskDate = taskDate;
        DueTime = dueTime;
        Reminder = reminder?.Clone();
        _dateBadgeText = dateBadgeText ?? string.Empty;
        _priorityBadgeText = priorityBadgeText ?? string.Empty;
        _priorityTier = priorityTier;
        _reminderBadgeText = reminderBadgeText ?? (reminder is { Enabled: true } ? "已设提醒" : "添加提醒");
        _hasReminder = reminder is { Enabled: true };
        ExtraBadgeText = extraBadgeText ?? string.Empty;
        _isSelected = isSelected;
    }

    public string ProposalItemId { get; }
    public string ActionTag { get; }
    public string ActionKind { get; }
    public string Title { get; }
    public string ChangeSummary { get; }
    public string Reason { get; }
    public ScheduleOp? Operation { get; set; }
    public string? ViolationWarning { get; }
    public bool HasViolation => !string.IsNullOrEmpty(ViolationWarning);

    public DateOnly? TaskDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public TaskReminder? Reminder { get; set; }

    // 精简两行结构的差异化圆角胶囊（支持行内即时改优先级与提醒）
    [ObservableProperty]
    private string _dateBadgeText;

    public bool HasDateBadge => !string.IsNullOrEmpty(DateBadgeText);

    [ObservableProperty]
    private string _priorityBadgeText;

    [ObservableProperty]
    private TaskPriority? _priorityTier;

    public bool HasPriorityBadge => !string.IsNullOrEmpty(PriorityBadgeText);
    public bool IsPriorityHigh => PriorityTier == TaskPriority.High;
    public bool IsPriorityMedium => PriorityTier == TaskPriority.Medium;
    public bool IsPriorityLow => PriorityTier == TaskPriority.Low;
    public bool IsPriorityNone => PriorityTier is null or TaskPriority.None;

    [ObservableProperty]
    private string _reminderBadgeText;

    [ObservableProperty]
    private bool _hasReminder;

    public string ExtraBadgeText { get; }
    public bool HasExtraBadge => !string.IsNullOrEmpty(ExtraBadgeText);
    public bool ShowFallbackSummary => !HasDateBadge && !HasPriorityBadge && !HasExtraBadge && !CanEditTaskProps && !string.IsNullOrEmpty(ChangeSummary);

    public bool CanEditTaskProps => !IsLocked && !IsItemUndone && Operation is CreateTaskOp or UpdateTaskOp;
    public bool ShowPriorityPill => CanEditTaskProps || HasPriorityBadge;
    public bool ShowReminderPill => CanEditTaskProps || HasReminder;

    public bool IsAdd => ActionKind == "add";
    public bool IsMove => ActionKind == "move";
    public bool IsEdit => ActionKind == "edit";
    public bool IsDelete => ActionKind == "delete";

    /// <summary>实际写入后对应的任务/清单 EntityId，供单条/多选撤销定位。</summary>
    public string? AppliedEntityId { get; set; }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>卡片进入「已应用」态后用于多选撤销的勾选状态。</summary>
    [ObservableProperty]
    private bool _isUndoChecked;

    /// <summary>卡片是否处于「已应用」态。</summary>
    [ObservableProperty]
    private bool _isAppliedMode;

    /// <summary>该条是否已被单独或批量撤销。</summary>
    [ObservableProperty]
    private bool _isItemUndone;

    /// <summary>卡片已彻底结束（放弃 / 全部撤销），禁止继续操作。</summary>
    [ObservableProperty]
    private bool _isLocked;

    /// <summary>复选框当前是否打勾（待确认时看 IsSelected；已应用时看 IsUndoChecked）。</summary>
    public bool IsCheckboxChecked => IsAppliedMode ? IsUndoChecked : IsSelected;

    /// <summary>复选框是否可交互。</summary>
    public bool ShowCheckbox => !IsLocked && !(IsAppliedMode && IsItemUndone);

    /// <summary>已应用且未撤销的条目在行尾显示「撤销」单条撤销按钮。</summary>
    public bool CanUndoSingle => IsAppliedMode && !IsItemUndone && !IsLocked && !string.IsNullOrEmpty(AppliedEntityId);

    public bool IsDimmed => (!IsAppliedMode && !IsSelected) || IsItemUndone;

    public void UpdatePriorityDisplay(TaskPriority priority)
    {
        PriorityTier = priority;
        PriorityBadgeText = priority switch
        {
            TaskPriority.High => "高优先级",
            TaskPriority.Medium => "中优先级",
            TaskPriority.Low => "低优先级",
            _ => "无优先级",
        };
    }

    public void UpdateReminderDisplay(DateOnly? taskDate, TimeOnly? dueTime, TaskReminder? reminder, string? updatedDateBadge, DateOnly today)
    {
        TaskDate = taskDate;
        DueTime = dueTime;
        Reminder = reminder?.Clone();
        if (updatedDateBadge is not null)
        {
            DateBadgeText = updatedDateBadge;
        }

        HasReminder = reminder is { Enabled: true };
        ReminderBadgeText = taskDate is { } d
            ? TaskReminderCalculator.FormatPillBadge(d, dueTime, reminder, today)
            : (HasReminder ? "已设提醒" : "添加提醒");
    }

    public void ToggleCheckbox()
    {
        if (IsLocked)
        {
            return;
        }

        if (IsAppliedMode)
        {
            if (!IsItemUndone)
            {
                IsUndoChecked = !IsUndoChecked;
            }
        }
        else
        {
            IsSelected = !IsSelected;
        }
    }

    partial void OnDateBadgeTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDateBadge));
        OnPropertyChanged(nameof(ShowFallbackSummary));
    }

    partial void OnPriorityBadgeTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPriorityBadge));
        OnPropertyChanged(nameof(ShowPriorityPill));
        OnPropertyChanged(nameof(ShowFallbackSummary));
    }

    partial void OnPriorityTierChanged(TaskPriority? value)
    {
        OnPropertyChanged(nameof(IsPriorityHigh));
        OnPropertyChanged(nameof(IsPriorityMedium));
        OnPropertyChanged(nameof(IsPriorityLow));
        OnPropertyChanged(nameof(IsPriorityNone));
    }

    partial void OnHasReminderChanged(bool value) => OnPropertyChanged(nameof(ShowReminderPill));

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckboxChecked));
        OnPropertyChanged(nameof(IsDimmed));
    }

    partial void OnIsUndoCheckedChanged(bool value) => OnPropertyChanged(nameof(IsCheckboxChecked));

    partial void OnIsAppliedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCheckboxChecked));
        OnPropertyChanged(nameof(ShowCheckbox));
        OnPropertyChanged(nameof(CanUndoSingle));
        OnPropertyChanged(nameof(IsDimmed));
    }

    partial void OnIsItemUndoneChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckbox));
        OnPropertyChanged(nameof(CanUndoSingle));
        OnPropertyChanged(nameof(IsDimmed));
        OnPropertyChanged(nameof(CanEditTaskProps));
        OnPropertyChanged(nameof(ShowPriorityPill));
        OnPropertyChanged(nameof(ShowReminderPill));
    }

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCheckbox));
        OnPropertyChanged(nameof(CanUndoSingle));
        OnPropertyChanged(nameof(CanEditTaskProps));
        OnPropertyChanged(nameof(ShowPriorityPill));
        OnPropertyChanged(nameof(ShowReminderPill));
    }
}

/// <summary>
/// 对话内嵌的日程变更卡片（支持待确认勾选应用、自动生效展示，以及单条撤销 / 多选撤销 / 全部撤销）。
/// </summary>
public sealed partial class ChatProposalCardViewModel : ObservableObject
{
    private readonly Action<ChatProposalCardViewModel> _applyCallback;
    private readonly Action<ChatProposalCardViewModel> _undoAllCallback;
    private readonly Action<ChatProposalCardViewModel, IReadOnlyList<ChatProposalItemViewModel>> _undoItemsCallback;

    public ChatProposalCardViewModel(
        string title,
        string subtitle,
        IEnumerable<ChatProposalItemViewModel> items,
        Action<ChatProposalCardViewModel> applyCallback,
        Action<ChatProposalCardViewModel> undoAllCallback,
        Action<ChatProposalCardViewModel, IReadOnlyList<ChatProposalItemViewModel>> undoItemsCallback,
        string? proposalId = null)
    {
        ProposalId = proposalId;
        _title = title;
        _subtitle = subtitle;
        _applyCallback = applyCallback;
        _undoAllCallback = undoAllCallback;
        _undoItemsCallback = undoItemsCallback;

        foreach (var item in items)
        {
            item.PropertyChanged += (_, _) => NotifyCounts();
            Items.Add(item);
        }

        NotifyCounts();
    }

    public string? ProposalId { get; }
    public ObservableCollection<ChatProposalItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _subtitle;

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    partial void OnSubtitleChanged(string value) => OnPropertyChanged(nameof(HasSubtitle));

    [ObservableProperty]
    private bool _isPending = true;

    [ObservableProperty]
    private bool _isApplied;

    [ObservableProperty]
    private bool _isDiscarded;

    [ObservableProperty]
    private bool _isUndone;

    [ObservableProperty]
    private string? _appliedChangeSetId;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public int TotalCount => Items.Count;
    public int AddCount => Items.Count(i => i.IsAdd);
    public int MoveCount => Items.Count(i => i.IsMove);
    public int EditCount => Items.Count(i => i.IsEdit);
    public int DeleteCount => Items.Count(i => i.IsDelete);
    public bool HasAdd => AddCount > 0;
    public bool HasMove => MoveCount > 0;
    public bool HasEdit => EditCount > 0;
    public bool HasDelete => DeleteCount > 0;
    public bool CanApply => IsPending && SelectedCount > 0;
    public string SelectionSummary => $"已选 {SelectedCount}/{TotalCount} 项";
    public string AddSummary => $"新增 {AddCount}";
    public string MoveSummary => $"改期 {MoveCount}";
    public string EditSummary => $"修改 {EditCount}";
    public string DeleteSummary => $"删除 {DeleteCount}";

    // 已应用态下的细粒度撤销状态
    public int ActiveAppliedCount => Items.Count(i => i.IsAppliedMode && !i.IsItemUndone);
    public int SelectedUndoCount => Items.Count(i => i.IsAppliedMode && !i.IsItemUndone && i.IsUndoChecked);
    public bool CanUndoSelected => IsApplied && SelectedUndoCount > 0;
    public string UndoSelectedLabel => $"撤销已选 ({SelectedUndoCount})";

    public void Apply() => _applyCallback(this);

    public void Discard()
    {
        IsPending = false;
        IsDiscarded = true;
        foreach (var item in Items)
        {
            item.IsLocked = true;
        }

        StatusText = "已放弃本次建议";
    }

    public void UndoAll() => _undoAllCallback(this);

    public void UndoSingle(ChatProposalItemViewModel item)
    {
        if (item.CanUndoSingle)
        {
            _undoItemsCallback(this, [item]);
        }
    }

    public void UndoSelected()
    {
        var chosen = Items.Where(i => i.IsAppliedMode && !i.IsItemUndone && i.IsUndoChecked && !string.IsNullOrEmpty(i.AppliedEntityId)).ToList();
        if (chosen.Count > 0)
        {
            _undoItemsCallback(this, chosen);
        }
    }

    public void MarkApplied(
        string? changeSetId,
        int count,
        IReadOnlyDictionary<string, string>? entityMap = null,
        bool isAutoApplied = false)
    {
        AppliedChangeSetId = changeSetId;
        IsPending = false;
        IsApplied = true;
        IsUndone = false;
        Title = isAutoApplied ? "已同步到日程" : "已应用到日程";
        Subtitle = string.Empty;

        foreach (var item in Items)
        {
            if (entityMap is not null && entityMap.TryGetValue(item.ProposalItemId, out var eid))
            {
                item.AppliedEntityId = eid;
            }
            else if (item.Operation is UpdateTaskOp u)
            {
                item.AppliedEntityId = u.TaskId;
            }
            else if (item.Operation is SetCompletedOp s)
            {
                item.AppliedEntityId = s.TaskId;
            }
            else if (item.Operation is DeleteTaskOp d)
            {
                item.AppliedEntityId = d.TaskId;
            }

            // 若在待确认态用户取消勾选了某条，标记为已跳过/锁定
            if (!isAutoApplied && !item.IsSelected)
            {
                item.IsLocked = true;
                item.IsItemUndone = true;
                continue;
            }

            item.IsAppliedMode = true;
            item.IsUndoChecked = false;
            item.IsLocked = false;
        }

        StatusText = $"已同步 {count} 项到日程";
        NotifyCounts();
    }

    public void MarkItemsUndone(IReadOnlyCollection<string> undoneEntityIds)
    {
        var set = new HashSet<string>(undoneEntityIds, StringComparer.Ordinal);
        foreach (var item in Items)
        {
            if (!string.IsNullOrEmpty(item.AppliedEntityId) && set.Contains(item.AppliedEntityId))
            {
                item.IsItemUndone = true;
                item.IsUndoChecked = false;
            }
        }

        var remaining = ActiveAppliedCount;
        if (remaining == 0)
        {
            MarkAllUndone();
        }
        else
        {
            var undoneCount = Items.Count(i => i.IsAppliedMode && i.IsItemUndone);
            StatusText = $"已撤销 {undoneCount} 项 · 剩余 {remaining} 项生效";
            NotifyCounts();
        }
    }

    public void MarkAllUndone()
    {
        IsApplied = false;
        IsUndone = true;
        foreach (var item in Items)
        {
            item.IsItemUndone = true;
            item.IsUndoChecked = false;
            item.IsLocked = true;
        }

        StatusText = "已全部撤销";
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(ActiveAppliedCount));
        OnPropertyChanged(nameof(SelectedUndoCount));
        OnPropertyChanged(nameof(CanUndoSelected));
        OnPropertyChanged(nameof(UndoSelectedLabel));
    }
}

/// <summary>单条对话消息模型（支持按时间序交错工具/正文，以及历史用户消息就地编辑文本与附件）。</summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    private ChatTextBlockViewModel? _activeTextBlock;

    public ChatMessageViewModel(
        bool isUser,
        string text,
        IEnumerable<ChatAttachmentItem>? attachments = null,
        string? modelTag = null,
        string? id = null,
        string? timestamp = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        IsUser = isUser;
        _text = text;
        ModelTag = modelTag ?? string.Empty;
        Timestamp = string.IsNullOrWhiteSpace(timestamp) ? DateTime.Now.ToString("HH:mm") : timestamp;
        if (!isUser && !string.IsNullOrEmpty(text))
        {
            Blocks.Add(new ChatTextBlockViewModel(text, isStreaming: false));
        }

        if (attachments is not null)
        {
            foreach (var a in attachments)
            {
                Attachments.Add(a);
            }
        }
    }

    public string Id { get; }
    public bool IsUser { get; }
    public bool IsAssistant => !IsUser;
    public string Timestamp { get; }
    public string ModelTag { get; }
    public bool HasModelTag => !string.IsNullOrEmpty(ModelTag);

    public ObservableCollection<ChatAttachmentItem> Attachments { get; } = [];
    public ObservableCollection<ChatAttachmentItem> EditAttachments { get; } = [];
    public ObservableCollection<ChatBlockViewModel> Blocks { get; } = [];
    public ObservableCollection<ChatToolStepViewModel> ToolSteps { get; } = [];

    public bool HasAttachments => Attachments.Count > 0;
    public bool HasEditAttachments => EditAttachments.Count > 0;
    public bool HasBlocks => Blocks.Count > 0;
    public bool HasToolSteps => ToolSteps.Count > 0;

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = string.Empty;

    [ObservableProperty]
    private bool _isThinking;

    /// <summary>控制「正在思考」文字是否到达显现时机（活体 Lana 飞向左侧基本就位后放行，ADR-0072）。</summary>
    [ObservableProperty]
    private bool _isThinkingRevealed = true;

    [ObservableProperty]
    private string _thinkingLabel = "Lana 正在思考…";

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private bool _isCopied;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private ChatProposalCardViewModel? _proposal;

    /// <summary>是否为当前会话最后一条助理消息（活体 Lana 停靠于此条左侧，其余历史助理消息显示柔和静态留影）。</summary>
    [ObservableProperty]
    private bool _isLatestAssistantMessage;

    public bool HasText => !string.IsNullOrEmpty(Text);
    public bool HasError => !string.IsNullOrEmpty(ErrorText);
    public bool HasProposal => Proposal is not null;
    public bool ShowThinkingOrb => IsThinking && IsThinkingRevealed;
    public bool ShowPastStaticAvatar => IsAssistant && !IsLatestAssistantMessage;
    public bool ShowUserDisplayBubble => IsUser && !IsEditing;
    public bool CanSaveEdit => !string.IsNullOrWhiteSpace(EditText) || EditAttachments.Count > 0;
    public bool ShowActionRow => IsAssistant && !IsStreaming && !IsThinking && (HasText || HasError || HasProposal);
    public string CopyButtonLabel => IsCopied ? "✓ 已复制" : "复制";

    public void BeginEdit()
    {
        EditText = Text;
        EditAttachments.Clear();
        foreach (var a in Attachments)
        {
            EditAttachments.Add(a.CloneForEdit());
        }

        IsEditing = true;
        OnPropertyChanged(nameof(HasEditAttachments));
        OnPropertyChanged(nameof(CanSaveEdit));
    }

    public void CancelEdit()
    {
        IsEditing = false;
        EditAttachments.Clear();
        OnPropertyChanged(nameof(HasEditAttachments));
    }

    public void AddEditAttachment(ChatAttachmentItem item)
    {
        EditAttachments.Add(item);
        OnPropertyChanged(nameof(HasEditAttachments));
        OnPropertyChanged(nameof(CanSaveEdit));
    }

    public void RemoveEditAttachment(ChatAttachmentItem item)
    {
        EditAttachments.Remove(item);
        OnPropertyChanged(nameof(HasEditAttachments));
        OnPropertyChanged(nameof(CanSaveEdit));
    }

    public void CommitEditDraft(string finalText)
    {
        Text = finalText;
        Attachments.Clear();
        foreach (var a in EditAttachments)
        {
            Attachments.Add(a);
        }

        EditAttachments.Clear();
        IsEditing = false;
        OnPropertyChanged(nameof(HasAttachments));
        OnPropertyChanged(nameof(HasEditAttachments));
    }

    /// <summary>按时间顺序插入工具步骤块（执行中以流光提示，不提前打钩）。</summary>
    public void AddToolStep(ChatToolStepViewModel step)
    {
        if (_activeTextBlock is not null)
        {
            _activeTextBlock.FlushBuffer();
            _activeTextBlock.IsStreaming = false;
            _activeTextBlock = null;
            SyncFullText();
        }

        ToolSteps.Add(step);
        Blocks.Add(step);
        IsThinking = false; // 工具步骤自身正在展示「Lana 正在…」，避免与思考流光重复堆叠
        OnPropertyChanged(nameof(HasToolSteps));
        OnPropertyChanged(nameof(HasBlocks));
    }

    /// <summary>工具调用结束后标记完成，若尚无后续正文/提案则切回流光思考态等待模型生成后续回复。</summary>
    public void CompleteToolStep(ChatToolStepViewModel step, string completedLabel, bool succeeded)
    {
        step.Label = completedLabel;
        step.Succeeded = succeeded;
        step.IsRunning = false;
        // 仅当全部工具结束且仍处于生成流且尚未吐出正文/卡片/报错时，才切回流光思考态
        if (IsStreaming &&
            _activeTextBlock is null &&
            !HasError &&
            !HasProposal &&
            Blocks.OfType<ChatToolStepViewModel>().All(s => !s.IsRunning))
        {
            ThinkingLabel = LanaThinkingOrb.PickAfterToolPhrase();
            IsThinking = true;
        }
    }

    /// <summary>追加流式文本增量（若上一块不是正文块，则新开一段，确保与工具调用严格交错）。</summary>
    public void AppendStreamText(string delta, bool flushNow)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        if (IsThinking)
        {
            IsThinking = false;
        }

        if (_activeTextBlock is null)
        {
            _activeTextBlock = new ChatTextBlockViewModel(isStreaming: true);
            Blocks.Add(_activeTextBlock);
            OnPropertyChanged(nameof(HasBlocks));
        }

        _activeTextBlock.Append(delta);
        if (flushNow)
        {
            _activeTextBlock.FlushBuffer();
            SyncFullText();
        }
    }

    public void FlushStreamText()
    {
        if (_activeTextBlock is not null)
        {
            _activeTextBlock.FlushBuffer();
            SyncFullText();
        }
    }

    public void SetFallbackText(string text)
    {
        if (_activeTextBlock is null)
        {
            _activeTextBlock = new ChatTextBlockViewModel(text, isStreaming: false);
            Blocks.Add(_activeTextBlock);
            OnPropertyChanged(nameof(HasBlocks));
        }
        else
        {
            _activeTextBlock.Text = text;
        }

        Text = text;
    }

    public void FinishStreaming()
    {
        FlushStreamText();
        foreach (var b in Blocks)
        {
            if (b is ChatTextBlockViewModel tb)
            {
                tb.IsStreaming = false;
            }
            else if (b is ChatToolStepViewModel step && step.IsRunning)
            {
                if (!string.IsNullOrEmpty(step.PendingCompletedLabel))
                {
                    step.Label = step.PendingCompletedLabel;
                    step.Succeeded = step.PendingSucceeded;
                }

                step.IsRunning = false;
            }
        }

        _activeTextBlock = null;
        IsThinking = false;
        IsStreaming = false;
    }

    private void SyncFullText()
    {
        var parts = Blocks.OfType<ChatTextBlockViewModel>().Select(b => b.RawText).Where(s => !string.IsNullOrEmpty(s));
        Text = string.Join("\n\n", parts);
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasText));
        OnPropertyChanged(nameof(ShowActionRow));
    }

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(ShowUserDisplayBubble));
    partial void OnEditTextChanged(string value) => OnPropertyChanged(nameof(CanSaveEdit));
    partial void OnIsThinkingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowThinkingOrb));
        OnPropertyChanged(nameof(ShowActionRow));
    }

    partial void OnIsThinkingRevealedChanged(bool value) => OnPropertyChanged(nameof(ShowThinkingOrb));
    partial void OnIsStreamingChanged(bool value) => OnPropertyChanged(nameof(ShowActionRow));
    partial void OnIsCopiedChanged(bool value) => OnPropertyChanged(nameof(CopyButtonLabel));

    partial void OnErrorTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ShowActionRow));
    }

    partial void OnProposalChanged(ChatProposalCardViewModel? value)
    {
        OnPropertyChanged(nameof(HasProposal));
        OnPropertyChanged(nameof(ShowActionRow));
    }

    partial void OnIsLatestAssistantMessageChanged(bool value) => OnPropertyChanged(nameof(ShowPastStaticAvatar));
}

/// <summary>会话侧边栏条目。</summary>
public sealed partial class ChatSessionItemViewModel : ObservableObject
{
    public ChatSessionItemViewModel(
        string title,
        bool isSelected = false,
        string? id = null,
        DateTimeOffset? updatedAt = null,
        bool isPersistedHistory = false)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        _title = title;
        _isSelected = isSelected;
        _updatedLabel = (updatedAt ?? DateTimeOffset.Now).LocalDateTime.ToString("M/d HH:mm");
        HasLoadedStoredMessages = !isPersistedHistory;
    }

    public string Id { get; }
    public bool HasLoadedStoredMessages { get; set; }
    public List<ChatMessageViewModel> StoredMessages { get; } = [];
    internal CancellationTokenSource? ActiveRunCts { get; set; }
    internal bool LastGenerationSucceeded { get; set; } = true;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _updatedLabel;

    /// <summary>该会话是否正在后台或前台运行 Lana 回复（支持多会话并发运行，ADR-0072）。</summary>
    [ObservableProperty]
    private bool _isGenerating;

    internal void CancelActiveRun()
    {
        LastGenerationSucceeded = false;
        var cts = ActiveRunCts;
        ActiveRunCts = null;
        IsGenerating = false;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}

/// <summary>
/// Lana 助理对话页面视图模型（ADR-0044 / ADR-0047 / ADR-0048）：
/// - 顶栏极简模型下拉切换（无多余体验演示按钮）；
/// - 实时系统时间问候 + 用户称呼 + 多样化自然欢迎副标题；
/// - 实时流式工具进度 + 纯粉紫流光思考文字 + Markdown 表格/代码块；
/// - 默认自动应用 + 单条/多选/整批撤销卡片；
/// - 历史消息就地编辑（含附件增删改）与重新生成同步截断持久化历史。
/// </summary>
public sealed partial class AssistantPageViewModel : ObservableObject, IDisposable
{
    private const int MaxAttachments = 5;
    private const long MaxAttachmentBytes = 20L * 1024 * 1024;

    private static readonly string[] PendingSubtitleTemplates =
    [
        "今天是 {0}，手头还有 {1} 项待办等着搞定，先从哪一件开始呀？(*^▽^*)",
        "今天是 {0}，目前有 {1} 件事待安排，需要 Lana 帮你理一理顺序吗 ✨",
        "今天是 {0}，还剩 {1} 项待办啦，有什么新计划直接丢给我吧 (●'◡'●)",
        "今天是 {0}，今天有 {1} 项日程，想新增任务还是调调节奏都可以找我哦 🌸",
    ];

    private static readonly string[] ClearSubtitleTemplates =
    [
        "今天是 {0}，今天的待办都清空啦 ✨ 好好放松一下，或者聊聊接下来的计划？",
        "今天是 {0}，日程表干干净净的，心情是不是也轻盈了不少呀 (≧▽≦)",
        "今天是 {0}，暂时没有待办任务，有新灵感随时丢给 Lana 记下来哦 🌷",
    ];

    private readonly ITaskRepository _repository;
    private readonly IScheduleService? _schedule;
    private readonly ModelProfileStore _modelStore;
    private readonly UserProfileStore _profileStore;
    private readonly LiveChatPreviewClient _liveClient;
    private readonly Func<IAssistantHost>? _hostFactory;
    private readonly IConversationStore? _conversationStore;
    private readonly ToastService _toast;
    private readonly Action _openModelSettings;
    private readonly DateOnly _fallbackToday;
    private IAssistantHost? _host;
    private ChatSessionItemViewModel _currentSession;
    private ChatProposalCardViewModel? _activeOverlayCard;
    private ChatProposalItemViewModel? _activeOverlayItem;
    private int _mentionAtIndex = -1;
    private int _mentionTokenLength;
    private int _selectedMentionIndex;
    private int _greetingVariantIndex;
    private bool _persistedSessionsLoaded;
    private bool _wasGenerating;
    private bool _lastGenerationSucceeded = true;
    private bool _suppressHappyOnGeneratingChange;
    private bool _disposed;

    public AssistantPageViewModel(
        ITaskRepository repository,
        ModelProfileStore modelStore,
        ToastService toast,
        Action openModelSettings,
        DateOnly today,
        LiveChatPreviewClient? liveClient = null,
        Func<IAssistantHost>? hostFactory = null,
        UserProfileStore? profileStore = null,
        IConversationStore? conversationStore = null)
    {
        _repository = repository;
        _schedule = repository as IScheduleService;
        _modelStore = modelStore;
        _profileStore = profileStore ?? new UserProfileStore();
        _toast = toast;
        _openModelSettings = openModelSettings;
        _fallbackToday = today;
        _liveClient = liveClient ?? new LiveChatPreviewClient();
        _hostFactory = hostFactory;
        _conversationStore = conversationStore ?? (repository is SqliteTaskRepository sqliteRepo
            ? new SqliteConversationStore(sqliteRepo.Database)
            : null);
        _greetingVariantIndex = Random.Shared.Next(0, 16);
        ProposalReminderEditor = new ReminderEditorViewModel(OnProposalReminderEdited);

        _currentSession = new ChatSessionItemViewModel("新对话", isSelected: true);
        Sessions.Add(_currentSession);

        _modelStore.Changed += OnModelStoreChanged;
        _modelStore.ActiveChanged += OnModelStoreChanged;
        RefreshModelStatus();
        RefreshGreeting(pickNewVariant: false);
    }

    public event EventHandler? MessagesUpdated;

    public event EventHandler? SessionSwitched;

    /// <summary>对话画布中唯一活体 Lana 的锚点切换与转场请求（首轮飞向左侧 / 多轮滑落 / 新对话浮回居中 / 会话切换即时贴合）。</summary>
    public event EventHandler<LanaStageTransitionEventArgs>? StageTransitionRequested;

    public ObservableCollection<ChatSessionItemViewModel> Sessions { get; } = [];

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatAttachmentItem> DraftAttachments { get; } = [];

    public ObservableCollection<ModelSwitcherItemViewModel> AvailableModels { get; } = [];

    public ObservableCollection<MentionFileCandidate> MentionCandidates { get; } = [];

    public ReminderEditorViewModel ProposalReminderEditor { get; }

    /// <summary>最近一次并行的会话标题生成任务（不阻塞主回复，供观测与单测同步等待）。</summary>
    public Task? ActiveTitleGenerationTask { get; private set; }

    [ObservableProperty]
    private bool _isHistorySidebarOpen = true;

    [ObservableProperty]
    private bool _isModelDropdownOpen;

    [ObservableProperty]
    private bool _isMentionOpen;

    [ObservableProperty]
    private bool _isProposalPriorityMenuOpen;

    [ObservableProperty]
    private bool _isProposalReminderEditorOpen;

    [ObservableProperty]
    private TaskPriority _activeProposalPriority = TaskPriority.None;

    public bool HasProposalOverlay => IsProposalPriorityMenuOpen || IsProposalReminderEditorOpen;
    public bool IsActiveProposalPriorityHigh => ActiveProposalPriority == TaskPriority.High;
    public bool IsActiveProposalPriorityMedium => ActiveProposalPriority == TaskPriority.Medium;
    public bool IsActiveProposalPriorityLow => ActiveProposalPriority == TaskPriority.Low;
    public bool IsActiveProposalPriorityNone => ActiveProposalPriority == TaskPriority.None;

    partial void OnIsProposalPriorityMenuOpenChanged(bool value) => OnPropertyChanged(nameof(HasProposalOverlay));
    partial void OnIsProposalReminderEditorOpenChanged(bool value) => OnPropertyChanged(nameof(HasProposalOverlay));
    partial void OnActiveProposalPriorityChanged(TaskPriority value)
    {
        OnPropertyChanged(nameof(IsActiveProposalPriorityHigh));
        OnPropertyChanged(nameof(IsActiveProposalPriorityMedium));
        OnPropertyChanged(nameof(IsActiveProposalPriorityLow));
        OnPropertyChanged(nameof(IsActiveProposalPriorityNone));
    }

    [ObservableProperty]
    private string _draftInput = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>助理页是否处于当前展示页（由主窗口同步；离开页时舞台与左栏 Lana 同步进入 Sleep 省电态）。</summary>
    [ObservableProperty]
    private bool _isPageActive;

    /// <summary>底部输入框是否聚焦（驱动左栏与对话舞台 Lana 的 Listening 状态）。</summary>
    [ObservableProperty]
    private bool _isComposerFocused;

    /// <summary>对话舞台与左侧导航栏共用的 Lana 五态真源（Sleep / Idle / Listening / Thinking / Happy）。</summary>
    [ObservableProperty]
    private LanaVisualState _stageLanaState = LanaVisualState.Sleep;

    /// <summary>当前会话最后一条助理消息（活体 Lana 的停靠目标，空态时为 null）。</summary>
    [ObservableProperty]
    private ChatMessageViewModel? _latestAssistantMessage;

    [ObservableProperty]
    private bool _isDragOver;

    [ObservableProperty]
    private string? _attachmentNotice;

    [ObservableProperty]
    private bool _hasActiveModel;

    [ObservableProperty]
    private string _activeModelLabel = "尚未配置模型";

    [ObservableProperty]
    private string _activeProviderMonogram = "!";

    [ObservableProperty]
    private IBrush _activeProviderBrush = ProviderCardViewModel.CreateBrush("#C27613");

    [ObservableProperty]
    private Bitmap? _activeProviderLogo;

    [ObservableProperty]
    private string _greetingPrefix = "你好，我是 ";

    [ObservableProperty]
    private string _greetingSubtitle = string.Empty;

    public bool HasActiveProviderLogo => ActiveProviderLogo is not null;
    public bool HasAvailableModels => AvailableModels.Count > 0;
    public bool IsEmptyState => Messages.Count == 0;
    public bool HasMessages => Messages.Count > 0;
    public bool HasDraftAttachments => DraftAttachments.Count > 0;
    public bool HasAttachmentNotice => !string.IsNullOrEmpty(AttachmentNotice);
    public bool CanSend => !IsGenerating && (!string.IsNullOrWhiteSpace(DraftInput) || DraftAttachments.Count > 0);

    /// <summary>按当前真实系统时间与用户画像称呼动态刷新问候语和多样化副标题。</summary>
    public void RefreshGreeting(bool pickNewVariant = false)
    {
        if (pickNewVariant)
        {
            _greetingVariantIndex++;
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now.Date);
        var timeGreet = now.Hour switch
        {
            >= 5 and < 11 => "早上好",
            >= 11 and < 14 => "中午好",
            >= 14 and < 18 => "下午好",
            >= 18 and < 23 => "晚上好",
            _ => "夜深了",
        };

        var nickname = _profileStore.GetNickname();
        GreetingPrefix = string.IsNullOrWhiteSpace(nickname)
            ? $"{timeGreet}，我是 "
            : $"{timeGreet}，{nickname}！我是 ";

        var lunar = CnCalendar.Describe(today);
        var weekday = DateLabels.Weekday(today);
        var dateDesc = $"{today.Month}月{today.Day}日 {weekday}（{lunar.Text}）";
        var pendingToday = _repository.Query(new TaskQuery { To = today, IncludeCompleted = false }).Count;

        if (pendingToday > 0)
        {
            var tpl = PendingSubtitleTemplates[Math.Abs(_greetingVariantIndex) % PendingSubtitleTemplates.Length];
            GreetingSubtitle = string.Format(tpl, dateDesc, pendingToday);
        }
        else
        {
            var tpl = ClearSubtitleTemplates[Math.Abs(_greetingVariantIndex) % ClearSubtitleTemplates.Length];
            GreetingSubtitle = string.Format(tpl, dateDesc);
        }
    }

    [RelayCommand]
    public void ToggleHistorySidebar() => IsHistorySidebarOpen = !IsHistorySidebarOpen;

    [RelayCommand]
    public void ToggleModelDropdown()
    {
        if (!IsModelDropdownOpen)
        {
            RefreshModelStatus();
        }

        IsModelDropdownOpen = !IsModelDropdownOpen;
    }

    public void CloseModelDropdown() => IsModelDropdownOpen = false;

    public void SelectModelProfile(ModelSwitcherItemViewModel item)
    {
        IsModelDropdownOpen = false;
        if (_modelStore.SetActive(item.ProfileId))
        {
            _toast.Show($"已切换为 {item.Name} · {item.Model} ✨");
        }
    }

    [RelayCommand]
    public void OpenModelSettings()
    {
        IsModelDropdownOpen = false;
        _openModelSettings();
    }

    public bool ClosePopups()
    {
        if (HasProposalOverlay)
        {
            CloseProposalOverlays();
            return true;
        }

        var editingMsg = Messages.FirstOrDefault(m => m.IsEditing);
        if (editingMsg is not null)
        {
            editingMsg.CancelEdit();
            return true;
        }

        if (IsMentionOpen)
        {
            CloseMention();
            return true;
        }

        if (IsModelDropdownOpen)
        {
            IsModelDropdownOpen = false;
            return true;
        }

        return false;
    }

    public void OpenProposalPriorityMenu(ChatProposalCardViewModel card, ChatProposalItemViewModel item)
    {
        if (!item.CanEditTaskProps)
        {
            return;
        }

        var toggleSame = IsProposalPriorityMenuOpen && ReferenceEquals(_activeOverlayItem, item);
        CloseProposalOverlays();
        if (toggleSame)
        {
            return;
        }

        _activeOverlayCard = card;
        _activeOverlayItem = item;
        ActiveProposalPriority = item.PriorityTier ?? TaskPriority.None;
        IsProposalPriorityMenuOpen = true;
    }

    public void OpenProposalReminderEditor(ChatProposalCardViewModel card, ChatProposalItemViewModel item)
    {
        if (!item.CanEditTaskProps)
        {
            return;
        }

        var toggleSame = IsProposalReminderEditorOpen && ReferenceEquals(_activeOverlayItem, item);
        CloseProposalOverlays();
        if (toggleSame)
        {
            return;
        }

        _activeOverlayCard = card;
        _activeOverlayItem = item;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var taskDate = item.TaskDate ?? today;
        ProposalReminderEditor.Load(taskDate, item.DueTime, item.Reminder, today);
        IsProposalReminderEditorOpen = true;
    }

    public void CloseProposalOverlays()
    {
        IsProposalPriorityMenuOpen = false;
        IsProposalReminderEditorOpen = false;
        _activeOverlayCard = null;
        _activeOverlayItem = null;
    }

    public void SelectActiveProposalPriority(TaskPriority priority)
    {
        var card = _activeOverlayCard;
        var item = _activeOverlayItem;
        CloseProposalOverlays();
        if (card is not null && item is not null)
        {
            ApplyInlinePriority(card, item, priority);
        }
    }

    /// <summary>卡片内即时调节优先级（点击即生效，无需确认按钮）：已同步直接改仓储，待确认直接改提案操作。</summary>
    public void ApplyInlinePriority(ChatProposalCardViewModel card, ChatProposalItemViewModel item, TaskPriority priority)
    {
        if (!item.CanEditTaskProps)
        {
            return;
        }

        if (item.IsAppliedMode && !string.IsNullOrEmpty(item.AppliedEntityId))
        {
            var existing = _repository.GetById(item.AppliedEntityId);
            if (existing is not null && existing.Priority != priority)
            {
                existing.Priority = priority;
                _repository.Update(existing);
            }
        }
        else if (item.Operation is not null)
        {
            ScheduleOp? updated = item.Operation switch
            {
                CreateTaskOp create => new CreateTaskOp(create.Draft with { Priority = priority }),
                UpdateTaskOp update => new UpdateTaskOp(update.TaskId, update.Patch with { Priority = priority }),
                _ => null,
            };

            if (updated is not null)
            {
                item.Operation = updated;
                if (!string.IsNullOrEmpty(card.ProposalId))
                {
                    _host?.UpdateProposalItemOperation(card.ProposalId, item.ProposalItemId, updated);
                }
            }
        }

        item.UpdatePriorityDisplay(priority);
    }

    /// <summary>卡片内即时调节日程时刻与提醒（点击即生效，无需确认按钮）：已同步直接改仓储，待确认直接改提案操作。</summary>
    public void ApplyInlineReminder(
        ChatProposalCardViewModel card,
        ChatProposalItemViewModel item,
        TimeOnly? dueTime,
        TaskReminder? reminder)
    {
        if (!item.CanEditTaskProps)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var clonedReminder = reminder?.Clone();

        if (item.IsAppliedMode && !string.IsNullOrEmpty(item.AppliedEntityId))
        {
            var existing = _repository.GetById(item.AppliedEntityId);
            if (existing is not null)
            {
                existing.DueTime = dueTime;
                existing.Reminder = clonedReminder?.Clone();
                _repository.Update(existing);
                item.TaskDate = existing.Date;
            }
        }
        else if (item.Operation is not null)
        {
            ScheduleOp? updated = item.Operation switch
            {
                CreateTaskOp create => new CreateTaskOp(create.Draft with
                {
                    DueTime = dueTime,
                    Reminder = clonedReminder?.Clone(),
                }),
                UpdateTaskOp update => new UpdateTaskOp(update.TaskId, update.Patch with
                {
                    DueTime = dueTime,
                    ClearDueTime = dueTime is null,
                    Reminder = clonedReminder?.Clone(),
                    ClearReminder = clonedReminder is null,
                }),
                _ => null,
            };

            if (updated is not null)
            {
                item.Operation = updated;
                if (!string.IsNullOrEmpty(card.ProposalId))
                {
                    _host?.UpdateProposalItemOperation(card.ProposalId, item.ProposalItemId, updated);
                }
            }
        }

        string? newDateBadge = item.TaskDate is { } d
            ? FormatProposalDateLabel(d, dueTime, today)
            : null;
        item.UpdateReminderDisplay(item.TaskDate, dueTime, clonedReminder, newDateBadge, today);
    }

    private void OnProposalReminderEdited(TimeOnly? dueTime, TaskReminder? reminder)
    {
        if (_activeOverlayCard is { } card && _activeOverlayItem is { } item)
        {
            ApplyInlineReminder(card, item, dueTime, reminder);
        }
    }

    [RelayCommand]
    public void StartNewSession()
    {
        ClosePopups();
        SaveCurrentSessionMessages();
        RefreshGreeting(pickNewVariant: true);

        if (Messages.Count == 0 && !_currentSession.IsGenerating)
        {
            SessionSwitched?.Invoke(this, EventArgs.Empty);
            return;
        }

        var existingBlank = Sessions.FirstOrDefault(s =>
            s.HasLoadedStoredMessages &&
            s.StoredMessages.Count == 0 &&
            !s.IsGenerating &&
            string.Equals(s.Title, "新对话", StringComparison.Ordinal) &&
            !ReferenceEquals(s, _currentSession));
        if (existingBlank is not null)
        {
            foreach (var s in Sessions) s.IsSelected = ReferenceEquals(s, existingBlank);
            _currentSession = existingBlank;
            Messages.Clear();
            RefreshLatestAssistantMarker();
            DraftAttachments.Clear();
            AttachmentNotice = null;
            NotifyEmptyAndAttachments();
            SyncCurrentSessionGeneratingState(triggerHappy: false);
            StageTransitionRequested?.Invoke(this, new LanaStageTransitionEventArgs(LanaFlightKind.ReturnHome, null));
            SessionSwitched?.Invoke(this, EventArgs.Empty);
            return;
        }

        foreach (var s in Sessions) s.IsSelected = false;
        _currentSession = new ChatSessionItemViewModel("新对话", isSelected: true);
        Sessions.Insert(0, _currentSession);
        Messages.Clear();
        RefreshLatestAssistantMarker();
        DraftAttachments.Clear();
        AttachmentNotice = null;
        NotifyEmptyAndAttachments();
        SyncCurrentSessionGeneratingState(triggerHappy: false);
        StageTransitionRequested?.Invoke(this, new LanaStageTransitionEventArgs(LanaFlightKind.ReturnHome, null));
        SessionSwitched?.Invoke(this, EventArgs.Empty);
    }

    public void SelectSession(ChatSessionItemViewModel session)
    {
        if (ReferenceEquals(session, _currentSession))
        {
            return;
        }

        ClosePopups();
        SaveCurrentSessionMessages();
        HydrateSessionMessagesIfNeeded(session);

        foreach (var s in Sessions) s.IsSelected = ReferenceEquals(s, session);
        _currentSession = session;
        Messages.Clear();
        foreach (var m in session.StoredMessages)
        {
            Messages.Add(m);
        }

        RefreshLatestAssistantMarker();
        if (Messages.Count == 0)
        {
            RefreshGreeting(pickNewVariant: true);
        }

        NotifyEmptyAndAttachments();
        SyncCurrentSessionGeneratingState(triggerHappy: false);
        StageTransitionRequested?.Invoke(this, new LanaStageTransitionEventArgs(LanaFlightKind.None, LatestAssistantMessage));
        SessionSwitched?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteSession(ChatSessionItemViewModel session)
    {
        if (Sessions.Count <= 1)
        {
            var hadMessages = Messages.Count > 0;
            _currentSession.CancelActiveRun();
            StartNewSession();
            Messages.Clear();
            RefreshLatestAssistantMarker();
            _currentSession.StoredMessages.Clear();
            _currentSession.Title = "新对话";
            DeletePersistedSession(_currentSession.Id);
            NotifyEmptyAndAttachments();
            SyncCurrentSessionGeneratingState(triggerHappy: false);
            if (!hadMessages)
            {
                StageTransitionRequested?.Invoke(this, new LanaStageTransitionEventArgs(LanaFlightKind.None, null));
            }

            SessionSwitched?.Invoke(this, EventArgs.Empty);
            return;
        }

        var wasCurrent = ReferenceEquals(session, _currentSession);
        session.CancelActiveRun();
        DeletePersistedSession(session.Id);
        Sessions.Remove(session);
        if (wasCurrent)
        {
            SelectSession(Sessions[0]);
        }
    }

    /// <summary>首次进入助理页时按需拉取最近 30 条持久化会话（仅读常驻 SqliteConversationStore，不触发 VGList.Agent 加载）。</summary>
    public void EnsurePersistedSessionsLoaded()
    {
        if (_persistedSessionsLoaded || _conversationStore is null)
        {
            return;
        }

        _persistedSessionsLoaded = true;
        var persisted = _conversationStore.ListSessions(SqliteConversationStore.MaxRetainedSessions);
        foreach (var summary in persisted)
        {
            if (Sessions.Any(existing => string.Equals(existing.Id, summary.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            Sessions.Add(new ChatSessionItemViewModel(
                summary.Title,
                isSelected: false,
                id: summary.Id,
                updatedAt: summary.UpdatedAt,
                isPersistedHistory: true));
        }
    }

    private void HydrateSessionMessagesIfNeeded(ChatSessionItemViewModel session)
    {
        if (session.HasLoadedStoredMessages || _conversationStore is null)
        {
            return;
        }

        var records = _conversationStore.LoadMessages(session.Id);
        session.StoredMessages.Clear();
        foreach (var rec in records)
        {
            var isUser = string.Equals(rec.Role, "user", StringComparison.OrdinalIgnoreCase);
            var attachments = rec.Attachments is { Count: > 0 }
                ? rec.Attachments.Select(a => new ChatAttachmentItem(
                    a.Name,
                    a.FullPath,
                    FormatSize(a.SizeBytes),
                    a.KindTag,
                    thumbnail: null)).ToList()
                : null;

            session.StoredMessages.Add(new ChatMessageViewModel(
                isUser: isUser,
                text: rec.Text,
                attachments: attachments,
                modelTag: rec.ModelTag,
                id: rec.Id,
                timestamp: rec.CreatedAt.LocalDateTime.ToString("HH:mm")));
        }

        session.HasLoadedStoredMessages = true;
    }

    private void DeletePersistedSession(string sessionId)
    {
        if (_host is not null)
        {
            _host.DeleteSession(sessionId);
        }
        else
        {
            _conversationStore?.DeleteSession(sessionId);
        }
    }

    private void TruncatePersistedMessages(string sessionId, int keepCount)
    {
        if (_host is not null)
        {
            _host.TruncateSessionMessages(sessionId, keepCount);
        }
        else
        {
            _conversationStore?.TruncateMessages(sessionId, keepCount);
        }
    }

    public void AddAttachmentFiles(IEnumerable<string> paths)
    {
        AttachmentNotice = null;
        foreach (var path in paths)
        {
            if (DraftAttachments.Count >= MaxAttachments)
            {
                _toast.Show($"单条消息最多附加 {MaxAttachments} 个文件", durationMs: 3600);
                break;
            }

            var item = TryBuildAttachment(path, DraftAttachments);
            if (item is not null)
            {
                DraftAttachments.Add(item);
            }
        }

        NotifyEmptyAndAttachments();
    }

    /// <summary>向正在编辑的历史消息添加附件（支持图片与多格式文档）。</summary>
    public void AddEditAttachmentFiles(ChatMessageViewModel userMsg, IEnumerable<string> paths)
    {
        AttachmentNotice = null;
        foreach (var path in paths)
        {
            if (userMsg.EditAttachments.Count >= MaxAttachments)
            {
                _toast.Show($"单条消息最多附加 {MaxAttachments} 个文件", durationMs: 3600);
                break;
            }

            var item = TryBuildAttachment(path, userMsg.EditAttachments);
            if (item is not null)
            {
                userMsg.AddEditAttachment(item);
            }
        }
    }

    public bool AddPastedImageBytes(byte[] rawBytes, bool isPng)
    {
        var savedPath = SavePastedImageToDisk(rawBytes, isPng);
        if (savedPath is null)
        {
            return false;
        }

        AddAttachmentFiles([savedPath]);
        return true;
    }

    public bool AddEditPastedImageBytes(ChatMessageViewModel userMsg, byte[] rawBytes, bool isPng)
    {
        var savedPath = SavePastedImageToDisk(rawBytes, isPng);
        if (savedPath is null)
        {
            return false;
        }

        AddEditAttachmentFiles(userMsg, [savedPath]);
        return true;
    }

    private ChatAttachmentItem? TryBuildAttachment(string path, ICollection<ChatAttachmentItem> existing)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (existing.Any(a => string.Equals(a.FullPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (info.Length > MaxAttachmentBytes)
        {
            _toast.Show($"「{info.Name}」超过 20MB 上限，已跳过", durationMs: 4000);
            return null;
        }

        var ext = info.Extension.ToLowerInvariant();
        var kind = AttachmentFormats.GetKindLabel(ext);
        if (string.IsNullOrEmpty(kind))
        {
            _toast.Show($"暂不支持 {ext} 格式（{AttachmentFormats.SupportedSummaryText}）", durationMs: 4200);
            return null;
        }

        if (AttachmentFormats.IsImage(ext) && _modelStore.ActiveProfile is { } active)
        {
            var supportsImages = ProviderCatalog.Shared.TryGetKnownSupportsImages(active.PresetId, active.Model, out var known)
                ? (known || active.SupportsImages)
                : active.SupportsImages;
            if (!supportsImages)
            {
                _toast.Show($"当前模型「{active.Model}」不支持图片输入，请切换多模态模型", durationMs: 4500);
            }
        }

        var thumb = AttachmentFormats.IsImage(ext) ? TryLoadThumbnail(path) : null;
        return new ChatAttachmentItem(info.Name, path, FormatSize(info.Length), kind, thumb);
    }

    private string? SavePastedImageToDisk(byte[] rawBytes, bool isPng)
    {
        if (rawBytes.Length == 0)
        {
            return null;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VGList",
                "assistant",
                "pasted");
            Directory.CreateDirectory(dir);
            var fileName = $"截图_{DateTime.Now:HHmmss_fff}.png";
            var destPath = Path.Combine(dir, fileName);

            if (isPng)
            {
                File.WriteAllBytes(destPath, rawBytes);
            }
            else
            {
                using var ms = new MemoryStream(rawBytes);
                using var bmp = new Bitmap(ms);
                using var fs = File.Create(destPath);
                bmp.Save(fs, PngBitmapEncoderOptions.Default);
            }

            return destPath;
        }
        catch (Exception ex)
        {
            _toast.Show($"粘贴图片失败：{ex.Message}", durationMs: 4000);
            return null;
        }
    }

    public void RemoveAttachment(ChatAttachmentItem item)
    {
        DraftAttachments.Remove(item);
        AttachmentNotice = null;
        NotifyEmptyAndAttachments();
    }

    // ===== 历史消息编辑（支持文本与图片/文件增删改 + 同步截断历史） =====

    public void BeginEditMessage(ChatMessageViewModel userMsg)
    {
        if (IsGenerating || !userMsg.IsUser)
        {
            return;
        }

        foreach (var m in Messages)
        {
            if (m.IsEditing && !ReferenceEquals(m, userMsg))
            {
                m.CancelEdit();
            }
        }

        userMsg.BeginEdit();
    }

    public void CancelEditMessage(ChatMessageViewModel userMsg) => userMsg.CancelEdit();

    public void RemoveEditAttachment(ChatMessageViewModel userMsg, ChatAttachmentItem item) =>
        userMsg.RemoveEditAttachment(item);

    public async Task CommitEditMessageAsync(ChatMessageViewModel userMsg)
    {
        if (IsGenerating || !userMsg.IsUser || !userMsg.CanSaveEdit)
        {
            return;
        }

        var userIdx = Messages.IndexOf(userMsg);
        if (userIdx < 0)
        {
            return;
        }

        var trimmed = userMsg.EditText.Trim();
        var displayText = string.IsNullOrWhiteSpace(trimmed) && userMsg.EditAttachments.Count > 0
            ? BuildDefaultAttachmentPrompt(userMsg.EditAttachments.Count)
            : trimmed;

        userMsg.CommitEditDraft(displayText);

        // 删除该条用户消息之后的所有旧对话记录（UI 与 SQLite 同步截断，防止模型看到过期历史）
        while (Messages.Count > userIdx + 1)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        var targetSession = _currentSession;
        TruncatePersistedMessages(targetSession.Id, keepCount: userIdx);

        if (userIdx == 0)
        {
            var fallbackTitle = displayText.Length > 16 ? displayText[..16] + "…" : displayText;
            targetSession.Title = fallbackTitle;
            StartParallelTitleGeneration(targetSession, fallbackTitle, displayText);
        }

        targetSession.UpdatedLabel = DateTime.Now.ToString("HH:mm");
        var attachments = userMsg.Attachments.ToList();
        SaveCurrentSessionMessages();
        MessagesUpdated?.Invoke(this, EventArgs.Empty);

        await StreamReplyForUserPromptAsync(targetSession, displayText, attachments);
    }

    // ===== @ 引用当前输入框内已添加的图片/文件（ADR-0056，不再扫描电脑目录） =====

    public static bool TryExtractMentionQuery(string? text, int caretIndex, out int atIndex, out string query)
    {
        atIndex = -1;
        query = string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var safeCaret = Math.Clamp(caretIndex, 0, text.Length);
        if (safeCaret == 0)
        {
            return false;
        }

        for (var i = safeCaret - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch == '@')
            {
                // 避免邮箱地址（abc@example.com）误触发，同时支持中文文本内直接紧接输入 @
                var isEmailPrefix = i > 0 && text[i - 1] is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-';
                if (!isEmailPrefix)
                {
                    atIndex = i;
                    query = text.Substring(i + 1, safeCaret - i - 1);
                    return true;
                }

                return false;
            }

            if (char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return false;
    }

    public void UpdateMentionState(string text, int caretIndex)
    {
        if (DraftAttachments.Count == 0 || !TryExtractMentionQuery(text, caretIndex, out var atIdx, out var query))
        {
            CloseMention();
            return;
        }

        var filtered = string.IsNullOrWhiteSpace(query)
            ? DraftAttachments.ToList()
            : DraftAttachments.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (filtered.Count == 0)
        {
            CloseMention();
            return;
        }

        _mentionAtIndex = atIdx;
        _mentionTokenLength = query.Length + 1;

        MentionCandidates.Clear();
        foreach (var item in filtered)
        {
            MentionCandidates.Add(new MentionFileCandidate(
                item.Name,
                item.FullPath,
                $"{item.KindTag} · {item.SizeLabel}",
                item.FileBadgeText,
                item.FileBadgeBrush,
                isBrowseAction: false,
                thumbnailBitmap: item.ThumbnailBitmap));
        }

        _selectedMentionIndex = 0;
        SyncMentionSelection();
        IsMentionOpen = true;
    }

    public void MoveMentionSelection(int delta)
    {
        if (!IsMentionOpen || MentionCandidates.Count == 0)
        {
            return;
        }

        _selectedMentionIndex = (_selectedMentionIndex + delta + MentionCandidates.Count) % MentionCandidates.Count;
        SyncMentionSelection();
    }

    public bool ApplyMentionCandidate(
        MentionFileCandidate? explicitCandidate,
        string currentText,
        out bool needsBrowseDialog,
        out string updatedText,
        out int updatedCaret)
    {
        needsBrowseDialog = false;
        updatedText = currentText;
        updatedCaret = currentText.Length;

        if (!IsMentionOpen || MentionCandidates.Count == 0)
        {
            return false;
        }

        var chosen = explicitCandidate ?? MentionCandidates[Math.Clamp(_selectedMentionIndex, 0, MentionCandidates.Count - 1)];
        var insertion = $"@{chosen.Name} ";

        if (_mentionAtIndex >= 0 && _mentionAtIndex + _mentionTokenLength <= currentText.Length)
        {
            updatedText = currentText.Remove(_mentionAtIndex, _mentionTokenLength).Insert(_mentionAtIndex, insertion);
            updatedCaret = _mentionAtIndex + insertion.Length;
        }

        CloseMention();
        return true;
    }

    public void CloseMention()
    {
        IsMentionOpen = false;
        _mentionAtIndex = -1;
        _mentionTokenLength = 0;
    }

    private void SyncMentionSelection()
    {
        for (var i = 0; i < MentionCandidates.Count; i++)
        {
            MentionCandidates[i].IsSelected = i == _selectedMentionIndex;
        }
    }

    [RelayCommand]
    public void StopGeneration()
    {
        _currentSession.CancelActiveRun();
        SyncCurrentSessionGeneratingState(triggerHappy: false);
    }

    [RelayCommand]
    public async Task SendAsync()
    {
        if (!CanSend)
        {
            return;
        }

        ClosePopups();
        var text = DraftInput.Trim();
        await SendInternalAsync(text);
    }

    /// <summary>重新生成回复：同步截断 UI 与 SQLite 持久化历史中刚刚那一轮对话，绝不残留旧上下文。</summary>
    public async Task RetryMessageAsync(ChatMessageViewModel assistantMsg)
    {
        if (IsGenerating)
        {
            return;
        }

        var idx = Messages.IndexOf(assistantMsg);
        if (idx <= 0)
        {
            return;
        }

        var userIdx = idx - 1;
        var userMsg = Messages[userIdx];
        var targetSession = _currentSession;

        // 移除当前助理回复及之后可能存在的任何记录
        while (Messages.Count > idx)
        {
            Messages.RemoveAt(Messages.Count - 1);
        }

        // 同步截断 SQLite 持久化历史（保留 userMsg 之前的全部消息，即将重新写入本轮 userMsg + 新回复）
        TruncatePersistedMessages(targetSession.Id, keepCount: userIdx);
        var attachments = userMsg.Attachments.ToList();
        SaveCurrentSessionMessages();

        await StreamReplyForUserPromptAsync(targetSession, userMsg.Text, attachments);
    }

    public async Task MarkCopiedAsync(ChatMessageViewModel message)
    {
        message.IsCopied = true;
        await Task.Delay(1500);
        message.IsCopied = false;
    }

    public static string BuildDefaultAttachmentPrompt(int attachmentCount) => attachmentCount <= 1
        ? "先帮我看看这个附件的内容，把你打算怎么安排日程的计划列出来跟我确认，同意后再执行"
        : $"先帮我看看这 {attachmentCount} 个附件的内容，把你打算怎么安排日程的计划列出来跟我确认，同意后再执行";

    private async Task SendInternalAsync(string text)
    {
        var attachments = DraftAttachments.ToList();
        var displayText = string.IsNullOrWhiteSpace(text) && attachments.Count > 0
            ? BuildDefaultAttachmentPrompt(attachments.Count)
            : text;

        var wasEmpty = Messages.Count == 0;
        var isFirstTurn = wasEmpty && _currentSession.Title == "新对话";
        var targetSession = _currentSession;

        var userMsg = new ChatMessageViewModel(isUser: true, displayText, attachments);
        AppendMessageToSession(targetSession, userMsg);

        string? fallbackTitle = null;
        if (isFirstTurn)
        {
            fallbackTitle = displayText.Length > 16 ? displayText[..16] + "…" : displayText;
            targetSession.Title = fallbackTitle;
        }

        targetSession.UpdatedLabel = DateTime.Now.ToString("HH:mm");
        DraftInput = string.Empty;
        DraftAttachments.Clear();
        AttachmentNotice = null;
        NotifyEmptyAndAttachments();
        NotifyMessagesUpdatedIfCurrent(targetSession);

        // 首条会话标题生成与主对话回复并行执行，互不阻塞等待
        if (isFirstTurn && fallbackTitle is not null)
        {
            StartParallelTitleGeneration(targetSession, fallbackTitle, displayText);
        }

        await StreamReplyForUserPromptAsync(targetSession, displayText, attachments, isFirstTurnTransition: wasEmpty);
    }

    private void StartParallelTitleGeneration(
        ChatSessionItemViewModel targetSession,
        string fallbackTitle,
        string firstUserMessage)
    {
        if (!HasActiveModel || _hostFactory is null)
        {
            return;
        }

        try
        {
            _host ??= _hostFactory();
        }
        catch
        {
            return;
        }

        var host = _host;
        var sessionId = targetSession.Id;

        ActiveTitleGenerationTask = Task.Run(async () =>
        {
            try
            {
                var generated = await host.GenerateSessionTitleAsync(sessionId, firstUserMessage);
                if (string.IsNullOrWhiteSpace(generated))
                {
                    return;
                }

                PostToUi(() =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (!Sessions.Contains(targetSession))
                    {
                        return;
                    }

                    // 仅当标题仍是占位或自动截断文本时更新，避免覆盖用户手工改名
                    if (string.Equals(targetSession.Title, fallbackTitle, StringComparison.Ordinal) ||
                        string.Equals(targetSession.Title, "新对话", StringComparison.Ordinal))
                    {
                        targetSession.Title = generated;
                    }
                });
            }
            catch
            {
                // 标题生成失败不影响主对话
            }
        });
    }

    private static void PostToUi(Action action)
    {
        if (Avalonia.Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(action);
        }
        else
        {
            action();
        }
    }

    private async Task StreamReplyForUserPromptAsync(
        ChatSessionItemViewModel targetSession,
        string userPrompt,
        IReadOnlyList<ChatAttachmentItem> sentAttachments,
        bool isFirstTurnTransition = false)
    {
        targetSession.CancelActiveRun();
        var runCts = new CancellationTokenSource();
        var ct = runCts.Token;
        targetSession.ActiveRunCts = runCts;
        targetSession.LastGenerationSucceeded = true;
        targetSession.IsGenerating = true;

        if (ReferenceEquals(targetSession, _currentSession))
        {
            SyncCurrentSessionGeneratingState(triggerHappy: false);
        }

        var modelLabel = HasActiveModel
            ? $"{_modelStore.ActiveProfile?.Name} · {_modelStore.ActiveProfile?.Model}"
            : string.Empty;
        var aiMsg = new ChatMessageViewModel(isUser: false, string.Empty, modelTag: modelLabel)
        {
            IsThinking = true,
            ThinkingLabel = LanaThinkingOrb.PickThinkingPhrase(),
            IsStreaming = true,
        };
        AppendMessageToSession(targetSession, aiMsg);
        if (ReferenceEquals(targetSession, _currentSession))
        {
            StageTransitionRequested?.Invoke(
                this,
                new LanaStageTransitionEventArgs(
                    isFirstTurnTransition ? LanaFlightKind.FirstTurn : LanaFlightKind.NextTurn,
                    aiMsg));
            MessagesUpdated?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            if (!HasActiveModel)
            {
                targetSession.LastGenerationSucceeded = false;
                aiMsg.ErrorText = "还没有配置模型 API 哦，请点击左上角模型按钮前往「模型服务」填入 API Key ✨";
                return;
            }

            await RunLiveModelAsync(targetSession, aiMsg, userPrompt, sentAttachments, ct);
            if (ct.IsCancellationRequested)
            {
                targetSession.LastGenerationSucceeded = false;
                aiMsg.FlushStreamText();
                if (!aiMsg.HasText && !aiMsg.HasError && !aiMsg.HasProposal)
                {
                    aiMsg.SetFallbackText("（已经停下来啦，随时可以继续告诉我哦 🌷）");
                }
            }
        }
        catch (OperationCanceledException)
        {
            targetSession.LastGenerationSucceeded = false;
            aiMsg.FlushStreamText();
            if (!aiMsg.HasText && !aiMsg.HasError && !aiMsg.HasProposal)
            {
                aiMsg.SetFallbackText("（已经停下来啦，随时可以继续告诉我哦 🌷）");
            }
        }
        catch (Exception ex)
        {
            targetSession.LastGenerationSucceeded = false;
            aiMsg.ErrorText = ex.Message;
        }
        finally
        {
            if (aiMsg.HasError)
            {
                targetSession.LastGenerationSucceeded = false;
            }

            aiMsg.FinishStreaming();
            targetSession.UpdatedLabel = DateTime.Now.ToString("HH:mm");
            var isStillActiveRun = ReferenceEquals(targetSession.ActiveRunCts, runCts);
            if (isStillActiveRun)
            {
                targetSession.ActiveRunCts = null;
                targetSession.IsGenerating = false;
            }

            runCts.Dispose();
            if (ReferenceEquals(targetSession, _currentSession))
            {
                SyncCurrentSessionGeneratingState(triggerHappy: isStillActiveRun && targetSession.LastGenerationSucceeded);
                SaveCurrentSessionMessages();
                MessagesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>懒加载 VGList.Agent 内核并消费实时工具调用、流式 SSE、自动执行与细粒度撤销事件流。</summary>
    private async Task RunLiveModelAsync(
        ChatSessionItemViewModel targetSession,
        ChatMessageViewModel aiMsg,
        string userPrompt,
        IReadOnlyList<ChatAttachmentItem> sentAttachments,
        CancellationToken ct)
    {
        if (_hostFactory is not null)
        {
            _host ??= _hostFactory();
            var attachments = sentAttachments
                .Select(a => new AttachmentRef(a.Name, a.FullPath, File.Exists(a.FullPath) ? new FileInfo(a.FullPath).Length : 0, a.KindTag))
                .ToList();

            var req = new AssistantRequest(targetSession.Id, userPrompt, attachments);
            var activeStepQueues = new Dictionary<string, Queue<ChatToolStepViewModel>>(StringComparer.Ordinal);
            var lastFlush = Environment.TickCount64;
            const long minToolSpinMs = 300;

            await foreach (var evt in _host.RunAsync(req, ct))
            {
                switch (evt)
                {
                    case ToolStartedEvent ts:
                        if (!activeStepQueues.TryGetValue(ts.ToolName, out var q))
                        {
                            q = new Queue<ChatToolStepViewModel>();
                            activeStepQueues[ts.ToolName] = q;
                        }

                        // 若流式探测已经为同一工具建立了运行中胶囊，就地更新其精确文案而不重复建两条
                        var existingRunning = q.FirstOrDefault(s => s.IsRunning && string.IsNullOrEmpty(s.PendingCompletedLabel));
                        if (existingRunning is not null)
                        {
                            existingRunning.Label = ts.HumanLabel;
                        }
                        else
                        {
                            var stepVm = new ChatToolStepViewModel(ts.HumanLabel, isRunning: true);
                            q.Enqueue(stepVm);
                            aiMsg.AddToolStep(stepVm);
                        }

                        NotifyMessagesUpdatedIfCurrent(targetSession);
                        break;

                    case ToolFinishedEvent tf:
                        if (activeStepQueues.TryGetValue(tf.ToolName, out var finishedQ) && finishedQ.Count > 0)
                        {
                            var doneStep = finishedQ.Dequeue();
                            var finalLabel = tf.Succeeded ? tf.HumanLabel : $"{tf.HumanLabel}（已反馈给 Lana）";
                            doneStep.PendingCompletedLabel = finalLabel;
                            doneStep.PendingSucceeded = tf.Succeeded;

                            var elapsed = Environment.TickCount64 - doneStep.StartedTickMs;
                            var remaining = Avalonia.Application.Current is not null
                                ? minToolSpinMs - elapsed
                                : 0;

                            if (remaining <= 16)
                            {
                                aiMsg.CompleteToolStep(doneStep, finalLabel, tf.Succeeded);
                                NotifyMessagesUpdatedIfCurrent(targetSession);
                            }
                            else
                            {
                                _ = CompleteToolStepAfterDelayAsync(targetSession, aiMsg, doneStep, finalLabel, tf.Succeeded, (int)remaining, ct);
                            }
                        }

                        break;

                    case TextDeltaEvent td:
                        var now = Environment.TickCount64;
                        var shouldFlush = now - lastFlush >= 48;
                        aiMsg.AppendStreamText(td.Delta, flushNow: shouldFlush);
                        if (shouldFlush)
                        {
                            lastFlush = now;
                            NotifyMessagesUpdatedIfCurrent(targetSession);
                        }

                        break;

                    case ProposalReadyEvent pr:
                        aiMsg.FlushStreamText();
                        aiMsg.Proposal = BuildProposalCardFromAgent(pr.Proposal, isAutoApplied: false);
                        NotifyMessagesUpdatedIfCurrent(targetSession);
                        break;

                    case AutoAppliedEvent aa:
                        aiMsg.FlushStreamText();
                        if (aa.Proposal is not null)
                        {
                            var appliedCard = BuildProposalCardFromAgent(aa.Proposal, isAutoApplied: true);
                            appliedCard.MarkApplied(aa.ChangeSetId, aa.AppliedCount, aa.AppliedEntityMap, isAutoApplied: true);
                            aiMsg.Proposal = appliedCard;
                        }

                        _toast.Show($"已同步 {aa.AppliedCount} 项日程变动 ✨", () =>
                        {
                            if (aiMsg.Proposal is { } card)
                            {
                                UndoProposalCardAll(card);
                            }
                            else
                            {
                                _ = UndoAgentChangeSetAsync(aa.ChangeSetId);
                            }
                        });
                        NotifyMessagesUpdatedIfCurrent(targetSession);
                        break;

                    case MemoryChangedEvent mem when mem.Action == "added":
                        _toast.Show($"Lana 记住啦：{mem.Text}", () =>
                        {
                            new MemoryStore().Remove(mem.MemoryId);
                            _toast.Show("已撤销该条记忆");
                        });
                        break;

                    case ProfileNicknameChangedEvent nick:
                        RefreshGreeting(pickNewVariant: false);
                        _toast.Show($"以后 Lana 就叫你「{nick.Nickname}」啦 ✨");
                        break;

                    case WarningEvent w:
                        _toast.Show(w.Message, durationMs: 4500);
                        break;

                    case ErrorEvent errEvt:
                        aiMsg.ErrorText = errEvt.UserMessage;
                        break;

                    case DoneEvent:
                        aiMsg.FlushStreamText();
                        break;
                }
            }

            aiMsg.FlushStreamText();
            return;
        }

        // 无宿主工厂时的纯 HttpClient 回退路径
        if (!_modelStore.TryResolveActive(out var resolved, out var err) || resolved is null)
        {
            aiMsg.ErrorText = err ?? "尚未配置可用模型";
            return;
        }

        var fallbackStep = new ChatToolStepViewModel("Lana 正在翻看你的日程…", isRunning: true);
        aiMsg.AddToolStep(fallbackStep);
        var contextPrompt = BuildLightSystemPrompt();
        await Task.Delay(180, ct);
        aiMsg.CompleteToolStep(fallbackStep, "已查看当前日程", succeeded: true);

        var history = targetSession.StoredMessages
            .Where(m => !ReferenceEquals(m, aiMsg) && m.HasText)
            .TakeLast(12)
            .Select(m => (m.IsUser ? "user" : "assistant", m.Text))
            .ToList();

        var last = Environment.TickCount64;
        await foreach (var delta in _liveClient.StreamReplyAsync(resolved, contextPrompt, history, ct))
        {
            var now = Environment.TickCount64;
            var flush = now - last >= 28;
            aiMsg.AppendStreamText(delta, flushNow: flush);
            if (flush)
            {
                last = now;
                NotifyMessagesUpdatedIfCurrent(targetSession);
            }
        }

        aiMsg.FlushStreamText();
    }

    private async Task CompleteToolStepAfterDelayAsync(
        ChatSessionItemViewModel targetSession,
        ChatMessageViewModel aiMsg,
        ChatToolStepViewModel step,
        string finalLabel,
        bool succeeded,
        int delayMs,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(Math.Max(16, delayMs), ct);
            if (_disposed || ct.IsCancellationRequested || !step.IsRunning)
            {
                return;
            }

            aiMsg.CompleteToolStep(step, finalLabel, succeeded);
            NotifyMessagesUpdatedIfCurrent(targetSession);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private ChatProposalCardViewModel BuildProposalCardFromAgent(Proposal proposal, bool isAutoApplied)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var items = proposal.Items.Select(i =>
        {
            var (actionKind, cleanTag) = ClassifyProposalAction(i);
            var warn = i.Violations is { Count: > 0 }
                ? string.Join("；", i.Violations.Select(v => v.Message))
                : null;
            var badge = ExtractProposalBadges(i, _repository, today);
            return new ChatProposalItemViewModel(
                actionTag: cleanTag,
                actionKind: actionKind,
                title: i.Title,
                changeSummary: i.ChangeSummary,
                reason: i.Reason,
                operation: i.Operation,
                violationWarning: warn,
                isSelected: true,
                proposalItemId: i.Id,
                dateBadgeText: badge.DateBadge,
                priorityBadgeText: badge.PriorityBadge,
                priorityTier: badge.PriorityTier,
                extraBadgeText: badge.ExtraBadge,
                taskDate: badge.TaskDate,
                dueTime: badge.DueTime,
                reminder: badge.Reminder,
                reminderBadgeText: badge.ReminderBadge);
        }).ToList();

        return new ChatProposalCardViewModel(
            title: isAutoApplied ? "已同步到日程" : "待确认的日程改动",
            subtitle: string.Empty,
            items: items,
            applyCallback: ApplyProposalCard,
            undoAllCallback: UndoProposalCardAll,
            undoItemsCallback: UndoProposalCardItems,
            proposalId: proposal.Id);
    }

    /// <summary>严格根据真实 ScheduleOp 分类动作类型与角标文案（避免未改日期却显示「改期 1」）。</summary>
    public static (string ActionKind, string ActionTag) ClassifyProposalAction(ProposalItem item) =>
        item.Operation switch
        {
            CreateTaskOp => ("add", "新增"),
            CreateListOp => ("add", "新建清单"),
            DeleteTaskOp => ("delete", "删除"),
            SetCompletedOp s => ("edit", s.Completed ? "完成" : "重开"),
            UpdateTaskOp u when u.Patch.Date is not null => ("move", "改期"),
            UpdateTaskOp u when u.Patch.AppendSubtasks is { Count: > 0 } &&
                                u.Patch.Title is null && u.Patch.Priority is null && u.Patch.ListId is null &&
                                u.Patch.DueTime is null && !u.Patch.ClearDueTime && u.Patch.Reminder is null && !u.Patch.ClearReminder => ("edit", "拆步骤"),
            UpdateTaskOp u when u.Patch.ListId is not null &&
                                u.Patch.Title is null && u.Patch.Priority is null && u.Patch.AppendSubtasks is null &&
                                u.Patch.DueTime is null && !u.Patch.ClearDueTime && u.Patch.Reminder is null && !u.Patch.ClearReminder => ("edit", "归类"),
            _ => ("edit", "修改"),
        };

    /// <summary>统一按「月 + 日」中文形式格式化卡片日期胶囊（ADR-0055，不再出现斜杠日期）。</summary>
    public static string FormatProposalDateLabel(DateOnly d, TimeOnly? dueTime, DateOnly today)
    {
        string datePart;
        if (d == today) datePart = $"今天 ({d.Month}月{d.Day}日)";
        else if (d == today.AddDays(1)) datePart = $"明天 ({d.Month}月{d.Day}日)";
        else if (d == today.AddDays(2)) datePart = $"后天 ({d.Month}月{d.Day}日)";
        else if (d == today.AddDays(-1)) datePart = $"昨天 ({d.Month}月{d.Day}日)";
        else datePart = $"{d.Month}月{d.Day}日";

        return dueTime is { } dt ? $"{datePart} {dt:HH:mm}" : datePart;
    }

    public readonly record struct ProposalBadgeInfo(
        string? DateBadge,
        string? PriorityBadge,
        TaskPriority? PriorityTier,
        string? ExtraBadge,
        DateOnly? TaskDate,
        TimeOnly? DueTime,
        TaskReminder? Reminder,
        string? ReminderBadge)
    {
        public void Deconstruct(
            out string? dateBadge,
            out string? priorityBadge,
            out TaskPriority? priorityTier,
            out string? extraBadge)
        {
            dateBadge = DateBadge;
            priorityBadge = PriorityBadge;
            priorityTier = PriorityTier;
            extraBadge = ExtraBadge;
        }
    }

    /// <summary>从提案操作提取清晰可读的日期 / 优先级 / 提醒 / 附加胶囊文案（杜绝晦涩缩写与斜杠日期）。</summary>
    public static ProposalBadgeInfo ExtractProposalBadges(ProposalItem item, ITaskRepository repository, DateOnly today)
    {
        static string FormatPriority(TaskPriority p) => p switch
        {
            TaskPriority.High => "高优先级",
            TaskPriority.Medium => "中优先级",
            TaskPriority.Low => "低优先级",
            _ => "无优先级",
        };

        static string FormatSubtasksLabel(IReadOnlyList<TaskSubtask> subs)
        {
            if (subs.Count == 1)
            {
                var t = subs[0].Title.Trim();
                if (t.Length > 16) t = t[..16] + "…";
                return $"子待办：{t}";
            }

            return $"新增 {subs.Count} 项子待办";
        }

        switch (item.Operation)
        {
            case CreateTaskOp create:
            {
                var d = create.Draft;
                var extraParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(d.ListId) && d.ListId != TaskList.InboxId)
                {
                    var listName = repository.Lists.FirstOrDefault(l => l.Id == d.ListId)?.Name;
                    if (!string.IsNullOrEmpty(listName)) extraParts.Add($"清单：{listName}");
                }

                if (d.Subtasks is { Count: > 0 })
                {
                    extraParts.Add(FormatSubtasksLabel(d.Subtasks));
                }

                return new ProposalBadgeInfo(
                    DateBadge: FormatProposalDateLabel(d.Date, d.DueTime, today),
                    PriorityBadge: FormatPriority(d.Priority),
                    PriorityTier: d.Priority,
                    ExtraBadge: extraParts.Count > 0 ? string.Join(" · ", extraParts) : null,
                    TaskDate: d.Date,
                    DueTime: d.DueTime,
                    Reminder: d.Reminder,
                    ReminderBadge: TaskReminderCalculator.FormatPillBadge(d.Date, d.DueTime, d.Reminder, today));
            }

            case UpdateTaskOp update:
            {
                var existing = repository.GetById(update.TaskId);
                var p = update.Patch;
                var effectiveDate = p.Date ?? existing?.Date;
                var effectiveDueTime = p.ClearDueTime ? null : (p.DueTime ?? existing?.DueTime);
                var effectiveReminder = p.ClearReminder ? null : (p.Reminder ?? existing?.Reminder);

                string? dateBadge;
                if (p.Date is { } nd)
                {
                    dateBadge = existing is not null && existing.Date != nd
                        ? $"{FormatProposalDateLabel(existing.Date, existing.DueTime, today)} → {FormatProposalDateLabel(nd, effectiveDueTime, today)}"
                        : FormatProposalDateLabel(nd, effectiveDueTime, today);
                }
                else if (effectiveDate is { } ed)
                {
                    dateBadge = FormatProposalDateLabel(ed, effectiveDueTime, today);
                }
                else
                {
                    dateBadge = null;
                }

                var prioTier = p.Priority ?? existing?.Priority ?? TaskPriority.None;
                var prioBadge = FormatPriority(prioTier);

                var extras = new List<string>();
                if (p.Title is { } nt) extras.Add($"改为「{nt}」");
                if (p.ListId is { } nl)
                {
                    var listName = repository.Lists.FirstOrDefault(l => l.Id == nl)?.Name ?? nl;
                    extras.Add($"移至「{listName}」");
                }

                if (p.AppendSubtasks is { Count: > 0 } subs)
                {
                    extras.Add(FormatSubtasksLabel(subs));
                }

                var remBadge = effectiveDate is { } rd
                    ? TaskReminderCalculator.FormatPillBadge(rd, effectiveDueTime, effectiveReminder, today)
                    : (effectiveReminder is { Enabled: true } ? "已设提醒" : "添加提醒");

                return new ProposalBadgeInfo(
                    DateBadge: dateBadge,
                    PriorityBadge: prioBadge,
                    PriorityTier: prioTier,
                    ExtraBadge: extras.Count > 0 ? string.Join(" · ", extras) : null,
                    TaskDate: effectiveDate,
                    DueTime: effectiveDueTime,
                    Reminder: effectiveReminder,
                    ReminderBadge: remBadge);
            }

            case SetCompletedOp done:
            {
                var existing = repository.GetById(done.TaskId);
                return new ProposalBadgeInfo(
                    DateBadge: existing is not null ? FormatProposalDateLabel(existing.Date, existing.DueTime, today) : null,
                    PriorityBadge: null,
                    PriorityTier: null,
                    ExtraBadge: done.Completed ? "标记已完成" : "恢复未完成",
                    TaskDate: existing?.Date,
                    DueTime: existing?.DueTime,
                    Reminder: null,
                    ReminderBadge: null);
            }

            case DeleteTaskOp del:
            {
                var existing = repository.GetById(del.TaskId);
                return new ProposalBadgeInfo(
                    DateBadge: existing is not null ? FormatProposalDateLabel(existing.Date, existing.DueTime, today) : null,
                    PriorityBadge: existing is { Priority: not TaskPriority.None } ex ? FormatPriority(ex.Priority) : null,
                    PriorityTier: existing?.Priority,
                    ExtraBadge: "将删除此任务",
                    TaskDate: existing?.Date,
                    DueTime: existing?.DueTime,
                    Reminder: null,
                    ReminderBadge: null);
            }

            case CreateListOp:
                return new ProposalBadgeInfo(null, null, null, "新建自定义清单", null, null, null, null);

            default:
                return new ProposalBadgeInfo(null, null, null, item.ChangeSummary, null, null, null, null);
        }
    }

    private async Task UndoAgentChangeSetAsync(string changeSetId)
    {
        if (_host is null)
        {
            return;
        }

        var res = await _host.UndoAsync(changeSetId);
        _toast.Show(res.Success ? $"已撤销 {res.RevertedCount} 项改动" : "撤销失败或已撤销");
    }

    private void ApplyProposalCard(ChatProposalCardViewModel card)
    {
        if (_host is not null && !string.IsNullOrEmpty(card.ProposalId))
        {
            var selectedIds = card.Items.Where(i => i.IsSelected).Select(i => i.ProposalItemId).ToList();
            if (selectedIds.Count == 0)
            {
                return;
            }

            var res = _host.ApplyProposalAsync(card.ProposalId, selectedIds).GetAwaiter().GetResult();
            if (res.Success)
            {
                card.MarkApplied(res.ChangeSetId, res.AppliedCount, res.AppliedEntityMap, isAutoApplied: false);
                var note = res.SkippedReasons.Count > 0
                    ? $"已应用 {res.AppliedCount} 项（{res.SkippedReasons.Count} 项因任务已不存在跳过）"
                    : $"已同步 {res.AppliedCount} 项改动到日程 ✨";
                _toast.Show(note, () => UndoProposalCardAll(card));
            }
            else
            {
                _toast.Show(res.ErrorMessage ?? "应用提案失败");
            }

            return;
        }

        var chosenItems = card.Items.Where(i => i.IsSelected && i.Operation is not null).ToList();
        var ops = chosenItems.Select(i => i.Operation!).ToList();
        if (ops.Count == 0)
        {
            return;
        }

        if (_schedule is not null)
        {
            var cs = _schedule.Apply(ops, ChangeSource.Assistant, card.Title);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (cs.Entries.Count == chosenItems.Count)
            {
                for (var i = 0; i < chosenItems.Count; i++)
                {
                    map[chosenItems[i].ProposalItemId] = cs.Entries[i].EntityId;
                }
            }

            card.MarkApplied(cs.Id, ops.Count, map, isAutoApplied: false);
            _toast.Show($"已同步 {ops.Count} 项改动到日程 ✨", () => UndoProposalCardAll(card));
        }
        else
        {
            card.MarkApplied(null, ops.Count);
            _toast.Show($"已同步 {ops.Count} 项改动到日程 ✨");
        }
    }

    private void UndoProposalCardItems(ChatProposalCardViewModel card, IReadOnlyList<ChatProposalItemViewModel> items)
    {
        var entityIds = items
            .Where(i => !i.IsItemUndone && !string.IsNullOrEmpty(i.AppliedEntityId))
            .Select(i => i.AppliedEntityId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (entityIds.Count == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(card.AppliedChangeSetId) || _schedule is null)
        {
            card.MarkItemsUndone(entityIds);
            return;
        }

        var result = _schedule.UndoEntries(card.AppliedChangeSetId, entityIds);
        if (result.RevertedCount > 0)
        {
            var skippedIds = new HashSet<string>(result.Skipped.Select(s => s.EntityId), StringComparer.Ordinal);
            var actuallyUndone = entityIds.Where(id => !skippedIds.Contains(id)).ToList();
            card.MarkItemsUndone(actuallyUndone);

            var msg = result.Skipped.Count > 0
                ? $"已撤销 {result.RevertedCount} 项，{result.Skipped.Count} 项因之后又被修改而保留"
                : (result.RevertedCount == 1 ? $"已撤销「{items[0].Title}」" : $"已撤销选中的 {result.RevertedCount} 项改动");
            _toast.Show(msg);
        }
        else if (result.Skipped.Count > 0)
        {
            _toast.Show(result.Skipped[0].Reason);
        }
        else
        {
            _toast.Show(result.Error ?? "该条目已撤销或无法撤销");
        }
    }

    private void UndoProposalCardAll(ChatProposalCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.AppliedChangeSetId) || _schedule is null)
        {
            card.MarkAllUndone();
            return;
        }

        var result = _schedule.Undo(card.AppliedChangeSetId);
        card.MarkAllUndone();
        if (result.Skipped.Count > 0)
        {
            _toast.Show($"已撤销 {result.RevertedCount} 项，{result.Skipped.Count} 项因之后又被修改而保留");
        }
        else
        {
            _toast.Show($"已全部撤销 {result.RevertedCount} 项改动");
        }
    }

    private string BuildLightSystemPrompt()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lunar = CnCalendar.Describe(today);
        var pending = _repository.Query(new TaskQuery { IncludeCompleted = false }).Take(20).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"你是 Lana，Plana 桌面日程应用里的可爱贴心小助理。今天是 {today:yyyy-MM-dd}（{lunar.Text}）。");
        var nick = _profileStore.GetNickname();
        if (!string.IsNullOrWhiteSpace(nick))
        {
            sb.AppendLine($"用户的称呼是「{nick}」，请自然亲切地这样称呼对方。");
        }

        sb.AppendLine("回复语气温暖俏皮、自然可爱，适当点缀可爱的颜文字（如 (*^▽^*)、(●'◡'●)、(≧▽≦)）与 emoji，保持简洁清晰、不浮夸。");
        sb.AppendLine("以下是用户当前未完成的部分任务摘要（只读参考）：");
        foreach (var t in pending)
        {
            sb.AppendLine($"- [{t.Date:yyyy-MM-dd}][优先级:{t.Priority}] {t.Title}");
        }

        return sb.ToString();
    }

    private void SaveCurrentSessionMessages()
    {
        _currentSession.HasLoadedStoredMessages = true;
        _currentSession.StoredMessages.Clear();
        _currentSession.StoredMessages.AddRange(Messages);
    }

    private void AppendMessageToSession(ChatSessionItemViewModel session, ChatMessageViewModel message)
    {
        session.HasLoadedStoredMessages = true;
        session.StoredMessages.Add(message);
        if (ReferenceEquals(session, _currentSession))
        {
            Messages.Add(message);
            if (message.IsAssistant)
            {
                RefreshLatestAssistantMarker();
            }
        }
    }

    private void NotifyMessagesUpdatedIfCurrent(ChatSessionItemViewModel session)
    {
        if (ReferenceEquals(session, _currentSession))
        {
            MessagesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncCurrentSessionGeneratingState(bool triggerHappy)
    {
        var next = _currentSession.IsGenerating;
        if (triggerHappy)
        {
            _lastGenerationSucceeded = _currentSession.LastGenerationSucceeded;
            var changed = IsGenerating != next;
            IsGenerating = next;
            if (!changed)
            {
                RefreshStageLanaState(force: false);
            }

            return;
        }

        _suppressHappyOnGeneratingChange = true;
        try
        {
            var changed = IsGenerating != next;
            IsGenerating = next;
            if (!changed)
            {
                _wasGenerating = next;
                RefreshStageLanaState(force: true);
            }
        }
        finally
        {
            _suppressHappyOnGeneratingChange = false;
        }
    }

    private void OnModelStoreChanged(object? sender, EventArgs e) => UiDispatcher.PostIfNeeded(() =>
    {
        if (!_disposed)
        {
            RefreshModelStatus();
        }
    });

    private void RefreshModelStatus()
    {
        var active = _modelStore.ActiveProfile;
        HasActiveModel = active is not null;
        if (active is not null)
        {
            var preset = ProviderCatalog.Shared.FindById(active.PresetId);
            ActiveModelLabel = $"{active.Name} · {active.Model}";
            ActiveProviderMonogram = preset?.Monogram ?? "自";
            ActiveProviderBrush = ProviderCardViewModel.CreateBrush(preset?.Color ?? "#E46B92");
            ActiveProviderLogo = ProviderLogoLoader.TryLoad(preset?.Id);
        }
        else
        {
            ActiveModelLabel = "尚未配置模型";
            ActiveProviderMonogram = "!";
            ActiveProviderBrush = ProviderCardViewModel.CreateBrush("#C27613");
            ActiveProviderLogo = null;
        }

        AvailableModels.Clear();
        foreach (var profile in _modelStore.Profiles)
        {
            var preset = ProviderCatalog.Shared.FindById(profile.PresetId);
            AvailableModels.Add(new ModelSwitcherItemViewModel(
                profileId: profile.Id,
                name: profile.Name,
                model: profile.Model,
                logoBitmap: ProviderLogoLoader.TryLoad(preset?.Id),
                monogram: preset?.Monogram ?? "自",
                brandBrush: ProviderCardViewModel.CreateBrush(preset?.Color ?? "#E46B92"),
                supportsImages: profile.SupportsImages,
                isActive: active is not null && string.Equals(active.Id, profile.Id, StringComparison.Ordinal)));
        }

        OnPropertyChanged(nameof(HasAvailableModels));
    }

    private static Bitmap? TryLoadThumbnail(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Bitmap.DecodeToWidth(fs, 360);
        }
        catch
        {
            return null;
        }
    }

    partial void OnActiveProviderLogoChanged(Bitmap? value) => OnPropertyChanged(nameof(HasActiveProviderLogo));

    private void NotifyEmptyAndAttachments()
    {
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(HasDraftAttachments));
        OnPropertyChanged(nameof(HasAttachmentNotice));
        OnPropertyChanged(nameof(CanSend));
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    /// <summary>刷新每条助理消息的「是否为最新回复」标记，确保活体 Lana 仅停靠于最新回复、历史回复展示静影。</summary>
    public void RefreshLatestAssistantMarker()
    {
        ChatMessageViewModel? latest = null;
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            if (Messages[i].IsAssistant)
            {
                latest = Messages[i];
                break;
            }
        }

        foreach (var msg in Messages)
        {
            if (msg.IsAssistant)
            {
                msg.IsLatestAssistantMessage = ReferenceEquals(msg, latest);
            }
        }

        LatestAssistantMessage = latest;
    }

    /// <summary>Lana 庆祝（Happy 2s）完成后回落至 Idle / Listening（幂等，左栏与舞台共用）。</summary>
    public void CompleteLanaHappy() => RefreshStageLanaState(force: true);

    private void RefreshStageLanaState(bool force = false)
    {
        if (!IsPageActive)
        {
            StageLanaState = LanaVisualState.Sleep;
            return;
        }

        if (IsGenerating)
        {
            StageLanaState = LanaVisualState.Thinking;
            return;
        }

        // Happy 约 2 秒庆祝期间不被输入框聚焦变化打断
        if (!force && StageLanaState == LanaVisualState.Happy)
        {
            return;
        }

        StageLanaState = IsComposerFocused ? LanaVisualState.Listening : LanaVisualState.Idle;
    }

    partial void OnIsPageActiveChanged(bool value)
    {
        if (value)
        {
            EnsurePersistedSessionsLoaded();
        }

        RefreshStageLanaState(force: true);
    }
    partial void OnIsComposerFocusedChanged(bool value) => RefreshStageLanaState();
    partial void OnDraftInputChanged(string value) => OnPropertyChanged(nameof(CanSend));
    partial void OnIsGeneratingChanged(bool value)
    {
        if (_suppressHappyOnGeneratingChange)
        {
            _wasGenerating = value;
            RefreshStageLanaState(force: true);
            OnPropertyChanged(nameof(CanSend));
            return;
        }

        if (value)
        {
            _wasGenerating = true;
            _lastGenerationSucceeded = true;
            if (IsPageActive)
            {
                StageLanaState = LanaVisualState.Thinking;
            }
        }
        else if (_wasGenerating)
        {
            _wasGenerating = false;
            if (IsPageActive && _lastGenerationSucceeded)
            {
                StageLanaState = LanaVisualState.Happy;
            }
            else
            {
                RefreshStageLanaState(force: true);
            }
        }
        else
        {
            RefreshStageLanaState();
        }

        OnPropertyChanged(nameof(CanSend));
    }

    partial void OnAttachmentNoticeChanged(string? value) => OnPropertyChanged(nameof(HasAttachmentNotice));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentSession.CancelActiveRun();
        foreach (var s in Sessions)
        {
            s.CancelActiveRun();
        }

        SyncCurrentSessionGeneratingState(triggerHappy: false);
        _host?.Dispose();
        _modelStore.Changed -= OnModelStoreChanged;
        _modelStore.ActiveChanged -= OnModelStoreChanged;
    }
}
