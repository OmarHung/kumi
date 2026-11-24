using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>資料列轉成對外的型別。</summary>
internal static class HostAgentMapper
{
    public static HostAgent ToAgent(
        HostAgentRow row,
        IReadOnlyDictionary<(Guid AgentId, string Engine), HostAuthorization> auths,
        IEnumerable<HostLoginRow> logins,
        DateTimeOffset now) => new(
        row.Id,
        row.Name,
        HostEnums.ParseState(row.State),
        row.LastSeenAt is { } seen && now - seen <= AiRules.HostAgentOnlineWindow,
        row.LastSeenAt,
        row.MachineName,
        row.AgentVersion,
        row.ServerUrl,
        [.. HostEnginesJson.Deserialize(row.Engines).Select(report =>
        {
            auths.TryGetValue((row.Id, AiEnumNames.NameOf(report.Kind)), out var auth);
            return HostReadiness.Evaluate(report.Kind, report, auth);
        })],
        [.. logins.Where(l => l.AgentId == row.Id && IsVisible(l, now)).OrderBy(l => l.CreatedAt).Select(ToLogin)],
        row.State == HostAgentStates.AwaitingPairing ? row.PairingExpiresAt : null,
        row.CreatedAt);

    public static HostLogin ToLogin(HostLoginRow row) => new(
        row.Id,
        AiEnumNames.ParseKind(row.Engine),
        HostEnums.ParseLoginState(row.State),
        row.ExpiresAt,
        row.Error,
        row.StartedBy,
        row.CreatedAt);

    /// <summary>進行中（還沒過期的等認領、已認領）的，與 <see cref="AiRules.HostLoginLifetime"/> 內失敗的。</summary>
    private static bool IsVisible(HostLoginRow login, DateTimeOffset now) => login.State switch
    {
        HostLoginStates.Pending or HostLoginStates.Claimed => login.ExpiresAt > now,
        HostLoginStates.Failed => now - (login.CompletedAt ?? login.CreatedAt) <= AiRules.HostLoginLifetime,
        _ => false,
    };
}
