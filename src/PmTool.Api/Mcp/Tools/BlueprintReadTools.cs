using PmTool.Api.Endpoints;
using PmTool.Blueprint;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Mcp.Tools;

/// <summary>讀專案設定（藍圖）的工具（<c>blueprint:read</c>）：目前的藍圖、內建模板、使用狀況。</summary>
internal static class BlueprintReadTools
{
    public static IEnumerable<McpTool> All { get; } =
    [
        new("get_blueprint",
            "讀一個專案目前的設定：藍圖的版本與全文（項目類型、流程與狀態、欄位、檢視、角色），以及你能不能提出、核准變更集。" +
            "建立或修改項目、改設定之前先讀它，才知道類型、狀態與欄位的 key。",
            McpSchemas.Object(required: ["projectKey"], ("projectKey", McpSchemas.Text("專案代號，例如 WEB。"))),
            AccessTokenScope.BlueprintRead, ReadOnly: true, GetBlueprintAsync),
        new("list_templates",
            "列出內建的模板：每個模板的 key、名稱、說明、項目類型、流程與狀態、欄位名稱。要參考別人怎麼設時用。",
            McpSchemas.NoParameters, AccessTokenScope.BlueprintRead, ReadOnly: true, ListTemplates),
        new("get_template",
            "讀一個內建模板的整份藍圖（格式和 get_blueprint 的 blueprint 一樣）。",
            McpSchemas.Object(required: ["templateKey"], ("templateKey", McpSchemas.Text("模板的 key，用 list_templates 查，例如 simple_tasks。"))),
            AccessTokenScope.BlueprintRead, ReadOnly: true, GetTemplate),
        new("describe_usage",
            "一個專案的設定用得怎麼樣（只有數字）：各項目類型、各狀態有幾個項目，各欄位有幾個項目填了值。" +
            "判斷欄位或狀態有沒有人用、改設定會動到多少資料時用。",
            McpSchemas.Object(required: ["projectKey"], ("projectKey", McpSchemas.Text("專案代號，例如 WEB。"))),
            AccessTokenScope.BlueprintRead, ReadOnly: true, DescribeUsageAsync),
    ];

    private static async Task<McpToolResult> GetBlueprintAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Projects.GetAsync(context.Actor, context.Project(arguments.RequiredPlain("projectKey")), cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var detail = result.Value;
        return McpToolResult.Ok(new
        {
            version = detail.Project.BlueprintVersion,
            blueprint = BlueprintSerializer.ToElement(detail.Blueprint),
            canPropose = detail.MyPermissions.Contains(WorkPermissions.BlueprintPropose),
            canApply = detail.MyPermissions.Contains(WorkPermissions.BlueprintApply),
        });
    }

    private static Task<McpToolResult> ListTemplates(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken) =>
        Task.FromResult(McpToolResult.Ok(new { templates = context.Services.Templates.List().Select(TemplateData.From) }));

    private static Task<McpToolResult> GetTemplate(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var key = arguments.RequiredPlain("templateKey");
        var blueprint = context.Services.Templates.FindBlueprint(key);
        return Task.FromResult(blueprint is null
            ? McpToolResult.Error("沒有這個模板，請用 list_templates 看有哪些。")
            : McpToolResult.Ok(new { key, blueprint = BlueprintSerializer.ToElement(blueprint) }));
    }

    private static async Task<McpToolResult> DescribeUsageAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var result = await context.Services.Usage.DescribeAsync(context.Actor, context.Project(arguments.RequiredPlain("projectKey")), cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var usage = result.Value;
        return McpToolResult.Ok(new
        {
            blueprintVersion = usage.BlueprintVersion,
            itemCount = usage.ItemCount,
            states = usage.States.Select(state => new { itemType = state.ItemType, state = state.State, items = state.Items }),
            fields = usage.Fields.Select(field => new { field = field.Field, filledItems = field.FilledItems }),
        });
    }
}
