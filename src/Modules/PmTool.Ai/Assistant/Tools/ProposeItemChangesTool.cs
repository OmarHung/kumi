using System.Text.Json;
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
    + "startDate、dueDate（yyyy-MM-dd，null 清掉）、estimate（數字，null 清掉）、fields.<自訂欄位的 key>；同一個欄位只能出現一次。"
    + "comment 是要一併加上的留言。改說明時一定要帶 descriptionRevision（get_item 回的那個），沒帶會被擋下，請先重讀；說明超過長度上限的項目不能改說明，請改用留言。提出之後告訴使用者「請按套用」，不要說已經改好了。",
    $$$"""
    {"type":"object","properties":{"itemKey":{"type":"string","description":"項目編號，例如 WEB-12"},"changes":{"type":"array","description":"要改的欄位，最多 {{{AiRules.MaxItemProposalChanges}}} 個","items":{"type":"object","properties":{"field":{"type":"string","description":"欄位名稱"},"value":{"description":"新的值，形狀依欄位；null 是清掉"}},"required":["field","value"],"additionalProperties":false}},"descriptionRevision":{"type":"string","description":"改說明時必填：get_item 回的 descriptionRevision"},"comment":{"type":"string","description":"要加的留言（Markdown）"}},"required":["itemKey"],"additionalProperties":false}
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

        var draft = new ItemProposalDraft(Context.ProjectKey, itemKey, changes.Value!, ToolArguments.String(arguments, "comment"))
        {
            ConversationId = Context.ConversationId,
            DescriptionRevision = ToolArguments.String(arguments, "descriptionRevision"),
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
            note = CreatedNote,
        });
    }

    private static (IReadOnlyList<ItemFieldChange>? Value, string? Error) ReadChanges(AIFunctionArguments arguments)
    {
        if (!arguments.ContainsKey("changes"))
        {
            return ([], null);
        }

        if (ToolArguments.Array(arguments, "changes") is not { } elements)
        {
            return (null, "參數 changes 要是 [{field, value}] 的陣列。");
        }

        var changes = new List<ItemFieldChange>(elements.Count);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("field", out var field)
                || field.ValueKind != JsonValueKind.String)
            {
                return (null, "changes 的每一項都要是 {field, value}，field 是字串。");
            }

            changes.Add(new ItemFieldChange(field.GetString()!, element.TryGetProperty("value", out var value) ? value : default));
        }

        return (changes, null);
    }
}
