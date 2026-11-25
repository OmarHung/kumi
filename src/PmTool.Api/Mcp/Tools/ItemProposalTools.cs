using System.Text.Json;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 給本機代理（<c>agent:run</c>）的工具：對項目提出修改建議。只建立建議、不改項目；使用者在網頁按「套用」才以自己的權限寫入。
/// 一般的 MCP 用戶端改項目用 <c>update_item</c>、<c>move_item</c>（<c>items:write</c>），沒有這個範圍看不到、也叫不到它。
/// 檢查與建立的邏輯在 AI 模組（<see cref="IItemProposalCreator"/>），內建助理的同名工具用同一份。
/// </summary>
internal static class ItemProposalTools
{
    private const string CreatedNote = "已經建立修改建議，使用者在網頁上按「套用」才會改。不要說已經改好了。";

    public static IEnumerable<McpTool> All { get; } =
    [
        new("propose_item_changes",
            "對一個項目提出修改建議（不會真的改）：使用者在 Kumi 網頁上看到後按「套用」才會寫入。changes 是 [{field, value}]，" +
            $"field 可以是 title、description（Markdown）、status（狀態的 key）、assignee（人員編號，null 清掉）、priority（none、low、medium、high、urgent）、" +
            $"startDate、dueDate（yyyy-MM-dd，null 清掉）、estimate（數字，null 清掉）、fields.<自訂欄位的 key>；同一個欄位只能出現一次，最多 {AiRules.MaxItemProposalChanges} 個。" +
            "comment 是要一併加上的留言。改說明時一定要帶 descriptionRevision（get_item 回的那個），沒帶會被擋下；說明超過 20000 字的項目不能改說明，請改用留言。assignee 的值可以寫 me、none 或專案成員的 Email。提出之後告訴使用者「請按套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["projectKey", "itemKey"],
                ("projectKey", McpSchemas.Text("這一則對話的專案代號，項目要在這個專案裡。")),
                ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12。")),
                ("changes", McpSchemas.ArrayOf(
                    "要改的欄位 [{field, value}]。",
                    McpSchemas.ObjectOf(
                        required: ["field", "value"],
                        ("field", McpSchemas.Text("欄位名稱。")),
                        ("value", McpSchemas.Any("新的值，形狀依欄位；null 是清掉。"))))),
                ("descriptionRevision", McpSchemas.Text("改說明時必填：get_item 回的 descriptionRevision。")),
                ("comment", McpSchemas.Text("要加的留言（Markdown）。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeAsync),
    ];

    private static async Task<McpToolResult> ProposeAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var changes = new List<ItemFieldChange>();
        foreach (var element in arguments.OptionalArray("changes") ?? [])
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("field", out var field)
                || field.ValueKind != JsonValueKind.String)
            {
                throw new McpArgumentException("changes 的每一項都要是 {field, value}，field 是字串。");
            }

            var name = field.GetString()!;
            var raw = element.TryGetProperty("value", out var value) ? value : default;
            changes.Add(new ItemFieldChange(name, name == "assignee" ? await ResolveAssigneeAsync(context, projectKey, raw, cancellationToken) : raw));
        }

        var draft = new ItemProposalDraft(
            projectKey, context.Item(arguments.RequiredPlain("itemKey")), changes, arguments.OptionalString("comment"))
        {
            DescriptionRevision = arguments.OptionalPlain("descriptionRevision"),
        };
        var created = await context.Services.ItemProposals.CreateAsync(context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var proposal = created.Value;
        return McpToolResult.Ok(new
        {
            proposalId = proposal.Id,
            itemKey = proposal.ItemKey,
            changes = proposal.Changes.Select(change => new { field = change.Field, value = change.Value }),
            comment = proposal.Comment,
            note = CreatedNote,
            notice = ItemReadTools.UserContentNotice,
        });
    }

    // 代理不知道人員編號：assignee 和其他 MCP 工具一樣收 me、none、Email，在這裡換成人員編號；已經是編號的照舊交給模組檢查。
    private static async Task<JsonElement> ResolveAssigneeAsync(
        McpToolContext context, string projectKey, JsonElement value, CancellationToken cancellationToken)
    {
        if (value.ValueKind != JsonValueKind.String || Guid.TryParse(value.GetString(), out _))
        {
            return value;
        }

        var id = await McpAssignees.ResolveAsync(context, projectKey, "assignee", value.GetString()!, cancellationToken);
        return JsonSerializer.SerializeToElement(id?.ToString());
    }
}
