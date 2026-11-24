using Microsoft.EntityFrameworkCore;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.OAuth;

/// <summary>Token 端點與撤銷端點的邏輯：換授權碼、換更新權杖、撤銷。規則見 <c>internal/HANDOFF-mcp.md</c> 4.3。</summary>
internal sealed class OAuthTokenEndpoint(
    IDbContextFactory<IdentityDbContext> contexts,
    SessionCache cache,
    TimeProvider time)
{
    private static readonly OAuthFailure BadCode = new(OAuthError.InvalidGrant, "授權碼不對、已經過期，或已經用過了。");

    private static readonly OAuthFailure BadRefresh = new(OAuthError.InvalidGrant, "更新權杖不對、已經過期或已經失效，請重新授權。");

    private static readonly OAuthFailure WrongResource = new(OAuthError.InvalidTarget, "resource 和授權時的不一致。");

    private sealed record CodeContext(OAuthCodeRow Code, OAuthGrantRow Grant, bool AccountDisabled);

    private sealed record RefreshContext(OAuthRefreshTokenRow Token, OAuthGrantRow Grant, bool AccountDisabled);

    public async Task<OAuthResult<OAuthTokens>> ExchangeCodeAsync(OAuthCodeExchange request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OAuthText.IsUsable(request.ClientId, OAuthRules.MaxRedirectUriLength) || !OAuthText.IsUsable(request.Code, 200)
            || !OAuthText.IsUsable(request.RedirectUri, OAuthRules.MaxRedirectUriLength) || request.CodeVerifier is null)
        {
            return new OAuthFailure(OAuthError.InvalidRequest, "缺少 code、client_id、redirect_uri 或 code_verifier。");
        }

        if (!SessionToken.TryHash(request.Code, out var codeHash))
        {
            return BadCode;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await FindCodeAsync(db, codeHash, cancellationToken);
        if (context is null)
        {
            return BadCode;
        }

        var now = time.GetUtcNow();
        if (context.Code.UsedAt is not null)
        {
            await RevokeCodeReuseAsync(db, context, now, cancellationToken);
            return BadCode;
        }

        if (CheckCode(context, request, now) is { } failure)
        {
            return failure;
        }

        return await RedeemAsync(db, context, now, cancellationToken);
    }

    public async Task<OAuthResult<OAuthTokens>> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OAuthText.IsUsable(request.ClientId, OAuthRules.MaxRedirectUriLength) || string.IsNullOrEmpty(request.RefreshToken))
        {
            return new OAuthFailure(OAuthError.InvalidRequest, "缺少 refresh_token 或 client_id。");
        }

        var hash = OAuthTokenIssuer.HashRefreshToken(request.RefreshToken);
        if (hash is null)
        {
            return BadRefresh;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await FindRefreshAsync(db, hash, cancellationToken);
        if (context is null)
        {
            return BadRefresh;
        }

        var now = time.GetUtcNow();
        if (context.Token.UsedAt is not null && context.Token.RevokedAt is null)
        {
            // 用過的更新權杖又出現：不知道是誰拿的，整筆授權作廢，使用者要重新授權。
            await RevokeGrantAsync(db, context.Grant, SessionEndReasons.OAuthReuseDetected, now, cancellationToken);
            return BadRefresh;
        }

        var scopes = CheckRefresh(context, request, now, out var failure);
        return failure is not null ? failure : await RotateAsync(db, context, scopes, now, cancellationToken);
    }

    public async Task RevokeTokenAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        if (AccessTokenSecret.TryHash(token, out var accessHash))
        {
            var found = await db.AccessTokens.AsNoTracking()
                .Where(t => t.TokenHash == accessHash && t.GrantId != null).SingleOrDefaultAsync(cancellationToken);
            if (found is not null)
            {
                await OAuthGrantStore.RevokeAccessTokenAsync(db, found.OrganizationId, found.Id, SessionEndReasons.TokenRevoked, now, cancellationToken);
                cache.Forget(found.AccountId);
            }

            return;
        }

        if (OAuthTokenIssuer.HashRefreshToken(token) is { } refreshHash)
        {
            var context = await FindRefreshAsync(db, refreshHash, cancellationToken);
            if (context is not null)
            {
                await RevokeGrantAsync(db, context.Grant, SessionEndReasons.OAuthGrantRevoked, now, cancellationToken);
            }
        }
    }

    private static async Task<CodeContext?> FindCodeAsync(IdentityDbContext db, byte[] hash, CancellationToken cancellationToken)
    {
        var found = await (from code in db.OAuthCodes
                           join grant in db.OAuthGrants on code.GrantId equals grant.Id
                           join account in db.Accounts on grant.AccountId equals account.Id
                           where code.CodeHash == hash
                           select new { code, grant, disabled = account.DisabledAt != null })
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return found is null ? null : new CodeContext(found.code, found.grant, found.disabled);
    }

    private static async Task<RefreshContext?> FindRefreshAsync(IdentityDbContext db, byte[] hash, CancellationToken cancellationToken)
    {
        var found = await (from token in db.OAuthRefreshTokens
                           join grant in db.OAuthGrants on token.GrantId equals grant.Id
                           join account in db.Accounts on grant.AccountId equals account.Id
                           where token.TokenHash == hash
                           select new { token, grant, disabled = account.DisabledAt != null })
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return found is null ? null : new RefreshContext(found.token, found.grant, found.disabled);
    }

    /// <summary>
    /// 授權碼對得上：沒過期、授權還有效、用戶端與導回網址與授權時一樣、PKCE 對。
    /// 不對時不把碼作廢：PKCE 對不上的人拿不到權杖，也不能靠亂送讓真正的用戶端換不到。
    /// </summary>
    private static OAuthFailure? CheckCode(CodeContext context, OAuthCodeExchange request, DateTimeOffset now)
    {
        var (code, grant, accountDisabled) = context;
        if (now >= code.ExpiresAt || grant.RevokedAt is not null || accountDisabled)
        {
            return BadCode;
        }

        if (!string.Equals(grant.ClientId, request.ClientId, StringComparison.Ordinal)
            || !string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
        {
            return BadCode;
        }

        // RFC 8707：沒帶 resource 就是授權時的那個；帶了就要一樣。
        if (request.Resource is not null && !string.Equals(code.Resource, request.Resource, StringComparison.Ordinal))
        {
            return WrongResource;
        }

        return Pkce.IsValidVerifier(request.CodeVerifier) && Pkce.Verify(request.CodeVerifier!, code.CodeChallenge) ? null : BadCode;
    }

    private async Task<OAuthResult<OAuthTokens>> RedeemAsync(
        IdentityDbContext db, CodeContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var familyId = Guid.CreateVersion7();

        // 授權與帳號的狀態在交易外讀過一次，那之後可能被撤銷；鎖住授權再查一次才發，見 LockActiveAsync。
        if (!await OAuthGrantStore.LockActiveAsync(db, context.Grant.OrganizationId, context.Grant.Id, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadCode;
        }

        // 授權碼發出之後授權可能縮小過範圍：只發兩邊都有的，沒有交集就不發。
        var scopes = await WithinGrantAsync(db, context.Grant, OAuthText.ScopesFromNames(context.Code.Scopes), cancellationToken);
        if (scopes.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadCode;
        }

        // 條件寫進更新本身：同時來的兩個請求只有一個搶得到，另一個當作重複使用。
        var marked = await db.OAuthCodes
            .Where(c => c.Id == context.Code.Id && c.OrganizationId == context.Code.OrganizationId && c.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.UsedAt, (DateTimeOffset?)now)
                    .SetProperty(c => c.IssuedFamilyId, (Guid?)familyId),
                cancellationToken);
        if (marked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeGrantAsync(db, context.Grant, SessionEndReasons.OAuthReuseDetected, now, cancellationToken);
            return BadCode;
        }

        var tokens = await OAuthTokenIssuer.IssueAsync(
            db, context.Grant, scopes, familyId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tokens;
    }

    /// <summary>授權碼用過又出現：撤銷它當時發出的權杖（整串更新權杖與存取權杖），授權本身留著。</summary>
    private async Task RevokeCodeReuseAsync(IdentityDbContext db, CodeContext context, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (context.Code.IssuedFamilyId is { } familyId)
        {
            await OAuthGrantStore.RevokeFamilyAsync(
                db, context.Code.OrganizationId, familyId, SessionEndReasons.OAuthReuseDetected, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        cache.Forget(context.Grant.AccountId);
    }

    /// <summary>更新權杖對得上：沒過期、沒撤銷、授權還有效、用戶端與 resource 一樣；scope 只能縮小。</summary>
    private static IReadOnlyList<AccessTokenScope> CheckRefresh(
        RefreshContext context, OAuthRefreshRequest request, DateTimeOffset now, out OAuthFailure? failure)
    {
        var (token, grant, accountDisabled) = context;
        failure = null;
        var granted = OAuthText.ScopesFromNames(grant.Scopes);
        if (token.RevokedAt is not null || now >= token.ExpiresAt || grant.RevokedAt is not null || accountDisabled
            || !string.Equals(grant.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            failure = BadRefresh;
            return granted;
        }

        if (request.Resource is not null && !string.Equals(grant.Resource, request.Resource, StringComparison.Ordinal))
        {
            failure = WrongResource;
            return granted;
        }

        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            return granted;
        }

        var narrowed = OAuthText.ParseScopes(request.Scope, out _);
        var unknown = request.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != narrowed.Count;
        if (narrowed.Count == 0 || unknown || narrowed.Any(scope => !granted.Contains(scope)))
        {
            failure = new OAuthFailure(OAuthError.InvalidScope, "scope 只能是授權範圍的子集。");
            return granted;
        }

        return narrowed;
    }

    private async Task<OAuthResult<OAuthTokens>> RotateAsync(
        IdentityDbContext db, RefreshContext context, IReadOnlyList<AccessTokenScope> scopes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await OAuthGrantStore.LockActiveAsync(db, context.Grant.OrganizationId, context.Grant.Id, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRefresh;
        }

        var issuable = await WithinGrantAsync(db, context.Grant, scopes, cancellationToken);
        if (issuable.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRefresh;
        }

        var marked = await db.OAuthRefreshTokens
            .Where(t => t.Id == context.Token.Id && t.OrganizationId == context.Token.OrganizationId && t.UsedAt == null && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.UsedAt, (DateTimeOffset?)now), cancellationToken);
        if (marked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            await RevokeGrantAsync(db, context.Grant, SessionEndReasons.OAuthReuseDetected, now, cancellationToken);
            return BadRefresh;
        }

        var tokens = await OAuthTokenIssuer.IssueAsync(db, context.Grant, issuable, context.Token.FamilyId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tokens;
    }

    /// <summary>要發的範圍，限在授權「現在」的範圍之內（交易外讀到的授權可能剛被縮小）。要在鎖住授權之後呼叫。</summary>
    private static async Task<IReadOnlyList<AccessTokenScope>> WithinGrantAsync(
        IdentityDbContext db, OAuthGrantRow grant, IReadOnlyList<AccessTokenScope> wanted, CancellationToken cancellationToken)
    {
        var current = await OAuthGrantStore.CurrentScopesAsync(db, grant.OrganizationId, grant.Id, cancellationToken);
        return [.. wanted.Where(current.Contains)];
    }

    private async Task RevokeGrantAsync(IdentityDbContext db, OAuthGrantRow grant, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await OAuthGrantStore.RevokeGrantAsync(db, grant.OrganizationId, grant.Id, reason, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 提交之後才清：在這之前開始的查詢可能還讀到「有效」。
        cache.Forget(grant.AccountId);
    }
}
