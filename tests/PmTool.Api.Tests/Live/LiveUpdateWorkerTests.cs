using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Live;
using PmTool.Work;

namespace PmTool.Api.Tests.Live;

/// <summary>讀事件並推送的工作：用假的事件流與假的 hub，不需要資料庫。</summary>
[TestFixture]
public sealed class LiveUpdateWorkerTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private static readonly Guid OtherProjectId = Guid.CreateVersion7();
    private static readonly Guid Person = Guid.CreateVersion7();

    private sealed record Setup(
        LiveUpdateWorker Worker, StubEventStream Stream, FakeHubContext Hub, LiveConnections Connections, StubMigrationStatus Migrations,
        CapturedLogProvider Logs, ServiceProvider Services);

    private static Setup Create(TimeSpan? sendTimeout = null)
    {
        var logs = new CapturedLogProvider();
        var stream = new StubEventStream(new WorkEventPosition(1, 1));
        var migrations = new StubMigrationStatus();
        var hub = new FakeHubContext();
        var connections = new LiveConnections();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddSingleton<IWorkEventStream>(stream);
        services.AddSingleton<IMigrationStatus>(migrations);
        var provider = services.BuildServiceProvider();
        var sender = new LiveSender(new LiveOptions { SendTimeout = sendTimeout ?? TimeSpan.FromMilliseconds(200) }, provider.GetRequiredService<ILogger<LiveSender>>());
        var worker = new LiveUpdateWorker(
            provider.GetRequiredService<IServiceScopeFactory>(), hub, connections, sender, provider.GetRequiredService<ILogger<LiveUpdateWorker>>());
        return new Setup(worker, stream, hub, connections, migrations, logs, provider);
    }

    private static WorkEvent EventIn(Guid project, string key, long sequence) => new(
        Guid.CreateVersion7(),
        new WorkEventPosition(10, sequence),
        WorkEventKind.ItemUpdated,
        Guid.CreateVersion7(),
        Person,
        DateTimeOffset.UnixEpoch,
        new ItemEventData(
            project, "MNT", Guid.CreateVersion7(), key, "標題", Person, null, AssigneeChanged: false, null, null, null, null, [], []));

    private static void Connect(Setup setup) =>
        setup.Connections.TryAdd(new LiveConnection(Guid.NewGuid().ToString(), Guid.CreateVersion7(), Person, "token", () => { }));

    [Test]
    public async Task RunOnce_AGroupWhoseSendHangs_DoesNotStopThePushToTheOtherGroups()
    {
        // Arrange
        var setup = Create();
        await using var _ = setup.Services;
        Connect(setup);
        await setup.Worker.RunOnceAsync(CancellationToken.None);
        setup.Hub.HangingGroups.Add(LiveGroups.Project(ProjectId));
        setup.Stream.Add(EventIn(ProjectId, "MNT-1", 2));
        setup.Stream.Add(EventIn(OtherProjectId, "WEB-1", 3));

        // Act
        var run = setup.Worker.RunOnceAsync(CancellationToken.None);
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))) == run;

        // Assert
        Assert.That(finished, Is.True, "卡住的群組不能讓這一輪一直等下去");
        Assert.That(setup.Hub.Sent.Select(m => m.Group), Does.Contain(LiveGroups.Project(OtherProjectId)));
        Assert.That(setup.Logs.Entries.Select(e => e.Level), Does.Contain(LogLevel.Warning));
        Assert.That(await setup.Worker.RunOnceAsync(CancellationToken.None), Is.Zero, "位置已經往前，不重送");
    }

    [Test]
    public async Task RunOnce_AFailureReadingEvents_IsSwallowedAndTheNextRoundDeliversThem()
    {
        // Arrange
        var setup = Create();
        await using var _ = setup.Services;
        Connect(setup);
        await setup.Worker.RunOnceAsync(CancellationToken.None);
        setup.Stream.Add(EventIn(ProjectId, "MNT-1", 2));
        setup.Stream.FailNextRead = new TimeoutException("資料庫暫時連不上");

        // Act
        var failed = await setup.Worker.RunOnceAsync(CancellationToken.None);
        var recovered = await setup.Worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That((failed, recovered), Is.EqualTo((0, 1)));
        Assert.That(setup.Hub.Sent.Select(m => m.Group), Does.Contain(LiveGroups.Project(ProjectId)));
        Assert.That(setup.Logs.Entries.Select(e => e.Level), Does.Contain(LogLevel.Error));
    }

    [Test]
    public async Task RunOnce_PendingMigrations_SkipsTheRoundAndResumesWhenTheyAreDone()
    {
        // Arrange
        var setup = Create();
        await using var _ = setup.Services;
        Connect(setup);
        setup.Migrations.Pending = ["20261121_AddEvents"];

        // Act
        await setup.Worker.RunOnceAsync(CancellationToken.None);
        var whilePending = setup.Stream.LatestCalls;
        setup.Migrations.Pending = [];
        await setup.Worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That(whilePending, Is.Zero, "遷移沒做完不能去碰事件的資料表");
        Assert.That(setup.Stream.LatestCalls, Is.EqualTo(1));
        Assert.That(setup.Logs.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(1));
    }

    [Test]
    public async Task RunOnce_WithNoConnections_OnlyMovesThePositionAndNeverReadsEvents()
    {
        // Arrange
        var setup = Create();
        await using var _ = setup.Services;
        setup.Stream.Add(EventIn(ProjectId, "MNT-1", 2));

        // Act
        await setup.Worker.RunOnceAsync(CancellationToken.None);
        await setup.Worker.RunOnceAsync(CancellationToken.None);

        // Assert
        Assert.That((setup.Stream.ReadCalls, setup.Stream.LatestCalls), Is.EqualTo((0, 2)));
        Assert.That(setup.Hub.Sent, Is.Empty);
    }

    [TestCase(true, 0, true, TestName = "IsDue_WithConnections_RunsEveryRound")]
    [TestCase(false, 0, false, TestName = "IsDue_NoConnectionsJustRan_Waits")]
    [TestCase(false, 4, false, TestName = "IsDue_NoConnectionsFourSecondsAgo_Waits")]
    [TestCase(false, 5, true, TestName = "IsDue_NoConnectionsFiveSecondsAgo_Runs")]
    public void IsDue(bool hasConnections, int secondsSinceLastRun, bool expected) =>
        Assert.That(LiveUpdateWorker.IsDue(hasConnections, TimeSpan.FromSeconds(secondsSinceLastRun)), Is.EqualTo(expected));
}
