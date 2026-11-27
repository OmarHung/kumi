using PmTool.Abstractions;

namespace PmTool.Ai.Proposals;

internal static class ProposalFailures
{
    public static Failure NotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.ProposalNotFound, "找不到這份建議：它不在這則對話裡，或不是這位使用者的。用「已經提出的建議」裡寫的代號。");

    public static Failure Closed() =>
        new(FailureKind.Conflict, AiErrorCodes.ProposalClosed, "這份建議已經套用、略過或正在建立，不能撤回了。");

    public static Failure NotWithdrawable() =>
        new(FailureKind.Conflict, AiErrorCodes.ProposalNotWithdrawable, "變更集不能撤回，只有能核准的人可以在「變更集」頁否決它。");
}
