using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 把專案對話的一個工具包給不選專案的組織對話用：Schema 多一個必填的 <c>projectKey</c>，呼叫時先確認這是使用者看得到的專案、
/// 記下這則對話讀過它（之後使用者在那個專案看不到全部項目時整則對話會刪掉），再換成那個專案的上下文執行原本的工具。
/// 建議工具的對話編號照舊用原本的對話，所以建議還是掛在這則對話底下。
/// </summary>
internal sealed class OrganizationProjectTool : AssistantTool
{
    public const string ProjectKeyArgument = "projectKey";

    private readonly Func<AssistantToolContext, AIFunction> _factory;

    public OrganizationProjectTool(Func<AssistantToolContext, AIFunction> factory, AssistantToolContext context)
        : this(factory, factory(context), context)
    {
    }

    // 用組織的上下文只為了讀名稱、說明與 Schema：真正執行時才換成專案的上下文。
    private OrganizationProjectTool(Func<AssistantToolContext, AIFunction> factory, AIFunction template, AssistantToolContext context)
        : base(
            template.Name,
            template.Description + "（組織對話用：一定要帶 projectKey，也就是專案代號，例如 WEB；用 list_projects 或使用者給的網址找。）",
            ExtendSchema(template.JsonSchema),
            context) => _factory = factory;

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var key = ToolArguments.String(arguments, ProjectKeyArgument)?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return ToolResults.Error("請帶 projectKey（專案代號，例如 WEB），可以用 list_projects 或使用者給的網址找。");
        }

        var project = await Context.Projects.GetAsync(Context.Actor, key, cancellationToken);
        if (project.Failure is not null)
        {
            return ToolResults.Error($"找不到專案 {key}，或使用者看不到它；請用 list_projects 確認專案代號。");
        }

        var projectKey = project.Value.Project.Key;
        await Context.Services.GetRequiredService<IAssistantConversationReads>().RememberAsync(Context.Actor, Context.ConversationId, [projectKey], cancellationToken);

        var inner = _factory(Context with { ProjectKey = projectKey });
        var rest = new AIFunctionArguments();
        foreach (var (name, value) in arguments)
        {
            if (!string.Equals(name, ProjectKeyArgument, StringComparison.Ordinal))
            {
                rest[name] = value;
            }
        }

        var result = await inner.InvokeAsync(rest, cancellationToken);
        return result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            _ => ToolResults.Error("工具沒有回傳結果。"),
        };
    }

    private static string ExtendSchema(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = root["properties"] as JsonObject ?? [];
        root["properties"] = properties;
        properties[ProjectKeyArgument] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "專案代號，例如 WEB；用 list_projects 或使用者給的網址找",
        };

        var required = root["required"] as JsonArray ?? [];
        root["required"] = required;
        if (!required.Any(node => node?.GetValue<string>() == ProjectKeyArgument))
        {
            required.Add(ProjectKeyArgument);
        }

        return root.ToJsonString();
    }
}
