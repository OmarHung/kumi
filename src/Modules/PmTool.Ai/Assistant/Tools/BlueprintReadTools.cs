using Microsoft.Extensions.AI;
using PmTool.Blueprint;
using PmTool.Work;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>讀設定的工具：目前的藍圖、內建的模板、使用狀況的統計。</summary>
internal sealed class GetBlueprintTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.GetBlueprint,
    "讀這個專案目前的設定：藍圖的版本與全文（項目類型、流程、欄位、檢視、角色），以及使用者能不能提出、核准變更集。寫變更集之前一定先呼叫。",
    NoParametersSchema,
    context)
{
    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await Context.Projects.GetAsync(Context.Actor, Context.ProjectKey, cancellationToken);
        if (result.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var detail = result.Value;
        return ToolResults.Ok(new
        {
            version = detail.Project.BlueprintVersion,
            blueprint = BlueprintSerializer.ToElement(detail.Blueprint),
            canPropose = detail.MyPermissions.Contains(WorkPermissions.BlueprintPropose),
            canApply = detail.MyPermissions.Contains(WorkPermissions.BlueprintApply),
        });
    }
}

internal sealed class ListTemplatesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ListTemplates,
    "列出內建的模板：每個模板的 key、名稱、說明、項目類型、流程與狀態、欄位名稱。要參考別人怎麼設時用。",
    NoParametersSchema,
    context)
{
    protected override Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var templates = Context.Templates.List().Select(template => new
        {
            key = template.Key,
            name = template.Name,
            description = template.Description,
            itemTypes = template.Workflows.SelectMany(workflow => workflow.ItemTypeNames).Distinct(StringComparer.Ordinal),
            workflows = template.Workflows.Select(workflow => new
            {
                name = workflow.Name,
                itemTypes = workflow.ItemTypeNames,
                states = workflow.StateNames,
                canceledStates = workflow.CanceledStateNames,
            }),
            parents = template.Parents.Select(parent => new { itemType = parent.ItemTypeName, parentTypes = parent.ParentTypeNames }),
            fields = template.FieldNames,
        });
        return Task.FromResult(ToolResults.Ok(new { templates }));
    }
}

internal sealed class GetTemplateTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.GetTemplate,
    "讀一個內建模板的整份藍圖（格式和 get_blueprint 的 blueprint 一樣）。templateKey 是 list_templates 回的 key。",
    """
    {"type":"object","properties":{"templateKey":{"type":"string","description":"模板的 key，例如 simple_tasks"}},"required":["templateKey"],"additionalProperties":false}
    """,
    context)
{
    protected override Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var key = ToolArguments.String(arguments, "templateKey");
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(ToolResults.Error("缺少參數 templateKey。"));
        }

        var blueprint = Context.Templates.FindBlueprint(key);
        return Task.FromResult(blueprint is null
            ? ToolResults.Error("沒有這個模板，請用 list_templates 看有哪些。")
            : ToolResults.Ok(new { key, blueprint = BlueprintSerializer.ToElement(blueprint) }));
    }
}

internal sealed class DescribeUsageTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.DescribeUsage,
    "這個專案設定的使用狀況（只有數字）：各項目類型、各狀態有幾個項目，各欄位有幾個項目填了值。判斷欄位或狀態有沒有人用、改動會影響多少資料時用。",
    NoParametersSchema,
    context)
{
    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var result = await Context.Usage.DescribeAsync(Context.Actor, Context.ProjectKey, cancellationToken);
        if (result.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var usage = result.Value;
        return ToolResults.Ok(new
        {
            blueprintVersion = usage.BlueprintVersion,
            itemCount = usage.ItemCount,
            states = usage.States.Select(state => new { itemType = state.ItemType, state = state.State, items = state.Items }),
            fields = usage.Fields.Select(field => new { field = field.Field, filledItems = field.FilledItems }),
        });
    }
}
