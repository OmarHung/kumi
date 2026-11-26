using Microsoft.Extensions.DependencyInjection;
using PmTool.Work.Blueprints;

namespace PmTool.Work.Tests.Projects;

/// <summary>一次讀好幾個專案的藍圖：版本是呼叫端給的，讀的當下專案升了版也照拿原本要的那一版。</summary>
[TestFixture]
public sealed class BlueprintCacheReadManyTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetManyAsync_ProjectMovedToANewerVersionMeanwhile_StillReturnsTheRequestedVersion()
    {
        // Arrange：呼叫端先讀到第 1 版，讀藍圖之前有人套用變更集升到第 2 版。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.SimpleTasks);
        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value.Project;
        var oldVersion = project.BlueprintVersion;
        var document = await StoredBlueprints.ReadCurrentAsync(_host, scenario.OrganizationId, "WEB");
        await StoredBlueprints.PublishDocumentAsync(_host, scenario, "WEB", document);
        await using var restarted = _host.Restart();
        var cache = restarted.Root.GetRequiredService<BlueprintCache>();
        var factory = restarted.Root.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<PmTool.Work.Persistence.WorkDbContext>>();
        await using var db = await factory.CreateDbContextAsync();

        // Act
        var schemas = await cache.GetManyAsync(db, scenario.OrganizationId, [(project.Id, oldVersion)], CancellationToken.None);

        // Assert
        Assert.That(schemas.ContainsKey(project.Id), Is.True);
    }
}
