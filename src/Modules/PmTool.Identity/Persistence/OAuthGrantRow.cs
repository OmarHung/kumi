namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.oauth_grants</c> 的一列：一個人授權一個用戶端使用他的帳號。
/// 同一個帳號、同一個用戶端、同一個 resource 只有一筆還沒撤銷的（資料表的唯一索引），再授權一次就更新它。
/// 撤銷的留著，查問題時看得到。
/// </summary>
internal sealed record OAuthGrantRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AccountId { get; init; }

    public required string ClientId { get; init; }

    /// <summary>授權當時用戶端報的名稱；用戶端之後改名不影響已經授權的紀錄。</summary>
    public required string ClientName { get; init; }

    /// <summary>Client ID Metadata Document 的網域；動態註冊的是 null。</summary>
    public string? ClientHost { get; init; }

    /// <summary>範圍的對外名稱（<c>items:read</c> 這些）。</summary>
    public required string[] Scopes { get; init; }

    public required string Resource { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? RevokedReason { get; init; }
}
