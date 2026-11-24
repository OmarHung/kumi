using Microsoft.EntityFrameworkCore;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.OAuth;

/// <summary>
/// 發一組權杖：一把存取權杖（<c>access_tokens</c> 的一列，帶 <c>grant_id</c>，和個人存取權杖同一套驗證與快取）
/// 加一把更新權杖。呼叫端在同一個交易裡呼叫並提交。
/// </summary>
internal static class OAuthTokenIssuer
{
    public static async Task<OAuthTokens> IssueAsync(
        IdentityDbContext db,
        OAuthGrantRow grant,
        IReadOnlyList<AccessTokenScope> scopes,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (accessSecret, accessHash) = AccessTokenSecret.Create();
        var (refreshBody, refreshHash) = SessionToken.Create();
        var accessId = Guid.CreateVersion7();

        db.AccessTokens.Add(new AccessTokenRow
        {
            Id = accessId,
            OrganizationId = grant.OrganizationId,
            AccountId = grant.AccountId,
            Name = grant.ClientName.Length > AccessTokenRules.MaxNameLength ? grant.ClientName[..AccessTokenRules.MaxNameLength] : grant.ClientName,
            TokenHash = accessHash,
            DisplayPrefix = accessSecret[..AccessTokenSecret.DisplayPrefixLength],
            Scopes = [.. scopes.Select(AccessTokenScopes.NameOf)],
            CreatedAt = now,
            ExpiresAt = now + OAuthRules.AccessTokenLifetime,
            GrantId = grant.Id,
        });
        db.OAuthRefreshTokens.Add(new OAuthRefreshTokenRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = grant.OrganizationId,
            GrantId = grant.Id,
            FamilyId = familyId,
            AccessTokenId = accessId,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now + OAuthRules.RefreshTokenLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        await OAuthGrantStore.TouchAsync(db, grant.OrganizationId, grant.Id, now, cancellationToken);
        await OAuthGrantStore.DeleteEndedAsync(db, grant.OrganizationId, now, cancellationToken);

        return new OAuthTokens(
            accessSecret,
            (int)OAuthRules.AccessTokenLifetime.TotalSeconds,
            OAuthRules.RefreshTokenPrefix + refreshBody,
            OAuthText.FormatScopes(scopes));
    }

    /// <summary>更新權杖：前綴與內容不對就回 null，不送進資料庫。</summary>
    public static byte[]? HashRefreshToken(string? token)
    {
        if (token is null || !token.StartsWith(OAuthRules.RefreshTokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return SessionToken.TryHash(token[OAuthRules.RefreshTokenPrefix.Length..], out var hash) ? hash : null;
    }
}
