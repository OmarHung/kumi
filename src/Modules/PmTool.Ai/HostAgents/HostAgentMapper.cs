using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>資料列轉成對外的型別。</summary>
internal static class HostAgentMapper
{
    public static HostAgent ToAgent(
        HostAgentRow row,
        IReadOnlyDictionary<(Guid AgentId, string Engine), HostAuthorization> auths,
        IEnumerable<HostLoginRow> logins,
        DateTimeOffset now,
        IReadOnlySet<Guid> currentOwners) => new(
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
        row.CreatedAt)
    {
        CredentialExpiresAt = row.State == HostAgentStates.AwaitingPairing ? null : row.CredentialExpiresAt,
        Warning = WarningOf(row, now, currentOwners),
    };

    /// <summary>
    /// 三種都讓機器憑證不能用，重新配對後清掉；同時成立時先說最需要動作的原因（配對的人不是擁有者，
    /// 重新配對要換人）。還沒配對的代理沒有憑證，不顯示。
    /// </summary>
    private static HostAgentWarning? WarningOf(HostAgentRow row, DateTimeOffset now, IReadOnlySet<Guid> currentOwners)
    {
        if (row.State == HostAgentStates.AwaitingPairing || row.CredentialHash is null)
        {
            return null;
        }

        if (row.PairingStartedBy is not { } pairedBy || !currentOwners.Contains(pairedBy))
        {
            return new HostAgentWarning(HostAgentWarningKind.PairedByFormerOwner, null, row.UpdatedAt);
        }

        if (row.SuspectSeenAt is { } seen)
        {
            return new HostAgentWarning(HostAgentWarningKind.OtherMachine, row.SuspectMachineName, seen);
        }

        return row.CredentialExpiresAt is { } expires && expires <= now
            ? new HostAgentWarning(HostAgentWarningKind.CredentialExpired, null, expires)
            : null;
    }

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
