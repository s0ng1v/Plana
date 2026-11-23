using System.ComponentModel;
using Microsoft.Extensions.AI;
using VGList.App.Assistant;

namespace VGList.Agent.Tools;

/// <summary>
/// 助理长期记忆与称呼工具集（A5 / ADR-0048）：remember / update_memory / forget / set_user_nickname。
/// 任何写入都经过 SensitiveInfoFilter 敏感信息拦截，并通过事件通知前端实时刷新与可撤销。
/// </summary>
public sealed class MemoryTools
{
    private readonly MemoryStore _store;
    private readonly UserProfileStore? _profileStore;
    private readonly Func<bool> _isEnabled;
    private readonly Action<AssistantEvent> _emit;

    public MemoryTools(
        MemoryStore store,
        Func<bool> isEnabled,
        Action<AssistantEvent> emit,
        UserProfileStore? profileStore = null)
    {
        _store = store;
        _profileStore = profileStore;
        _isEnabled = isEnabled;
        _emit = emit;
    }

    public IReadOnlyList<AITool> CreateTools()
    {
        var list = new List<AITool>
        {
            AIFunctionFactory.Create(Remember, "remember", "记录一条对未来日程安排长期有用的用户习惯、偏好或目标（严禁记录密码、身份证号、银行卡、验证码等敏感信息）"),
            AIFunctionFactory.Create(UpdateMemory, "update_memory", "当用户的既有习惯或偏好发生变化时，更新已有记忆条目而不是重复新增"),
            AIFunctionFactory.Create(Forget, "forget", "按记忆 Id 删除一条旧记忆"),
        };

        if (_profileStore is not null)
        {
            list.Add(AIFunctionFactory.Create(
                SetUserNickname,
                "set_user_nickname",
                "当用户告诉你可以怎么称呼 TA 时，将这个称呼保存到用户画像中，以后按此称呼对方"));
        }

        return list;
    }

    public object Remember(
        [Description("简洁的一句话记忆内容，例如「周五下午固定不开会」")] string content,
        [Description("类别：习惯 / 偏好 / 目标 / 事实")] string category = "偏好")
    {
        _emit(new ToolStartedEvent("remember", "Lana 正在记进小本本…"));
        if (!_isEnabled())
        {
            _emit(new ToolFinishedEvent("remember", "长期记忆未开启", false));
            return new { ok = false, error = "用户已关闭助理记忆功能，不会保存新记忆。" };
        }

        var (ok, item, err) = _store.Add(content, category, "对话");
        if (!ok || item is null)
        {
            _emit(new ToolFinishedEvent("remember", "未保存该条记忆", false));
            return new { ok = false, error = err };
        }

        _emit(new ToolFinishedEvent("remember", "已记住这条偏好", true));
        _emit(new MemoryChangedEvent(item.Id, "added", item.Content));
        return new { ok = true, id = item.Id, content = item.Content, category = item.Category };
    }

    public object UpdateMemory(
        [Description("已有记忆 Id")] string memoryId,
        [Description("更新后的完整记忆内容")] string newContent,
        [Description("可选新类别：习惯 / 偏好 / 目标 / 事实")] string? category = null)
    {
        _emit(new ToolStartedEvent("update_memory", "Lana 正在更新小本本…"));
        if (!_isEnabled())
        {
            _emit(new ToolFinishedEvent("update_memory", "长期记忆未开启", false));
            return new { ok = false, error = "用户已关闭助理记忆功能。" };
        }

        var (ok, item, err) = _store.Update(memoryId, newContent, category);
        if (!ok || item is null)
        {
            _emit(new ToolFinishedEvent("update_memory", "未找到对应记忆", false));
            return new { ok = false, error = err };
        }

        _emit(new ToolFinishedEvent("update_memory", "已更新记忆条目", true));
        _emit(new MemoryChangedEvent(item.Id, "updated", item.Content));
        return new { ok = true, id = item.Id, content = item.Content };
    }

    public object Forget([Description("要忘记的记忆 Id")] string memoryId)
    {
        _emit(new ToolStartedEvent("forget", "Lana 正在移除旧记忆…"));
        if (!_isEnabled())
        {
            _emit(new ToolFinishedEvent("forget", "长期记忆未开启", false));
            return new { ok = false, error = "用户已关闭助理记忆功能。" };
        }

        var removed = _store.Remove(memoryId);
        _emit(new ToolFinishedEvent("forget", removed ? "已忘掉这条旧记忆" : "未找到对应记忆", removed));
        if (removed)
        {
            _emit(new MemoryChangedEvent(memoryId, "removed", string.Empty));
        }

        return new { ok = removed, message = removed ? "已忘记该条记忆。" : $"未找到记忆 {memoryId}" };
    }

    public object SetUserNickname([Description("用户希望被称呼的名字或昵称，例如「小明」「阿宇」「老板」")] string nickname)
    {
        var clean = (nickname ?? string.Empty).Trim();
        _emit(new ToolStartedEvent("set_user_nickname", "Lana 正在记住你的称呼…"));

        if (_profileStore is null || string.IsNullOrEmpty(clean) || clean.Length > 24)
        {
            _emit(new ToolFinishedEvent("set_user_nickname", "称呼无效", false));
            return new { ok = false, error = "称呼不能为空且不超过 24 个字符。" };
        }

        if (SensitiveInfoFilter.ContainsSensitiveInfo(clean, out var detectedKind))
        {
            _emit(new ToolFinishedEvent("set_user_nickname", "未保存敏感信息", false));
            return new { ok = false, error = $"检测到疑似敏感信息（{detectedKind}），已拒绝保存。" };
        }

        _profileStore.SetNickname(clean);
        _emit(new ToolFinishedEvent("set_user_nickname", $"已记住称呼「{clean}」", true));
        _emit(new ProfileNicknameChangedEvent(clean));
        return new { ok = true, nickname = clean };
    }
}
