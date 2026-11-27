using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>專案說明的「沒被改過才寫」（SetIfUnchangedAsync）：比對與寫入在工作引擎的同一個交易裡。</summary>
[TestFixture]
public sealed class ProjectDescriptionConflictTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SetIfUnchangedAsync_NeverWrittenAndExpectingNone_Writes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Descriptions.SetIfUnchangedAsync(scenario.Admin, "WEB", "## 網址", null, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Text, Is.EqualTo("## 網址"));
    }

    [Test]
    public async Task SetIfUnchangedAsync_ExpectedTimeMatches_Writes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "舊", CancellationToken.None);

        // Act
        var result = await _host.Descriptions.SetIfUnchangedAsync(scenario.Admin, "WEB", "新", first.Value.UpdatedAt, CancellationToken.None);
        var read = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(read.Value.Text, Is.EqualTo("新"));
        }
    }

    [Test]
    public async Task SetIfUnchangedAsync_SomeoneElseChangedItMeanwhile_ReturnsConflictAndChangesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "舊", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "別人改的", CancellationToken.None);

        // Act
        var result = await _host.Descriptions.SetIfUnchangedAsync(scenario.Admin, "WEB", "新", first.Value.UpdatedAt, CancellationToken.None);
        var read = await _host.Descriptions.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionConflict));
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(read.Value.Text, Is.EqualTo("別人改的"));
        }
    }

    [Test]
    public async Task SetIfUnchangedAsync_ExpectingNoneButItWasWritten_ReturnsConflict()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "已經有了", CancellationToken.None);

        // Act
        var result = await _host.Descriptions.SetIfUnchangedAsync(scenario.Admin, "WEB", "新", null, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectDescriptionConflict));
    }

    [Test]
    public async Task SetIfUnchangedAsync_NoPermission_IsForbiddenBeforeTheComparison()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Descriptions.SetIfUnchangedAsync(viewer, "WEB", "新", DateTimeOffset.UnixEpoch, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task SetIfUnchangedAsync_TwoWritersWithTheSameExpectation_OnlyOneWins()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await _host.Descriptions.SetAsync(scenario.Admin, "WEB", "舊", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(i =>
                _host.Descriptions.SetIfUnchangedAsync(scenario.Admin, "WEB", $"寫入 {i}", first.Value.UpdatedAt, CancellationToken.None)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
    }
}
