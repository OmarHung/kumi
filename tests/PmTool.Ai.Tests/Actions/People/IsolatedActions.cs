using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Ai.Actions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Actions.People;

/// <summary>
/// 只裝自己要測的那一個處理器的建立與套用服務（真的資料庫與真的服務邏輯）：測試主機沒有註冊別種處理器用到的工作引擎與知識庫服務，
/// 用容器裡的 <see cref="IActionProposalCreator"/> 會連別人的處理器一起建構而失敗。
/// </summary>
internal sealed class IsolatedActions(AssistantHarness h, IActionProposalHandler handler)
{
    public IActionProposalCreator Creator { get; } = new ActionProposalCreator(
        h.Host.Get<IDbContextFactory<AiDbContext>>(), h.Host.Get<PmTool.Work.IProjectService>(), [handler], h.Host.Get<TimeProvider>());

    public IActionProposalService Service { get; } = new ActionProposalService(
        h.Host.Get<IDbContextFactory<AiDbContext>>(), h.Host.Get<PmTool.Work.IProjectService>(), [handler], h.Host.Get<TimeProvider>());
}
