using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

/// <summary>比對成功的帳號，與比對時用的那一份密碼雜湊。</summary>
internal sealed record VerifiedCredential(AuthenticatedUser User, string PasswordHash);

internal sealed class AuthenticationService : IAuthenticationService
{
    private readonly IDbContextFactory<IdentityDbContext> _contexts;
    private readonly IPasswordHasher<PasswordCredential> _hasher;

    /// <summary>
    /// 查無此 Email 時拿來比對的雜湊。用同一個雜湊器算，參數和真的雜湊一樣，
    /// 比對花的時間才一樣；密碼是亂數，所以不可能比對成功。
    /// </summary>
    private readonly Lazy<string> _placeholderHash;

    public AuthenticationService(IDbContextFactory<IdentityDbContext> contexts, IPasswordHasher<PasswordCredential> hasher)
    {
        _contexts = contexts;
        _hasher = hasher;
        _placeholderHash = new(() => hasher.HashPassword(PasswordCredential.Instance, RandomNumberGenerator.GetHexString(32)));
    }

    public async Task<AuthenticatedUser?> SignInAsync(string email, string password, CancellationToken cancellationToken) =>
        (await VerifyAsync(email, password, cancellationToken))?.User;

    /// <summary>
    /// 確認 Email 與密碼，回傳帳號與比對成功的那一份雜湊。開工作階段時拿這份雜湊再確認一次：
    /// 比對的這段時間裡密碼被改掉、被重設，或帳號被停用，就不能開。
    /// </summary>
    internal async Task<VerifiedCredential?> VerifyAsync(string email, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(password);

        // 不管 Email 存不存在都在這裡擋，所以不會讓人分得出來。
        if (PersonInput.IsPasswordTooLong(password))
        {
            return null;
        }

        // 不可能有帳號的 Email 不送進資料庫：NUL 這類字元會讓 PostgreSQL 丟例外，對外變成 500。
        if (!EmailAddress.TryNormalize(email, out var normalizedEmail))
        {
            VerifyPlaceholder(password);
            return null;
        }

        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        var candidate = await AccountsWithPeople(db)
            .Where(row => row.Account.Email == normalizedEmail)
            .SingleOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            VerifyPlaceholder(password);
            return null;
        }

        // 停用的帳號也照樣比對一次，花的時間才和密碼不對時一樣。
        var verification = _hasher.VerifyHashedPassword(PasswordCredential.Instance, candidate.Account.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed || candidate.Account.DisabledAt is not null)
        {
            return null;
        }

        var passwordHash = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? await RehashAsync(db, candidate.Account, password, cancellationToken)
            : candidate.Account.PasswordHash;
        return passwordHash is null ? null : new VerifiedCredential(candidate.ToUser(), passwordHash);
    }

    /// <summary>沒有帳號可比對時也算一次，花的時間才和密碼不對時一樣，分不出這個 Email 有沒有帳號。</summary>
    private void VerifyPlaceholder(string password) =>
        _hasher.VerifyHashedPassword(PasswordCredential.Instance, _placeholderHash.Value, password);

    private static IQueryable<AccountWithPerson> AccountsWithPeople(IdentityDbContext db) =>
        from account in db.Accounts
        join person in db.People on account.PersonId equals person.Id
        join organization in db.Organizations on person.OrganizationId equals organization.Id
        select new AccountWithPerson { Account = account, Person = person, Organization = organization };

    /// <summary>
    /// 雜湊的參數調高之後，舊的雜湊只有在知道明碼的這一刻才換得掉。只換掉剛比對過的那一份：
    /// 比對的同時密碼被重設了，就不能用舊密碼的新雜湊蓋掉新密碼；這時當作登入失敗，回 null。
    /// </summary>
    private async Task<string?> RehashAsync(IdentityDbContext db, AccountRow account, string password, CancellationToken cancellationToken)
    {
        var newHash = _hasher.HashPassword(PasswordCredential.Instance, password);
        var updated = await db.Accounts
            .Where(a => a.Id == account.Id && a.OrganizationId == account.OrganizationId && a.PasswordHash == account.PasswordHash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.PasswordHash, newHash), cancellationToken);
        return updated == 1 ? newHash : null;
    }

    private sealed record AccountWithPerson
    {
        public required AccountRow Account { get; init; }

        public required PersonRow Person { get; init; }

        public required OrganizationRow Organization { get; init; }

        public AuthenticatedUser ToUser() => new(
            Account.Id, Person.Id, Organization.Id, Organization.Name, Person.DisplayName, Account.Email, Person.Role);
    }
}
