using VGList.Core.Dates;

namespace VGList.App.Services;

/// <summary>
/// 跨入口共享的「中文自然语言日期自动填充」状态机：
/// 监听草稿文本变化（调用方可防抖或直接传入），用户手动选过日期后本次草稿不再自动覆盖；提交时移除已识别日期词。
/// </summary>
public sealed class DraftDateAutoFill
{
    private readonly IChineseDateParser _parser;
    private readonly Func<bool> _isEnabled;
    private DateOnly _defaultDate;

    public DraftDateAutoFill(DateOnly defaultDate, Func<bool>? isEnabled = null, IChineseDateParser? parser = null)
    {
        _defaultDate = defaultDate;
        _isEnabled = isEnabled ?? (() => true);
        _parser = parser ?? ChineseDateParser.Shared;
    }

    public bool UserPickedDate { get; private set; }

    public DateParseResult? LastMatch { get; private set; }

    public void SetDefaultDate(DateOnly defaultDate) => _defaultDate = defaultDate;

    public void MarkUserPickedDate()
    {
        UserPickedDate = true;
        LastMatch = null;
    }

    /// <summary>根据最新草稿文本尝试识别日期；若应更新草稿日期，返回新日期，否则返回 null。</summary>
    public DateOnly? Evaluate(string? text, DateOnly today)
    {
        if (!_isEnabled() || UserPickedDate)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            if (LastMatch is not null)
            {
                LastMatch = null;
                return _defaultDate;
            }

            return null;
        }

        var match = _parser.Parse(text, today);
        if (match is null)
        {
            if (LastMatch is not null)
            {
                LastMatch = null;
                return _defaultDate;
            }

            return null;
        }

        LastMatch = match;
        return match.Date;
    }

    /// <summary>提交前解析最终标题与日期：仅当用户未手选日期且当前文本能识别出有效日期时剥离日期词。</summary>
    public (string Title, DateOnly Date) ResolveForSubmit(string rawText, DateOnly currentDraftDate, DateOnly today)
    {
        var trimmed = rawText.Trim();
        if (!_isEnabled() || UserPickedDate || string.IsNullOrEmpty(trimmed))
        {
            return (trimmed, currentDraftDate);
        }

        var match = _parser.Parse(trimmed, today);
        if (match is null || string.IsNullOrWhiteSpace(match.CleanTitle))
        {
            return (trimmed, currentDraftDate);
        }

        return (match.CleanTitle, match.Date);
    }

    public void Reset(DateOnly? newDefaultDate = null)
    {
        if (newDefaultDate is { } d)
        {
            _defaultDate = d;
        }

        UserPickedDate = false;
        LastMatch = null;
    }
}

/// <summary>全局可订阅的「自动识别日期」开关状态，供主窗口与两个小组件即时同步。</summary>
public static class AutoDetectDatePreference
{
    public static bool IsEnabled { get; set; } = true;
}
