namespace VGList.App.Settings;

/// <summary>主窗口点击关闭按钮（×）时的行为策略。</summary>
public static class WindowCloseAction
{
    public const string Ask = "Ask";
    public const string Minimize = "Minimize";
    public const string Exit = "Exit";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Minimize, StringComparison.OrdinalIgnoreCase))
        {
            return Minimize;
        }

        if (string.Equals(value, Exit, StringComparison.OrdinalIgnoreCase))
        {
            return Exit;
        }

        return Ask;
    }
}

/// <summary>界面与 Lana 对话字体预设（ADR-0051 / ADR-0062）。</summary>
public static class FontPresetMode
{
    /// <summary>旧版默认值（思源）；运行时按当前默认「Anthropic + 思源宋体」解析，仅用于兼容已保存的设置。</summary>
    public const string Default = "Default";

    /// <summary>Anthropic + 思源宋体：英文数字 Anthropic Serif/Sans + 中文思源宋体/黑体（当前默认）。</summary>
    public const string AnthropicSerif = "AnthropicSerif";

    /// <summary>Claude 经典宋体：英文数字 Anthropic Serif + 中文 SimSun 宋体。</summary>
    public const string AnthropicClassic = "AnthropicClassic";

    /// <summary>纯净黑体：界面与对话全部使用思源黑体无衬线。</summary>
    public const string CleanSans = "CleanSans";

    /// <summary>微软雅黑 / Windows 系统默认字体。</summary>
    public const string System = "System";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, AnthropicClassic, StringComparison.OrdinalIgnoreCase))
        {
            return AnthropicClassic;
        }

        if (string.Equals(value, CleanSans, StringComparison.OrdinalIgnoreCase))
        {
            return CleanSans;
        }

        if (string.Equals(value, System, StringComparison.OrdinalIgnoreCase))
        {
            return System;
        }

        // AnthropicSerif、旧版 Default 与未设置值统一使用当前默认字体
        return AnthropicSerif;
    }
}

/// <summary>主窗口界面与桌面小组件联动偏好（仅存本地 UI 状态，不存业务任务数据）。</summary>
public sealed class MainSettings
{
    public bool ShowTodayWidget { get; set; } = true;

    public bool ShowMonthWidget { get; set; } = true;

    public bool ReduceMotion { get; set; }

    public bool IsSidebarCollapsed { get; set; }

    public bool CalendarShowCompleted { get; set; } = true;

    /// <summary>输入框离线自动识别中文日期开关（默认开）。</summary>
    public bool AutoDetectDate { get; set; } = true;

    /// <summary>界面主题：ClassicBlue（默认，经典蓝白）或 Plana（淡粉樱雾）。</summary>
    public string Theme { get; set; } = "ClassicBlue";

    /// <summary>界面与对话字体预设（默认 Anthropic + 思源宋体）。</summary>
    public string FontPreset { get; set; } = FontPresetMode.AnthropicSerif;

    /// <summary>点击主窗口右上角关闭按钮时的行为：Ask（每次询问）/ Minimize（最小化）/ Exit（退出 Plana）。</summary>
    public string CloseAction { get; set; } = WindowCloseAction.Ask;

    /// <summary>隐私开关：是否允许 Plana 助理通过只读工具查看任务与清单内容（默认开）。</summary>
    public bool AllowAssistantReadTasks { get; set; } = true;

    /// <summary>助理权限：拆分子待办是否默认自动应用（默认开）。</summary>
    public bool AssistantAutoSubtasks { get; set; } = true;

    /// <summary>助理权限：归类清单是否默认自动应用（默认开）。</summary>
    public bool AssistantAutoCategorize { get; set; } = true;

    /// <summary>助理调试日志开关（默认关，严禁在默认状态下记录消息/附件/密钥）。</summary>
    public bool AssistantDebugLogging { get; set; }

    /// <summary>A6③：每天首次打开时若存在逾期未完成任务，显示早安整理提示条（默认开）。</summary>
    public bool MorningBriefEnabled { get; set; } = true;

    /// <summary>A7：外部 AI（本地 MCP 命名管道）接入总开关（默认关，开启后才监听管道）。</summary>
    public bool McpEnabled { get; set; }

    /// <summary>A7：外部 AI 权限模式，true=仅只读，false=允许读写（删除与批量改期仍需主窗口内确认）。</summary>
    public bool McpReadOnly { get; set; }

    /// <summary>P1：开机自动启动 Plana（HKCU Run，无需管理员权限）。</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>P1：开机自启时静默隐藏主窗口至系统托盘（保留桌面小组件）。</summary>
    public bool StartMinimizedToTray { get; set; } = true;
}
