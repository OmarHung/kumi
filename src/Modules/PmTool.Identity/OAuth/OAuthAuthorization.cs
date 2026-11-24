using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.OAuth;

/// <summary>驗過的授權請求：用戶端、導回網址與範圍都確定了。</summary>
internal sealed record PreparedAuthorization(
    ResolvedClient Client,
    Uri RedirectUri,
    string CodeChallenge,
    IReadOnlyList<AccessTokenScope> Scopes,
    OAuthServer Server,
    string? State);

/// <summary>授權請求：檢查、允許（發授權碼）、拒絕。順序與規則見 <c>internal/HANDOFF-mcp.md</c> 4.2。</summary>
internal sealed class OAuthAuthorization(
    IDbContextFactory<IdentityDbContext> contexts,
    OAuthClientDirectory clients,
    SessionCache cache,
    TimeProvider time)
{
    public async Task<OAuthResult<PreparedAuthorization>> PrepareAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(parameters);

        // 用戶端與導回網址沒驗過之前，不能導回任何地方：失敗只在同意頁顯示。
        var client = await clients.ResolveAsync(parameters.ClientId, cancellationToken);
        if (client.Failure is { } clientFailure)
        {
            return clientFailure;
        }

        if (!OAuthRedirects.TryParse(parameters.RedirectUri, out var redirect) || !OAuthRedirects.Matches(client.Value.RedirectUris, redirect))
        {
            return new OAuthFailure(OAuthError.InvalidRequest, "導回網址（redirect_uri）不在這個應用登記的網址裡。");
        }

        // 動態註冊的用戶端（沒有網域）只能導回這台電腦；註冊時已經擋過，這裡再檢查一次，不靠資料表裡的資料一定乾淨。
        if (client.Value.Host is null && !OAuthRedirects.IsLoopback(redirect))
        {
            return new OAuthFailure(OAuthError.InvalidRequest, OAuthRedirects.UnverifiedClientMessage);
        }

        if (CheckRest(server, parameters, out var scopes) is { } failure)
        {
            return failure with { RedirectTo = ErrorRedirect(server, redirect, failure.Error, failure.Description, parameters.State) };
        }

        return new PreparedAuthorization(client.Value, redirect, parameters.CodeChallenge!, scopes, server, parameters.State);
    }

    public async Task<OAuthResult<OAuthAuthorizationRequest>> CheckAsync(
        OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(server, parameters, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var request = prepared.Value;
        return new OAuthAuthorizationRequest(
            request.Client.ClientId,
            request.Client.Name,
            request.Client.Host,
            OAuthRedirects.HostOf(request.RedirectUri),
            OAuthRedirects.IsLoopback(request.RedirectUri),
            request.Scopes);
    }

    public async Task<OAuthResult<string>> DenyAsync(OAuthServer server, OAuthAuthorizeParameters parameters, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(server, parameters, cancellationToken);
        return prepared.Failure is { } failure
            ? failure
            : ErrorRedirect(server, prepared.Value.RedirectUri, OAuthError.AccessDenied, "使用者拒絕了授權。", parameters.State);
    }

    public async Task<OAuthResult<string>> ApproveAsync(
        OAuthServer server,
        Actor actor,
        OAuthAuthorizeParameters parameters,
        IReadOnlyList<AccessTokenScope> grantedScopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grantedScopes);
        var prepared = await PrepareAsync(server, parameters, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var request = prepared.Value;
        if (grantedScopes.Count == 0 || grantedScopes.Distinct().Count() != grantedScopes.Count || grantedScopes.Any(s => !request.Scopes.Contains(s)))
        {
            // 勾的範圍不對是同意頁自己的問題，不是用戶端的：不導回。
            return new OAuthFailure(OAuthError.InvalidScope, "至少要勾一個範圍，而且不能超過應用要求的範圍。");
        }

        var issued = await IssueCodeAsync(actor, request, grantedScopes, cancellationToken);
        return issued.Failure is { } denied
            ? denied with { RedirectTo = ErrorRedirect(server, request.RedirectUri, denied.Error, denied.Description, request.State) }
            : OAuthRedirects.Append(
                request.RedirectUri.OriginalString,
                [("code", issued.Value), ("state", request.State), ("iss", server.Issuer)]);
    }

    private static OAuthFailure? CheckRest(OAuthServer server, OAuthAuthorizeParameters p, out IReadOnlyList<AccessTokenScope> scopes)
    {
        scopes = [];
        if (p.ResponseType != "code")
        {
            return Fail(OAuthError.UnsupportedResponseType, "只支援 response_type=code。");
        }

        if (!Pkce.IsValidChallenge(p.CodeChallenge))
        {
            return Fail(OAuthError.InvalidRequest, "缺少 code_challenge，或格式不對（要是 43 到 128 個 base64url 字元）。");
        }

        if (p.CodeChallengeMethod != Pkce.Method)
        {
            return Fail(OAuthError.InvalidRequest, "code_challenge_method 一定要是 S256。");
        }

        if (p.Resource != server.Resource)
        {
            return Fail(OAuthError.InvalidTarget, $"resource 要是這個伺服器的 MCP 端點（{server.Resource}）。");
        }

        scopes = OAuthText.ParseScopes(p.Scope, out var requestedAny);
        if (scopes.Count == 0 && requestedAny)
        {
            return Fail(OAuthError.InvalidScope, "要求的範圍都不認得。");
        }

        return p.State is { Length: > OAuthRules.MaxStateLength }
            ? Fail(OAuthError.InvalidRequest, $"state 不能超過 {OAuthRules.MaxStateLength} 個字。")
            : null;
    }

    private static OAuthFailure Fail(string error, string description) => new(error, description);

    /// <summary>
    /// 帳號要沒停用；鎖住帳號那一列讓同一個人同時的授權排隊，授權數的上限才算得準。
    /// 同一個用戶端再授權時更新原本那一筆（範圍換成這次的）。
    /// </summary>
    private async Task<OAuthResult<string>> IssueCodeAsync(
        Actor actor, PreparedAuthorization request, IReadOnlyList<AccessTokenScope> granted, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var accountId = await LockAccountAsync(db, actor, cancellationToken);
        if (accountId is null)
        {
            return new OAuthFailure(OAuthError.AccessDenied, "登入已經失效，請重新登入。");
        }

        var grant = await UpsertGrantAsync(db, actor.OrganizationId, accountId.Value, request, granted, now, cancellationToken);
        if (grant.Failure is { } failure)
        {
            return failure;
        }

        var (code, hash) = SessionToken.Create();
        db.OAuthCodes.Add(new OAuthCodeRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            GrantId = grant.Value,
            CodeHash = hash,
            RedirectUri = request.RedirectUri.OriginalString,
            CodeChallenge = request.CodeChallenge,
            Scopes = [.. granted.Select(AccessTokenScopes.NameOf)],
            Resource = request.Server.Resource,
            CreatedAt = now,
            ExpiresAt = now + OAuthRules.CodeLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 縮小範圍時撤銷了存取權杖：提交之後清快取，同一個程序裡才立刻失效。
        cache.Forget(accountId.Value);
        await clients.TouchAsync(request.Client.ClientId, cancellationToken);
        return code;
    }

    private static async Task<Guid?> LockAccountAsync(IdentityDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var locked = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM identity.accounts
                WHERE person_id = {actor.PersonId} AND organization_id = {actor.OrganizationId} AND disabled_at IS NULL
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return locked.Count == 1 ? locked[0] : null;
    }

    private static async Task<OAuthResult<Guid>> UpsertGrantAsync(
        IdentityDbContext db,
        Guid organizationId,
        Guid accountId,
        PreparedAuthorization request,
        IReadOnlyList<AccessTokenScope> granted,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var scopes = granted.Select(AccessTokenScopes.NameOf).ToArray();
        var existing = await db.OAuthGrants
            .Where(g => g.OrganizationId == organizationId && g.AccountId == accountId
                && g.ClientId == request.Client.ClientId && g.Resource == request.Server.Resource && g.RevokedAt == null)
            .Select(g => (Guid?)g.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            await db.OAuthGrants
                .Where(g => g.Id == id && g.OrganizationId == organizationId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(g => g.Scopes, scopes)
                        .SetProperty(g => g.ClientName, request.Client.Name)
                        .SetProperty(g => g.ClientHost, request.Client.Host),
                    cancellationToken);
            await OAuthGrantStore.RevokeBeyondScopesAsync(
                db, organizationId, id, scopes, SessionEndReasons.OAuthScopeNarrowed, now, cancellationToken);
            return id;
        }

        var active = await db.OAuthGrants.CountAsync(
            g => g.OrganizationId == organizationId && g.AccountId == accountId && g.RevokedAt == null, cancellationToken);
        if (active >= OAuthRules.MaxGrantsPerAccount)
        {
            return new OAuthFailure(OAuthError.AccessDenied, "授權的應用太多了，先到「我的帳號」撤銷不用的。");
        }

        var grant = new OAuthGrantRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            AccountId = accountId,
            ClientId = request.Client.ClientId,
            ClientName = request.Client.Name,
            ClientHost = request.Client.Host,
            Scopes = scopes,
            Resource = request.Server.Resource,
            CreatedAt = now,
        };
        db.OAuthGrants.Add(grant);
        await db.SaveChangesAsync(cancellationToken);
        return grant.Id;
    }

    /// <summary>RFC 6749 4.1.2.1：帶 error、error_description、state 導回去；RFC 9207 再加 iss。</summary>
    private static string ErrorRedirect(OAuthServer server, Uri redirect, string error, string description, string? state) =>
        OAuthRedirects.Append(
            redirect.OriginalString,
            [("error", error), ("error_description", description), ("state", state is { Length: <= OAuthRules.MaxStateLength } ? state : null), ("iss", server.Issuer)]);
}
