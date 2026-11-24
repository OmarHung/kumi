using PmTool.Abstractions;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Planning;

/// <summary>項目的週期與里程碑：建立、修改、活動與事件、改類型、篩選。</summary>
[TestFixture]
public sealed class ItemPlanningTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_WithCycleAndMilestone_ReturnsAndReadsBackBothIds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 28));

        // Act
        var created = await ItemInAsync(_host, scenario, "WEB", cycle.Id, milestone.Id);
        var read = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((created.CycleId, created.MilestoneId), Is.EqualTo((cycle.Id, milestone.Id)));
            Assert.That((read.Value.CycleId, read.Value.MilestoneId), Is.EqualTo((cycle.Id, milestone.Id)));
            Assert.That((listed.Value.Items.Single().CycleId, listed.Value.Items.Single().MilestoneId), Is.EqualTo((cycle.Id, milestone.Id)));
        }
    }

    [Test]
    public async Task CreateAsync_WithoutThem_HasNoCycleAndNoMilestone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateTaskAsync("WEB");

        // Assert
        Assert.That((created.CycleId, created.MilestoneId), Is.EqualTo(((Guid?)null, (Guid?)null)));
    }

    [Test]
    public async Task CreateAndUpdateAsync_CycleOrMilestoneOfAnotherProjectOrUnknown_IsRejectedWithTheFieldPath()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var foreignCycle = await CreateCycleAsync(_host, scenario.Admin, "OPS", CycleOf("衝刺 1", 0, 13));
        var foreignMilestone = await CreateMilestoneAsync(_host, scenario.Admin, "OPS", MilestoneOf("v1", 28));
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var createCycle = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "x", CycleId = foreignCycle.Id }, CancellationToken.None);
        var createMilestone = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "x", MilestoneId = Guid.NewGuid() }, CancellationToken.None);
        var updateBoth = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { CycleId = Guid.NewGuid(), MilestoneId = foreignMilestone.Id }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(createCycle, FailureKind.Invalid, WorkErrorCodes.ItemInvalidValue);
            Assert.That(createCycle.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "cycleId" }));
            Assert.That(createMilestone.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "milestoneId" }));
            Assert.That(updateBoth.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "cycleId", "milestoneId" }));
        }
    }

    [Test]
    public async Task UpdateAsync_SetsChangesAndClearsTheCycle()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var second = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var set = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = first.Id }, CancellationToken.None);
        var moved = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = second.Id }, CancellationToken.None);
        var cleared = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = new Optional<Guid?>(null) }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(set.Value.CycleId, Is.EqualTo(first.Id));
            Assert.That(moved.Value.CycleId, Is.EqualTo(second.Id));
            Assert.That(cleared.Value.CycleId, Is.Null);
        }
    }

    [Test]
    public async Task UpdateAsync_OnlyTheMilestone_LeavesTheCycleAlone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 28));
        var item = await ItemInAsync(_host, scenario, "WEB", cycleId: cycle.Id);

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { MilestoneId = milestone.Id }, CancellationToken.None);

        // Assert
        Assert.That((updated.Value.CycleId, updated.Value.MilestoneId), Is.EqualTo((cycle.Id, milestone.Id)));
    }

    [Test]
    public async Task UpdateAsync_SameCycleAgain_IsNotAChange()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var item = await ItemInAsync(_host, scenario, "WEB", cycleId: cycle.Id);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = cycle.Id }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(updated.Value.UpdatedAt, Is.EqualTo(item.UpdatedAt));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task UpdateAsync_RecordsCycleAndMilestoneInTheActivityAndTheEvent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 28));
        var item = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = cycle.Id, MilestoneId = milestone.Id }, CancellationToken.None);
        var set = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);
        var setEvent = await EventFeed.LastAsync(_host, scenario);
        // 兩次修改的時間要錯開：同一個時刻的活動依編號排，UUIDv7 在同一毫秒裡的順序是亂的，「最後一筆」可能拿到第一次的。
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { CycleId = new Optional<Guid?>(null) }, CancellationToken.None);
        var cleared = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ActivityLog.Shapes(set), Is.EqualTo(new (string, string?, string?)[]
            {
                ("cycle", null, $"\"{cycle.Id}\""),
                ("milestone", null, $"\"{milestone.Id}\""),
            }));
            Assert.That(setEvent.Kind, Is.EqualTo(WorkEventKind.ItemUpdated));
            Assert.That(setEvent.Item.ChangedFields, Is.EqualTo(new[] { "cycle", "milestone" }));
            Assert.That(ActivityLog.Shapes(cleared), Is.EqualTo(new (string, string?, string?)[] { ("cycle", $"\"{cycle.Id}\"", null) }));
        }
    }

    [Test]
    public async Task DeleteCycle_LeavesNoActivityAndNoEvent_AsKnownLimitation()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var item = await ItemInAsync(_host, scenario, "WEB", cycleId: cycle.Id);
        var eventsBefore = (await EventFeed.OfAsync(_host, scenario)).Count;

        // Act
        await _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Id, CancellationToken.None);

        // Assert
        Assert.That((await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Count, Is.EqualTo(eventsBefore));
    }

    [Test]
    public async Task ChangeTypeAsync_KeepsTheCycleAndTheMilestone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 28));
        var item = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "change_request", Title = "改", CycleId = cycle.Id, MilestoneId = milestone.Id });

        // Act
        var changed = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "task", CancellationToken.None);

        // Assert
        Assert.That(changed.Succeeded, Is.True, () => changed.Failure!.Message);
        Assert.That((changed.Value.CycleId, changed.Value.MilestoneId), Is.EqualTo((cycle.Id, milestone.Id)));
    }

    [Test]
    public async Task ListAsync_FiltersByCycleAndMilestoneAndNone()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var first = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var second = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));
        var foreign = await CreateCycleAsync(_host, scenario.Admin, "OPS", CycleOf("衝刺 1", 0, 13));
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 28));
        var a = await ItemInAsync(_host, scenario, "WEB", first.Id, milestone.Id, title: "A");
        var b = await ItemInAsync(_host, scenario, "WEB", second.Id, title: "B");
        var c = await ItemInAsync(_host, scenario, "WEB", title: "C");

        // Act
        async Task<string[]> KeysAsync(WorkItemQuery query) =>
            [.. (await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None)).Value.Items.Select(i => i.Key)];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await KeysAsync(new WorkItemQuery { CycleId = first.Id }), Is.EqualTo(new[] { a.Key }));
            Assert.That(await KeysAsync(new WorkItemQuery { OnlyWithoutCycle = true }), Is.EqualTo(new[] { c.Key }));
            Assert.That(await KeysAsync(new WorkItemQuery { MilestoneId = milestone.Id }), Is.EqualTo(new[] { a.Key }));
            Assert.That(await KeysAsync(new WorkItemQuery { OnlyWithoutMilestone = true }), Is.EqualTo(new[] { b.Key, c.Key }));
            Assert.That(await KeysAsync(new WorkItemQuery { CycleId = second.Id, OnlyWithoutMilestone = true }), Is.EqualTo(new[] { b.Key }));
            Assert.That(await KeysAsync(new WorkItemQuery { CycleId = foreign.Id }), Is.Empty, "別的專案的週期，結果是空的");
            Assert.That(await KeysAsync(new WorkItemQuery { CycleId = Guid.NewGuid() }), Is.Empty);
            Assert.That(await KeysAsync(new WorkItemQuery { MilestoneId = Guid.NewGuid() }), Is.Empty);
        }
    }

    [Test]
    public async Task ListAsync_CycleAndNoneTogether_IsAQueryError()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var cycle = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { CycleId = Guid.NewGuid(), OnlyWithoutCycle = true }, CancellationToken.None);
        var milestone = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { MilestoneId = Guid.NewGuid(), OnlyWithoutMilestone = true }, CancellationToken.None);

        // Assert
        AssertFailure(cycle, FailureKind.Invalid, WorkErrorCodes.QueryInvalid);
        AssertFailure(milestone, FailureKind.Invalid, WorkErrorCodes.QueryInvalid);
        Assert.That(cycle.Failure!.Details.Single().Path, Is.EqualTo("cycle"));
        Assert.That(milestone.Failure!.Details.Single().Path, Is.EqualTo("milestone"));
    }

    [Test]
    public async Task MyWorkAndSearch_CarryTheCycleId()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var created = await _host.Items.CreateAsync(
            scenario.Admin,
            "WEB",
            new NewWorkItem { Type = "task", Title = "獨特的標題", AssigneeId = scenario.Admin.PersonId, CycleId = cycle.Id },
            CancellationToken.None);

        // Act
        var mine = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        var found = await _host.Items.SearchAsync(scenario.Admin, "獨特的標題", 8, CancellationToken.None);

        // Assert
        Assert.That(mine.Single(i => i.Key == created.Value.Key).CycleId, Is.EqualTo(cycle.Id));
        Assert.That(found.Value.Single().CycleId, Is.EqualTo(cycle.Id));
    }

    [Test]
    public async Task Progress_CountsLiveItemsOfTheCycleAndSumsEstimates()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var other = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 2", 14, 27));
        var open = await ItemInAsync(_host, scenario, "WEB", cycle.Id, estimate: 3, title: "未完成");
        var done = await ItemInAsync(_host, scenario, "WEB", cycle.Id, estimate: 5.5m, title: "完成");
        var noEstimate = await ItemInAsync(_host, scenario, "WEB", cycle.Id, title: "沒有估計");
        var removed = await ItemInAsync(_host, scenario, "WEB", cycle.Id, estimate: 100, title: "刪掉的");
        await ItemInAsync(_host, scenario, "WEB", other.Id, title: "別的週期");
        await _host.Items.TransitionAsync(scenario.Admin, done.Key, "done", CancellationToken.None);
        await _host.Items.DeleteAsync(scenario.Admin, removed.Key, CancellationToken.None);

        // Act
        var cycles = (await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cycles[0].Progress, Is.EqualTo(new ItemProgress(3, 1, 0, 8.5m, 5.5m)));
            Assert.That(cycles[1].Progress, Is.EqualTo(new ItemProgress(1, 0, 0, null, null)));
            Assert.That(open.Key, Is.Not.Null);
            Assert.That(noEstimate.Key, Is.Not.Null);
        }
    }

    [Test]
    public async Task Progress_EstimatedItemsButNoneDone_EstimateDoneIsZeroNotNull()
    {
        // Arrange：有估計但一個都還沒完成，和「根本沒有估計」要分得出來。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var estimated = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("有估計", 0, 13));
        var unestimated = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("沒估計", 14, 27));
        await ItemInAsync(_host, scenario, "WEB", estimated.Id, estimate: 3, title: "進行中");
        await ItemInAsync(_host, scenario, "WEB", unestimated.Id, title: "沒有估計");

        // Act
        var cycles = (await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cycles[0].Progress, Is.EqualTo(new ItemProgress(1, 0, 0, 3, 0)));
            Assert.That(cycles[1].Progress, Is.EqualTo(new ItemProgress(1, 0, 0, null, null)));
        }
    }

    [Test]
    public async Task Progress_CountsCanceledAndMilestoneItems()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("BUG", "bug_tracking");
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "BUG", MilestoneOf("v1", 28));
        var canceled = await scenario.CreateItemAsync("BUG", new NewWorkItem { Type = "bug", Title = "不處理", MilestoneId = milestone.Id, Estimate = 2 });
        await scenario.CreateItemAsync("BUG", new NewWorkItem { Type = "bug", Title = "處理中", MilestoneId = milestone.Id, Estimate = 1 });
        var moved = await _host.Items.TransitionAsync(scenario.Admin, canceled.Key, "wontfix", CancellationToken.None);

        // Act
        var milestones = (await _host.Planning.ListMilestonesAsync(scenario.Admin, "BUG", CancellationToken.None)).Value;

        // Assert
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        Assert.That(milestones.Single().Progress, Is.EqualTo(new ItemProgress(2, 0, 1, 3, 0)));
    }
}
