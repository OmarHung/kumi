namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.oauth_refresh_tokens</c> 的一列：更新權杖，只存 SHA-256。每次換新都留舊的、標成用掉：
/// 用掉的再出現就是外流，撤銷整筆授權，所以用掉的要留到它自己過期。
/// </summary>
internal sealed record OAuthRefreshTokenRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid GrantId { get; init; }

    /// <summary>從同一個授權碼一路換下來的更新權杖共用同一個編號；授權碼被重複使用時一次撤銷整個家族。</summary>
    public required Guid FamilyId { get; init; }

    /// <summary>和它同時發出的存取權杖（<c>access_tokens</c> 的一列）。不設外鍵：存取權杖過期後會被清掉。</summary>
    public required Guid AccessTokenId { get; init; }

    public required byte[] TokenHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }
}
