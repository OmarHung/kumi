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

    private const string NewItemsCreatedNote = "已經建立新增項目的建議，使用者在網頁上按「全部建立」才會建立。不要說已經建好了。";

    public static IEnumerable<McpTool> All { get; } =
    [
        new("propose_item_changes",
            "對一個項目提出修改建議（不會真的改）：使用者在 Kumi 網頁上看到後按「套用」才會寫入。changes 是 [{field, value}]，" +
            $"field 可以是 title、description（Markdown）、status（狀態的 key）、type（項目類型的 key；改類型可能讓狀態回到新流程的起點）、parentKey（上層項目編號，null 移到最上層）、cycleId、milestoneId（get_project 或 get_planning 回的 id，null 清掉）、following（true 關注、false 取消關注）、assignee（人員編號，null 清掉）、priority（none、low、medium、high、urgent）、" +
            $"startDate、dueDate（yyyy-MM-dd，null 清掉）、estimate（數字，null 清掉）、fields.<自訂欄位的 key>；同一個欄位只能出現一次，最多 {AiRules.MaxItemProposalChanges} 個。" +
            "comment 是要一併加上的留言；replyTo 是回覆哪一則留言（list_comments 或 get_item 回的留言 id），沒給是新的一則。改說明時一定要帶 descriptionRevision（get_item 回的那個），沒帶會被擋下；說明超過 20000 字的項目不能改說明，請改用留言。assignee 的值寫 get_item 回的 members 裡的 id，或 me、none、專案成員的 Email。留言裡要提及成員時寫成 @[名稱](mention:<members 裡的 id>)。提出之後告訴使用者「請按套用」，不要說已經改好了。",
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
                ("comment", McpSchemas.Text("要加的留言（Markdown）。")),
                ("replyTo", McpSchemas.Text("回覆哪一則留言（留言的 id）；要有 comment 才能給。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeAsync),
        new("propose_new_items",
            $"建議新增項目（不會真的建立）：使用者在 Kumi 網頁上看到後按「全部建立」才會以他自己的身分建立。一份建議 1 到 {AiRules.MaxNewItemsPerProposal} 個項目，依順序建立；" +
            "把一份需求拆成幾個任務時放在同一份。每個項目要有 type（藍圖裡的類型 key，先 get_blueprint）與 title；狀態一律從流程的第一個開始，不能指定。" +
            "要掛在新建的項目底下：前面的項目給 ref（小寫英文、數字、-、_，最多 20 字），後面的用 parentRef 指它；要掛在既有的項目底下用 parentKey，兩個只能給一個，而且藍圖要允許這個類型掛在那個類型底下。" +
            "assignee 寫 me、none、人員編號或 Email（在整個組織裡找，list_people 能查；get_project 的 members 是專案成員）。負責人還不是專案成員時：你有管理成員的權限（get_project 的 myPermissions 有 project.manage）就把他放進 addMembers，按「全部建立」時會先加人再建項目；沒有就請使用者找 get_project 的 memberManagers 或組織管理員加人，或先指派給自己、不指派。週期與里程碑用 get_project 回的 id（cycleId、milestoneId）。資訊不足時先問使用者，或把不確定的寫進說明的「待確認」，不要捏造。提出之後告訴使用者「請按全部建立」，不要說已經建好了。",
            McpSchemas.Object(
                required: ["projectKey", "items"],
                ("projectKey", McpSchemas.Text("這一則對話的專案代號。")),
                ("addMembers", McpSchemas.ArrayOf(
                    $"建立項目之前先加進專案的人（負責人還不是成員時用），最多 {AiRules.MaxMemberChangesPerProposal} 個；已經是成員的略過、不改他的角色。",
                    McpSchemas.ObjectOf(
                        required: ["person"],
                        ("person", McpSchemas.Text("人員編號或 Email。")),
                        ("role", McpSchemas.Text("專案角色的 key（get_project 的 roles），沒給用 contributor（只看得到自己負責或建立的項目）。"))))),
                ("items", McpSchemas.ArrayOf(
                    "要新增的項目，依建立的順序。",
                    McpSchemas.ObjectOf(
                        required: ["type", "title"],
                        ("ref", McpSchemas.Text("這個項目的代稱，讓後面的項目用 parentRef 掛在它底下。")),
                        ("type", McpSchemas.Text("項目類型的 key。")),
                        ("title", McpSchemas.Text("標題。")),
                        ("description", McpSchemas.Text("說明（Markdown）。")),
                        ("assignee", McpSchemas.Text("負責人：me、none、人員編號或 Email。")),
                        ("priority", McpSchemas.Text("none、low、medium、high、urgent。")),
                        ("startDate", McpSchemas.Text("開始日，yyyy-MM-dd。")),
                        ("dueDate", McpSchemas.Text("到期日，yyyy-MM-dd。")),
                        ("estimate", McpSchemas.Number("估計，數字。")),
                        ("parentKey", McpSchemas.Text("既有項目的編號，例如 WEB-12。")),
                        ("parentRef", McpSchemas.Text("同一份建議裡排在前面的項目的 ref。")),
                        ("cycleId", McpSchemas.Text("排進這個週期（get_project 回的 cycles 的 id）。")),
                        ("milestoneId", McpSchemas.Text("歸屬這個里程碑（get_project 回的 milestones 的 id）。")),
                        ("fields", McpSchemas.FreeObject("自訂欄位 {key: 值}。")))))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeNewItemsAsync),
    ];

    private static async Task<McpToolResult> ProposeNewItemsAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        // 先讀 addMembers：負責人可以是還沒加進專案、但在 addMembers 裡的人，兩邊都在整個組織裡找人。
        var addMembers = await NewItemArguments.ReadAddMembersAsync(context, projectKey, arguments, cancellationToken);
        var draft = new NewItemProposalDraft(projectKey, await NewItemArguments.ReadAsync(context, projectKey, arguments, cancellationToken))
        {
            AddMembers = addMembers,
            ConversationId = context.ConversationId,
        };
        var created = await context.Services.NewItemProposals.CreateAsync(context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var proposal = created.Value;
        return McpToolResult.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            items = proposal.Items.Select(item => new { @ref = item.Ref, type = item.Type, title = item.Title, parentKey = item.ParentKey, parentRef = item.ParentRef, cycleId = item.CycleId, milestoneId = item.MilestoneId }),
            addMembers = proposal.AddMembers.Select(member => new { personId = member.PersonId, name = member.DisplayName, role = member.Role, roleName = member.RoleName }),
            note = NewItemsCreatedNote,
            notice = ItemReadTools.UserContentNotice,
        });
    }

    private static async Task<McpToolResult> ProposeAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var changes = await ReadChangesAsync(context, projectKey, arguments.OptionalArray("changes") ?? [], cancellationToken);

        var draft = new ItemProposalDraft(
            projectKey, context.Item(arguments.RequiredPlain("itemKey")), changes, arguments.OptionalString("comment"))
        {
            DescriptionRevision = arguments.OptionalPlain("descriptionRevision"),
            ReplyTo = ProjectWriteProposalTools.OptionalGuid(arguments, "replyTo"),
            ConversationId = context.ConversationId,
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

    /// <summary>
    /// changes 參數 [{field, value}] 的讀法（單一項目與批次修改共用）：只擋形狀，欄位與值的規則在 AI 模組的建立端。
    /// assignee 的值照同一份名單換成人員編號。
    /// </summary>
    internal static async Task<List<ItemFieldChange>> ReadChangesAsync(
        McpToolContext context, string projectKey, IReadOnlyList<JsonElement> elements, CancellationToken cancellationToken)
    {
        var changes = new List<ItemFieldChange>();
        foreach (var element in elements)
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

        return changes;
    }

    // assignee 和其他 MCP 工具一樣收 me、none、Email 與成員的編號，一律在這裡照同一份名單換成人員編號：
    // 編號也要經過，停用的成員才會和 update_item 一樣被擋、錯誤是同一句。null 照舊交給模組（清掉負責人）。
    private static async Task<JsonElement> ResolveAssigneeAsync(
        McpToolContext context, string projectKey, JsonElement value, CancellationToken cancellationToken)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return value;
        }

        var id = await McpAssignees.ResolveAsync(context, projectKey, "assignee", value.GetString()!, cancellationToken);
        return JsonSerializer.SerializeToElement(id?.ToString());
    }
}
