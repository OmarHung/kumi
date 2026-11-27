using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>一則回覆能用的全部工具。每則回覆建一組：工具帶著發起的人與專案。</summary>
internal static class AssistantToolSet
{
    public static IList<AITool> Create(AssistantToolContext context) =>
        context.ProjectKey is null ? CreateForOrganization(context) : CreateForProject(context);

    /// <summary>不選專案的組織對話：只有模板、看得到的專案清單、自己的工作（my_work）與提出新專案；讀不到專案的設定與別人的項目。</summary>
    private static IList<AITool> CreateForOrganization(AssistantToolContext context) =>
    [
        new ListTemplatesTool(context),
        new GetTemplateTool(context),
        new ListProjectsTool(context),
        new MyWorkTool(context),
        new ProposeProjectTool(context),
        new WithdrawProposalTool(context),
    ];

    private static IList<AITool> CreateForProject(AssistantToolContext context) =>
    [
        new GetBlueprintTool(context),
        new ListTemplatesTool(context),
        new GetTemplateTool(context),
        new DescribeUsageTool(context),
        new PreviewChangeSetTool(context),
        new ProposeChangeSetTool(context),
        new GetItemTool(context),
        new SearchItemsTool(context),
        new ProposeItemChangesTool(context),
        new ProposeNewItemsTool(context),
        new GetProjectTool(context),
        new ListPeopleTool(context),
        new MyWorkTool(context),
        new ProposeMemberChangesTool(context),
        new ProposeItemDeletionsTool(context),
        new ProposeBulkItemChangesTool(context),
        new WithdrawProposalTool(context),
    ];
}
