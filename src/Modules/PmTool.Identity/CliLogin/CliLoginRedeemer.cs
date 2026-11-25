using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Audit;
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
    AuditTrail audit,
    AccountEvents events,
    IRequestSource requestSource,
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
            await RevokeIfReusedAsync(db, codeHash, redemption.Origin, now, cancellationToken);
            return CodeInvalid;
        }

        var row = await db.CliLoginCodes.AsNoTracking().SingleAsync(c => c.CodeHash == codeHash, cancellationToken);
        // 來源不符和 verifier 不對一樣：碼已經燒掉，本人也不能再用同一個碼重試。碼只換給和核准時同一個來源鍵的請求；比對的只有來源鍵，不看是哪一台電腦。
        if (!CliLoginOrigin.IsValid(redemption.Origin) || redemption.Origin != row.OriginKey)
        {
            await RecordFailureAsync(db, row, "origin_mismatch", cancellationToken);
            return CodeInvalid;
        }

        if (!Pkce.IsValidVerifier(redemption.CodeVerifier) || !Pkce.Verify(redemption.CodeVerifier, row.CodeChallenge))
        {
            await RecordFailureAsync(db, row, "verifier_mismatch", cancellationToken);
            return CodeInvalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AccessTokenWriter.LockAccountAsync(db, row.OrganizationId, row.AccountId, cancellationToken)
            || !await IsStillRedeemableAsync(db, row.Id, now, cancellationToken))
        {
            return CodeInvalid;
        }

        await RevokeReplacedAsync(db, row, redemption.ReplaceTokens ?? [], now, cancellationToken);
        var created = await CreateTokensAsync(db, row, requestSource.Current?.Address, now, cancellationToken);
        if (created.Failure is { } failure)
        {
            return failure;
        }

        var account = await ReadAccountAsync(db, row, cancellationToken);
        var issued = new[] { created.Value.Cli, created.Value.Agent }.OfType<CreatedAccessToken>().ToList();
        await db.CliLoginCodes
            .Where(c => c.Id == row.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IssuedTokenIds, issued.Select(t => t.Token.TokenId).ToArray()), cancellationToken);
        var personId = await PersonIdOfAsync(db, row, cancellationToken);
        var eventId = await audit.AddAsync(
            db,
            new AuditRecord(row.OrganizationId, AuditActions.CliLoginRedeemed)
            {
                ActorPersonId = personId,
                TargetPersonId = personId,
                Details = new Dictionary<string, string> { ["machine"] = row.MachineName, ["tokens"] = string.Join(", ", issued.Select(t => t.Token.Name)) },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 提交之後才清快取：撤銷掉的舊權杖在同一個程序裡立刻失效。
        cache.Forget(row.AccountId);
        await PublishIssuedAsync(row, personId, eventId, issued, now);
        return new CliLoginTokens(account, created.Value.Cli, created.Value.Agent, row.AgentSkipped);
    }

    /// <summary>
    /// 拿到帳號的鎖之後重讀期限：碼是先燒掉的，等鎖的時候帳號可能剛改了密碼、重設或停用，
    /// 那些操作會把還沒發出權杖的碼的期限改成最早的時刻，這裡才認得出來、不再發權杖。
    /// </summary>
    private static Task<bool> IsStillRedeemableAsync(IdentityDbContext db, Guid codeId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.CliLoginCodes.AsNoTracking().AnyAsync(c => c.Id == codeId && c.ExpiresAt > now, cancellationToken);

    private Task PublishIssuedAsync(CliLoginCodeRow row, Guid? personId, Guid eventId, IReadOnlyList<CreatedAccessToken> issued, DateTimeOffset now) =>
        personId is { } person
            ? events.PublishAsync(new AccountEvent(eventId, row.OrganizationId, person, AccountEventKind.CliLoginTokensIssued, now)
            {
                Tokens = [.. issued.Select(t => new AccountEventToken(t.Token.Name, t.Token.Scopes, t.Token.ExpiresAt))],
                SourceAddress = requestSource.Current?.Address,
                MachineName = row.MachineName,
            })
            : Task.CompletedTask;

    /// <summary>
    /// 碼已經用過、卻又被拿來兌換：當作外流，撤銷它發出去的權杖。清空編號的條件更新讓同時來的幾個請求只有一個處理，
    /// 第三次再出現時沒有東西可撤，也不會重複通知。對外照舊是「碼不對」。
    /// 只有重放的來源和核准時的來源鍵相同才撤銷：碼會留在瀏覽紀錄裡，別的來源拿到碼重放，不能因此撤掉本人剛換到的權杖。
    /// </summary>
    private async Task RevokeIfReusedAsync(
        IdentityDbContext db, byte[] codeHash, string? origin, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var used = await db.CliLoginCodes.AsNoTracking()
            .Where(c => c.CodeHash == codeHash && c.UsedAt != null)
            .SingleOrDefaultAsync(cancellationToken);
        if (used is null || used.IssuedTokenIds.Length == 0 || !CliLoginOrigin.IsValid(origin) || origin != used.OriginKey)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cleared = await db.CliLoginCodes
            .Where(c => c.Id == used.Id && c.IssuedTokenIds.Length > 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IssuedTokenIds, Array.Empty<Guid>()), cancellationToken);
        if (cleared != 1)
        {
            return;
        }

        var ids = used.IssuedTokenIds;
        var revoked = await db.AccessTokens
            .Where(t => t.OrganizationId == used.OrganizationId && ids.Contains(t.Id) && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAt, (DateTimeOffset?)now)
                    .SetProperty(t => t.RevokedReason, SessionEndReasons.CliLoginCodeReused),
                cancellationToken);
        var personId = await PersonIdOfAsync(db, used, cancellationToken);
        var eventId = await audit.AddAsync(
            db,
            new AuditRecord(used.OrganizationId, AuditActions.CliLoginCodeReused)
            {
                ActorPersonId = personId,
                TargetPersonId = personId,
                Details = new Dictionary<string, string>
                {
                    ["revokedTokens"] = revoked.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["machine"] = used.MachineName,
                },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        cache.Forget(used.AccountId);
        if (revoked > 0 && personId is { } person)
        {
            await events.PublishAsync(new AccountEvent(eventId, used.OrganizationId, person, AccountEventKind.CliLoginCodeReused, now)
            {
                SourceAddress = requestSource.Current?.Address,
                MachineName = used.MachineName,
                RevokedTokens = revoked,
            });
        }
    }

    /// <summary>碼找得到才知道是哪個帳號，所以只有這時候才記失敗；原因只有兩種，不放碼與 verifier。</summary>
    private async Task RecordFailureAsync(IdentityDbContext db, CliLoginCodeRow row, string reason, CancellationToken cancellationToken)
    {
        var personId = await PersonIdOfAsync(db, row, cancellationToken);
        await audit.AddAsync(
            db,
            new AuditRecord(row.OrganizationId, AuditActions.CliLoginRedeemed, AuditOutcomes.Failed)
            {
                ActorPersonId = personId,
                TargetPersonId = personId,
                Details = new Dictionary<string, string> { ["reason"] = reason, ["machine"] = row.MachineName },
            },
            cancellationToken);
    }

    private static async Task<Guid?> PersonIdOfAsync(IdentityDbContext db, CliLoginCodeRow row, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(a => a.Id == row.AccountId && a.OrganizationId == row.OrganizationId)
            .Select(a => (Guid?)a.PersonId)
            .SingleOrDefaultAsync(cancellationToken);

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
        IdentityDbContext db, CliLoginCodeRow row, string? createdFrom, DateTimeOffset now, CancellationToken cancellationToken)
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
            cli = Add(db, row, CliLoginRules.CliTokenName(row.MachineName), scopes, row.ExpiresInDays, createdFrom, now);
        }

        if (row.IncludeAgent)
        {
            agent = Add(db, row, CliLoginRules.AgentTokenName(row.MachineName), CliLoginRules.AgentScopes, CliLoginRules.AgentTokenLifetimeDays, createdFrom, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (cli, agent);
    }

    private static CreatedAccessToken Add(
        IdentityDbContext db, CliLoginCodeRow row, string name, IReadOnlyList<AccessTokenScope> scopes, int? expiresInDays, string? createdFrom, DateTimeOffset now)
    {
        var (token, secret) = AccessTokenWriter.NewRow(row.OrganizationId, row.AccountId, name, scopes, expiresInDays, row.MachineName, createdFrom, now);
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
