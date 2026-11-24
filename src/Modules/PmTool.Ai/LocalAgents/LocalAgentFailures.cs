using PmTool.Abstractions;

namespace PmTool.Ai.LocalAgents;

/// <summary>本機代理那一頭回的失敗。代碼是對外合約（<see cref="AiErrorCodes"/>），訊息給人看。</summary>
internal static class LocalAgentFailures
{
    public static Failure Disabled() =>
        new(FailureKind.Forbidden, AiErrorCodes.PersonalDisabled, "組織目前不允許使用自己的 AI 帳號，本機代理也不能用。");

    public static Failure JobNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.AgentJobNotFound, "找不到這件工作，或它已經結束。");

    public static Failure Invalid(string path, string message) =>
        new(FailureKind.Invalid, AiErrorCodes.AgentInvalid, message)
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, message)],
        };

    public static Failure MachineNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.ConnectionNotFound, "找不到這台電腦。");
}
