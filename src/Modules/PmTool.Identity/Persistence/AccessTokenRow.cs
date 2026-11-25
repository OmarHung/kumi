namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.access_tokens</c> 的一列：一把個人存取權杖。和工作階段一樣只存權杖的 SHA-256，資料表外流時拿不到能用的權杖。
/// 撤銷或過期的留一段時間才刪，查問題時看得到。
/// </summary>
internal sealed record AccessTokenRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid AccountId { get; init; }

    public required string Name { get; init; }

    public required byte[] TokenHash { get; init; }

    /// <summary>明文的開頭幾個字，畫面上認得出是哪一把。</summary>
    public required string DisplayPrefix { get; init; }

    /// <summary>範圍的對外名稱（<c>items:read</c> 這些）。</summary>
    public required string[] Scopes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>上一次查到它還有效的時刻；查詢結果有快取，所以最多晚快取的時間。</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>null 表示不過期。</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>為什麼撤銷，值見 <see cref="Sessions.SessionEndReasons"/>。</summary>
    public string? RevokedReason { get; init; }

    /// <summary>OAuth 授權發出的權杖才有（<see cref="OAuthGrantRow"/>）：只能用在 MCP，個人存取權杖的清單與上限都不算它。</summary>
    public Guid? GrantId { get; init; }

    /// <summary>用瀏覽器登入（<c>ICliLoginService</c>）建立的權杖才有：kumi 報的電腦名稱。在網頁上自己建的是 null。</summary>
    public string? MachineName { get; init; }

    /// <summary>建立時伺服器看到的位址（走過轉送標頭）；舊的權杖是 null。</summary>
    public string? CreatedFrom { get; init; }
}
