using System.Text.Json;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 給本機與主機的引擎（<c>agent:run</c>）的工具：建議刪除項目（<c>propose_item_deletions</c>）、建議一次改好幾個項目（<c>propose_bulk_item_changes</c>），
/// 以及撤回自己還沒套用的建議（<c>withdraw_proposal</c>）。提出的兩個只建立建議、不動項目，使用者在網頁按「刪除」「全部套用」才寫入；
/// 撤回只丟掉助理自己的草稿，不佔提出的名額。檢查與建立的邏輯在 AI 模組，內建助理的同名工具用同一份。
/// 規格見 <c>internal/HANDOFF-ai-revise.md</c> 第 6 節。
/// </summary>
internal static class ItemRevisionTools
{
    private const string DeletionNote = "已經建立刪除建議，使用者在網頁上按「刪除」才會刪除。不要說已經刪了。";

    private const string BatchNote = "已經建立批次修改建議，使用者在網頁上按「全部套用」才會改。不要說已經改好了。";

    private const string WithdrawNote = "已經撤回這份建議（網頁的卡片會顯示已略過）。";

    private static readonly HashSet<string> EntryProperties = new(StringComparer.Ordinal)
    {
        "itemKey", "changes", "comment", "expectedDescriptionRevision",
    };

    public static IEnumerable<McpTool> All { get; } =
    [
        new("propose_item_deletions",
            $"建議刪除項目（不會真的刪）：使用者在 Kumi 網頁上看到後按「刪除」才會以他自己的身分刪除（軟刪除，之後能在專案的「已刪除」頁復原）。一份 1 到 {AiRules.MaxItemsPerDeletionProposal} 個項目、不重複。" +
            "只刪使用者明確要刪的項目。要刪的項目底下還有沒刪除的下層，而且下層不在這份裡時會被擋下，錯誤訊息會列出下層的編號：問使用者是要一起刪還是不刪，不要自己決定。" +
            "reason 是一行說明為什麼刪，給卡片顯示，可以省略。提出之後告訴使用者「請按刪除」，不要說已經刪了。",
            McpSchemas.Object(
                required: ["projectKey", "itemKeys"],
                ("projectKey", McpSchemas.Text("這一則對話的專案代號，項目要在這個專案裡。")),
                ("itemKeys", McpSchemas.ArrayOf("要刪除的項目編號，例如 WEB-12。", McpSchemas.Text("項目編號。"))),
                ("reason", McpSchemas.Text("為什麼刪，一行（最長 200 字）。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeDeletionsAsync),
        new("propose_bulk_item_changes",
            "建議一次修改好幾個項目（不會真的改）：每個項目各自一組欄位修改（值可以不同，例如六個任務各自的到期日）與一則留言，一張卡片、使用者按一次「全部套用」才會以他自己的身分寫入。" +
            $"一份 1 到 {AiRules.MaxItemsPerBatchProposal} 個項目、同一個項目只能出現一次；要改兩個以上的項目時用這個，不要一個一個提 propose_item_changes。" +
            $"每個項目的 changes 寫法和 propose_item_changes 完全一樣（[{{field, value}}]，最多 {AiRules.MaxItemProposalChanges} 個欄位，assignee 寫 me、none、專案成員的 Email 或人員編號）；" +
            "改說明的項目一定要帶 expectedDescriptionRevision（get_item 回的 descriptionRevision）。已經套用的建議要修改時，用它改建出來的項目，不要再提一份新增項目的建議。" +
            "提出之後告訴使用者「請按全部套用」，不要說已經改好了。",
            McpSchemas.Object(
                required: ["projectKey", "items"],
                ("projectKey", McpSchemas.Text("這一則對話的專案代號，項目要在這個專案裡。")),
                ("items", McpSchemas.ArrayOf(
                    "要修改的項目，依套用的順序。",
                    McpSchemas.ObjectOf(
                        required: ["itemKey"],
                        ("itemKey", McpSchemas.Text("項目編號，例如 WEB-12。")),
                        ("changes", McpSchemas.ArrayOf(
                            "這個項目要改的欄位 [{field, value}]；只加留言時可以省略。",
                            McpSchemas.ObjectOf(
                                required: ["field", "value"],
                                ("field", McpSchemas.Text("欄位名稱。")),
                                ("value", McpSchemas.Any("新的值，形狀依欄位；null 是清掉。"))))),
                        ("comment", McpSchemas.Text("要加在這個項目的留言（Markdown）。")),
                        ("expectedDescriptionRevision", McpSchemas.Text("改說明時必填：get_item 回的 descriptionRevision。")))))),
            AccessTokenScope.AgentRun, ReadOnly: false, ProposeBulkChangesAsync),
        new("withdraw_proposal",
            "撤回（作廢）你在這則對話裡提出、使用者還沒處理的建議（新增項目、修改、批次修改、刪除、成員、新專案）。撤回不會動到任何項目。" +
            "使用者說「要改」「不要了」而建議還在等待時，先撤回再提新的，並在回覆裡告訴使用者撤回了哪一份、為什麼。已經套用或略過的建議不能撤回；變更集不能撤回（只有能核准的人能在變更集頁否決）。" +
            "proposalId 寫建議的編號（最新一則訊息前面 Kumi 附註列出的代號，或提出時的結果裡有）。",
            McpSchemas.Object(
                required: ["proposalId"],
                ("proposalId", McpSchemas.Text("要撤回的建議編號。"))),
            AccessTokenScope.AgentRun, ReadOnly: false, WithdrawAsync),
    ];

    private static async Task<McpToolResult> ProposeDeletionsAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var elements = arguments.OptionalArray("itemKeys") ?? throw new McpArgumentException("缺少參數 itemKeys，它要是一個 JSON 陣列。");
        if (elements.Count > AiRules.MaxItemsPerDeletionProposal)
        {
            throw new McpArgumentException($"一份建議最多 {AiRules.MaxItemsPerDeletionProposal} 個項目，itemKeys 有 {elements.Count} 個。");
        }

        var keys = new List<string>(elements.Count);
        foreach (var element in elements)
        {
            keys.Add(ReadItemKey(context, element, $"itemKeys[{keys.Count}]"));
        }

        var draft = new ItemDeletionProposalDraft(projectKey, keys) { Reason = arguments.OptionalPlain("reason"), ConversationId = context.ConversationId };
        var created = await context.Services.ItemDeletionProposals.CreateAsync(context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var proposal = created.Value;
        return McpToolResult.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            itemKeys = proposal.Items.Select(item => item.ItemKey),
            note = DeletionNote,
            notice = ItemReadTools.UserContentNotice,
        });
    }

    private static async Task<McpToolResult> ProposeBulkChangesAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var projectKey = context.Project(arguments.RequiredPlain("projectKey"));
        var elements = arguments.OptionalArray("items") ?? throw new McpArgumentException("缺少參數 items，它要是一個 JSON 陣列。");
        // 先擋數量：超過上限的請求不必逐筆讀（每一筆的負責人都要查專案）。
        if (elements.Count > AiRules.MaxItemsPerBatchProposal)
        {
            throw new McpArgumentException($"一份建議最多 {AiRules.MaxItemsPerBatchProposal} 個項目，items 有 {elements.Count} 個。");
        }

        var entries = new List<ItemBatchEntryDraft>(elements.Count);
        foreach (var element in elements)
        {
            entries.Add(await ReadEntryAsync(context, projectKey, element, entries.Count, cancellationToken));
        }

        var created = await context.Services.ItemBatchProposals.CreateAsync(
            context.Actor, new ItemBatchProposalDraft(projectKey, entries) { ConversationId = context.ConversationId }, cancellationToken);
        if (created.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var proposal = created.Value;
        return McpToolResult.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            itemKeys = proposal.Items.Select(item => item.ItemKey),
            note = BatchNote,
            notice = ItemReadTools.UserContentNotice,
        });
    }

    private static async Task<McpToolResult> WithdrawAsync(McpToolContext context, McpArguments arguments, CancellationToken cancellationToken)
    {
        var reference = arguments.RequiredPlain("proposalId");
        // 對話由伺服器從工作找到；一般的權杖沒有工作（工具清單已經擋掉，這裡是第二道）。
        if (context.ConversationId is not { } conversationId)
        {
            throw new McpArgumentException("找不到這件工作的對話。");
        }

        var result = await context.Services.ProposalLedger.WithdrawAsync(context.Actor, conversationId, reference, cancellationToken);
        if (result.Failure is { } failure)
        {
            return McpToolResult.FromFailure(failure);
        }

        var entry = result.Value;
        return McpToolResult.Ok(new { proposalId = entry.Reference, kind = entry.Kind, state = entry.State, note = WithdrawNote });
    }

    private static string ReadItemKey(McpToolContext context, JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new McpArgumentException($"{path} 要是項目編號（字串）。");
        }

        var key = element.GetString()!;
        return key.Any(char.IsControl)
            ? throw new McpArgumentException($"{path} 不能含有 NUL 或其他控制字元。")
            : context.Item(key);
    }

    private static async Task<ItemBatchEntryDraft> ReadEntryAsync(
        McpToolContext context, string projectKey, JsonElement element, int index, CancellationToken cancellationToken)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new McpArgumentException($"items[{index}] 要是一個物件。");
        }

        var values = element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        if (values.Keys.FirstOrDefault(name => !EntryProperties.Contains(name)) is { } unknown)
        {
            throw new McpArgumentException($"items[{index}] 不認得的屬性：{unknown}。");
        }

        try
        {
            var entry = new McpArguments(values);
            var changes = await ItemProposalTools.ReadChangesAsync(context, projectKey, entry.OptionalArray("changes") ?? [], cancellationToken);
            return new ItemBatchEntryDraft(
                context.Item(entry.RequiredPlain("itemKey")),
                changes,
                entry.OptionalString("comment"),
                entry.OptionalPlain("expectedDescriptionRevision"));
        }
        catch (McpArgumentException exception)
        {
            throw new McpArgumentException($"items[{index}]：{exception.Message}");
        }
    }
}
