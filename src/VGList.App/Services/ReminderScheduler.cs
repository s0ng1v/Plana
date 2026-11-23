using VGList.Core.Abstractions;
using VGList.Core.Models;

namespace VGList.App.Services;

/// <summary>单次到点提醒触发载荷。</summary>
public sealed record ReminderTriggeredEventArgs(
    TaskItem Task,
    DateTimeOffset TriggeredAt,
    int FireNumber,
    bool IsRepeat,
    string NotificationTitle,
    string NotificationBody);

/// <summary>
/// 常驻轻量任务提醒调度器（ADR-0055）：
/// 位于 VGList.App，零 VGList.Agent 依赖；定期检查未完成任务的提醒配置，
/// 支持提前任意时间与按间隔反复提醒，并在休眠唤醒后避免刷屏补发。
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxOneShotCatchupWindow = TimeSpan.FromHours(24);

    private readonly ITaskRepository _repository;
    private readonly Action<ReminderTriggeredEventArgs> _onTriggered;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _pollInterval;
    private readonly Lock _gate = new();

    private Timer? _timer;
    private bool _disposed;

    public ReminderScheduler(
        ITaskRepository repository,
        Action<ReminderTriggeredEventArgs> onTriggered,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? pollInterval = null)
    {
        _repository = repository;
        _onTriggered = onTriggered;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null)
            {
                return;
            }

            _timer = new Timer(_ => CheckDueReminders(), null, TimeSpan.FromSeconds(3), _pollInterval);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>同步扫描一次到期提醒并触发回调（供定时器与单元测试直接调用）。</summary>
    public IReadOnlyList<ReminderTriggeredEventArgs> CheckDueReminders(DateTimeOffset? nowOverride = null)
    {
        var fired = new List<ReminderTriggeredEventArgs>();

        lock (_gate)
        {
            if (_disposed)
            {
                return fired;
            }

            var now = nowOverride ?? _clock();
            IReadOnlyList<TaskItem> activeTasks;
            try
            {
                activeTasks = _repository.Query(new TaskQuery { IncludeCompleted = false });
            }
            catch
            {
                return fired;
            }

            foreach (var task in activeTasks)
            {
                var rem = task.Reminder;
                if (rem is null || !rem.Enabled)
                {
                    continue;
                }

                var nextAt = TaskReminderCalculator.ComputeNextTriggerAt(task.Date, task.DueTime, rem);
                if (nextAt is null || nextAt.Value > now)
                {
                    continue;
                }

                // 超过 24 小时的陈旧单次提醒直接记为已过期，避免老日程弹窗打扰
                if (rem.FiredCount == 0 && !rem.IsRepeating && (now - nextAt.Value) > MaxOneShotCatchupWindow)
                {
                    _repository.RecordReminderFired(task.Id, 1, now);
                    continue;
                }

                var nextCount = rem.FiredCount + 1;
                var isRepeat = nextCount > 1;
                _repository.RecordReminderFired(task.Id, nextCount, now);

                var (title, body) = FormatNotification(task, rem, nextCount, isRepeat);
                var payload = new ReminderTriggeredEventArgs(
                    Task: task.Clone(),
                    TriggeredAt: now,
                    FireNumber: nextCount,
                    IsRepeat: isRepeat,
                    NotificationTitle: title,
                    NotificationBody: body);
                fired.Add(payload);
            }
        }

        foreach (var item in fired)
        {
            try
            {
                _onTriggered(item);
            }
            catch
            {
            }
        }

        return fired;
    }

    private static (string Title, string Body) FormatNotification(
        TaskItem task,
        TaskReminder reminder,
        int fireNumber,
        bool isRepeat)
    {
        var title = isRepeat
            ? $"Plana 反复提醒（第 {fireNumber} 次）"
            : "Plana 日程提醒";

        var parts = new List<string> { task.Title };

        var timeText = task.DueTime is { } dt
            ? $"{task.Date.Month}月{task.Date.Day}日 {dt:HH:mm}"
            : $"{task.Date.Month}月{task.Date.Day}日";

        if (reminder.AbsoluteAt is null)
        {
            parts.Add($"{timeText} · {TaskReminderCalculator.FormatOffsetLabel(reminder.OffsetMinutes)}");
        }
        else
        {
            parts.Add($"日程：{timeText}");
        }

        if (reminder.IsRepeating)
        {
            var hasMore = reminder.RepeatMaxCount == 0 || fireNumber < reminder.RepeatMaxCount;
            if (hasMore)
            {
                parts.Add($"每 {reminder.RepeatIntervalMinutes} 分钟重复，完成后自动停止");
            }
        }

        return (title, string.Join("\n", parts));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
