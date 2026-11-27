using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 一則回覆裡的工具共用的東西。權限一律用發起的人的 <see cref="Actor"/>：助理不會因為是 AI 就多拿到什麼。
/// <see cref="ProjectKey"/> 是 null 時是不選專案的組織對話。
/// </summary>
internal sealed record AssistantToolContext(
    Actor Actor,
    string? ProjectKey,
    IProjectService Projects,
    IBlueprintChangeService Changes,
    IBlueprintUsageService Usage,
    ITemplateLibrary Templates,
    IWorkItemService Items,
    IItemActivityService Activity,
    IPeopleDirectory People,
    IItemProposalCreator Proposals,
    IProjectProposalCreator ProjectProposals,
    INewItemProposalCreator NewItemProposals,
    IStaffingBriefService Staffing,
    IMemberProposalCreator MemberProposals,
    IItemDeletionProposalCreator DeletionProposals,
    IItemBatchProposalCreator BatchProposals,
    IAssistantProposalLedger Ledger,
    Guid ConversationId,
    ILogger Logger)
{
    /// <summary>專案對話的工具用：專案對話一定有專案代號（不選專案的對話只組得出組織版的工具，碰不到這裡）。</summary>
    public string RequiredProjectKey => ProjectKey ?? throw new InvalidOperationException("不選專案的對話沒有專案代號。");
}
