using System.Text.Json;
using PmTool.Ai;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// propose_new_items 的 <c>items</c> 參數：把模型給的每一項讀成 <see cref="NewItemDraft"/>。
/// 這裡只擋形狀（型別、不認得的屬性、控制字元）；類型、日期、欄位值這些規則只有 AI 模組的建立端一份。
/// 負責人要換成人員編號（me、none、Email、編號），所以是非同步的。
/// </summary>
internal static class NewItemArguments
{
    private const int MaxListedMembers = 30;

    private static readonly HashSet<string> KnownProperties = new(StringComparer.Ordinal)
    {
        "ref", "type", "title", "description", "assignee", "priority", "startDate", "dueDate", "estimate", "parentKey", "parentRef", "fields",
    };

    public static async Task<IReadOnlyList<NewItemDraft>> ReadAsync(
        McpToolContext context, string projectKey, McpArguments arguments, CancellationToken cancellationToken)
    {
        var elements = arguments.OptionalArray("items") ?? throw new McpArgumentException("缺少參數 items，它要是一個 JSON 陣列。");
        // 先擋數量：超過上限的請求不必逐項讀取（每一項的負責人都要查專案）。
        if (elements.Count > AiRules.MaxNewItemsPerProposal)
        {
            throw new McpArgumentException($"一份建議最多 {AiRules.MaxNewItemsPerProposal} 個項目，items 有 {elements.Count} 個。");
        }

        var drafts = new List<NewItemDraft>(elements.Count);
        foreach (var element in elements)
        {
            drafts.Add(await ReadOneAsync(context, projectKey, element, drafts.Count, cancellationToken));
        }

        return drafts;
    }

    private static async Task<NewItemDraft> ReadOneAsync(
        McpToolContext context, string projectKey, JsonElement element, int index, CancellationToken cancellationToken)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new McpArgumentException($"items[{index}] 要是一個物件。");
        }

        var values = element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        if (values.Keys.FirstOrDefault(name => !KnownProperties.Contains(name)) is { } unknown)
        {
            throw new McpArgumentException($"items[{index}] 不認得的屬性：{unknown}。");
        }

        var item = new McpArguments(values);
        try
        {
            var assignee = item.OptionalPlain("assignee");
            var assigneeId = assignee is null ? null : await ResolveAssigneeAsync(context, projectKey, assignee, cancellationToken);
            return new NewItemDraft(item.RequiredPlain("type"), item.RequiredString("title"))
            {
                Ref = item.OptionalPlain("ref"),
                Description = item.OptionalString("description"),
                AssigneeId = assigneeId,
                Priority = item.OptionalPlain("priority"),
                StartDate = item.OptionalPlain("startDate"),
                DueDate = item.OptionalPlain("dueDate"),
                Estimate = ReadEstimate(values),
                ParentKey = item.OptionalPlain("parentKey"),
                ParentRef = item.OptionalPlain("parentRef"),
                Fields = item.OptionalObject("fields"),
            };
        }
        catch (McpArgumentException exception)
        {
            throw new McpArgumentException($"items[{index}]：{exception.Message}");
        }
    }

    // 解析失敗時附上專案成員的名單：空的專案沒有項目可以 get_item 拿成員，代理才知道可以指派給誰。
    // 看不到專案時不列（McpAssignees 對這種情況刻意回同一句錯誤）；名單和 get_item 的成員同一份條件（專案成員、沒停用）。
    private static async Task<Guid?> ResolveAssigneeAsync(McpToolContext context, string projectKey, string assignee, CancellationToken cancellationToken)
    {
        try
        {
            return await McpAssignees.ResolveAsync(context, projectKey, "assignee", assignee, cancellationToken);
        }
        catch (McpArgumentException exception)
        {
            var project = await context.Services.Projects.GetAsync(context.Actor, context.Project(projectKey), cancellationToken);
            if (project.Failure is not null)
            {
                throw;
            }

            var members = await McpMembers.OfAsync(context, project.Value, cancellationToken);
            var listed = members.Count == 0
                ? "這個專案目前沒有可以指派的成員。"
                : "專案成員：" + string.Join("、", members.Take(MaxListedMembers).Select(member => $"{member.Name.Replace('「', '『').Replace('」', '』')}（{member.Id}）")) + "。";
            throw new McpArgumentException($"{exception.Message}。{listed}");
        }
    }

    private static decimal? ReadEstimate(Dictionary<string, JsonElement> values)
    {
        if (!values.TryGetValue("estimate", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : throw new McpArgumentException("參數 estimate 要是數字。");
    }
}
