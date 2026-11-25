namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.audit_events</c> 的一列：一件帳號或權限相關的事。只能新增（執行的角色沒有 UPDATE、DELETE、TRUNCATE），
/// 保存 <see cref="AuditRules.RetentionDays"/> 天，遷移時用擁有者的角色清掉更舊的。
/// </summary>
internal sealed record AuditEventRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>值見 <see cref="AuditActions"/>。</summary>
    public required string Action { get; init; }

    /// <summary>值見 <see cref="AuditOutcomes"/>。</summary>
    public required string Outcome { get; init; }

    /// <summary>做這件事的人；登入失敗這類不知道是誰的是 null。</summary>
    public Guid? ActorPersonId { get; init; }

    /// <summary>被做的人（例如被重設密碼的人）；沒有對象的事是 null。</summary>
    public Guid? TargetPersonId { get; init; }

    public string? SourceAddress { get; init; }

    public string? UserAgent { get; init; }

    /// <summary>細項，值一律是字串。</summary>
    public Dictionary<string, string> Details { get; init; } = [];
}
