using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 建議復原已刪除的項目：只建立建議，使用者在網頁按「復原」才會復原。檢查與建立的邏輯在
/// <see cref="IItemRestoreProposalCreator"/>，MCP 的同名工具用同一份。
/// </summary>
internal sealed class ProposeItemRestoresTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.ProposeItemRestores,
    $"對一個或多個已刪除的項目提出復原建議（不會真的復原）：使用者在網頁上看到後按「復原」才會復原。itemKeys 是已刪除的項目編號（1 到 {AiRules.MaxItemsPerDeletionProposal} 個，不重複），要復原的編號從 list_deleted_items 拿；"
    + "reason 是一行理由（可省略，給卡片顯示）。項目的上層也是刪除的、又不在這份裡時會被擋下，錯誤訊息列出上層：問使用者要不要一起復原，不要自己決定。"
    + "使用者沒有復原（刪除項目）的權限時會被擋下，照實轉告。提出之後告訴使用者「請按復原」，不要說已經復原了。",
    $$$"""
    {"type":"object","properties":{"itemKeys":{"type":"array","description":"要復原的項目編號，最多 {{{AiRules.MaxItemsPerDeletionProposal}}} 個","items":{"type":"string","description":"項目編號，例如 WEB-12"}},"reason":{"type":"string","description":"為什麼復原（一行）"}},"required":["itemKeys"],"additionalProperties":false}
    """,
    context)
{
    public const string CreatedNote = "已經建立復原建議，使用者在網頁上按「復原」才會生效。不要說已經復原了。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (arguments.Keys.FirstOrDefault(k => k is not ("itemKeys" or "reason")) is { } extra)
        {
            return ToolResults.Error($"不認得的參數：{extra}。只有 itemKeys、reason。");
        }

        if (ToolArguments.Array(arguments, "itemKeys") is not { } elements || elements.Any(e => e.ValueKind != JsonValueKind.String))
        {
            return ToolResults.Error("參數 itemKeys 要是項目編號（字串）的陣列。");
        }

        var draft = new ItemRestoreProposalDraft(Context.RequiredProjectKey, [.. elements.Select(e => e.GetString()!)])
        {
            ConversationId = Context.ConversationId,
            Reason = ToolArguments.String(arguments, "reason"),
        };
        var created = await Context.RestoreProposals.CreateAsync(Context.Actor, draft, cancellationToken);
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
