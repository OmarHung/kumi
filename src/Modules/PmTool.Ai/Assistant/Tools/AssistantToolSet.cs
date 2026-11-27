using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>一則回覆能用的全部工具。每則回覆建一組：工具帶著發起的人與專案。</summary>
internal static class AssistantToolSet
{
    public static IList<AITool> Create(AssistantToolContext context) =>
        context.ProjectKey is null ? CreateForOrganization(context) : CreateForProject(context);

    /// <summary>
    /// 不選專案的組織對話：組織自己的工具（模板、看得到的專案清單、自己的工作、跨專案查項目、找人、知識庫等），
    /// 加上專案對話的所有工具（組織組沒有的那些）、每個多一個必填的 projectKey（<see cref="OrganizationProjectTool"/>）。
    /// </summary>
    private static IList<AITool> CreateForOrganization(AssistantToolContext context)
    {
        var own = OrganizationOwnTools(context);
        var ownNames = own.Select(tool => ((AIFunction)tool).Name).ToHashSet(StringComparer.Ordinal);
        var wrapped = ProjectFactories
            .Where(factory => !ownNames.Contains(factory(context).Name))
            .Select(factory => (AITool)new OrganizationProjectTool(factory, context));
        return [.. own, .. wrapped];
    }

    private static List<AITool> OrganizationOwnTools(AssistantToolContext context) =>
    [
        new ListTemplatesTool(context),
        new GetTemplateTool(context),
        new ListProjectsTool(context),
        new MyWorkTool(context),
        new FindItemsTool(context),
        new ListPeopleTool(context),
        new ProposeProjectTool(context),
        new WithdrawProposalTool(context),
        new ListSpacesTool(context),
        new GetPageTreeTool(context),
        new GetPageTool(context),
        new ListPageTrashTool(context),
        new ProposeKnowledgeChangesTool(context),
        new ProposePeopleChangesTool(context),
        new ProposeAttachmentChangesTool(context),
        new ListSavedViewsTool(context),
        new ListDescriptionTemplatesTool(context),
        new GetOverviewTool(context),
        new ListNotificationsTool(context),
        new ListChangeSetsTool(context),
        new ProposeSavedViewChangesTool(context),
        new ProposeDescriptionTemplateChangesTool(context),
    ];

    /// <summary>專案對話的工具（每個是「用這個上下文建出工具」的工廠）：組織對話包一層 <see cref="OrganizationProjectTool"/> 時要在呼叫當下換成專案的上下文。</summary>
    private static readonly Func<AssistantToolContext, AIFunction>[] ProjectFactories =
    [
        c => new GetBlueprintTool(c),
        c => new ListTemplatesTool(c),
        c => new GetTemplateTool(c),
        c => new DescribeUsageTool(c),
        c => new PreviewChangeSetTool(c),
        c => new ProposeChangeSetTool(c),
        c => new GetItemTool(c),
        c => new SearchItemsTool(c),
        c => new ProposeItemChangesTool(c),
        c => new ProposeNewItemsTool(c),
        c => new GetProjectTool(c),
        c => new ListPeopleTool(c),
        c => new MyWorkTool(c),
        c => new FindItemsTool(c),
        c => new ProposeMemberChangesTool(c),
        c => new ProposeItemDeletionsTool(c),
        c => new ProposeBulkItemChangesTool(c),
        c => new WithdrawProposalTool(c),
        c => new ListCommentsTool(c),
        c => new ListDeletedItemsTool(c),
        c => new GetProjectInfoTool(c),
        c => new GetPlanningTool(c),
        c => new ProposePlanningChangesTool(c),
        c => new ProposeCommentChangesTool(c),
        c => new ProposeItemRestoresTool(c),
        c => new ProposeProjectChangesTool(c),
        c => new ListSpacesTool(c),
        c => new GetPageTreeTool(c),
        c => new GetPageTool(c),
        c => new ListPageTrashTool(c),
        c => new ProposeKnowledgeChangesTool(c),
        c => new ProposePeopleChangesTool(c),
        c => new ProposeAttachmentChangesTool(c),
        c => new ListSavedViewsTool(c),
        c => new ListDescriptionTemplatesTool(c),
        c => new GetOverviewTool(c),
        c => new ListNotificationsTool(c),
        c => new ListChangeSetsTool(c),
        c => new ProposeSavedViewChangesTool(c),
        c => new ProposeDescriptionTemplateChangesTool(c),
    ];

    private static IList<AITool> CreateForProject(AssistantToolContext context) =>
        [.. ProjectFactories.Select(factory => (AITool)factory(context))];
}
