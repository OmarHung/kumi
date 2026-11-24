using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>變更集的一生：等待核准 → 套用、拒絕或過期。誰可以讀、套用、拒絕；決定之後就不能再改。</summary>
[TestFixture]
public sealed class ChangeSetLifecycleTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RejectAsync_ByTheProposerWithoutApply_RejectsItForGood()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var (planner, _) = await PlannersAsync(scenario);
        var proposed = await ProposeRemovingViewsAsync(scenario, planner);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var rejected = await _host.Changes.RejectAsync(planner, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var again = await _host.Changes.RejectAsync(planner, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var apply = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(rejected.Succeeded, Is.True, () => ChangeSetDrafts.Describe(rejected.Failure!));
        Assert.That(
            (rejected.Value.ChangeSet.Status, rejected.Value.ChangeSet.DecidedBy, rejected.Value.ChangeSet.DecidedAt, rejected.Value.CanApply),
            Is.EqualTo((ChangeSetStatus.Rejected, (Guid?)planner.PersonId, (DateTimeOffset?)_host.Clock.GetUtcNow(), false)));
        ItemAssert.Failed(again, FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending);
        ItemAssert.Failed(apply, FailureKind.Conflict, WorkErrorCodes.ChangeSetNotPending);
        Assert.That(apply.Failure!.Message, Is.EqualTo("變更集 #2 已經被拒絕了，不能再套用或拒絕。"));
    }

    [Test]
    public async Task RejectAsync_SomeoneElseWithoutApply_IsForbiddenAndItStaysPending()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var (planner, otherPlanner) = await PlannersAsync(scenario);
        var proposed = await ProposeRemovingViewsAsync(scenario, planner);

        // Act
        var result = await _host.Changes.RejectAsync(otherPlanner, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var readBack = await _host.Changes.GetAsync(otherPlanner, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That((readBack.Value.ChangeSet.Status, readBack.Value.CanApply), Is.EqualTo((ChangeSetStatus.Pending, false)), "沒有核准權限的人看不到套用");
    }

    [Test]
    public async Task RejectAsync_SomeoneWithApply_CanRejectAnyonesChangeSet()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var (planner, _) = await PlannersAsync(scenario);
        var proposed = await ProposeRemovingViewsAsync(scenario, planner);

        // Act
        var result = await _host.Changes.RejectAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That((result.Value.ChangeSet.Status, result.Value.ChangeSet.DecidedBy), Is.EqualTo((ChangeSetStatus.Rejected, (Guid?)scenario.Admin.PersonId)));
    }

    [Test]
    public async Task ApplyAsync_OtherPendingChangeSets_BecomeExpired()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        var second = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", first.ChangeSet.Number);

        // Act
        var expired = await _host.Changes.GetAsync(scenario.Admin, "WEB", second.ChangeSet.Number, CancellationToken.None);
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var apply = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", second.ChangeSet.Number, CancellationToken.None);
        var reject = await _host.Changes.RejectAsync(scenario.Admin, "WEB", second.ChangeSet.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (expired.Value.ChangeSet.Status, expired.Value.ChangeSet.DecidedBy, expired.Value.ChangeSet.DecidedAt, expired.Value.ChangeSet.AppliedVersion),
                Is.EqualTo((ChangeSetStatus.Expired, (Guid?)null, (DateTimeOffset?)_host.Clock.GetUtcNow(), (long?)null)));
            Assert.That((expired.Value.Blockers.Count, expired.Value.CanApply), Is.EqualTo((0, false)));
            Assert.That(expired.Value.Changes.Select(c => c.Key), Is.EqualTo(new[] { "doing" }), "差異照當時的基準版本算");
            Assert.That(
                listed.Value.Select(c => (c.Number, c.Status)),
                Is.EqualTo(new[] { (2L, ChangeSetStatus.Expired), (1L, ChangeSetStatus.Applied) }));
            Assert.That(apply.Failure!.Message, Is.EqualTo("變更集 #2 已經過期了（別的變更集先套用了），不能再套用或拒絕。"));
            Assert.That(reject.Failure!.Code, Is.EqualTo(WorkErrorCodes.ChangeSetNotPending));
        }
    }

    [Test]
    public async Task ApplyAsync_BaseVersionNoLongerCurrent_IsOutdatedAndTheExpiryIsKept()
    {
        // Arrange：基準版本在送出之後被換掉（不是經過套用），等待核准的這份已經過時。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        var (blueprint, _) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        await StoredBlueprints.PublishAsync(_host, scenario, "WEB", blueprint);

        // Act
        var result = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.ChangeSetOutdated);
        Assert.That(result.Failure!.Message, Is.EqualTo("變更集 #1 是照第 1 版的設定寫的，但目前已經是第 2 版，所以它已經過期了；請照目前的設定重新提出。"));
        Assert.That(readBack.Value.ChangeSet.Status, Is.EqualTo(ChangeSetStatus.Expired));
    }

    [Test]
    public async Task GetAsync_PendingWhoseBaseVersionWasReplaced_IsReadAsExpired()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        var (blueprint, _) = await ChangeSetDrafts.CurrentAsync(scenario, "WEB");
        await StoredBlueprints.PublishAsync(_host, scenario, "WEB", blueprint);

        // Act
        var listed = await _host.Changes.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Single().Status, Is.EqualTo(ChangeSetStatus.Expired));
        Assert.That((readBack.Value.ChangeSet.Status, readBack.Value.Impacts.Count, readBack.Value.CanApply), Is.EqualTo((ChangeSetStatus.Expired, 0, false)));
    }

    [Test]
    public async Task ApplyAsync_WithoutApplyPermissionOrUnknown_IsRefused()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var proposed = await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        var outsider = new Scenario(_host).Admin;

        // Act
        var forbidden = await _host.Changes.ApplyAsync(editor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var unknown = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", 99, CancellationToken.None);
        var hidden = await _host.Changes.ApplyAsync(outsider, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var detail = await _host.Changes.GetAsync(editor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        ItemAssert.Failed(forbidden, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(forbidden.Failure!.Message, Is.EqualTo("你在這個專案沒有核准套用設定的修改的權限。"));
        ItemAssert.Failed(unknown, FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound);
        Assert.That(unknown.Failure!.Message, Is.EqualTo("找不到變更集 #99。"));
        ItemAssert.Failed(hidden, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        Assert.That((detail.Value.ChangeSet.Status, detail.Value.CanApply), Is.EqualTo((ChangeSetStatus.Pending, false)));
    }

    [Test]
    public async Task ReadingChangeSets_ViewerCanButARoleWithoutBlueprintReadCannot()
    {
        // Arrange：內建的檢視者有 blueprint.read；接案專案的客戶沒有。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var client = await scenario.MemberAsync("WEB", "client");
        var proposed = await ProposeRemovingViewsAsync(scenario, scenario.Admin);

        // Act
        var viewerGet = await _host.Changes.GetAsync(viewer, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var clientGet = await _host.Changes.GetAsync(client, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var clientList = await _host.Changes.ListAsync(client, "WEB", CancellationToken.None);
        var stranger = await _host.Changes.ListAsync(scenario.Person(), "WEB", CancellationToken.None);
        var unknown = await _host.Changes.GetAsync(viewer, "WEB", 42, CancellationToken.None);

        // Assert
        Assert.That((viewerGet.Value.ChangeSet.Number, viewerGet.Value.CanApply), Is.EqualTo((1L, false)));
        ItemAssert.Failed(clientGet, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(clientGet.Failure!.Message, Is.EqualTo("你在這個專案沒有查看專案設定的權限。"));
        ItemAssert.Failed(clientList, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(stranger, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
        ItemAssert.Failed(unknown, FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound);
    }

    [Test]
    public async Task RejectAsync_RoleWithoutBlueprintRead_IsForbiddenWhetherOrNotTheChangeSetExists()
    {
        // Arrange：接案專案的客戶沒有 blueprint.read。#1 存在、#99 不存在，兩個回覆要一樣，才看不出哪些編號存在。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        var proposed = await ProposeRemovingViewsAsync(scenario, scenario.Admin);

        // Act
        var existing = await _host.Changes.RejectAsync(client, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var missing = await _host.Changes.RejectAsync(client, "WEB", 99, CancellationToken.None);
        var readBack = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        ItemAssert.Failed(existing, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(missing, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(existing.Failure!.Message, Is.EqualTo("你在這個專案沒有查看專案設定的權限。"));
        Assert.That(missing.Failure!.Message, Is.EqualTo(existing.Failure.Message));
        Assert.That(readBack.Value.ChangeSet.Status, Is.EqualTo(ChangeSetStatus.Pending));
    }

    [Test]
    public async Task RejectAsync_UnknownNumberOrHiddenProject_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var unknown = await _host.Changes.RejectAsync(scenario.Admin, "WEB", 7, CancellationToken.None);
        var hidden = await _host.Changes.RejectAsync(scenario.Person(), "WEB", 1, CancellationToken.None);

        // Assert
        ItemAssert.Failed(unknown, FailureKind.NotFound, WorkErrorCodes.ChangeSetNotFound);
        ItemAssert.Failed(hidden, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task ListAsync_NewestFirst()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        await ProposeRemovingViewsAsync(scenario, scenario.Admin);
        await _host.Changes.RejectAsync(scenario.Admin, "WEB", first.ChangeSet.Number, CancellationToken.None);
        await ProposeRemovingViewsAsync(scenario, scenario.Admin);

        // Act
        var listed = await _host.Changes.ListAsync(scenario.Admin, "web", CancellationToken.None);

        // Assert
        Assert.That(
            listed.Value.Select(c => (c.Number, c.Status)),
            Is.EqualTo(new[] { (3L, ChangeSetStatus.Pending), (2L, ChangeSetStatus.Pending), (1L, ChangeSetStatus.Rejected) }));
    }

    /// <summary>
    /// 開專案並套用一份加上「規劃者」角色的變更集（第 1 號）：規劃者能送出、不能套用。回傳兩位規劃者。
    /// </summary>
    private async Task<(Actor Planner, Actor OtherPlanner)> PlannersAsync(Scenario scenario)
    {
        await scenario.CreateProjectAsync("WEB");
        var role = new RoleDefinition { Key = "planner", Name = "規劃者", Permissions = ["item.read", "blueprint.read", "blueprint.propose"] };
        var withRole = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Roles = [.. b.Roles, role] });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", withRole.ChangeSet.Number);
        return (await scenario.MemberAsync("WEB", "planner"), await scenario.MemberAsync("WEB", "planner"));
    }

    private static Task<ChangeSetDetail> ProposeRemovingViewsAsync(Scenario scenario, Actor proposer) =>
        ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Views = [] }, proposer: proposer);
}
