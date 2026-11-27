using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議編輯、刪除或復原既有的留言：只建立建議，使用者在網頁按「套用」才會寫入。檢查與建立的邏輯在
/// <see cref="ICommentProposalCreator"/>，MCP 的同名工具用同一份。新的留言與回覆不走這裡（修改建議的 comment／replyTo）。
/// </summary>
internal sealed class ProposeCommentChangesTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeCommentChanges,
    $"對既有的留言提出編輯、刪除或復原的建議（不會真的改）：使用者在網頁上看到後按「套用」才會寫入。changes 是 [{{itemKey, commentId, action, body?}}]（1 到 {AiRules.MaxCommentChangesPerProposal} 筆，"
    + "同一則留言只出現一次，可以跨同一個專案的好幾個項目）；action 是 edit（改內容，要給 body）、delete（刪除，留言會顯示「已刪除」）、restore（復原已刪除的留言），delete 與 restore 不能給 body。"
    + "commentId 從 get_item 的活動或 list_comments 拿。只能編輯使用者自己的留言；刪除別人的留言要有專案管理權限；匯入的留言不能編輯。"
    + "新的留言與回覆不要用這個工具（用 propose_item_changes 的 comment／replyTo）。權限不夠或內容不合規定時會被擋下，照實轉告。提出之後告訴使用者「請按套用」，不要說已經改好了。",
    $$$$"""
    {"type":"object","properties":{"changes":{"type":"array","description":"要做的留言修改，最多 {{{{AiRules.MaxCommentChangesPerProposal}}}} 筆","items":{"type":"object","properties":{"itemKey":{"type":"string","description":"留言所在的項目編號，例如 WEB-12"},"commentId":{"type":"string","description":"留言的 id（get_item 的活動或 list_comments 回的 id）"},"action":{"type":"string","enum":["edit","delete","restore"],"description":"edit 改內容、delete 刪除、restore 復原已刪除的"},"body":{"type":"string","description":"edit 才給：新的留言內容（Markdown）"}},"required":["itemKey","commentId","action"],"additionalProperties":false}}},"required":["changes"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立留言建議，使用者在網頁上按「套用」才會生效。不要說已經改好了。";

    private static readonly string[] ChangeProperties = ["itemKey", "commentId", "action", "body"];

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Keys.FirstOrDefault(k => k != "changes") is { } extra)
        {
            return ToolResults.Error($"不認得的參數：{extra}。只有 changes。");
        }

        if (ToolArguments.Array(arguments, "changes") is not { } elements)
        {
            return ToolResults.Error("參數 changes 要是 [{itemKey, commentId, action, body?}] 的陣列。");
        }

        var changes = new List<CommentChangeDraft>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var read = ReadChange(elements[i], $"changes[{i}]");
            if (read.Error is { } error)
            {
                return ToolResults.Error(error);
            }

            changes.Add(read.Change!);
        }

        var draft = new CommentProposalDraft(Context.RequiredProjectKey, changes) { ConversationId = Context.ConversationId };
        var created = await Context.CommentProposals.CreateAsync(Context.Actor, draft, cancellationToken);
        if (created.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var proposal = created.Value;
        return ToolResults.Ok(new
        {
            proposalId = proposal.Id,
            projectKey = proposal.ProjectKey,
            count = proposal.Changes.Count,
            itemKeys = proposal.Changes.Select(c => c.ItemKey).Distinct(StringComparer.Ordinal),
            note = CreatedNote,
        });
    }

    private static (CommentChangeDraft? Change, string? Error) ReadChange(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, $"{path} 要是物件。");
        }

        var unknown = element.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !ChangeProperties.Contains(name));
        if (unknown is not null)
        {
            return (null, $"{path} 不認得的屬性：{unknown}（只有 itemKey、commentId、action、body）。");
        }

        var itemKey = Text(element, "itemKey");
        if (string.IsNullOrWhiteSpace(itemKey))
        {
            return (null, $"{path}.itemKey 要是項目編號（字串）。");
        }

        if (!Guid.TryParse(Text(element, "commentId"), out var commentId))
        {
            return (null, $"{path}.commentId 要是留言的 id（字串）。");
        }

        CommentAction? action = Text(element, "action") switch
        {
            "edit" => CommentAction.Edit,
            "delete" => CommentAction.Delete,
            "restore" => CommentAction.Restore,
            _ => null,
        };
        if (action is null)
        {
            return (null, $"{path}.action 要是 edit、delete、restore 其中之一。");
        }

        if (element.TryGetProperty("body", out var body) && body.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            return (null, $"{path}.body 要是字串。");
        }

        return (new CommentChangeDraft(itemKey, commentId, action.Value, Text(element, "body")), null);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
