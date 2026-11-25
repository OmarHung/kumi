using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 配對。另外定義而不直接收合約的型別，模組加屬性時對外的內文不會跟著變。帶著配對碼：<see cref="ToString"/> 不印它。
/// </summary>
internal sealed record HostPairRequest(string Code, string MachineKey, string MachineName, string AgentVersion)
{
    public HostPairingRequest ToRequest() => new(Code, MachineKey, MachineName, AgentVersion);

    public override string ToString() => $"HostPairRequest {{ Code = ***, MachineKey = {MachineKey} }}";
}

/// <summary>機器憑證只在這個回應裡出現一次。</summary>
internal sealed record HostPairData(Guid AgentId, string AgentName, string Credential)
{
    public static HostPairData From(HostPairingResult result) => new(result.AgentId, result.AgentName, result.Credential);

    public override string ToString() => $"HostPairData {{ AgentId = {AgentId}, Credential = *** }}";
}

/// <summary>
/// 登記。<c>capacity</c> 是這台現在還能同時多跑幾件，0 時只登記、不領。
/// <c>engines</c> 兩個引擎各一筆（沒裝也要報）。<c>machineKey</c> 必填（模組檢查，沒帶是 422），
/// 和配對時的不同就是 401；<c>machineName</c> 只在機器編號不同時用來寫警告。
/// </summary>
internal sealed record HostCheckInRequest(
    string AgentVersion, string ServerUrl, int Capacity, IReadOnlyList<HostEngineRequest?> Engines, string? MachineKey = null, string? MachineName = null)
{
    /// <summary>一次最多領幾件的上限：worker 同時最多跑 <see cref="AiRules.HostWorkerMaxConcurrent"/> 件，給寬一點，只是擋掉不合理的數字。</summary>
    public const int MaxCapacity = 16;

    /// <summary>陣列裡有 null 的元素時的失敗（要在 <see cref="ToCheckIn"/> 之前檢查）；沒有就是 null。</summary>
    public Failure? FindNullElement() => NullElementChecks.InEngines(Engines, engine => engine.Models, AiErrorCodes.HostAgentInvalid);

    public HostAgentCheckIn ToCheckIn() =>
        new(AgentVersion, ServerUrl, [.. Engines.Select(engine => engine!.ToReport())]) { MachineKey = MachineKey, MachineName = MachineName };
}

/// <param name="Kind">snake_case：claude_code、codex。</param>
/// <param name="Billing">snake_case：api、subscription、unknown。</param>
internal sealed record HostEngineRequest(
    AiProviderKind Kind,
    bool Installed,
    bool Supported,
    bool LoggedIn,
    HostBilling Billing,
    IReadOnlyList<LocalModelRequest?> Models,
    string? Version = null,
    Guid? LoginId = null,
    string? Problem = null)
{
    public HostEngineReport ToReport() =>
        new(Kind, Installed, Version, Supported, LoggedIn, Billing, LoginId, [.. Models.Select(model => model!.ToOption())], Problem);
}

/// <summary>
/// 領到的工作與要登出的引擎。<c>job</c> 沒有是 null（寫出 null，不省略）。<c>rotatedCredential</c> 是快到期時發的新機器憑證（只在這個回應裡），
/// 沒有要換是 null；<see cref="ToString"/> 不印它。
/// </summary>
internal sealed record HostCheckInData(HostJobData? Job, IReadOnlyList<AiProviderKind> Logout, bool Disabled, string? RotatedCredential)
{
    public static HostCheckInData From(HostCheckInResult result) =>
        new(result.Job is null ? null : HostJobData.From(result.Job), result.Logout, result.Disabled, result.RotatedCredential);

    public override string ToString() =>
        $"HostCheckInData {{ Job = {Job?.JobId}, Disabled = {Disabled}, RotatedCredential = {(RotatedCredential is null ? "null" : "***")} }}";
}

/// <summary>領到的工作。<c>toolToken</c> 是這件工作專用的授權，只在這個回應裡；<see cref="ToString"/> 不印它。</summary>
internal sealed record HostJobData(
    Guid JobId,
    int Lease,
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string ProjectKey,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    string McpUrl,
    int MaxTurns,
    string ToolToken,
    DateTimeOffset ToolTokenExpiresAt)
{
    public static HostJobData From(HostJob job) => new(
        job.JobId,
        job.Lease,
        job.Engine,
        job.Model,
        job.Effort,
        job.ProjectKey,
        job.SystemPrompt,
        job.Prompt,
        job.AllowedTools,
        job.McpUrl,
        job.MaxTurns,
        job.ToolToken,
        job.ToolTokenExpiresAt);

    public override string ToString() => $"HostJobData {{ JobId = {JobId}, Engine = {Engine}, ProjectKey = {ProjectKey}, ToolToken = *** }}";
}

/// <summary>回報一批進度。<c>events</c> 的形狀和本機代理的相同。</summary>
internal sealed record HostEventsRequest(int Lease, int FirstSeq, IReadOnlyList<LocalAgentEventRequest?> Events);

internal sealed record HostAckData(bool Cancelled, int NextSeq)
{
    public static HostAckData From(HostReportAck ack) => new(ack.Cancelled, ack.NextSeq);
}

/// <param name="Status">snake_case：complete、stopped、failed。</param>
internal sealed record HostFinishRequest(int Lease, AssistantMessageStatus Status, string? Error = null, LocalAgentUsageRequest? Usage = null)
{
    public LocalAgentFinish ToFinish() =>
        new(Status, Error, Usage is null ? null : new AssistantUsage(Usage.InputTokens, Usage.OutputTokens));
}

/// <summary>認領登入：登入碼。<see cref="ToString"/> 不印它。</summary>
internal sealed record HostClaimRequest(string Code)
{
    public override string ToString() => "HostClaimRequest { Code = *** }";
}

internal sealed record HostClaimData(Guid LoginId, AiProviderKind Engine, DateTimeOffset ExpiresAt)
{
    public static HostClaimData From(HostLoginClaim claim) => new(claim.LoginId, claim.Engine, claim.ExpiresAt);
}

/// <param name="Billing">snake_case：api、subscription、unknown。</param>
internal sealed record HostCompleteRequest(bool LoggedIn, HostBilling Billing, string? Problem = null)
{
    public HostLoginReport ToReport() => new(LoggedIn, Billing, Problem);
}

internal sealed record HostCompleteData(Guid LoginId, AiProviderKind Engine)
{
    public static HostCompleteData From(HostLoginCompleted completed) => new(completed.LoginId, completed.Engine);
}

internal static class HostWorkerFailures
{
    /// <summary>worker 的進度內容不對：和本機代理共用同一個檢查，但失敗代碼是主機代理的。</summary>
    public static Failure AsHostInvalid(Failure failure) => failure with { Code = AiErrorCodes.HostAgentInvalid };
}
