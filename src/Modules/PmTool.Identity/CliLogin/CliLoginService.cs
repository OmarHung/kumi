using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Audit;
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
    AuditTrail audit,
    ICurrentSession currentSession,
    TimeProvider time) : ICliLoginService
{
    /// <summary>過期超過這麼久的碼才刪：用過、過期的留一下，同一個碼再出現時看得出來。</summary>
    private static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(1);

    /// <summary>細項的路徑用 HTTP 內文的屬性名稱。</summary>
    public const string ConfirmationCodePath = "confirmationCode";

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

        if (!CliLoginRules.ConfirmationCodeMatches(parameters.CodeChallenge, grant.ConfirmationCode))
        {
            await RecordMismatchAsync(actor, request.Value.MachineName, cancellationToken);
            return Detailed(
                IdentityErrorCodes.CliLoginConfirmationMismatch, ConfirmationCodePath,
                "確認碼不對。請輸入終端機上顯示的確認碼；如果你沒有在自己的終端機執行 kumi，請按「拒絕」。");
        }

        var (code, hash) = SessionToken.Create();
        var stored = await StoreCodeAsync(actor, parameters, request.Value, grant, hash, cancellationToken);
        return stored is { } failure
            ? failure
            : new CliLoginApproval(RedirectUriOf(parameters.Port, code, parameters.State));
    }

    /// <summary>存下登入碼；失敗回 <see cref="Failure"/>，成功回 null。整個在一個交易裡，先鎖帳號、再確認呼叫的工作階段。</summary>
    private async Task<Failure?> StoreCodeAsync(
        Actor actor, CliLoginParameters parameters, CliLoginRequest request, CliLoginGrant grant, byte[] codeHash, CancellationToken cancellationToken)
    {
        var wantsCli = request.Tokens.Contains(CliLoginTokenKind.Cli);
        var includeAgent = request.Tokens.Contains(CliLoginTokenKind.Agent) && grant.IncludeAgent;
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var accountId = await ActiveAccountIdOfAsync(db, actor, cancellationToken);
        if (accountId is null)
        {
            return SessionEnded;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 先對帳號那一列加共享鎖、再確認呼叫的工作階段：改密碼與停用先提交的話，這裡一定看得到，不會替已經作廢的工作階段存下登入碼。
        if (!await AccessTokenWriter.LockAccountSharedAsync(db, actor.OrganizationId, accountId.Value, cancellationToken)
            || !await SessionStore.IsCallerSessionActiveAsync(db, actor.OrganizationId, accountId.Value, currentSession.Token, now, cancellationToken))
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

        db.CliLoginCodes.Add(NewCodeRow(actor, accountId.Value, parameters, request, grant, includeAgent, codeHash, now));
        await audit.AddAsync(db, ApprovedRecord(actor, request, grant), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private static CliLoginCodeRow NewCodeRow(
        Actor actor, Guid accountId, CliLoginParameters parameters, CliLoginRequest request, CliLoginGrant grant,
        bool includeAgent, byte[] codeHash, DateTimeOffset now)
    {
        var wantsCli = request.Tokens.Contains(CliLoginTokenKind.Cli);
        return new CliLoginCodeRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            AccountId = accountId,
            CodeHash = codeHash,
            CodeChallenge = parameters.CodeChallenge,
            MachineName = request.MachineName,
            CliScopes = wantsCli ? [.. grant.CliScopes.Select(AccessTokenScopes.NameOf)] : [],
            IncludeAgent = includeAgent,
            AgentSkipped = request.Tokens.Contains(CliLoginTokenKind.Agent) && !includeAgent,
            ExpiresInDays = grant.ExpiresInDays,
            OriginKey = grant.Origin,
            CreatedAt = now,
            ExpiresAt = now + CliLoginRules.CodeLifetime,
        };
    }

    private static AuditRecord ApprovedRecord(Actor actor, CliLoginRequest request, CliLoginGrant grant) =>
        new(actor.OrganizationId, AuditActions.CliLoginApproved)
        {
            ActorPersonId = actor.PersonId,
            TargetPersonId = actor.PersonId,
            Details = new Dictionary<string, string>
            {
                ["machine"] = request.MachineName,
                ["tokens"] = string.Join(' ', request.Tokens.Select(kind => kind.ToString().ToLowerInvariant())),
                ["scopes"] = string.Join(' ', grant.CliScopes.Select(AccessTokenScopes.NameOf)),
            },
        };

    /// <summary>確認碼不符也留一筆：有人拿著別人的網址一直試，本人在稽核紀錄裡看得到。</summary>
    private async Task RecordMismatchAsync(Actor actor, string machineName, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await audit.AddAsync(
            db,
            new AuditRecord(actor.OrganizationId, AuditActions.CliLoginApproved, AuditOutcomes.Failed)
            {
                ActorPersonId = actor.PersonId,
                TargetPersonId = actor.PersonId,
                Details = new Dictionary<string, string> { ["reason"] = "confirmation_mismatch", ["machine"] = machineName },
            },
            cancellationToken);
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

        if (CheckExpiry(grant.ExpiresInDays) is { } expiry)
        {
            return Detailed(expiry.Code, expiry.Path, expiry.Message);
        }

        if (!CliLoginOrigin.IsValid(grant.Origin))
        {
            return Detailed(IdentityErrorCodes.CliLoginRequestInvalid, "origin", "請求的來源不對。");
        }

        return !wantsCli && wantsAgent && !grant.IncludeAgent ? AgentNotAllowed : null;
    }

    private static FailureDetail? CheckExpiry(int? days) => AccessTokenInput.CheckExpiry(days);

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
