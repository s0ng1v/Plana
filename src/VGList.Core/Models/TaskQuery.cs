namespace VGList.Core.Models;

/// <summary>查询条件。为空的条件不参与过滤。</summary>
public sealed record TaskQuery
{
    public DateOnly? Date { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public string? ListId { get; init; }

    public string? Tag { get; init; }

    public bool IncludeCompleted { get; init; } = true;

    /// <summary>是否包含回收站中已软删除的任务（默认 false，所有常规视图自动屏蔽回收站）。</summary>
    public bool IncludeDeleted { get; init; }

    /// <summary>仅查询回收站中的任务。</summary>
    public bool DeletedOnly { get; init; }

    public static TaskQuery ForDate(DateOnly date) => new() { Date = date };
}
