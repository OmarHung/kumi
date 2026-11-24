using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;
using PmTool.Identity.OAuth;
using PmTool.Identity.Sessions;

namespace PmTool.Identity;

internal sealed class AccountService(
    IDbContextFactory<IdentityDbContext> contexts,
    IPasswordHasher<PasswordCredential> hasher,
    SessionCache cache,
    TimeProvider time) : IAccountService
{
    private static readonly Failure SessionEnded =
        new(FailureKind.Forbidden, IdentityErrorCodes.SessionEnded, "工作階段已經失效，請重新登入。");

    private static readonly FailureDetail IncorrectPassword =
        new(IdentityErrorCodes.PasswordIncorrect, PersonInput.CurrentPasswordPath, "目前的密碼不正確。");

    public async Task<Result<PasswordChanged>> ChangeOwnPasswordAsync(
        string sessionToken, string currentPassword, string newPassword, CancellationToken cancellationToken)
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
        FailureDetail?[] checks =
        [
            CheckCurrentPassword(found.Account.PasswordHash, currentPassword),
            PersonInput.CheckPassword(newPassword, PersonInput.NewPasswordPath),
        ];
        IReadOnlyList<FailureDetail> issues = [.. checks.OfType<FailureDetail>()];
        if (issues.Count > 0)
        {
            return PersonInput.Invalid(issues);
        }

        // 在交易之外算雜湊：它是最花時間的一步，不必讓交易與鎖等它。
        var passwordHash = hasher.HashPassword(PasswordCredential.Instance, newPassword);
        if (await ReplacePasswordAsync(db, found, passwordHash, now, cancellationToken) is not { } signedOut)
        {
            return SessionEnded;
        }

        // 刻意不撤銷這個人的個人存取權杖（停用與管理員重設才撤銷）：CI 用的權杖不該因為改密碼就壞掉；
        // 網頁在改密碼的地方說明，懷疑權杖外流要自己到「存取權杖」撤銷。
        // OAuth 授權不適用這個理由（代表的是人授權的 AI 代理，重新授權只要按一下），所以已經在同一個交易裡撤銷了。
        cache.Forget(found.Account.Id);
        return new PasswordChanged(signedOut);
    }

    /// <summary>太長的不拿去算雜湊（成本跟著長度走）；存著的密碼不可能那麼長，所以一定不對。</summary>
    private FailureDetail? CheckCurrentPassword(string storedHash, string currentPassword) =>
        PersonInput.IsPasswordTooLong(currentPassword)
        || hasher.VerifyHashedPassword(PasswordCredential.Instance, storedHash, currentPassword) == PasswordVerificationResult.Failed
            ? IncorrectPassword
            : null;

    /// <summary>
    /// 換密碼與作廢其他工作階段在同一個交易裡：只做到一半的話，偷了 Cookie 的人在改完密碼之後還能用。
    /// 剛才是先查再寫，中間可能有別人動了帳號，所以寫的時候再確認一次，任一個不成立就整個不做、回 null：
    /// 密碼還是剛才比對過的那一份、帳號沒停用（否則會蓋掉同時進行的重設或另一次改密碼），
    /// 這個工作階段也還沒被作廢（例如別的裝置同時按了「登出其他裝置」）。
    /// 先改帳號、再動工作階段，和重設密碼、停用的順序一樣，同時進行時才不會互相等成死結。
    /// </summary>
    private static async Task<int?> ReplacePasswordAsync(
        IdentityDbContext db, SessionWithAccount found, string passwordHash, DateTimeOffset now, CancellationToken cancellationToken)
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
        await OAuthGrantStore.RevokeAllForAccountAsync(
            db, account.OrganizationId, account.Id, SessionEndReasons.OAuthPasswordChanged, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return signedOut;
    }
}
