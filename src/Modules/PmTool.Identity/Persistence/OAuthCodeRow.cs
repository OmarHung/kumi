namespace PmTool.Identity.Persistence;

/// <summary>
/// <c>identity.oauth_codes</c> 的一列：一次性的授權碼。只存 SHA-256。用過的留一陣子（不立刻刪），
/// 這樣同一個碼再出現時才認得出「用過了」，並撤銷它發出去的權杖。
/// </summary>
internal sealed record OAuthCodeRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid GrantId { get; init; }

    public required byte[] CodeHash { get; init; }

    /// <summary>授權請求時的導回網址，換權杖時要完全相同。</summary>
    public required string RedirectUri { get; init; }

    public required string CodeChallenge { get; init; }

    /// <summary>這一次使用者勾的範圍（可能比授權原有的窄）。</summary>
    public required string[] Scopes { get; init; }

    public required string Resource { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }

    /// <summary>用掉時發出的更新權杖家族（<see cref="OAuthRefreshTokenRow.FamilyId"/>）；碼再出現時撤銷整個家族。</summary>
    public Guid? IssuedFamilyId { get; init; }
}
