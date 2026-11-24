using System.Buffers.Binary;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.OAuth;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity;

/// <summary>管理別人的帳號：停用、重新啟用、重設密碼、改角色。這幾件事的權限規則一樣，只有訊息不同。</summary>
internal sealed class AccountAdministration(
    IDbContextFactory<IdentityDbContext> contexts,
    IPasswordHasher<PasswordCredential> hasher,
    SessionCache cache,
    TimeProvider time)
{
    /// <summary>
    /// 同一個組織裡管理帳號的操作排隊做（交易層級的鎖，鍵是這個常數加上組織）。
    /// 不排隊的話，兩位擁有者同時停用對方，兩邊都看到自己還能用，結果一位能登入的擁有者都不剩。
    /// </summary>
    /// <summary>組織層級的鎖的第一個鍵。internal：測試要從外面拿住同一把鎖，確定兩個同時的請求真的排在它後面。</summary>
    internal const int AccountAdministrationLock = 0x6964_6E74;

    private static readonly AccountAction Disable = new(
        "只有組織的擁有者或管理員可以停用帳號。", "只有擁有者可以停用擁有者或管理員的帳號。", "不能停用自己的帳號。");

    private static readonly AccountAction Enable = new(
        "只有組織的擁有者或管理員可以重新啟用帳號。", "只有擁有者可以重新啟用擁有者或管理員的帳號。", "不能重新啟用自己的帳號。");

    private static readonly AccountAction Reset = new(
        "只有組織的擁有者或管理員可以重設密碼。", "只有擁有者可以重設擁有者或管理員的密碼。", "要改自己的密碼，請到「我的帳號」。");

    private static readonly AccountAction ChangeRole = new(
        "只有組織的擁有者或管理員可以改角色。", "只有擁有者可以改擁有者或管理員的角色，也只有擁有者可以把人設成擁有者或管理員。", "不能改自己的角色。");

    public async Task<Result<PersonSummary>> SetDisabledAsync(
        Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await LoadTargetAsync(db, actor, personId, disabled ? Disable : Enable, lockOrganization: true, cancellationToken);
        if (loaded.Failure is { } failure)
        {
            return failure;
        }

        var (person, account) = loaded.Value;
        if ((account.DisabledAt is not null) == disabled)
        {
            return Summary(person, account, disabled);
        }

        var now = time.GetUtcNow();
        await db.Accounts
            .Where(a => a.Id == account.Id && a.OrganizationId == actor.OrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.DisabledAt, disabled ? now : null), cancellationToken);
        if (disabled)
        {
            await SessionStore.RevokeAsync(
                db, actor.OrganizationId, account.Id, null, SessionEndReasons.AccountDisabled, now, cancellationToken);
            await AccessTokenStore.RevokeAllAsync(
                db, actor.OrganizationId, account.Id, SessionEndReasons.AccountDisabled, now, cancellationToken);
            await OAuthGrantStore.RevokeAllForAccountAsync(
                db, actor.OrganizationId, account.Id, SessionEndReasons.OAuthAccountDisabled, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // 重新啟用不必清快取：停用時工作階段、權杖與 OAuth 授權（含更新權杖）都作廢了，它們不會因為重新啟用而變回有效。
        if (disabled)
        {
            cache.Forget(account.Id);
        }

        return Summary(person, account, disabled);
    }

    /// <summary>
    /// 改別人的組織角色。規則在停用、重設密碼的規則之上多一條：要改成擁有者或管理員，也只有擁有者能做，
    /// 否則管理員可以把自己信任的人升成管理員，等於替自己擴張權限。工作階段不作廢，只清快取：角色是每次從資料庫讀到快取的。
    /// </summary>
    public async Task<Result<PersonSummary>> SetRoleAsync(
        Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Enum.IsDefined(role))
        {
            // API 只會把認得的角色名稱轉成列舉，走到這裡是程式寫錯了。
            throw new ArgumentOutOfRangeException(nameof(role), role, "不認得的組織角色。");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await LoadTargetAsync(db, actor, personId, ChangeRole, lockOrganization: true, cancellationToken);
        if (loaded.Failure is { } failure)
        {
            return failure;
        }

        var (person, account) = loaded.Value;
        if (role is OrganizationRole.Owner or OrganizationRole.Admin && !await IsOwnerAsync(db, actor, cancellationToken))
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, ChangeRole.OwnerOnly);
        }

        if (person.Role != role)
        {
            await db.People
                .Where(p => p.Id == person.Id && p.OrganizationId == actor.OrganizationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Role, role), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        cache.Forget(account.Id);
        return new PersonSummary(person.Id, person.DisplayName, account.Email, role, account.DisabledAt is not null);
    }

    private static Task<bool> IsOwnerAsync(IdentityDbContext db, Actor actor, CancellationToken cancellationToken) =>
        db.People.AnyAsync(
            p => p.Id == actor.PersonId && p.OrganizationId == actor.OrganizationId && p.Role == OrganizationRole.Owner,
            cancellationToken);

    public async Task<Result<PersonSummary>> ResetPasswordAsync(
        Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(newPassword);

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先不鎖、不開交易地預檢一次權限，過了才檢查密碼與算雜湊：雜湊最花時間，不該在拿著組織的鎖時算；
        // 先看權限再看密碼，和新增人員一樣，沒有權限的人不必知道他送的密碼合不合規定。
        if ((await LoadTargetAsync(db, actor, personId, Reset, lockOrganization: false, cancellationToken)).Failure is { } denied)
        {
            return denied;
        }

        if (PersonInput.CheckPassword(newPassword, PersonInput.NewPasswordPath) is { } invalid)
        {
            return PersonInput.Invalid([invalid]);
        }

        var passwordHash = hasher.HashPassword(PasswordCredential.Instance, newPassword);

        // 預檢之後狀態可能變了（例如做這件事的人剛被停用），拿到鎖之後照同樣的規則再查一次才算數。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var loaded = await LoadTargetAsync(db, actor, personId, Reset, lockOrganization: true, cancellationToken);
        if (loaded.Failure is { } failure)
        {
            return failure;
        }

        var (person, account) = loaded.Value;
        var now = time.GetUtcNow();
        await db.Accounts
            .Where(a => a.Id == account.Id && a.OrganizationId == actor.OrganizationId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.PasswordHash, passwordHash), cancellationToken);
        await SessionStore.RevokeAsync(
            db, actor.OrganizationId, account.Id, null, SessionEndReasons.PasswordReset, now, cancellationToken);
        await AccessTokenStore.RevokeAllAsync(
            db, actor.OrganizationId, account.Id, SessionEndReasons.PasswordReset, now, cancellationToken);
        await OAuthGrantStore.RevokeAllForAccountAsync(
            db, actor.OrganizationId, account.Id, SessionEndReasons.OAuthPasswordReset, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache.Forget(account.Id);
        return Summary(person, account, account.DisabledAt is not null);
    }

    private static PersonSummary Summary(PersonRow person, AccountRow account, bool disabled) =>
        new(person.Id, person.DisplayName, account.Email, person.Role, disabled);

    /// <summary>
    /// 照順序檢查，回傳要動的人與他的帳號。不是管理者先擋（不必查資料庫，也不讓他知道那個人存不存在）；
    /// 角色要查資料庫，因為 <see cref="Actor"/> 分不出擁有者與管理員。也確認做這件事的人自己的帳號還沒被停用：
    /// 他的工作階段可能是在別的程序裡被作廢的，最多晚一個快取期間才生效。
    /// </summary>
    private static async Task<Result<(PersonRow Person, AccountRow Account)>> LoadTargetAsync(
        IdentityDbContext db, Actor actor, Guid personId, AccountAction action, bool lockOrganization, CancellationToken cancellationToken)
    {
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, action.NotAdmin);
        }

        if (personId == actor.PersonId)
        {
            return new Failure(FailureKind.Conflict, IdentityErrorCodes.IsSelf, action.Self);
        }

        if (lockOrganization)
        {
            await db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock({AccountAdministrationLock}, {LockKeyOf(actor.OrganizationId)})", cancellationToken);
        }

        var people = await db.People
            .Where(p => p.OrganizationId == actor.OrganizationId && (p.Id == actor.PersonId || p.Id == personId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var accounts = await db.Accounts
            .Where(a => a.OrganizationId == actor.OrganizationId && (a.PersonId == actor.PersonId || a.PersonId == personId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var actorPerson = people.SingleOrDefault(p => p.Id == actor.PersonId);
        var actorAccount = accounts.SingleOrDefault(a => a.PersonId == actor.PersonId);
        if (actorPerson is not { Role: OrganizationRole.Owner or OrganizationRole.Admin } || actorAccount is not { DisabledAt: null })
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, action.NotAdmin);
        }

        return Target(people.SingleOrDefault(p => p.Id == personId), accounts.SingleOrDefault(a => a.PersonId == personId), actorPerson, action);
    }

    private static Result<(PersonRow Person, AccountRow Account)> Target(
        PersonRow? person, AccountRow? account, PersonRow actorPerson, AccountAction action)
    {
        if (person is null)
        {
            return new Failure(FailureKind.NotFound, IdentityErrorCodes.NotFound, "組織裡找不到這個人。");
        }

        if (person.Role is OrganizationRole.Owner or OrganizationRole.Admin && actorPerson.Role != OrganizationRole.Owner)
        {
            return new Failure(FailureKind.Forbidden, IdentityErrorCodes.Forbidden, action.OwnerOnly);
        }

        return account is null
            ? new Failure(FailureKind.Conflict, IdentityErrorCodes.NoAccount, "這個人沒有登入帳號。")
            : (person, account);
    }

    /// <summary>鎖的第二個鍵只有 32 位元，把組織編號折成一個整數。撞在一起只是多排一點隊，不影響正確性。</summary>
    internal static int LockKeyOf(Guid organizationId)
    {
        Span<byte> bytes = stackalloc byte[16];
        organizationId.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes)
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }

    private sealed record AccountAction(string NotAdmin, string OwnerOnly, string Self);
}
