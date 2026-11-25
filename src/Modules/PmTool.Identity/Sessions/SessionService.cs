using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Audit;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Sessions;

internal sealed class SessionService(
    IDbContextFactory<IdentityDbContext> contexts,
    AuthenticationService authentication,
    IPasswordHasher<PasswordCredential> hasher,
    SessionCache cache,
    AuditTrail audit,
    TimeProvider time) : ISessionService
{
    private static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    public async Task<SignedIn?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var verified = await authentication.VerifyAsync(email, password, cancellationToken);
        if (verified is null)
        {
            return null;
        }

        // 密碼已經比對過（最花時間的一步），所以「比對時密碼被改掉了」的失敗和密碼不對花的時間差不多。
        var started = await StartAsync(verified, cancellationToken);
        return started is null ? null : new SignedIn(verified.User, started);
    }

    public async Task<ActiveSession?> FindAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!SessionToken.TryHash(token, out var hash))
        {
            return null;
        }

        var found = await cache.GetAsync(SessionToken.CacheKeyOf(hash), () => LookUpAsync(hash), cancellationToken);
        return found.Session;
    }

    public async Task<bool> IsActiveAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!SessionToken.TryHash(token, out var hash))
        {
            return false;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var found = await SessionStore.FindAsync(db, hash, cancellationToken);
        return found is not null && SessionStore.IsActive(found, time.GetUtcNow());
    }

    public async Task EndAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!SessionToken.TryHash(token, out var hash))
        {
            return;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await SessionStore.RevokeOneAsync(db, hash, SessionEndReasons.SignedOut, time.GetUtcNow(), cancellationToken) is { } accountId)
        {
            cache.Forget(accountId);
        }
    }

    public async Task<Result<SessionRotation>> EndOthersAsync(string token, string currentPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(currentPassword);
        if (!SessionToken.TryHash(token, out var hash))
        {
            return SessionEnded;
        }

        // 直接查資料庫而不是快取：要作廢別人之前，確認這一個現在真的還有效。
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var found = await SessionStore.FindAsync(db, hash, cancellationToken);
        if (found is null || !SessionStore.IsActive(found, now))
        {
            return SessionEnded;
        }

        // 只有 Cookie 的人不能一直把本人踢出去：要目前的密碼。密碼不對寫一筆失敗的稽核，工作階段原封不動。
        if (!CurrentPasswordCheck.Matches(hasher, found.Account.PasswordHash, currentPassword))
        {
            await RecordIncorrectPasswordAsync(db, found, cancellationToken);
            return PersonInput.Invalid([CurrentPasswordCheck.Incorrect]);
        }

        var rotation = await RotateAsync(db, found, now, cancellationToken);
        if (rotation is null)
        {
            return SessionEnded;
        }

        // 已經閒置或過期、沒算進數字裡的也作廢了，所以不管數字是多少都清快取。
        cache.Forget(found.Account.Id);
        return rotation;
    }

    private Task<Guid> RecordIncorrectPasswordAsync(IdentityDbContext db, SessionWithAccount found, CancellationToken cancellationToken) =>
        audit.AddAsync(
            db,
            new AuditRecord(found.Session.OrganizationId, AuditActions.OtherSessionsSignedOut, AuditOutcomes.Failed)
            {
                ActorPersonId = found.Person.Id,
                TargetPersonId = found.Person.Id,
                Details = new Dictionary<string, string> { ["reason"] = "incorrect_password" },
            },
            cancellationToken);

    /// <summary>
    /// 換成新的、作廢其餘的，在同一個交易裡。新的沿用舊的絕對期限，不讓這個按鈕延長它。
    /// 交易一開始先鎖帳號那一列（和改密碼、重設密碼、停用的第一步一樣：先帳號、再工作階段），
    /// 同一個帳號同時的換發就排隊，不會兩邊各握著對方要動的工作階段而死結；
    /// 後到的看到自己的工作階段已經被作廢，整個不做。改密碼、重設密碼與停用先提交時，條件不成立，插不進去；
    /// 後提交時連這一筆一起作廢。
    /// </summary>
    private async Task<SessionRotation?> RotateAsync(
        IdentityDbContext db, SessionWithAccount found, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var organizationId = found.Session.OrganizationId;
        var (row, newToken) = SessionStore.NewRow(organizationId, found.Account.Id, now, found.Session.ExpiresAt);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AccessTokenWriter.LockAccountAsync(db, organizationId, found.Account.Id, cancellationToken)
            || !await SessionStore.InsertForVerifiedAsync(db, row, found.Account.PasswordHash, cancellationToken)
            || !await SessionStore.TouchAsync(db, found.Session, now, cancellationToken))
        {
            return null;
        }

        await SessionStore.RevokeByIdAsync(db, organizationId, found.Session.Id, SessionEndReasons.Rotated, now, cancellationToken);
        var ended = await SessionStore.RevokeAsync(
            db, organizationId, found.Account.Id, row.Id, SessionEndReasons.OtherSessions, now, cancellationToken);
        await audit.AddAsync(
            db,
            new AuditRecord(organizationId, AuditActions.OtherSessionsSignedOut)
            {
                ActorPersonId = found.Person.Id,
                TargetPersonId = found.Person.Id,
                Details = new Dictionary<string, string> { ["signedOutSessions"] = ended.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new SessionRotation(ended, new StartedSession(row.Id, newToken, row.ExpiresAt));
    }

    /// <summary>
    /// 快取沒有可用的結果時才走到這裡。不給取消權杖：這次查詢由所有在等的請求共用，
    /// 不能因為觸發它的那一個斷線就中止；期限交給資料庫驅動程式的指令逾時。
    /// </summary>
    private async Task<SessionLookup> LookUpAsync(byte[] hash)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        var now = time.GetUtcNow();
        var found = await SessionStore.FindAsync(db, hash, CancellationToken.None);
        if (found is null)
        {
            return SessionLookup.Unknown;
        }

        // 讀到之後、記下使用時刻之前，它可能剛被作廢；記不下去就當作無效。
        if (!SessionStore.IsActive(found, now) || !await SessionStore.TouchAsync(db, found.Session, now, CancellationToken.None))
        {
            return new SessionLookup(found.Account.Id, null);
        }

        return new SessionLookup(found.Account.Id, new ActiveSession(found.Session.Id, found.User, found.Session.ExpiresAt));
    }

    private async Task<StartedSession?> StartAsync(VerifiedCredential verified, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (row, token) = SessionStore.NewRow(verified.User.OrganizationId, verified.User.AccountId, now, now + SessionStore.AbsoluteLimit);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 登入是少數一定會寫入的時候，順便清掉很久以前結束的，不必另外排程。
        await SessionStore.DeleteEndedAsync(db, row.OrganizationId, now, cancellationToken);
        return await SessionStore.InsertForVerifiedAsync(db, row, verified.PasswordHash, cancellationToken)
            ? new StartedSession(row.Id, token, row.ExpiresAt)
            : null;
    }
}
