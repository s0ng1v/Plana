namespace VGList.App.Mcp;

public enum McpConfirmationOutcome
{
    Approved,
    Denied,
    TimedOut,
    Cancelled,
}

/// <summary>
/// A7 外部 AI 危险操作待确认队列（删除任务、一次改期 > 5 项）：
/// 线程安全；单请求默认最多等待 90 秒，超时自动按拒绝处理并移除条目。
/// </summary>
public sealed class McpPendingConfirmationQueue
{
    private readonly object _gate = new();
    private readonly List<PendingEntry> _entries = [];

    private sealed record PendingEntry(McpPendingConfirmation Request, TaskCompletionSource<McpConfirmationOutcome> Tcs);

    public event EventHandler<McpPendingConfirmation>? RequestAdded;
    public event EventHandler? Changed;

    public IReadOnlyList<McpPendingConfirmation> GetSnapshot()
    {
        lock (_gate)
        {
            return _entries.Select(e => e.Request).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public async Task<McpConfirmationOutcome> EnqueueAndWaitAsync(
        McpPendingConfirmation request,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<McpConfirmationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new PendingEntry(request, tcs);

        lock (_gate)
        {
            _entries.Add(entry);
        }

        RequestAdded?.Invoke(this, request);
        Changed?.Invoke(this, EventArgs.Empty);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        await using var reg = timeoutCts.Token.Register(() =>
        {
            var outcome = ct.IsCancellationRequested
                ? McpConfirmationOutcome.Cancelled
                : McpConfirmationOutcome.TimedOut;
            tcs.TrySetResult(outcome);
        });

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            RemoveEntry(request.Id);
        }
    }

    public bool Resolve(string requestId, bool approved)
    {
        PendingEntry? target;
        lock (_gate)
        {
            target = _entries.FirstOrDefault(e => string.Equals(e.Request.Id, requestId, StringComparison.Ordinal));
            if (target is not null)
            {
                _entries.Remove(target);
            }
        }

        if (target is null)
        {
            return false;
        }

        var set = target.Tcs.TrySetResult(approved ? McpConfirmationOutcome.Approved : McpConfirmationOutcome.Denied);
        Changed?.Invoke(this, EventArgs.Empty);
        return set;
    }

    public void RejectAll()
    {
        List<PendingEntry> snapshot;
        lock (_gate)
        {
            snapshot = [.. _entries];
            _entries.Clear();
        }

        foreach (var entry in snapshot)
        {
            entry.Tcs.TrySetResult(McpConfirmationOutcome.Cancelled);
        }

        if (snapshot.Count > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RemoveEntry(string requestId)
    {
        var removed = false;
        lock (_gate)
        {
            var idx = _entries.FindIndex(e => string.Equals(e.Request.Id, requestId, StringComparison.Ordinal));
            if (idx >= 0)
            {
                _entries.RemoveAt(idx);
                removed = true;
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
