using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 同一個專案的變更集同時被套用、拒絕或送出：一次只有一個套用做得成，版本號不重複，後到的看到的是做完之後的狀態。
/// 「進行中」的做法和 <see cref="ChangeSetLockTests"/> 一樣：停住的時鐘讓操作拿著鎖、還沒提交。
/// </summary>
[TestFixture]
public sealed class ChangeSetConcurrencyTests
{
    // 沒有排隊的請求幾毫秒就做完了；等這麼久還沒好，表示它在等另一個交易放開鎖。
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_TwoChangeSetsAtTheSameTime_OnlyOneIsAppliedAndTheVersionIsNotReused()
    {
        // Arrange：#1 與 #2 都照第 1 版寫。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        var second = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var (applied, other) = await WhileApplyingAsync(
            scenario, first.ChangeSet.Number, () => _host.Changes.ApplyAsync(scenario.Admin, "WEB", second.ChangeSet.Number, CancellationToken.None));
        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(applied.Value.ChangeSet.AppliedVersion, Is.EqualTo(2));
        ItemAssert.Failed(other, FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(other.Failure!.Message, Is.EqualTo("變更集 #2 已經過期了（別的變更集先套用了），不能再套用或拒絕。"));
            Assert.That(project.Project.BlueprintVersion, Is.EqualTo(2));
            Assert.That(project.Blueprint.Views, Is.Empty, "藍圖是先套用的那一份");
            Assert.That(
                listed.Value.Select(c => (c.Number, c.Status, c.AppliedVersion)),
                Is.EqualTo(new[] { (2L, ChangeSetStatus.Expired, (long?)null), (1L, ChangeSetStatus.Applied, (long?)2) }));
        }
    }

    [Test]
    public async Task RejectAsync_WhileTheSameChangeSetIsBeingApplied_WaitsAndThenFindsItApplied()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });

        // Act
        var (applied, rejected) = await WhileApplyingAsync(
            scenario, proposed.ChangeSet.Number, () => _host.Changes.RejectAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
        ItemAssert.Failed(rejected, FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending);
        Assert.That(rejected.Failure!.Message, Is.EqualTo("變更集 #1 已經套用了，不能再套用或拒絕。"));
        Assert.That((readBack.Value.ChangeSet.Status, readBack.Value.ChangeSet.DecidedBy), Is.EqualTo((ChangeSetStatus.Applied, (Guid?)scenario.Admin.PersonId)));
    }

    [Test]
    public async Task ProposeAsync_WhileAnApplyIsInFlight_WaitsAndThenIsCheckedAgainstTheNewVersion()
    {
        // Arrange：新的這份照第 1 版寫；套用做完之後已經是第 2 版，不能存成照舊版本寫的等待核准。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));

        // Act
        var (applied, late) = await WhileApplyingAsync(
            scenario, proposed.ChangeSet.Number, () => _host.Changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None));
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
        ItemAssert.Failed(late, FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid);
        Assert.That(late.Failure!.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (IssueCodes.PlanBaseVersionMismatch, "/baseVersion") }));
        Assert.That(listed.Value.Select(c => (c.Number, c.Status)), Is.EqualTo(new[] { (1L, ChangeSetStatus.Applied) }));
    }

    [Test]
    public async Task ApplyAsync_WhileAProposalIsInFlight_WaitsForItAndThenExpiresIt()
    {
        // Arrange：#2 送出到一半（拿著專案的共用鎖、還沒提交）時，#1 開始套用。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] });
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowChanges = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();
        var propose = Task.Run(() => slowChanges.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None));
        await clock.Paused;

        // Act
        Task<Result<ChangeSetDetail>> apply;
        bool appliedWhileProposing;
        try
        {
            apply = Task.Run(() => _host.Changes.ApplyAsync(scenario.Admin, "WEB", first.ChangeSet.Number, CancellationToken.None));
            appliedWhileProposing = await Task.WhenAny(apply, Task.Delay(Grace)) == apply;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(propose, apply);
        var second = await _host.Changes.GetAsync(scenario.Admin, "WEB", propose.Result.Value.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(appliedWhileProposing, Is.False, "套用要等送出提交");
        Assert.That((propose.Result.Succeeded, apply.Result.Succeeded), Is.EqualTo((true, true)));
        Assert.That((propose.Result.Value.ChangeSet.Number, propose.Result.Value.ChangeSet.Status), Is.EqualTo((2L, ChangeSetStatus.Pending)));
        Assert.That((second.Value.ChangeSet.Status, second.Value.CanApply), Is.EqualTo((ChangeSetStatus.Expired, false)));
    }

    /// <summary>
    /// <paramref name="number"/> 的套用在鎖住專案與變更集、讀了時間之後停住；這時發出 <paramref name="other"/>，
    /// 確認它一直等著，再讓套用做完。
    /// </summary>
    private async Task<(Result<ChangeSetDetail> Apply, Result<ChangeSetDetail> Other)> WhileApplyingAsync(
        Scenario scenario, long number, Func<Task<Result<ChangeSetDetail>>> other)
    {
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowChanges = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();
        var apply = Task.Run(() => slowChanges.ApplyAsync(scenario.Admin, "WEB", number, CancellationToken.None));
        await clock.Paused;

        Task<Result<ChangeSetDetail>> pending;
        bool finishedWhileApplying;
        try
        {
            pending = Task.Run(other);
            finishedWhileApplying = await Task.WhenAny(pending, Task.Delay(Grace)) == pending;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(apply, pending);
        Assert.That(finishedWhileApplying, Is.False, "後到的要等套用提交");
        return (apply.Result, pending.Result);
    }
}
