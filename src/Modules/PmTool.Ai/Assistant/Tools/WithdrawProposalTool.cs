using Microsoft.Extensions.AI;
using PmTool.Ai.Proposals;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>
/// 撤回助理自己還沒套用的建議。助理可以直接做、不必再提建議：它只丟掉助理自己的草稿、不碰使用者的資料，和使用者按「不要」一樣
/// （卡片照舊顯示「已略過」）。檢查在 <see cref="IAssistantProposalLedger"/>：只能撤回這則對話裡、這個人自己的、還在等待的。
/// </summary>
internal sealed class WithdrawProposalTool(AssistantToolContext context) : AssistantTool(
    AssistantToolNames.WithdrawProposal,
    "撤回你在這則對話裡提出、使用者還沒套用的建議（卡片會顯示已略過）。proposalId 用 Kumi 附註「這則對話裡你提出過的建議」寫的代號（變更集寫 changeset:12，但變更集不能撤回）。"
    + "已經套用或略過的撤不了；要改已經套用的內容，改建出來的項目或提新的建議。撤回之後在回覆裡告訴使用者你撤回了哪一份、為什麼。",
    """
    {"type":"object","properties":{"proposalId":{"type":"string","description":"建議的代號，例如 4f0c…（GUID）"}},"required":["proposalId"],"additionalProperties":false}
    """,
    context)
{
    public const string WithdrawnNote = "已經撤回這份建議（網頁的卡片會顯示已略過）。";

    protected override async Task<string> RunAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var proposalId = ToolArguments.String(arguments, "proposalId");
        if (string.IsNullOrWhiteSpace(proposalId))
        {
            return ToolResults.Error("缺少參數 proposalId。");
        }

        var withdrawn = await Context.Ledger.WithdrawAsync(Context.Actor, Context.ConversationId, proposalId, cancellationToken);
        if (withdrawn.Failure is { } failure)
        {
            return ToolResults.FromFailure(failure);
        }

        var entry = withdrawn.Value;
        return ToolResults.Ok(new
        {
            proposalId = entry.Reference,
            kind = ProposalNames.KindCode(entry.Kind),
            state = ProposalNames.StateCode(entry.State),
            note = WithdrawnNote,
        });
    }
}
