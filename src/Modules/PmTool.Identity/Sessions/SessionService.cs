using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Sessions;

internal sealed class SessionService(
    IDbContextFactory<IdentityDbContext> contexts,
    AuthenticationService authentication,
    SessionCache cache,
    TimeProvider time) : ISessionService
{
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

    public async Task<int> EndOthersAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!SessionToken.TryHash(token, out var hash))
        {
            return 0;
        }

        // 直接查資料庫而不是快取：要作廢別人之前，確認這一個現在真的還有效。
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var found = await SessionStore.FindAsync(db, hash, cancellationToken);
        if (found is null || !SessionStore.IsActive(found, now))
        {
            return 0;
        }

        var ended = await SessionStore.RevokeAsync(
            db, found.Session.OrganizationId, found.Account.Id, found.Session.Id, SessionEndReasons.OtherSessions, now,
            cancellationToken);
        // 已經閒置或過期、沒算進數字裡的也作廢了，所以不管數字是多少都清快取。
        cache.Forget(found.Account.Id);
        return ended;
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
        var (token, hash) = SessionToken.Create();
        var now = time.GetUtcNow();
        var row = new SessionRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = verified.User.OrganizationId,
            AccountId = verified.User.AccountId,
            TokenHash = hash,
            CreatedAt = now,
            LastUsedAt = now,
            ExpiresAt = now + SessionStore.AbsoluteLimit,
        };

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 登入是少數一定會寫入的時候，順便清掉很久以前結束的，不必另外排程。
        await SessionStore.DeleteEndedAsync(db, row.OrganizationId, now, cancellationToken);
        return await SessionStore.InsertForVerifiedAsync(db, row, verified.PasswordHash, cancellationToken)
            ? new StartedSession(row.Id, token, row.ExpiresAt)
            : null;
    }
}
