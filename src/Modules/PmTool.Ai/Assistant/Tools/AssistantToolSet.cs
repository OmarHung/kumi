using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>一則回覆能用的全部工具。每則回覆建一組：工具帶著發起的人與專案。</summary>
internal static class AssistantToolSet
{
    public static IList<AITool> Create(AssistantToolContext context) =>
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
    ];
}
