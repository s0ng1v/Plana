using System.Runtime.CompilerServices;
using VGList.Agent.Calendar;
using VGList.Agent.Runtime;
using VGList.App.Assistant;
using VGList.App.Models;
using VGList.Core.Abstractions;
using VGList.Data;

namespace VGList.Desktop.Services;

/// <summary>
/// 助理内核懒加载隔离工厂（ADR-0042 / ADR-0046）：
/// VGList.Desktop 中唯一引用 VGList.Agent 的类型；通过 NoInlining 保证常驻态绝不触发
/// VGList.Agent / Microsoft.Extensions.AI / PdfPig 程序集加载。
/// </summary>
public static class AssistantHostFactory
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IAssistantHost Create(
        ITaskRepository repository,
        IScheduleService schedule,
        IModelProfileProvider modelProvider,
        WidgetCoordinator coordinator,
        DateOnly today)
    {
        IConversationStore? conversationStore = repository is SqliteTaskRepository sqliteRepo
            ? new SqliteConversationStore(sqliteRepo.Database)
            : null;

        var rulesStore = new RulesStore();
        var memoryStore = new MemoryStore();
        var profileStore = new UserProfileStore();
        var holidays = CnHolidayCalendar.Shared;

        var validator = new RuleProposalValidator(
            repository,
            () => rulesStore.Load(),
            d =>
            {
                var info = holidays.GetDayInfo(d);
                return (info.IsHoliday, info.IsMakeupWorkday, info.HolidayName);
            });

        IContextContributor[] contributors =
        [
            new RulesContextContributor(() => rulesStore.Load()),
            new ProfileContextContributor(profileStore, () => rulesStore.Load().UseProfileInChat),
            new MemoryContextContributor(memoryStore, () => rulesStore.Load().MemoryEnabled),
        ];

        return new AssistantHost(
            repository: repository,
            schedule: schedule,
            modelProvider: modelProvider,
            conversationStore: conversationStore,
            holidays: holidays,
            allowReadTasks: () => coordinator.MainSettings.AllowAssistantReadTasks,
            permissionConfig: () => rulesStore.Load().Permissions,
            todayProvider: () => DateOnly.FromDateTime(DateTime.Today),
            proposalValidator: validator,
            contextContributors: contributors,
            memoryStore: memoryStore,
            memoryEnabled: () => rulesStore.Load().MemoryEnabled,
            profileStore: profileStore);
    }
}
