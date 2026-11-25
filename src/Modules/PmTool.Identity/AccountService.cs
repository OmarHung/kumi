using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Audit;
using PmTool.Identity.CliLogin;
using PmTool.Identity.Persistence;
using PmTool.Identity.OAuth;
using PmTool.Identity.Sessions;

namespace PmTool.Identity;

internal sealed class AccountService(
    IDbContextFactory<IdentityDbContext> contexts,
    IPasswordHasher<PasswordCredential> hasher,
    SessionCache cache,
    AuditTrail audit,
    TimeProvider time) : IAccountService
{
    private static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    public async Task<Result<PasswordChanged>> ChangeOwnPasswordAsync(
        string sessionToken, string currentPassword, string newPassword, bool revokeAccessTokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);
        ArgumentNullException.ThrowIfNull(currentPassword);
        ArgumentNullException.ThrowIfNull(newPassword);
        if (!SessionToken.TryHash(sessionToken, out var hash))
        {
            return SessionEnded;
        }

        // 直接查資料庫而不是快取：要作廢別的工作階段之前，確認這一個現在真的還有效。
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var found = await SessionStore.FindAsync(db, hash, cancellationToken);
        if (found is null || !SessionStore.IsActive(found, now))
        {
            return SessionEnded;
        }

        // 兩個欄位的問題一次列出來，網頁才能一起標出來。
        var currentPasswordIssue = CheckCurrentPassword(found.Account.PasswordHash, currentPassword);
        FailureDetail?[] checks = [currentPasswordIssue, PersonInput.CheckPassword(newPassword, PersonInput.NewPasswordPath)];
        IReadOnlyList<FailureDetail> issues = [.. checks.OfType<FailureDetail>()];
        if (issues.Count > 0)
        {
            // 目前的密碼不對才留稽核：有人拿著別人的工作階段猜密碼，本人在紀錄裡看得到；新密碼的格式問題不算。
            if (currentPasswordIssue is not null)
            {
                await RecordIncorrectPasswordAsync(db, found, cancellationToken);
            }

            return PersonInput.Invalid(issues);
        }

        // 在交易之外算雜湊：它是最花時間的一步，不必讓交易與鎖等它。
        var passwordHash = hasher.HashPassword(PasswordCredential.Instance, newPassword);
        if (await ReplacePasswordAsync(db, found, passwordHash, revokeAccessTokens, now, cancellationToken) is not { } replaced)
        {
            return SessionEnded;
        }

        // OAuth 授權不受 revokeAccessTokens 影響（代表的是人授權的 AI 代理，重新授權只要按一下），一律在同一個交易裡撤銷了；
        // 個人存取權杖（命令列、本機代理、CI）由呼叫端決定：CI 用的權杖可能不想因為改密碼就壞掉。
        cache.Forget(found.Account.Id);
        return new PasswordChanged(replaced.SignedOutSessions, replaced.RevokedAccessTokens, replaced.Session);
    }

    private Task<Guid> RecordIncorrectPasswordAsync(IdentityDbContext db, SessionWithAccount found, CancellationToken cancellationToken) =>
        audit.AddAsync(
            db,
            new AuditRecord(found.Account.OrganizationId, AuditActions.PasswordChanged, AuditOutcomes.Failed)
            {
                ActorPersonId = found.Person.Id,
                TargetPersonId = found.Person.Id,
                Details = new Dictionary<string, string> { ["reason"] = "incorrect_password" },
            },
            cancellationToken);

    private sealed record Replaced(int SignedOutSessions, int RevokedAccessTokens, StartedSession Session);

    private FailureDetail? CheckCurrentPassword(string storedHash, string currentPassword) =>
        CurrentPasswordCheck.Matches(hasher, storedHash, currentPassword) ? null : CurrentPasswordCheck.Incorrect;

    /// <summary>
    /// 換密碼、作廢所有工作階段並換上新的，在同一個交易裡：只做到一半的話，偷了 Cookie 的人在改完密碼之後還能用。
    /// 剛才是先查再寫，中間可能有別人動了帳號，所以寫的時候再確認一次，任一個不成立就整個不做、回 null：
    /// 密碼還是剛才比對過的那一份、帳號沒停用（否則會蓋掉同時進行的重設或另一次改密碼），
    /// 這個工作階段也還沒被作廢（例如別的裝置同時按了「登出其他裝置」）。
    /// 先改帳號、再動工作階段，和重設密碼、停用的順序一樣，同時進行時才不會互相等成死結。
    /// 目前這一個也作廢、換成新的（密碼剛驗過，等於重新登入，絕對期限重新算）：舊的權杖萬一外流過，就此失效。
    /// </summary>
    private async Task<Replaced?> ReplacePasswordAsync(
        IdentityDbContext db, SessionWithAccount found, string passwordHash, bool revokeAccessTokens, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var account = found.Account;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var replaced = await db.Accounts
            .Where(a => a.Id == account.Id && a.OrganizationId == account.OrganizationId
                && a.PasswordHash == account.PasswordHash && a.DisabledAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.PasswordHash, passwordHash), cancellationToken);
        if (replaced != 1 || !await SessionStore.TouchAsync(db, found.Session, now, cancellationToken))
        {
            return null;
        }

        var signedOut = await SessionStore.RevokeAsync(
            db, account.OrganizationId, account.Id, found.Session.Id, SessionEndReasons.PasswordChanged, now, cancellationToken);
        await SessionStore.RevokeByIdAsync(db, account.OrganizationId, found.Session.Id, SessionEndReasons.Rotated, now, cancellationToken);
        var (row, token) = SessionStore.NewRow(account.OrganizationId, account.Id, now, now + SessionStore.AbsoluteLimit);
        if (!await SessionStore.InsertForVerifiedAsync(db, row, passwordHash, cancellationToken))
        {
            return null;
        }

        var revokedTokens = revokeAccessTokens
            ? await AccessTokenStore.RevokePersonalAsync(db, account.OrganizationId, account.Id, SessionEndReasons.PasswordChanged, now, cancellationToken)
            : 0;

        // 還沒兌換的瀏覽器登入碼一起作廢：不然偷到 Cookie 的人先核准一次、不兌換，本人改完密碼之後他還換得到新的權杖。
        await CliLoginCodeStore.InvalidateForAccountAsync(db, account.OrganizationId, account.Id, now, cancellationToken);
        await OAuthGrantStore.RevokeAllForAccountAsync(
            db, account.OrganizationId, account.Id, SessionEndReasons.OAuthPasswordChanged, now, cancellationToken);
        await audit.AddAsync(
            db,
            new AuditRecord(account.OrganizationId, AuditActions.PasswordChanged)
            {
                ActorPersonId = found.Person.Id,
                TargetPersonId = found.Person.Id,
                Details = new Dictionary<string, string>
                {
                    ["signedOutSessions"] = signedOut.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["revokedAccessTokens"] = revokedTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Replaced(signedOut, revokedTokens, new StartedSession(row.Id, token, row.ExpiresAt));
    }
}
