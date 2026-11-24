namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.oauth_clients</c> 的一列：OAuth 用戶端的資料。兩種來源共用一張表（<see cref="Kind"/>）：
/// 動態註冊的（RFC 7591）與抓過的 Client ID Metadata Document 的快取。
/// 用戶端在有人登入之前就要註冊、要查，這時還不知道是哪個組織，所以這張表不帶 organization_id；
/// 授權（<see cref="OAuthGrantRow"/>）才屬於組織。
/// </summary>
internal sealed record OAuthClientRow
{
    public const string RegisteredKind = "registered";

    public const string MetadataKind = "metadata";

    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    /// <summary>動態註冊的是 <c>pmc_…</c>；Client ID Metadata Document 的是文件的 https 網址。</summary>
    public required string ClientId { get; init; }

    public required string Name { get; init; }

    public required string[] RedirectUris { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>上一次有人用它發出授權碼的時刻；動態註冊的用戶端 90 天沒用就清掉。</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>只有 <see cref="MetadataKind"/>：抓到文件的時刻。</summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>只有 <see cref="MetadataKind"/>：過了這個時刻就要重新抓（抓不到時還可以先用舊的，見 <see cref="OAuth.OAuthClientDirectory"/>）。</summary>
    public DateTimeOffset? CacheExpiresAt { get; init; }
}
