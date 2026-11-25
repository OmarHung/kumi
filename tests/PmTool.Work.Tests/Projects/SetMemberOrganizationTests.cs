using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>加成員前先確認這個人在同一個組織：不在的回 404，專案裡什麼都不會多出來。</summary>
[TestFixture]
public sealed class SetMemberOrganizationTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SetMemberAsync_PersonOutsideTheOrganization_IsPersonNotFoundAndAddsNobody()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var stranger = Guid.NewGuid();
        _host.OrganizationMembers.Exclude(stranger);

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", stranger, "editor", CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.PersonNotFound)));
            Assert.That(detail.Value.Members.Select(m => m.PersonId), Is.EqualTo(new[] { scenario.Admin.PersonId }));
        }
    }

    [Test]
    public async Task SetMemberAsync_PersonInTheOrganization_IsAdded()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var person = Guid.NewGuid();

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", person, "editor", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }
}
