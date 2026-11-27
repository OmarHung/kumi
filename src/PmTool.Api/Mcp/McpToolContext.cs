using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp;

/// <summary>
/// 工具用到的模組服務。只認合約介面，和 API 的端點一樣。用到才向容器要：一個工具呼叫通常只碰一兩個服務，
/// 不必為了其他工具先建好全部。
/// </summary>
internal sealed class McpServices(IServiceProvider provider)
{
    public IProjectService Projects => provider.GetRequiredService<IProjectService>();

    public IWorkItemService Items => provider.GetRequiredService<IWorkItemService>();

    public IItemActivityService Activity => provider.GetRequiredService<IItemActivityService>();

    public IBlueprintChangeService Changes => provider.GetRequiredService<IBlueprintChangeService>();

    public IBlueprintUsageService Usage => provider.GetRequiredService<IBlueprintUsageService>();

    public ITemplateLibrary Templates => provider.GetRequiredService<ITemplateLibrary>();

    public IItemProposalCreator ItemProposals => provider.GetRequiredService<IItemProposalCreator>();

    public INewItemProposalCreator NewItemProposals => provider.GetRequiredService<INewItemProposalCreator>();

    public IMemberProposalCreator MemberProposals => provider.GetRequiredService<IMemberProposalCreator>();

    public IItemDeletionProposalCreator ItemDeletionProposals => provider.GetRequiredService<IItemDeletionProposalCreator>();

    public IItemBatchProposalCreator ItemBatchProposals => provider.GetRequiredService<IItemBatchProposalCreator>();

    public IAssistantProposalLedger ProposalLedger => provider.GetRequiredService<IAssistantProposalLedger>();

    public IStaffingBriefService Staffing => provider.GetRequiredService<IStaffingBriefService>();

    public IAssistantItemFinder ItemFinder => provider.GetRequiredService<IAssistantItemFinder>();

    public IPeopleDirectory People => provider.GetRequiredService<IPeopleDirectory>();

    public IOrganizationClock Clock => provider.GetRequiredService<IOrganizationClock>();

    public TimeProvider Time => provider.GetService<TimeProvider>() ?? TimeProvider.System;

    public IProjectProposalCreator ProjectProposals => provider.GetRequiredService<IProjectProposalCreator>();
}

/// <summary>
/// 一次工具呼叫的環境。權限一律用授權的人的 <see cref="Actor"/>：代理不會因為是 AI 就多拿到什麼。
/// </summary>
/// <param name="Issuer">這個部署對外的網址（<c>OAuthServerResolver</c> 算出來的），給「回傳網頁的網址」用。</param>
internal sealed record McpToolContext(Actor Actor, string Issuer, McpServices Services)
{
    /// <summary>
    /// 這次請求碰得到的範圍（<see cref="McpScope"/>：限定專案、只限組織、不限）。一定要明講，沒有預設值：
    /// 忘了設定時編譯不過，而不是默默變成不限。所有碰專案或項目的工具都要經過 <see cref="Project"/>、<see cref="Item"/>，限制只在這裡檢查一次。
    /// </summary>
    public required McpScope Scope { get; init; }

    /// <summary>
    /// 代理這件工作的對話編號（伺服器從工作找到，模型改不了）；<c>propose_project</c> 用它記建議屬於哪則對話。
    /// 一般的權杖沒有工作，是 null。
    /// </summary>
    public Guid? ConversationId { get; init; }

    /// <summary>專案代號要是範圍允許的（不限都可以、限定要同一個、只限組織都不行）；不是的當作不存在。</summary>
    public string Project(string projectKey) =>
        Scope.AllowsProject(projectKey) ? projectKey : throw new McpArgumentException($"找不到專案「{projectKey}」。");

    /// <summary>項目編號是「專案代號-流水號」：專案代號要是範圍允許的；不是的當作不存在。</summary>
    public string Item(string itemKey)
    {
        if (Scope.Kind == McpScopeKind.Unrestricted)
        {
            return itemKey;
        }

        var dash = itemKey.LastIndexOf('-');
        return dash > 0 && Scope.AllowsProject(itemKey[..dash]) ? itemKey : throw new McpArgumentException($"找不到項目「{itemKey}」。");
    }
}
