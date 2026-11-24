using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

internal sealed class IdentityBootstrapper(
    IDbContextFactory<IdentityDbContext> contexts,
    IPasswordHasher<PasswordCredential> hasher,
    TimeProvider time) : IIdentityBootstrapper
{
    /// <summary>
    /// 所有程序都搶同一把交易層級的鎖：兩個容器同時啟動時，後到的會等先到的提交，
    /// 再看到已經有帳號而什麼都不做。只靠 Email 的唯一限制不夠，兩邊設定的 Email 不同就會建出兩個組織。
    /// </summary>
    private const long BootstrapLockKey = 0x706D_746F_6F6C_4944;

    public async Task<bool> EnsureFirstOwnerAsync(FirstOwner owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({BootstrapLockKey})", cancellationToken);

        // 設定只有第一次啟動用得到，所以有帳號之後就不再看：設定後來被改壞（例如密碼改短），也不該讓每次啟動都失敗。
        if (await db.Accounts.AnyAsync(cancellationToken))
        {
            return false;
        }

        var (organizationName, person) = Check(owner);
        var passwordHash = hasher.HashPassword(PasswordCredential.Instance, owner.Password);
        AddOwner(db, organizationName, person, passwordHash);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>訊息只說哪一項不對，不帶出設定的值：啟動失敗的訊息會進記錄，裡面不能有密碼或 Email。</summary>
    private static (string OrganizationName, CheckedPerson Person) Check(FirstOwner owner)
    {
        var organizationName = owner.OrganizationName.Trim();
        var person = PersonInput.Check(owner.DisplayName, owner.Email, owner.Password);
        var messages = OrganizationNameIssues(organizationName)
            .Concat(person.Failure?.Details.Select(issue => issue.Message) ?? [])
            .ToList();

        if (messages.Count > 0)
        {
            throw new ArgumentException($"第一個擁有者的設定不正確：{string.Join(" ", messages)}", nameof(owner));
        }

        return (organizationName, person.Value);
    }

    private static IEnumerable<string> OrganizationNameIssues(string organizationName)
    {
        if (organizationName.Length == 0)
        {
            yield return "請填寫組織名稱。";
        }

        if (organizationName.Length > IdentityDbContext.MaxOrganizationNameLength)
        {
            yield return $"組織名稱不能超過 {IdentityDbContext.MaxOrganizationNameLength} 個字。";
        }

        if (InputText.HasForbiddenCharacters(organizationName))
        {
            yield return "組織名稱不能有換行、控制字元或無效的字元。";
        }
    }

    private void AddOwner(IdentityDbContext db, string organizationName, CheckedPerson person, string passwordHash)
    {
        var now = time.GetUtcNow();
        var organization = new OrganizationRow { Id = Guid.CreateVersion7(), Name = organizationName, CreatedAt = now };
        var (personRow, account) = PersonRows.Create(
            organization.Id, person.DisplayName, person.Email, passwordHash, OrganizationRole.Owner, now);

        db.AddRange(organization, personRow, account);
    }
}
