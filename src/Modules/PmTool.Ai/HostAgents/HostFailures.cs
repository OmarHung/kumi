using PmTool.Abstractions;

namespace PmTool.Ai.HostAgents;

/// <summary>主機代理回的失敗。代碼是對外合約（<see cref="AiErrorCodes"/>），訊息給人看。</summary>
internal static class HostFailures
{
    public static Failure OwnerOnly() =>
        new(FailureKind.Forbidden, AiErrorCodes.HostOwnerOnly, "只有組織擁有者能管理主機 AI 代理。");

    public static Failure AgentNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.HostAgentNotFound, "找不到這台主機代理。");

    public static Failure SourceNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.HostSourceNotFound, "找不到這個來源。");

    public static Failure LoginNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.HostLoginNotFound, "找不到這次登入。");

    public static Failure AgentUnavailable() =>
        new(FailureKind.Invalid, AiErrorCodes.HostAgentUnavailable, "這台主機代理還沒配對或已經停用，不能開始登入。");

    public static Failure PairingInvalid() =>
        new(FailureKind.Invalid, AiErrorCodes.HostPairingInvalid, "配對碼不對、已經過期或用過了，請組織擁有者在網頁重新產生。");

    public static Failure LoginRejected(string message) =>
        new(FailureKind.Invalid, AiErrorCodes.HostLoginRejected, message);

    public static Failure BillingNotShareable() =>
        new(FailureKind.Invalid, AiErrorCodes.HostBillingNotShareable,
            "這個引擎登入的是訂閱帳號（或判斷不出計費方式）。Claude 與 ChatGPT 的訂閱依條款不能給全組織共用，請改用 API 用量計費的登入。");

    public static Failure SourceUnavailable(string message) =>
        new(FailureKind.Invalid, AiErrorCodes.HostSourceUnavailable, message);

    public static Failure QueueFull(string message) => new(FailureKind.Conflict, AiErrorCodes.HostQueueFull, message);

    public static Failure QuotaExceeded(string message) => new(FailureKind.Conflict, AiErrorCodes.HostQuotaExceeded, message);

    public static Failure LeaseLost() =>
        new(FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost, "這件工作的租約不對，或已經被伺服器結束，請停下引擎、不要再回報。");

    public static Failure MachineMismatch() =>
        new(FailureKind.Forbidden, AiErrorCodes.HostMachineMismatch, "登記的機器編號和配對時的不同。");

    public static Failure AgentInvalid(string path, string message) =>
        new(FailureKind.Invalid, AiErrorCodes.HostAgentInvalid, message)
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, path, message)],
        };

    public static Failure JobNotFound() =>
        new(FailureKind.NotFound, AiErrorCodes.AgentJobNotFound, "找不到這件工作。");

    public static Failure Settings(params FailureDetail[] details) =>
        new(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "主機代理的設定有幾個欄位不對。") { Details = details };

    public static FailureDetail Invalid(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    public static FailureDetail Required(string path, string message) => new(AiErrorCodes.FieldRequired, path, message);
}
