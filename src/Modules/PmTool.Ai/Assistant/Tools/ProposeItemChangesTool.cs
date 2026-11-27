using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議修改一個項目：只建立建議，使用者在網頁按「套用」才會改。檢查與建立的邏輯在 <see cref="IItemProposalCreator"/>，
/// MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeItemChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeItemChanges,
    "對一個項目提出修改建議（不會真的改）：使用者在網頁上看到後按「套用」才會寫入。changes 是 [{field, value}]，"
    + "field 可以是 title、description（Markdown）、status（狀態的 key）、assignee（人員 id，null 清掉）、priority（none、low、medium、high、urgent）、"
    + "startDate、dueDate（yyyy-MM-dd，null 清掉）、estimate（數字，null 清掉）、type（類型的 key）、parentKey（上層項目編號，null 移到最上層）、"
    + "cycleId、milestoneId（get_planning 回的 id，null 清掉）、fields.<自訂欄位的 key>；同一個欄位只能出現一次。"
    + "改 type 可能讓狀態回到新流程的起點（新類型沒有同 key、同類別的狀態，或使用者走不到它時），同一份裡的 status 與 fields.* 照新的類型檢查；上層與下層的類型也要允許新類型。"
    + "comment 是要一併加上的留言；要回覆某一則留言就帶 replyTo（get_item 或 list_comments 回的留言 id，一定要和 comment 一起給）。改說明時一定要帶 descriptionRevision（get_item 回的那個），沒帶會被擋下，請先重讀；說明超過長度上限的項目不能改說明，請改用留言。提出之後告訴使用者「請按套用」，不要說已經改好了。",
    $$$"""
    {"type":"object","properties":{"itemKey":{"type":"string","description":"項目編號，例如 WEB-12"},"changes":{"type":"array","description":"要改的欄位，最多 {{{AiRules.MaxItemProposalChanges}}} 個","items":{"type":"object","properties":{"field":{"type":"string","description":"欄位名稱"},"value":{"description":"新的值，形狀依欄位；null 是清掉"}},"required":["field","value"],"additionalProperties":false}},"descriptionRevision":{"type":"string","description":"改說明時必填：get_item 回的 descriptionRevision"},"comment":{"type":"string","description":"要加的留言（Markdown）"},"replyTo":{"type":"string","description":"要回覆的留言 id（要和 comment 一起給）"}},"required":["itemKey"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立修改建議，使用者在網頁上按「套用」才會改。不要說已經改好了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var itemKey = ToolArguments.String(arguments, "itemKey");
        if (string.IsNullOrWhiteSpace(itemKey))
        {
            return ToolResults.Error("缺少參數 itemKey。");
        }

        var changes = ReadChanges(arguments);
        if (changes.Error is { } error)
        {
            return ToolResults.Error(error);
        }

        var replyTo = ItemChangeArguments.ReadReplyTo(arguments, "replyTo");
        if (replyTo.Error is { } replyError)
        {
            return ToolResults.Error(replyError);
        }

        var draft = new ItemProposalDraft(Context.RequiredProjectKey, itemKey, changes.Value!, ToolArguments.String(arguments, "comment"))
        {
            ConversationId = Context.ConversationId,
            DescriptionRevision = ToolArguments.String(arguments, "descriptionRevision"),
            ReplyTo = replyTo.Value,
        };
        var created = await Context.Proposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            itemKey = proposal.ItemKey,
            changes = proposal.Changes.Select(change => new { field = change.Field, value = change.Value }),
            comment = proposal.Comment,
            replyTo = proposal.ReplyTo is { } reply ? new { commentId = reply.CommentId, authorName = reply.AuthorName } : null,
            note = CreatedNote,
        });
    }

    private static (IReadOnlyList<ItemFieldChange>? Value, string? Error) ReadChanges(AIFunctionArguments arguments) =>
        arguments.ContainsKey("changes")
            ? ItemChangeArguments.Read(ToolArguments.Array(arguments, "changes"), "changes")
            : ([], null);
}
