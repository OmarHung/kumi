using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Work.ChangeSets;

namespace PmTool.Work.Tests.Projects;

/// <summary>
/// 封存靠專案那一列的排他鎖和寫入互斥：進行中的寫入（共用鎖）做完才輪到封存，封存進行中的寫入要等它提交；
/// 封存等鎖有上限，等不到就放棄、什麼都不改。
/// </summary>
[TestFixture]
public sealed class ProjectArchiveLockTests
{
    // 沒有排隊的請求幾毫秒就做完了；等這麼久還沒好，表示它在等另一個交易放開專案那一列。
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ShortLockTimeout = TimeSpan.FromMilliseconds(200);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase(true)]
    [TestCase(false)]
    public async Task SetArchivedAsync_WhileAWriteHoldsTheProject_IsBusyAndChangesNothing(bool archive)
    {
        // Arrange：另一個交易拿著專案的共用鎖（進行中的項目寫入），封存等鎖的上限調得很短。
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        if (!archive)
        {
            await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        }

        await using var impatient = WorkTestHost.BuildProvider(
            _host.ConnectionString, _host.Clock, services => services.AddSingleton(ChangeSetLimits.Default with { ApplyLockTimeout = ShortLockTimeout }));
        await using var scope = impatient.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        await using var writer = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await writer.LockProjectSharedAsync(project.Id);

        // Act
        var result = archive
            ? await projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None)
            : await projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        await writer.CommitAsync();
        var after = (await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None)).Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ChangeSetBusy)));
            Assert.That(result.Failure?.Message, Does.Contain("請稍後再試"));
            Assert.That(after.ArchivedAt is not null, Is.EqualTo(!archive), "什麼都沒改");
        }
    }

    [Test]
    public async Task CreateCycleAsync_WhileArchiveHoldsTheProject_WaitsForItAndThenIsRefused()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        await using var archiver = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await archiver.LockProjectAsync(project.Id);

        // Act
        var create = Task.Run(() => _host.Planning.CreateCycleAsync(
            scenario.Admin, "WEB", new CycleDraft("衝刺", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 13)), CancellationToken.None));
        var finishedEarly = await Task.WhenAny(create, Task.Delay(Grace)) == create;
        await archiver.CommitAsync();
        var result = await create;

        // Assert
        Assert.That(finishedEarly, Is.False, "週期的寫入要等封存的排他鎖放開");
        Assert.That(result.Succeeded, Is.True, "這個測試只鎖不封存，放開之後照常寫入");
    }

    [Test]
    public async Task RejectAsync_WhileArchiveHoldsTheProject_WaitsForIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", blueprint => blueprint);
        await using var archiver = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await archiver.LockProjectAsync(project.Id);

        // Act
        var reject = Task.Run(() => _host.Changes.RejectAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
        var finishedEarly = await Task.WhenAny(reject, Task.Delay(Grace)) == reject;
        await archiver.CommitAsync();
        var result = await reject;

        // Assert
        Assert.That(finishedEarly, Is.False, "拒絕要等封存的排他鎖放開");
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task DeleteCycleAsync_WhileArchiveHoldsTheProject_WaitsForIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var cycle = await _host.Planning.CreateCycleAsync(
            scenario.Admin, "WEB", new CycleDraft("衝刺", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 13)), CancellationToken.None);
        await using var archiver = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await archiver.LockProjectAsync(project.Id);

        // Act
        var delete = Task.Run(() => _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Value.Id, CancellationToken.None));
        var finishedEarly = await Task.WhenAny(delete, Task.Delay(Grace)) == delete;
        await archiver.CommitAsync();
        var result = await delete;

        // Assert
        Assert.That(finishedEarly, Is.False);
        Assert.That(result.Succeeded, Is.True);
    }
}
