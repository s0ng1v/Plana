using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VGList.App.Mcp;
using VGList.Desktop.Services;

namespace VGList.Desktop.Main.Settings;

public sealed class McpAuditRowViewModel(McpAuditEntry entry)
{
    public string TimeText { get; } = entry.Time.ToLocalTime().ToString("MM-dd HH:mm:ss");
    public string Client { get; } = entry.Client;
    public string ToolLabel { get; } = FormatTool(entry.Tool);
    public string CountText { get; } = entry.Count > 0 ? $"{entry.Count} 项" : "—";
    public string ResultLabel { get; } = FormatResult(entry.Result);
    public bool IsError { get; } = !string.Equals(entry.Result, "ok", StringComparison.OrdinalIgnoreCase);

    private static string FormatTool(string tool) => tool switch
    {
        "get_today" => "读取今日",
        "list_tasks" => "查询任务",
        "list_lists" => "查询清单",
        "resolve_date" => "解析日期",
        "cn_calendar_info" => "查询历法",
        "create_tasks" => "新建任务",
        "update_task" => "修改任务",
        "complete_task" => "完成状态",
        "reschedule_tasks" => "批量改期",
        "delete_tasks" => "删除任务",
        _ => tool,
    };

    private static string FormatResult(string result) => result switch
    {
        "ok" => "成功",
        "read_only_denied" => "只读拒绝",
        "rate_limited" => "触发限流",
        "user_denied" => "用户拒绝",
        "confirmation_timeout" => "确认超时",
        "unknown_tool" => "未授权工具",
        "version_mismatch" => "版本不匹配",
        "cancelled" => "已取消",
        _ => result,
    };
}

/// <summary>
/// 设置页「外部 AI 接入（A7 · 本地 MCP）」视图模型：
/// 总开关（默认关）、只读/读写权限、各主流客户端配置片段一键复制、最近 50 条脱敏访问记录。
/// </summary>
public sealed partial class ExternalMcpSettingsViewModel : ObservableObject, IDisposable
{
    private readonly WidgetCoordinator _coordinator;
    private readonly ToastService _toast;
    private bool _syncing;
    private bool _disposed;

    public ExternalMcpSettingsViewModel(WidgetCoordinator coordinator, ToastService toast)
    {
        _coordinator = coordinator;
        _toast = toast;
        _coordinator.StateChanged += OnCoordinatorStateChanged;
        _coordinator.McpAuditUpdated += OnAuditUpdated;
        LoadFromCoordinator();
        RefreshAudit();
    }

    public ObservableCollection<McpAuditRowViewModel> AuditEntries { get; } = [];

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private string _selectedClient = "Claude";

    public bool IsReadWrite => !IsReadOnly;
    public bool IsClientClaude => SelectedClient == "Claude";
    public bool IsClientCursor => SelectedClient == "Cursor";
    public bool IsClientAntigravity => SelectedClient == "Antigravity";
    public bool IsClientGeneric => SelectedClient == "Generic";

    public bool IsListening => _coordinator.IsMcpListening;
    public string StatusText => IsEnabled
        ? (IsListening ? "已开启 · 正在监听本机当前用户命名管道" : "已开启")
        : "未开启（默认关闭，外部 AI 无法连接）";

    public bool HasAuditEntries => AuditEntries.Count > 0;
    public string AuditCountLabel => $"最近 {AuditEntries.Count} 条（仅记录工具与结果，不含任务正文）";

    public string McpExecutablePath => ResolveMcpExecutablePath();

    public string ConfigLocationHint => SelectedClient switch
    {
        "Claude" => OperatingSystem.IsWindows()
            ? "Claude Desktop 配置文件：%APPDATA%\\Claude\\claude_desktop_config.json"
            : "Claude Desktop 配置文件：~/Library/Application Support/Claude/claude_desktop_config.json",
        "Cursor" => "Cursor 项目或全局配置：.cursor/mcp.json（或 ~/.cursor/mcp.json）",
        "Antigravity" => "Antigravity MCP 配置：~/.gemini/antigravity/mcp_config.json",
        _ => "适用于 Codex CLI、VS Code 等任意支持标准 MCP stdio 协议的客户端",
    };

    public string ConfigSnippet => BuildConfigSnippet(SelectedClient, McpExecutablePath);

    public static string ResolveMcpExecutablePath()
    {
        var procPath = Environment.ProcessPath;
        var baseDir = !string.IsNullOrWhiteSpace(procPath)
            ? Path.GetDirectoryName(procPath)
            : AppContext.BaseDirectory;
        var executableName = OperatingSystem.IsWindows() ? "VGList.Mcp.exe" : "VGList.Mcp";
        return Path.Combine(baseDir ?? AppContext.BaseDirectory, executableName);
    }

    public static string BuildConfigSnippet(string client, string exePath)
    {
        var escaped = JsonEncodedText.Encode(exePath).ToString();
        return client switch
        {
            "Generic" => $$"""
                {
                  "command": "{{escaped}}",
                  "args": []
                }
                """,
            _ => $$"""
                {
                  "mcpServers": {
                    "plana": {
                      "command": "{{escaped}}",
                      "args": []
                    }
                  }
                }
                """,
        };
    }

    public void SelectClient(string client) => SelectedClient = client;

    public void SetReadOnlyMode(bool readOnly)
    {
        IsReadOnly = readOnly;
    }

    [RelayCommand]
    public void RefreshAudit()
    {
        var entries = _coordinator.LoadMcpAuditEntries(50);
        AuditEntries.Clear();
        foreach (var e in entries)
        {
            AuditEntries.Add(new McpAuditRowViewModel(e));
        }

        OnPropertyChanged(nameof(HasAuditEntries));
        OnPropertyChanged(nameof(AuditCountLabel));
    }

    [RelayCommand]
    public void ClearAudit()
    {
        _coordinator.ClearMcpAuditEntries();
        RefreshAudit();
        _toast.Show("已清空外部 AI 访问记录");
    }

    public void NotifyConfigCopied() =>
        _toast.Show($"已复制 {SelectedClient} MCP 配置片段");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;
        _coordinator.McpAuditUpdated -= OnAuditUpdated;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMcpEnabled(value);
            _toast.Show(value ? "已开启外部 AI 接入（本地 MCP）" : "已关闭外部 AI 接入并断开现有连接");
        }

        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnIsReadOnlyChanged(bool value)
    {
        if (!_syncing)
        {
            _coordinator.SetMcpReadOnly(value);
        }

        OnPropertyChanged(nameof(IsReadWrite));
    }

    partial void OnSelectedClientChanged(string value)
    {
        OnPropertyChanged(nameof(IsClientClaude));
        OnPropertyChanged(nameof(IsClientCursor));
        OnPropertyChanged(nameof(IsClientAntigravity));
        OnPropertyChanged(nameof(IsClientGeneric));
        OnPropertyChanged(nameof(ConfigLocationHint));
        OnPropertyChanged(nameof(ConfigSnippet));
    }

    private void OnCoordinatorStateChanged(object? sender, EventArgs e)
    {
        RunOnUi(LoadFromCoordinator);
    }

    private void OnAuditUpdated(object? sender, EventArgs e)
    {
        RunOnUi(RefreshAudit);
    }

    private void LoadFromCoordinator()
    {
        _syncing = true;
        try
        {
            IsEnabled = _coordinator.MainSettings.McpEnabled;
            IsReadOnly = _coordinator.MainSettings.McpReadOnly;
            OnPropertyChanged(nameof(IsReadWrite));
            OnPropertyChanged(nameof(IsListening));
            OnPropertyChanged(nameof(StatusText));
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void RunOnUi(Action action)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
