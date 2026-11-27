using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議刪除項目：只建立建議，使用者在網頁按「刪除」才會刪（軟刪除，能在「已刪除」復原）。檢查與建立的邏輯在
/// <see cref="IItemDeletionProposalCreator"/>，MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeItemDeletionsTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeItemDeletions,
    $"對一個或多個項目提出刪除建議（不會真的刪）：使用者在網頁上看到後按「刪除」才會刪，刪掉的項目能在專案的「已刪除」頁復原。itemKeys 是項目編號（1 到 {AiRules.MaxItemsPerDeletionProposal} 個，不重複），"
    + "reason 是一行理由（可省略，給卡片顯示）。只刪使用者要你刪的；要刪的項目底下還有沒刪除、又不在這份裡的下層時會被擋下，錯誤訊息列出那些下層：問使用者要一起刪還是不刪，不要自己決定。"
    + "使用者沒有刪除項目的權限時會被擋下，照實轉告。提出之後告訴使用者「請按刪除」，不要說已經刪了。",
    $$$"""
    {"type":"object","properties":{"itemKeys":{"type":"array","description":"要刪除的項目編號，最多 {{{AiRules.MaxItemsPerDeletionProposal}}} 個","items":{"type":"string","description":"項目編號，例如 WEB-12"}},"reason":{"type":"string","description":"為什麼刪（一行）"}},"required":["itemKeys"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立刪除建議，使用者在網頁上按「刪除」才會刪除。不要說已經刪了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (ToolArguments.Array(arguments, "itemKeys") is not { } elements || elements.Any(e => e.ValueKind != JsonValueKind.String))
        {
            return ToolResults.Error("參數 itemKeys 要是項目編號（字串）的陣列。");
        }

        var draft = new ItemDeletionProposalDraft(Context.RequiredProjectKey, [.. elements.Select(e => e.GetString()!)])
        {
            ConversationId = Context.ConversationId,
            Reason = ToolArguments.String(arguments, "reason"),
        };
        var created = await Context.DeletionProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            itemKeys = proposal.Items.Select(item => item.ItemKey),
            note = CreatedNote,
        });
    }
}

/// <summary>
/// 建議一次修改好幾個項目（每個項目各自的欄位與留言）：只建立建議，使用者在網頁按「全部套用」才會改。
/// 每一筆的檢查與單一項目的修改建議同一份（<see cref="IItemBatchProposalCreator"/>，MCP 的同名工具用同一份）。
/// </summary>
internal sealed class ProposeBulkItemChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeBulkItemChanges,
    $"對兩個以上的項目一次提出修改建議（不會真的改）：每個項目各自一組欄位修改（值可以不同，例如六個任務各自的到期日）與一則留言，一張卡片，使用者按一次「全部套用」才會寫入。"
    + $"items 是 [{{itemKey, changes, comment?, replyTo?, expectedDescriptionRevision?}}]（1 到 {AiRules.MaxItemsPerBatchProposal} 個項目，同一個項目只出現一次）；changes 的寫法和 propose_item_changes 完全一樣"
    + "（field 可以是 title、description、status、assignee、priority、startDate、dueDate、estimate、type、parentKey、cycleId、milestoneId、following（true 關注、false 取消關注）、fields.<自訂欄位的 key>；每個項目最多 "
    + $"{AiRules.MaxItemProposalChanges} 個欄位）。改某個項目的說明時，那一筆一定要帶 expectedDescriptionRevision（get_item 回的 descriptionRevision）。"
    + "改 type 可能讓那個項目的狀態回到新流程的起點，同一筆裡的 status 與 fields.* 照新的類型檢查；要回覆某一則留言，那一筆帶 replyTo（留言 id，要和 comment 一起給）。"
    + "要改兩個以上的項目時用它，不要一個一個提 propose_item_changes。提出之後告訴使用者「請按全部套用」，不要說已經改好了。",
    $$$$"""
    {"type":"object","properties":{"items":{"type":"array","description":"要改的項目，最多 {{{{AiRules.MaxItemsPerBatchProposal}}}} 個","items":{"type":"object","properties":{"itemKey":{"type":"string","description":"項目編號，例如 WEB-12"},"changes":{"type":"array","description":"這個項目要改的欄位，寫法同 propose_item_changes","items":{"type":"object","properties":{"field":{"type":"string","description":"欄位名稱"},"value":{"description":"新的值，形狀依欄位；null 是清掉"}},"required":["field","value"],"additionalProperties":false}},"comment":{"type":"string","description":"要加的留言（Markdown）"},"replyTo":{"type":"string","description":"要回覆的留言 id（要和 comment 一起給）"},"expectedDescriptionRevision":{"type":"string","description":"改說明時必填：get_item 回的 descriptionRevision"}},"required":["itemKey"],"additionalProperties":false}}},"required":["items"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立批次修改建議，使用者在網頁上按「全部套用」才會改。不要說已經改好了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (ToolArguments.Array(arguments, "items") is not { } elements)
        {
            return ToolResults.Error("參數 items 要是 [{itemKey, changes, comment?, expectedDescriptionRevision?}] 的陣列。");
        }

        var entries = new List<ItemBatchEntryDraft>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var read = ReadEntry(elements[i], $"items[{i}]");
            if (read.Error is { } error)
            {
                return ToolResults.Error(error);
            }

            entries.Add(read.Entry!);
        }

        var draft = new ItemBatchProposalDraft(Context.RequiredProjectKey, entries) { ConversationId = Context.ConversationId };
        var created = await Context.BatchProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            itemKeys = proposal.Items.Select(item => item.ItemKey),
            note = CreatedNote,
        });
    }

    private static (ItemBatchEntryDraft? Entry, string? Error) ReadEntry(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, $"{path} 要是物件。");
        }

        if (Text(element, "itemKey") is not { } itemKey || string.IsNullOrWhiteSpace(itemKey))
        {
            return (null, $"{path}.itemKey 要是項目編號（字串）。");
        }

        IReadOnlyList<ItemFieldChange> changes = [];
        if (element.TryGetProperty("changes", out var raw) && raw.ValueKind != JsonValueKind.Null)
        {
            var read = ItemChangeArguments.Read(raw.ValueKind == JsonValueKind.Array ? [.. raw.EnumerateArray().Select(e => e.Clone())] : null, $"{path}.changes");
            if (read.Error is { } error)
            {
                return (null, error);
            }

            changes = read.Value!;
        }

        foreach (var name in new[] { "comment", "expectedDescriptionRevision" })
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
            {
                return (null, $"{path}.{name} 要是字串。");
            }
        }

        var replyTo = ItemChangeArguments.ReadReplyTo(element, path);
        if (replyTo.Error is { } replyError)
        {
            return (null, replyError);
        }

        return (new ItemBatchEntryDraft(itemKey, changes, Text(element, "comment"), Text(element, "expectedDescriptionRevision"))
        {
            ReplyTo = replyTo.Value,
        }, null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
