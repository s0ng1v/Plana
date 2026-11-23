namespace VGList.Core.Dates;

/// <summary>单次中文自然语言日期解析结果。</summary>
public sealed record DateParseResult(
    DateOnly Date,
    int Start,
    int Length,
    string Matched,
    string CleanTitle);

/// <summary>离线中文自然语言日期解析器。供输入框自动填日期与助理历法工具共用。</summary>
public interface IChineseDateParser
{
    /// <summary>取最靠前且有效的一处日期表达；若移除日期词后标题为空则返回 null。</summary>
    DateParseResult? Parse(string text, DateOnly today);

    /// <summary>解析文本中全部合法日期表达（不要求移除后标题非空，供助理工具使用）。</summary>
    IReadOnlyList<DateParseResult> ParseAll(string text, DateOnly today);
}
