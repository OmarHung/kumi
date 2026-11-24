using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.CliLogin;

/// <summary>
/// <see cref="ICliLoginService"/>：檢查與核准在這裡，兌換交給 <see cref="CliLoginRedeemer"/>。
/// 規格見 <c>internal/HANDOFF-setup-wizard.md</c> 第 3 節。
/// </summary>
internal sealed class CliLoginService(
    IDbContextFactory<IdentityDbContext> contexts,
    CliLoginRedeemer redeemer,
    TimeProvider time) : ICliLoginService
{
    /// <summary>過期超過這麼久的碼才刪：用過、過期的留一下，同一個碼再出現時看得出來。</summary>
    private static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(1);

    private static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    private static readonly Failure LimitReached = new(
        FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached,
        $"最多同時有 {AccessTokenRules.MaxActiveTokensPerAccount} 把還能用的權杖，請先撤銷不用的。");

    private static readonly Failure AgentNotAllowed = new(
        FailureKind.Forbidden, IdentityErrorCodes.CliLoginAgentNotAllowed, "組織不允許成員使用自己的 AI 帳號，這次不能建立本機代理的權杖。");

    public Task<Result<CliLoginRequest>> CheckAsync(Actor actor, CliLoginParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return Task.FromResult(CliLoginRequestChecker.Check(parameters));
    }

    public async Task<Result<CliLoginApproval>> ApproveAsync(
        Actor actor, CliLoginParameters parameters, CliLoginGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grant);
        var request = CliLoginRequestChecker.Check(parameters);
        if (request.Failure is { } invalid)
        {
            return invalid;
        }

        var wantsCli = request.Value.Tokens.Contains(CliLoginTokenKind.Cli);
        var wantsAgent = request.Value.Tokens.Contains(CliLoginTokenKind.Agent);
        if (CheckGrant(grant, wantsCli, wantsAgent) is { } denied)
        {
            return denied;
        }

        var includeAgent = wantsAgent && grant.IncludeAgent;
        var (code, hash) = SessionToken.Create();
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var accountId = await ActiveAccountIdOfAsync(db, actor, cancellationToken);
        if (accountId is null)
        {
            return SessionEnded;
        }

        await db.CliLoginCodes
            .Where(c => c.OrganizationId == actor.OrganizationId && c.ExpiresAt < now - ExpiredRetention)
            .ExecuteDeleteAsync(cancellationToken);

        var needed = (wantsCli ? 1 : 0) + (includeAgent ? 1 : 0);
        var active = await AccessTokenWriter.CountActiveAsync(db, actor.OrganizationId, accountId.Value, now, cancellationToken);
        // 兌換時會撤銷最多 MaxReplaceTokens 把舊的：這裡只擋「就算全換掉也一定超過」的，真正的判斷留給兌換。
        if (active - CliLoginRules.MaxReplaceTokens + needed > AccessTokenRules.MaxActiveTokensPerAccount)
        {
            return LimitReached;
        }

        db.CliLoginCodes.Add(new CliLoginCodeRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            AccountId = accountId.Value,
            CodeHash = hash,
            CodeChallenge = parameters.CodeChallenge,
            MachineName = request.Value.MachineName,
            CliScopes = wantsCli ? [.. grant.CliScopes.Select(AccessTokenScopes.NameOf)] : [],
            IncludeAgent = includeAgent,
            AgentSkipped = wantsAgent && !includeAgent,
            ExpiresInDays = grant.ExpiresInDays,
            CreatedAt = now,
            ExpiresAt = now + CliLoginRules.CodeLifetime,
        });
        await db.SaveChangesAsync(cancellationToken);
        return new CliLoginApproval(RedirectUriOf(parameters.Port, code, parameters.State));
    }

    public Task<Result<CliLoginTokens>> RedeemAsync(CliLoginRedemption redemption, CancellationToken cancellationToken) =>
        redeemer.RedeemAsync(redemption, cancellationToken);

    private static Failure? CheckGrant(CliLoginGrant grant, bool wantsCli, bool wantsAgent)
    {
        var scopes = grant.CliScopes ?? [];
        if (CliLoginRequestChecker.CheckScopeList(scopes, wantsCli) is { } message)
        {
            return Detailed(IdentityErrorCodes.CliLoginRequestInvalid, CliLoginRequestChecker.ScopesPath, message);
        }

        if (AccessTokenInput.CheckExpiry(grant.ExpiresInDays) is { } expiry)
        {
            return Detailed(expiry.Code, expiry.Path, expiry.Message);
        }

        return !wantsCli && wantsAgent && !grant.IncludeAgent ? AgentNotAllowed : null;
    }

    private static Failure Detailed(string code, string path, string message) =>
        new(FailureKind.Invalid, code, message) { Details = [new FailureDetail(code, path, message)] };

    private static string RedirectUriOf(int port, string code, string state) =>
        $"http://127.0.0.1:{port}{CliLoginRules.CallbackPath}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";

    private static async Task<Guid?> ActiveAccountIdOfAsync(IdentityDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var ids = await db.Accounts
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId && a.DisabledAt == null)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        return ids.Count == 1 ? ids[0] : null;
    }
}
