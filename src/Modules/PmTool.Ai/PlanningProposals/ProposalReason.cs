using PmTool.Abstractions;
using PmTool.Ai.ItemRevisions;

namespace PmTool.Ai.PlanningProposals;

/// <summary>建議的「為什麼」（一行，最長 <see cref="ItemRevisionLimits.MaxReasonLength"/> 字，給卡片顯示）。</summary>
internal static class ProposalReason
{
    public static (string? Text, FailureDetail? Issue) Check(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (null, null);
        }

        if (text.Length > ItemRevisionLimits.MaxReasonLength)
        {
            return (null, new FailureDetail(AiErrorCodes.FieldInvalid, "reason", $"reason 最長 {ItemRevisionLimits.MaxReasonLength} 個字元。"));
        }

        return PlanningText.HasForbiddenCharacters(text, allowLineBreaks: false)
            ? (null, new FailureDetail(AiErrorCodes.FieldInvalid, "reason", "reason 不能含有換行、NUL 或其他控制字元。"))
            : (text, null);
    }
}
