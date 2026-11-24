namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.sessions</c> 的一列：一次登入。權杖的明文只在登入時交給呼叫端，這裡只存它的 SHA-256，
/// 資料表外流時拿不到能用的 Cookie。結束的工作階段留一段時間才刪，作廢的原因查問題時看得到。
/// </summary>
internal sealed record SessionRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AccountId { get; init; }

    public required byte[] TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>上一次查到它還有效的時刻。閒置期限從這裡算；查詢結果有快取，所以最多晚快取的時間。</summary>
    public required DateTimeOffset LastUsedAt { get; init; }

    /// <summary>絕對期限：登入加上 <see cref="IdentityRules.SessionAbsoluteDays"/>，不會延長。</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>為什麼作廢，值見 <see cref="Sessions.SessionEndReasons"/>。</summary>
    public string? RevokedReason { get; init; }
}
