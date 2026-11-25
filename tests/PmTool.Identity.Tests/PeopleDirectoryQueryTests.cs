using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>查人：只看得到同一個組織的人。</summary>
[TestFixture]
public sealed class PeopleDirectoryQueryTests
{
    private IdentityTestHost _host = null!;
    private AuthenticatedUser _owner = null!;
    private Actor _otherOrganization = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await IdentityTestHost.StartAsync();
        _owner = await _host.StartWithOwnerAsync();
        _otherOrganization = await _host.AddOrganizationAsync("另一個範例組織");
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_Organization_ReturnsItsPeopleSortedByDisplayName()
    {
        // Arrange：另開一個組織，裡面除了組織自己的擁有者，只有這個測試建的人
        var organization = await _host.AddOrganizationAsync("排序用的範例組織");
        var created = new List<PersonSummary>();
        foreach (var name in new[] { "C 範例使用者", "A 範例使用者", "B 範例使用者" })
        {
            created.Add(await CreateAsync(organization, name));
        }

        // Act
        var people = (await _host.People.ListAsync(organization.OrganizationId, CancellationToken.None))
            .Where(p => p.PersonId != organization.PersonId);

        // Assert
        Assert.That(people, Is.EqualTo(created.OrderBy(p => p.DisplayName, StringComparer.Ordinal)));
    }

    [Test]
    public async Task ListAsync_DoesNotIncludePeopleFromAnotherOrganization()
    {
        // Arrange
        var mine = await CreateAsync(_owner.ToActor(), "範例使用者");
        var theirs = await CreateAsync(_otherOrganization, "別的組織的範例使用者");

        // Act
        var people = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);

        // Assert
        Assert.That(people, Does.Contain(mine));
        Assert.That(people.Select(p => p.PersonId), Does.Not.Contain(theirs.PersonId));
        Assert.That(people.Select(p => p.PersonId), Does.Contain(_owner.PersonId));
    }

    [Test]
    public async Task ListAsync_UnknownOrganization_ReturnsEmpty()
    {
        // Act
        var people = await _host.People.ListAsync(Guid.CreateVersion7(), CancellationToken.None);

        // Assert
        Assert.That(people, Is.Empty);
    }

    [Test]
    public async Task ListAsync_PersonWithDisabledAccount_IsStillListed()
    {
        // Arrange：停用的是登入方式，人的紀錄要留著，工作項目上的負責人才顯示得出來
        var person = await CreateAsync(_owner.ToActor(), "已停用的範例使用者");
        var user = await _host.Authentication.SignInAsync(person.Email!, TestData.Password, CancellationToken.None);
        await _host.DisableAccountAsync(user!.AccountId);

        // Act
        var people = await _host.People.ListAsync(_owner.OrganizationId, CancellationToken.None);
        var found = await _host.People.FindManyAsync(_owner.OrganizationId, [person.PersonId], CancellationToken.None);

        // Assert：還在清單裡，標著停用
        var disabled = person with { IsDisabled = true };
        Assert.That(people, Does.Contain(disabled));
        Assert.That(found[person.PersonId], Is.EqualTo(disabled));
    }

    [Test]
    public async Task FindManyAsync_ReturnsOnlyRequestedPeopleOfTheOrganization()
    {
        // Arrange
        var first = await CreateAsync(_owner.ToActor(), "範例甲");
        var second = await CreateAsync(_owner.ToActor(), "範例乙");
        await CreateAsync(_owner.ToActor(), "沒被查的範例丙");
        var theirs = await CreateAsync(_otherOrganization, "別的組織的範例丁");
        Guid[] requested = [first.PersonId, second.PersonId, second.PersonId, theirs.PersonId, Guid.CreateVersion7()];

        // Act
        var found = await _host.People.FindManyAsync(_owner.OrganizationId, requested, CancellationToken.None);

        // Assert
        Assert.That(found, Is.EquivalentTo(new Dictionary<Guid, PersonSummary>
        {
            [first.PersonId] = first,
            [second.PersonId] = second,
        }));
    }

    [Test]
    public async Task FindManyAsync_NoIds_ReturnsEmpty()
    {
        // Act
        var found = await _host.People.FindManyAsync(_owner.OrganizationId, [], CancellationToken.None);

        // Assert
        Assert.That(found, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_EmailUsedInAnotherOrganization_ReturnsEmailTaken()
    {
        // Arrange：Email 是登入用的，全域只能有一個帳號
        var theirs = await CreateAsync(_otherOrganization, "別的組織的範例使用者");
        var person = new NewPerson("範例使用者", theirs.Email!, TestData.Password, OrganizationRole.Member);

        // Act
        var result = await _host.People.CreateAsync(_owner.ToActor(), person, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(IdentityErrorCodes.EmailTaken));
    }

    private async Task<PersonSummary> CreateAsync(Actor actor, string displayName)
    {
        var person = new NewPerson(displayName, TestData.UniqueEmail(), TestData.Password, OrganizationRole.Member);
        var result = await _host.People.CreateAsync(actor, person, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, result.Failure?.Message);
        return result.Value;
    }
}
