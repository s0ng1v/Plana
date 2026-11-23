using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VGList.Core.Models;

namespace VGList.Desktop.Controls;

public enum ReminderOffsetUnit
{
    Minutes,
    Hours,
    Days,
}

/// <summary>
/// 通用日程时刻与提醒编辑器视图模型（Lana 卡片与任务详情面板共用）：
/// 支持具体时刻、提前任意时间（分钟/小时/天）与反复提醒，所有调整即时生效、无需确认按钮。
/// </summary>
public sealed partial class ReminderEditorViewModel : ObservableObject
{
    private readonly Action<TimeOnly?, TaskReminder?> _onChanged;
    private DateOnly _taskDate;
    private DateOnly _today;
    private TimeOnly? _dueTime;
    private bool _syncing;

    public ReminderEditorViewModel(Action<TimeOnly?, TaskReminder?> onChanged)
    {
        _onChanged = onChanged;
        _today = DateOnly.FromDateTime(DateTime.Today);
        _taskDate = _today;
    }

    [ObservableProperty]
    private string _dueTimeText = string.Empty;

    [ObservableProperty]
    private bool _isReminderEnabled;

    [ObservableProperty]
    private int _offsetMinutes = TaskReminder.DefaultOffsetMinutes;

    [ObservableProperty]
    private string _customAmountText = "10";

    [ObservableProperty]
    private ReminderOffsetUnit _customUnit = ReminderOffsetUnit.Minutes;

    [ObservableProperty]
    private bool _isRepeatEnabled;

    [ObservableProperty]
    private int _repeatIntervalMinutes = 5;

    [ObservableProperty]
    private string _repeatIntervalText = "5";

    [ObservableProperty]
    private int _repeatMaxCount = TaskReminder.DefaultRepeatMaxCount;

    public bool HasDueTime => _dueTime is not null;

    public bool IsOff => !IsReminderEnabled;
    public bool IsOnTime => IsReminderEnabled && OffsetMinutes == 0;
    public bool Is5Min => IsReminderEnabled && OffsetMinutes == 5;
    public bool Is10Min => IsReminderEnabled && OffsetMinutes == 10;
    public bool Is30Min => IsReminderEnabled && OffsetMinutes == 30;
    public bool Is1Hour => IsReminderEnabled && OffsetMinutes == 60;
    public bool Is1Day => IsReminderEnabled && OffsetMinutes == 1440;

    public bool IsUnitMinutes => CustomUnit == ReminderOffsetUnit.Minutes;
    public bool IsUnitHours => CustomUnit == ReminderOffsetUnit.Hours;
    public bool IsUnitDays => CustomUnit == ReminderOffsetUnit.Days;

    public bool IsRep5Min => IsRepeatEnabled && RepeatIntervalMinutes == 5;
    public bool IsRep10Min => IsRepeatEnabled && RepeatIntervalMinutes == 10;
    public bool IsRep15Min => IsRepeatEnabled && RepeatIntervalMinutes == 15;
    public bool IsRep30Min => IsRepeatEnabled && RepeatIntervalMinutes == 30;

    public bool IsMax3 => RepeatMaxCount == 3;
    public bool IsMax5 => RepeatMaxCount == 5;
    public bool IsMaxInfinite => RepeatMaxCount == 0;

    public string RepeatToggleLabel => IsRepeatEnabled ? "已开启" : "已关闭";

    public string TriggerPreviewText
    {
        get
        {
            var current = BuildReminder();
            if (current is null || !current.Enabled)
            {
                return "未开启提醒";
            }

            var trigger = TaskReminderCalculator.ComputeInitialTriggerAt(_taskDate, _dueTime, current);
            if (trigger is null)
            {
                return "未开启提醒";
            }

            var local = trigger.Value.LocalDateTime;
            var triggerDate = DateOnly.FromDateTime(local);
            var datePart = triggerDate == _today
                ? "今天"
                : triggerDate == _today.AddDays(1)
                    ? "明天"
                    : $"{local.Month}月{local.Day}日";
            var baseText = $"将于 {datePart} {local:HH:mm} 推送 Windows 通知";
            if (!current.IsRepeating)
            {
                return baseText;
            }

            var repeatText = current.RepeatMaxCount == 0
                ? $"每 {current.RepeatIntervalMinutes} 分钟重复至完成"
                : $"每 {current.RepeatIntervalMinutes} 分钟重复（共 {current.RepeatMaxCount} 次）";
            return $"{baseText} · {repeatText}";
        }
    }

    public void Load(DateOnly taskDate, TimeOnly? dueTime, TaskReminder? reminder, DateOnly today)
    {
        _syncing = true;
        try
        {
            _taskDate = taskDate;
            _today = today;
            _dueTime = dueTime;
            DueTimeText = dueTime is { } dt ? dt.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

            if (reminder is { Enabled: true })
            {
                IsReminderEnabled = true;
                OffsetMinutes = Math.Max(0, reminder.OffsetMinutes);
                SyncCustomFromOffset(OffsetMinutes);
                IsRepeatEnabled = reminder.RepeatIntervalMinutes is > 0;
                RepeatIntervalMinutes = reminder.RepeatIntervalMinutes is > 0 ? reminder.RepeatIntervalMinutes.Value : 5;
                RepeatIntervalText = RepeatIntervalMinutes.ToString(CultureInfo.InvariantCulture);
                RepeatMaxCount = Math.Max(0, reminder.RepeatMaxCount);
            }
            else
            {
                IsReminderEnabled = false;
                OffsetMinutes = TaskReminder.DefaultOffsetMinutes;
                SyncCustomFromOffset(OffsetMinutes);
                IsRepeatEnabled = false;
                RepeatIntervalMinutes = 5;
                RepeatIntervalText = "5";
                RepeatMaxCount = TaskReminder.DefaultRepeatMaxCount;
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
    }

    public void PickQuickTime(string hhmm)
    {
        if (!TaskReminderCalculator.TryParseTimeOnly(hhmm, out var parsed))
        {
            return;
        }

        _syncing = true;
        try
        {
            _dueTime = parsed;
            DueTimeText = parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (!IsReminderEnabled)
            {
                IsReminderEnabled = true;
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void ClearDueTime()
    {
        if (_dueTime is null && string.IsNullOrEmpty(DueTimeText))
        {
            return;
        }

        _syncing = true;
        try
        {
            _dueTime = null;
            DueTimeText = string.Empty;
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void DisableReminder()
    {
        _syncing = true;
        try
        {
            IsReminderEnabled = false;
            IsRepeatEnabled = false;
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void SelectPresetOffset(int minutes)
    {
        var clamped = Math.Max(0, minutes);
        _syncing = true;
        try
        {
            IsReminderEnabled = true;
            OffsetMinutes = clamped;
            SyncCustomFromOffset(clamped);
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void SelectUnit(ReminderOffsetUnit unit)
    {
        _syncing = true;
        try
        {
            CustomUnit = unit;
            IsReminderEnabled = true;
            var amount = ParsePositiveOrZero(CustomAmountText, fallback: unit == ReminderOffsetUnit.Minutes ? 10 : 1);
            OffsetMinutes = ConvertToMinutes(amount, unit);
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void ToggleRepeat()
    {
        _syncing = true;
        try
        {
            IsRepeatEnabled = !IsRepeatEnabled;
            if (IsRepeatEnabled)
            {
                IsReminderEnabled = true;
                if (RepeatIntervalMinutes <= 0)
                {
                    RepeatIntervalMinutes = 5;
                    RepeatIntervalText = "5";
                }
            }
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void SelectRepeatInterval(int minutes)
    {
        var clamped = Math.Clamp(minutes, 1, 1440);
        _syncing = true;
        try
        {
            IsReminderEnabled = true;
            IsRepeatEnabled = true;
            RepeatIntervalMinutes = clamped;
            RepeatIntervalText = clamped.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    public void SelectRepeatMaxCount(int maxCount)
    {
        var clamped = Math.Clamp(maxCount, 0, 99);
        _syncing = true;
        try
        {
            IsReminderEnabled = true;
            IsRepeatEnabled = true;
            RepeatMaxCount = clamped;
        }
        finally
        {
            _syncing = false;
        }

        RefreshComputed();
        EmitChanged();
    }

    partial void OnDueTimeTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmed))
        {
            if (_dueTime is not null)
            {
                _dueTime = null;
                RefreshComputed();
                EmitChanged();
            }

            return;
        }

        if (TaskReminderCalculator.TryParseTimeOnly(trimmed, out var parsed) && parsed != _dueTime)
        {
            _dueTime = parsed;
            if (!IsReminderEnabled)
            {
                _syncing = true;
                IsReminderEnabled = true;
                _syncing = false;
            }

            RefreshComputed();
            EmitChanged();
        }
    }

    partial void OnCustomAmountTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num >= 0)
        {
            _syncing = true;
            try
            {
                IsReminderEnabled = true;
                OffsetMinutes = ConvertToMinutes(num, CustomUnit);
            }
            finally
            {
                _syncing = false;
            }

            RefreshComputed();
            EmitChanged();
        }
    }

    partial void OnRepeatIntervalTextChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        if (int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var num) && num >= 1)
        {
            _syncing = true;
            try
            {
                IsReminderEnabled = true;
                IsRepeatEnabled = true;
                RepeatIntervalMinutes = Math.Clamp(num, 1, 1440);
            }
            finally
            {
                _syncing = false;
            }

            RefreshComputed();
            EmitChanged();
        }
    }

    private TaskReminder? BuildReminder()
    {
        if (!IsReminderEnabled)
        {
            return null;
        }

        return new TaskReminder
        {
            Enabled = true,
            OffsetMinutes = Math.Max(0, OffsetMinutes),
            RepeatIntervalMinutes = IsRepeatEnabled ? Math.Clamp(RepeatIntervalMinutes, 1, 1440) : null,
            RepeatMaxCount = IsRepeatEnabled ? Math.Max(0, RepeatMaxCount) : TaskReminder.DefaultRepeatMaxCount,
        };
    }

    private void EmitChanged() => _onChanged(_dueTime, BuildReminder());

    private void SyncCustomFromOffset(int minutes)
    {
        if (minutes > 0 && minutes % 1440 == 0)
        {
            CustomUnit = ReminderOffsetUnit.Days;
            CustomAmountText = (minutes / 1440).ToString(CultureInfo.InvariantCulture);
        }
        else if (minutes > 0 && minutes % 60 == 0)
        {
            CustomUnit = ReminderOffsetUnit.Hours;
            CustomAmountText = (minutes / 60).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            CustomUnit = ReminderOffsetUnit.Minutes;
            CustomAmountText = minutes.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int ConvertToMinutes(int amount, ReminderOffsetUnit unit)
    {
        var safe = Math.Clamp(amount, 0, 365 * 24 * 60);
        return unit switch
        {
            ReminderOffsetUnit.Days => Math.Min(safe * 1440, 365 * 1440),
            ReminderOffsetUnit.Hours => Math.Min(safe * 60, 365 * 1440),
            _ => safe,
        };
    }

    private static int ParsePositiveOrZero(string? raw, int fallback) =>
        int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
            ? n
            : fallback;

    private void RefreshComputed()
    {
        OnPropertyChanged(nameof(HasDueTime));
        OnPropertyChanged(nameof(IsOff));
        OnPropertyChanged(nameof(IsOnTime));
        OnPropertyChanged(nameof(Is5Min));
        OnPropertyChanged(nameof(Is10Min));
        OnPropertyChanged(nameof(Is30Min));
        OnPropertyChanged(nameof(Is1Hour));
        OnPropertyChanged(nameof(Is1Day));
        OnPropertyChanged(nameof(IsUnitMinutes));
        OnPropertyChanged(nameof(IsUnitHours));
        OnPropertyChanged(nameof(IsUnitDays));
        OnPropertyChanged(nameof(IsRep5Min));
        OnPropertyChanged(nameof(IsRep10Min));
        OnPropertyChanged(nameof(IsRep15Min));
        OnPropertyChanged(nameof(IsRep30Min));
        OnPropertyChanged(nameof(IsMax3));
        OnPropertyChanged(nameof(IsMax5));
        OnPropertyChanged(nameof(IsMaxInfinite));
        OnPropertyChanged(nameof(RepeatToggleLabel));
        OnPropertyChanged(nameof(TriggerPreviewText));
    }
}
