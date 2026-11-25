using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>管理別人的帳號與新增人員：每一件做成的事都記下做的人、對象與來源；沒做成的不記。</summary>
[TestFixture]
public sealed class AccountAdministrationAuditTests
{
    private const string ResetTo = "reset-by-the-owner-0001";
    private static readonly CancellationToken NoCancel = CancellationToken.None;

    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    /// <summary>這個對象相關的紀錄，新的在前。</summary>
    private async Task<IReadOnlyList<AuditEntry>> EntriesAboutAsync(Guid personId) =>
        (await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(Limit: AuditRules.MaxLimit, PersonId: personId), NoCancel)).Value.Items;

    [Test]
    public async Task ResetPassword_RecordsWhoDidItToWhom()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        _host.Request.Set("203.0.113.9");

        // Act
        await _host.People.ResetPasswordAsync(_owner.ToActor(), target.PersonId, ResetTo, NoCancel);

        // Assert
        var entry = (await EntriesAboutAsync(target.PersonId)).Single(e => e.Action == AuditActions.PasswordReset);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(_owner.PersonId));
            Assert.That(entry.TargetPersonId, Is.EqualTo(target.PersonId));
            Assert.That(entry.Outcome, Is.EqualTo(AuditOutcomes.Succeeded));
            Assert.That(entry.SourceAddress, Is.EqualTo("203.0.113.9"));
        }

        _host.Request.Clear();
    }

    [Test]
    public async Task ResetPassword_Refused_RecordsNothing()
    {
        // Arrange
        var member = await _host.AddUserAsync(_owner.ToActor());
        var other = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.People.ResetPasswordAsync(member.ToActor(), other.PersonId, ResetTo, NoCancel);

        // Assert
        Assert.That(result.Succeeded, Is.False);
        Assert.That((await EntriesAboutAsync(other.PersonId)).Where(e => e.Action == AuditActions.PasswordReset), Is.Empty);
    }

    [Test]
    public async Task Disable_ThenEnable_RecordsEachWithTheRightAction()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, NoCancel);
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, false, NoCancel);

        // Assert
        var entries = (await EntriesAboutAsync(target.PersonId)).Where(e => e.Action is AuditActions.AccountDisabled or AuditActions.AccountEnabled);
        Assert.That(entries.Select(e => (e.Action, e.ActorPersonId, e.TargetPersonId)), Is.EqualTo(new[]
        {
            (AuditActions.AccountEnabled, (Guid?)_owner.PersonId, (Guid?)target.PersonId),
            (AuditActions.AccountDisabled, (Guid?)_owner.PersonId, (Guid?)target.PersonId),
        }));
    }

    [Test]
    public async Task Disable_AlreadyDisabled_RecordsNothingMore()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, NoCancel);

        // Act
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, NoCancel);

        // Assert
        Assert.That((await EntriesAboutAsync(target.PersonId)).Count(e => e.Action == AuditActions.AccountDisabled), Is.EqualTo(1));
    }

    [Test]
    public async Task SetRole_RecordsTheOldAndNewRole()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.People.SetRoleAsync(_owner.ToActor(), target.PersonId, OrganizationRole.Guest, NoCancel);

        // Assert
        var entry = (await EntriesAboutAsync(target.PersonId)).Single(e => e.Action == AuditActions.RoleChanged);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(_owner.PersonId));
            Assert.That(entry.Details["from"], Is.EqualTo("member"));
            Assert.That(entry.Details["to"], Is.EqualTo("guest"));
        }
    }

    [Test]
    public async Task SetRole_ToTheSameRole_RecordsNothing()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        await _host.People.SetRoleAsync(_owner.ToActor(), target.PersonId, OrganizationRole.Member, NoCancel);

        // Assert
        Assert.That((await EntriesAboutAsync(target.PersonId)).Where(e => e.Action == AuditActions.RoleChanged), Is.Empty);
    }

    [Test]
    public async Task Create_RecordsTheNewPersonAndTheirRole()
    {
        // Arrange
        var person = new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Guest);

        // Act
        var created = await _host.People.CreateAsync(_owner.ToActor(), person, NoCancel);

        // Assert
        var entry = (await EntriesAboutAsync(created.Value.PersonId)).Single(e => e.Action == AuditActions.PersonCreated);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ActorPersonId, Is.EqualTo(_owner.PersonId));
            Assert.That(entry.TargetPersonId, Is.EqualTo(created.Value.PersonId));
            Assert.That(entry.Details["role"], Is.EqualTo("guest"));
        }
    }

    [Test]
    public async Task Create_DuplicateEmail_RecordsNothing()
    {
        // Arrange
        var existing = await _host.AddUserAsync(_owner.ToActor());
        var before = await CountAllAsync();

        // Act
        var result = await _host.People.CreateAsync(
            _owner.ToActor(), new NewPerson("重複", existing.Email, TestData.Password, OrganizationRole.Member), NoCancel);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.EmailTaken));
        Assert.That(await CountAllAsync(), Is.EqualTo(before));
    }

    [Test]
    public async Task Create_ByAnAdminWhoWasDisabledMeanwhile_IsForbidden()
    {
        // Arrange：Actor 是登入時的樣子，資料庫裡他已經被停用了。
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        await _host.DisableAccountAsync(admin.AccountId);

        // Act
        var result = await _host.People.CreateAsync(
            admin.ToActor(), new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member), NoCancel);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task Create_ByAnAdminDemotedMeanwhile_IsForbidden()
    {
        // Arrange
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        await _host.People.SetRoleAsync(_owner.ToActor(), admin.PersonId, OrganizationRole.Member, NoCancel);

        // Act
        var result = await _host.People.CreateAsync(
            admin.ToActor(), new NewPerson("範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member), NoCancel);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    private async Task<int> CountAllAsync()
    {
        var count = 0;
        string? cursor = null;
        do
        {
            var page = (await _host.Audit.ListAsync(_owner.ToActor(), new AuditQuery(cursor, AuditRules.MaxLimit), NoCancel)).Value;
            count += page.Items.Count;
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return count;
    }
}
