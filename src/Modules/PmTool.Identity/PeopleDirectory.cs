using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

internal sealed class PeopleDirectory(
    IDbContextFactory<IdentityDbContext> contexts,
    IPasswordHasher<PasswordCredential> hasher,
    AccountAdministration administration,
    TimeProvider time) : IPeopleDirectory
{
    public async Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Summaries(db, db.People.Where(p => p.OrganizationId == organizationId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        if (personIds.Count == 0)
        {
            return new Dictionary<Guid, PersonSummary>();
        }

        var ids = personIds.Distinct().ToArray();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var people = db.People.Where(p => p.OrganizationId == organizationId && ids.Contains(p.Id));
        return await Summaries(db, people).ToDictionaryAsync(p => p.PersonId, cancellationToken);
    }

    public async Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(person);
        if (!Enum.IsDefined(person.Role))
        {
            // API 只會把認得的角色名稱轉成列舉，走到這裡是程式寫錯了。
            throw new ArgumentOutOfRangeException(nameof(person), person.Role, "不認得的組織角色。");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (await CheckPermissionAsync(db, actor, person.Role, cancellationToken) is { } forbidden)
        {
            return forbidden;
        }

        var input = PersonInput.Check(person.DisplayName, person.Email, person.Password);
        if (input.Failure is { } invalid)
        {
            return invalid;
        }

        var (displayName, email) = input.Value;
        var passwordHash = hasher.HashPassword(PasswordCredential.Instance, person.Password);
        var (personRow, accountRow) = PersonRows.Create(actor.OrganizationId, displayName, email, passwordHash, person.Role, time.GetUtcNow());
        db.AddRange(personRow, accountRow);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailTaken(exception))
        {
            // 先查再寫擋不住兩個人同時用同一個 Email 新增，所以只靠資料庫的唯一限制判斷。
            const string message = "這個 Email 已經有人使用。";
            return new Failure(FailureKind.Conflict, IdentityErrorCodes.EmailTaken, message)
            {
                Details = [new FailureDetail(IdentityErrorCodes.EmailTaken, PersonInput.EmailPath, message)],
            };
        }

        return new PersonSummary(personRow.Id, displayName, email, person.Role, IsDisabled: false);
    }

    public Task<Result<PersonSummary>> SetDisabledAsync(
        Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken) =>
        administration.SetDisabledAsync(actor, personId, disabled, cancellationToken);

    public Task<Result<PersonSummary>> ResetPasswordAsync(
        Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        administration.ResetPasswordAsync(actor, personId, newPassword, cancellationToken);

    public Task<Result<PersonSummary>> SetRoleAsync(
        Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken) =>
        administration.SetRoleAsync(actor, personId, role, cancellationToken);

    /// <summary>Email 存在帳號上，而合約允許沒有帳號的人（Email 是 null），所以用左外部聯結。</summary>
    private static IQueryable<PersonSummary> Summaries(IdentityDbContext db, IQueryable<PersonRow> people) =>
        from person in people
        join account in db.Accounts on person.Id equals account.PersonId into accounts
        from account in accounts.DefaultIfEmpty()
        orderby person.DisplayName, person.Id
        select new PersonSummary(
            person.Id,
            person.DisplayName,
            account == null ? null : account.Email,
            person.Role,
            account != null && account.DisabledAt != null);

    /// <summary>擁有者只能由擁有者新增，管理員才不能替自己開出更大的權限；Actor 分不出這兩種，所以查資料庫。</summary>
    private static async Task<Failure?> CheckPermissionAsync(
        IdentityDbContext db, Actor actor, OrganizationRole role, CancellationToken cancellationToken)
    {
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有組織的擁有者或管理員可以新增人員。");
        }

        if (role != OrganizationRole.Owner)
        {
            return null;
        }

        var actorIsOwner = await db.People.AnyAsync(
            p => p.Id == actor.PersonId && p.OrganizationId == actor.OrganizationId && p.Role == OrganizationRole.Owner,
            cancellationToken);
        return actorIsOwner
            ? null
            : new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, "只有組織的擁有者可以新增擁有者。");
    }

    private static bool IsEmailTaken(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IdentityDbContext.AccountEmailIndex,
        };
}
