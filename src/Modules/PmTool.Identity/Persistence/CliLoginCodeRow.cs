namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.cli_login_codes</c> 的一列：命令列瀏覽器登入的一次性碼。只存 SHA-256；
/// 用過的留到過期一天後才刪，同一個碼再出現時才認得出「用過了」。
/// </summary>
internal sealed record CliLoginCodeRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AccountId { get; init; }

    public required byte[] CodeHash { get; init; }

    public required string CodeChallenge { get; init; }

    public required string MachineName { get; init; }

    /// <summary>命令列的權杖要的範圍；空的表示不建命令列的權杖（只要本機代理的）。</summary>
    public required string[] CliScopes { get; init; }

    public required bool IncludeAgent { get; init; }

    /// <summary>請求有 agent，但核准時組織不允許，不建本機代理的權杖。</summary>
    public required bool AgentSkipped { get; init; }

    /// <summary>null 表示不過期。</summary>
    public int? ExpiresInDays { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }
}
