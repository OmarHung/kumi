using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.OAuth;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity.CliLogin;

/// <summary>
/// 兌換一次性的碼。順序刻意是「先燒掉碼、再驗 PKCE」：猜錯 verifier 的人拿不到第二次機會。
/// 燒碼是自己的一個陳述式（不在後面的交易裡），後面不管怎麼失敗碼都已經算用過。
/// </summary>
internal sealed class CliLoginRedeemer(
    IDbContextFactory<IdentityDbContext> contexts,
    SessionCache cache,
    TimeProvider time)
{
    private static readonly Failure CodeInvalid = new(FailureKind.Invalid, IdentityErrorCodes.CliLoginCodeInvalid, "登入的碼不對或已經失效，請重新執行登入。");

    private static readonly Failure LimitReached = new(
        FailureKind.Conflict, IdentityErrorCodes.AccessTokenLimitReached,
        $"最多同時有 {AccessTokenRules.MaxActiveTokensPerAccount} 把還能用的權杖，請先撤銷不用的。");

    public async Task<Result<CliLoginTokens>> RedeemAsync(CliLoginRedemption redemption, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(redemption);
        if (redemption.Code is null || !SessionToken.TryHash(redemption.Code, out var codeHash))
        {
            return CodeInvalid;
        }

        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var burned = await db.CliLoginCodes
            .Where(c => c.CodeHash == codeHash && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.UsedAt, (DateTimeOffset?)now), cancellationToken);
        if (burned != 1)
        {
            return CodeInvalid;
        }

        var row = await db.CliLoginCodes.AsNoTracking().SingleAsync(c => c.CodeHash == codeHash, cancellationToken);
        // 來源不符和 verifier 不對一樣：碼已經燒掉，本人也不能再用同一個碼重試。碼只換給和核准時同一個來源鍵的請求；同一個對外位址後面的電腦分不出來。
        if (!CliLoginOrigin.IsValid(redemption.Origin) || redemption.Origin != row.OriginKey)
        {
            return CodeInvalid;
        }

        if (!Pkce.IsValidVerifier(redemption.CodeVerifier) || !Pkce.Verify(redemption.CodeVerifier, row.CodeChallenge))
        {
            return CodeInvalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AccessTokenWriter.LockAccountAsync(db, row.OrganizationId, row.AccountId, cancellationToken))
        {
            return CodeInvalid;
        }

        await RevokeReplacedAsync(db, row, redemption.ReplaceTokens ?? [], now, cancellationToken);
        var created = await CreateTokensAsync(db, row, now, cancellationToken);
        if (created.Failure is { } failure)
        {
            return failure;
        }

        var account = await ReadAccountAsync(db, row, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 提交之後才清快取：撤銷掉的舊權杖在同一個程序裡立刻失效。
        cache.Forget(row.AccountId);
        return new CliLoginTokens(account, created.Value.Cli, created.Value.Agent, row.AgentSkipped);
    }

    /// <summary>只撤銷同一個帳號、自己建的（不是 OAuth 發的）、還能用的；別的一律略過，不回錯誤也不透露。</summary>
    private static async Task RevokeReplacedAsync(
        IdentityDbContext db, CliLoginCodeRow row, IReadOnlyList<string> plaintexts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hashes = new List<byte[]>();
        foreach (var plaintext in plaintexts.Take(CliLoginRules.MaxReplaceTokens))
        {
            if (plaintext is not null && AccessTokenSecret.TryHash(plaintext, out var hash))
            {
                hashes.Add(hash);
            }
        }

        if (hashes.Count == 0)
        {
            return;
        }

        await db.AccessTokens
            .Where(t => t.OrganizationId == row.OrganizationId && t.AccountId == row.AccountId && t.GrantId == null
                && t.RevokedAt == null && (t.ExpiresAt == null || t.ExpiresAt > now) && hashes.Contains(t.TokenHash))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(t => t.RevokedReason, SessionEndReasons.TokenRevoked),
                cancellationToken);
    }

    private static async Task<Result<(CreatedAccessToken? Cli, CreatedAccessToken? Agent)>> CreateTokensAsync(
        IdentityDbContext db, CliLoginCodeRow row, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var needed = (row.CliScopes.Length > 0 ? 1 : 0) + (row.IncludeAgent ? 1 : 0);
        await AccessTokenStore.DeleteEndedAsync(db, row.OrganizationId, now, cancellationToken);
        var active = await AccessTokenWriter.CountActiveAsync(db, row.OrganizationId, row.AccountId, now, cancellationToken);
        if (active + needed > AccessTokenRules.MaxActiveTokensPerAccount)
        {
            return LimitReached;
        }

        CreatedAccessToken? cli = null;
        CreatedAccessToken? agent = null;
        if (row.CliScopes.Length > 0)
        {
            var scopes = row.CliScopes.Select(name => AccessTokenScopes.TryParse(name, out var scope) ? scope : (AccessTokenScope?)null).OfType<AccessTokenScope>().ToArray();
            cli = Add(db, row, CliLoginRules.CliTokenName(row.MachineName), scopes, row.ExpiresInDays, now);
        }

        if (row.IncludeAgent)
        {
            agent = Add(db, row, CliLoginRules.AgentTokenName(row.MachineName), CliLoginRules.AgentScopes, CliLoginRules.AgentTokenLifetimeDays, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (cli, agent);
    }

    private static CreatedAccessToken Add(
        IdentityDbContext db, CliLoginCodeRow row, string name, IReadOnlyList<AccessTokenScope> scopes, int? expiresInDays, DateTimeOffset now)
    {
        var (token, secret) = AccessTokenWriter.NewRow(row.OrganizationId, row.AccountId, name, scopes, expiresInDays, row.MachineName, now);
        db.AccessTokens.Add(token);
        return new CreatedAccessToken(AccessTokenService.Summarize(token), secret);
    }

    private static async Task<CliLoginAccount> ReadAccountAsync(IdentityDbContext db, CliLoginCodeRow row, CancellationToken cancellationToken) =>
        await (from account in db.Accounts
               join person in db.People on account.PersonId equals person.Id
               join organization in db.Organizations on person.OrganizationId equals organization.Id
               where account.Id == row.AccountId
               select new CliLoginAccount(person.DisplayName, account.Email, organization.Name))
            .AsNoTracking()
            .SingleAsync(cancellationToken);
}
