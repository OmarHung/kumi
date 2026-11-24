using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>改組織角色：誰可以改成什麼、不能改自己、改完下一個請求就是新的角色。</summary>
[TestFixture]
public sealed class SetRoleTests
{
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

    [TestCaseSource(nameof(PermissionCases))]
    public async Task SetRole_FollowsTheRoleRules(
        OrganizationRole actorRole, OrganizationRole targetRole, OrganizationRole newRole, bool allowed)
    {
        // Arrange
        var actor = await _host.AddUserAsync(_owner.ToActor(), actorRole);
        var target = await _host.AddUserAsync(_owner.ToActor(), targetRole);

        // Act
        var result = await _host.People.SetRoleAsync(actor.ToActor(), target.PersonId, newRole, CancellationToken.None);

        // Assert
        if (allowed)
        {
            Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
            Assert.That(result.Value, Is.EqualTo(new PersonSummary(target.PersonId, target.DisplayName, target.Email, newRole, IsDisabled: false)));
            return;
        }

        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
        Assert.That(await RoleOfAsync(target.PersonId), Is.EqualTo(targetRole), "被擋下就不能動到角色");
    }

    [TestCase(OrganizationRole.Owner)]
    [TestCase(OrganizationRole.Admin)]
    public async Task SetRole_OnTheirOwnRole_ReturnsIsSelf(OrganizationRole actorRole)
    {
        // Arrange
        var actor = await _host.AddUserAsync(_owner.ToActor(), actorRole);

        // Act
        var result = await _host.People.SetRoleAsync(actor.ToActor(), actor.PersonId, OrganizationRole.Member, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Conflict, IdentityErrorCodes.IsSelf);
        Assert.That(await RoleOfAsync(actor.PersonId), Is.EqualTo(actorRole));
    }

    [Test]
    public async Task SetRole_UnknownPersonOrAnotherOrganizationsPerson_ReturnsNotFound()
    {
        // Arrange
        var otherOrganization = await _host.AddOrganizationAsync("別的範例組織");
        var theirs = await _host.People.CreateAsync(
            otherOrganization, new NewPerson("別的組織的範例使用者", TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member),
            CancellationToken.None);

        // Act
        var unknown = await _host.People.SetRoleAsync(_owner.ToActor(), Guid.CreateVersion7(), OrganizationRole.Guest, CancellationToken.None);
        var elsewhere = await _host.People.SetRoleAsync(_owner.ToActor(), theirs.Value.PersonId, OrganizationRole.Guest, CancellationToken.None);

        // Assert
        AssertFailure(unknown, FailureKind.NotFound, IdentityErrorCodes.NotFound);
        AssertFailure(elsewhere, FailureKind.NotFound, IdentityErrorCodes.NotFound);
    }

    [Test]
    public async Task SetRole_AlreadyThatRole_SucceedsAndChangesNothing()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());

        // Act
        var result = await _host.People.SetRoleAsync(_owner.ToActor(), target.PersonId, OrganizationRole.Member, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Role, Is.EqualTo(OrganizationRole.Member));
        Assert.That(await RoleOfAsync(target.PersonId), Is.EqualTo(OrganizationRole.Member));
    }

    [Test]
    public async Task SetRole_DisabledAccount_ChangesTheRoleAndItStaysDisabled()
    {
        // Arrange
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.People.SetDisabledAsync(_owner.ToActor(), target.PersonId, true, CancellationToken.None);

        // Act
        var result = await _host.People.SetRoleAsync(_owner.ToActor(), target.PersonId, OrganizationRole.Guest, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new PersonSummary(target.PersonId, target.DisplayName, target.Email, OrganizationRole.Guest, IsDisabled: true)));
    }

    [Test]
    public async Task SetRole_ByAnAdminWhoseAccountWasJustDisabled_ReturnsForbidden()
    {
        // Arrange
        var admin = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        var target = await _host.AddUserAsync(_owner.ToActor());
        await _host.DisableAccountAsync(admin.AccountId);

        // Act
        var result = await _host.People.SetRoleAsync(admin.ToActor(), target.PersonId, OrganizationRole.Guest, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Forbidden, IdentityErrorCodes.Forbidden);
    }

    [Test]
    public async Task SetRole_TheirSessionStaysValidAndTheNextLookupSeesTheNewRole()
    {
        // Arrange：先查一次，舊角色進了快取；改完還讀到舊的就是沒清快取。
        var target = await _host.AddUserAsync(_owner.ToActor(), OrganizationRole.Admin);
        var session = await _host.StartSessionAsync(target);
        Assert.That((await _host.FindAsync(session))!.User.Role, Is.EqualTo(OrganizationRole.Admin));

        // Act
        await _host.People.SetRoleAsync(_owner.ToActor(), target.PersonId, OrganizationRole.Member, CancellationToken.None);

        // Assert
        var after = await _host.FindAsync(session);
        Assert.That(after, Is.Not.Null, "工作階段不作廢");
        Assert.That(after!.User.Role, Is.EqualTo(OrganizationRole.Member));
        var listed = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);
        Assert.That(listed.Single(p => p.PersonId == target.PersonId).Role, Is.EqualTo(OrganizationRole.Member));
    }

    [Test]
    public async Task SetRole_TwoOwnersDemoteEachOtherAtTheSameTime_AtLeastOneStaysOwner()
    {
        // Arrange
        await using var host = await IdentityTestHost.StartAsync();
        var first = await host.StartWithOwnerAsync();
        var second = await host.AddUserAsync(first.ToActor(), OrganizationRole.Owner);

        // Act
        var results = await Task.WhenAll(
            Task.Run(() => host.People.SetRoleAsync(first.ToActor(), second.PersonId, OrganizationRole.Member, CancellationToken.None)),
            Task.Run(() => host.People.SetRoleAsync(second.ToActor(), first.PersonId, OrganizationRole.Member, CancellationToken.None)));

        // Assert
        var people = await host.People.ListAsync(first.OrganizationId, CancellationToken.None);
        Assert.That(people.Count(p => p.Role == OrganizationRole.Owner), Is.EqualTo(1));
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
    }

    private async Task<OrganizationRole> RoleOfAsync(Guid personId) =>
        (await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None)).Single(p => p.PersonId == personId).Role;

    private static void AssertFailure(Result<PersonSummary> result, FailureKind kind, string code)
    {
        Assert.That(result.Failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(kind));
            Assert.That(result.Failure.Code, Is.EqualTo(code));
            Assert.That(result.Failure.Message, Is.Not.Empty);
        }
    }

    private static IEnumerable<TestCaseData> PermissionCases()
    {
        const OrganizationRole owner = OrganizationRole.Owner;
        const OrganizationRole admin = OrganizationRole.Admin;
        const OrganizationRole member = OrganizationRole.Member;
        const OrganizationRole guest = OrganizationRole.Guest;
        (OrganizationRole Actor, OrganizationRole Target, OrganizationRole New, bool Allowed)[] cases =
        [
            (owner, member, admin, true),
            (owner, member, owner, true),
            (owner, admin, member, true),
            (owner, owner, guest, true),
            (admin, member, guest, true),
            (admin, guest, member, true),
            (admin, member, admin, false),
            (admin, member, owner, false),
            (admin, admin, member, false),
            (admin, owner, member, false),
            (member, guest, member, false),
            (guest, member, guest, false),
        ];
        return cases.Select(c => new TestCaseData(c.Actor, c.Target, c.New, c.Allowed)
            .SetArgDisplayNames($"{c.Actor} 把 {c.Target} 改成 {c.New}", c.Allowed ? "可以" : "不行"));
    }
}
