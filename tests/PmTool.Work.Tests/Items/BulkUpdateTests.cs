using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>批次修改：每一筆各自走單一項目的規則，成功與失敗分開，順序照請求，重複只做一次。</summary>
[TestFixture]
public sealed class BulkUpdateTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_SeveralItems_ChangesEachAndKeepsTheRequestOrder()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var assignee = await scenario.MemberAsync("WEB", "editor");
        var items = new[] { await scenario.CreateTaskAsync("WEB", "一"), await scenario.CreateTaskAsync("WEB", "二"), await scenario.CreateTaskAsync("WEB", "三") };

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin,
            "WEB",
            new BulkItemUpdate
            {
                Keys = [items[2].Key, items[0].Key, items[1].Key],
                AssigneeId = new Optional<Guid?>(assignee.PersonId),
                Priority = Priority.High,
                DueDate = new Optional<DateOnly?>(new DateOnly(2026, 12, 1)),
            },
            CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Updated.Select(i => i.Key), Is.EqualTo(new[] { "WEB-3", "WEB-1", "WEB-2" }));
            Assert.That(result.Value.Failed, Is.Empty);
            Assert.That(result.Value.Updated.Select(i => (i.AssigneeId, i.Priority, i.DueDate)), Is.All.EqualTo(((Guid?)assignee.PersonId, Priority.High, (DateOnly?)new DateOnly(2026, 12, 1))));
        }
    }

    [Test]
    public async Task UpdateAsync_NullValue_ClearsThatPropertyAndLeavesTheOthersAlone()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "清掉",
            Priority = Priority.High,
            DueDate = new DateOnly(2026, 12, 1),
        });

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], DueDate = new Optional<DateOnly?>(null) }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Updated.Single().DueDate, Is.Null);
        Assert.That(result.Value.Updated.Single().Priority, Is.EqualTo(Priority.High));
    }

    [Test]
    public async Task UpdateAsync_ValueAlreadyTheSame_CountsAsUpdatedWithoutAnActivity()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "沒變", Priority = Priority.High });

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], Priority = Priority.High }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Value.Updated.Select(i => i.Key), Is.EqualTo(new[] { item.Key }));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task UpdateAsync_KeyFromAnotherProjectOrMalformed_IsThatItemsFailureAndTheOthersStillChange()
    {
        // Arrange
        var scenario = await ProjectAsync();
        await scenario.CreateProjectAsync("OTH");
        var mine = await scenario.CreateTaskAsync("WEB", "這個專案");
        var other = await scenario.CreateTaskAsync("OTH", "別的專案");

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [other.Key, mine.Key, "oops", "WEB-99"], Priority = Priority.Low }, CancellationToken.None);
        var untouched = await _host.Items.GetAsync(scenario.Admin, other.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Updated.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[]
            {
                ("OTH-1", WorkErrorCodes.ItemNotFound),
                ("oops", WorkErrorCodes.ItemNotFound),
                ("WEB-99", WorkErrorCodes.ItemNotFound),
            }));
            Assert.That(untouched.Value.Priority, Is.EqualTo(Priority.None), "別的專案的項目沒有被改");
        }
    }

    [Test]
    public async Task UpdateAsync_DuplicateKeysInAnyCase_AreDoneOnce()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key, "web-1", item.Key], Priority = Priority.High }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Value.Updated.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
        Assert.That(activities.Count(a => a.Kind == ActivityKind.Updated), Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateAsync_AssigneeWhoIsNotAMember_FailsEveryItemWithTheSingleItemRule()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], AssigneeId = new Optional<Guid?>(Guid.NewGuid()) }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Updated, Is.Empty);
        Assert.That(result.Value.Failed.Single().Failure.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Value.Failed.Single().Failure.Details.Select(d => d.Path), Is.EqualTo(new[] { "assignee" }));
    }

    [Test]
    public async Task UpdateAsync_State_MovesEachItemAndRecordsATransitionPerItem()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var first = await scenario.CreateTaskAsync("WEB", "一");
        var second = await scenario.CreateTaskAsync("WEB", "二");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [first.Key, second.Key], State = "doing" }, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, second.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Updated.Select(i => (i.Key, i.State, i.StateCategory)), Is.EqualTo(new[]
            {
                ("WEB-1", "doing", StateCategory.InProgress),
                ("WEB-2", "doing", StateCategory.InProgress),
            }));
            Assert.That((activity.Kind, activity.FromState, activity.ToState), Is.EqualTo((ActivityKind.Transitioned, "todo", "doing")));
        }
    }

    [Test]
    public async Task UpdateAsync_StateTheItemIsAlreadyIn_CountsAsUpdatedWithoutAnActivity()
    {
        // Arrange：批次裡「值沒變也算成功」也包含狀態；單一項目的改狀態照舊說「已經是…」（使用者 2026-11-18 實測選了整頁改成已關閉）。
        var scenario = await ProjectAsync();
        var already = await scenario.CreateTaskAsync("WEB", "已經在做");
        var moved = await _host.Items.TransitionAsync(scenario.Admin, already.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        var other = await scenario.CreateTaskAsync("WEB", "還沒開始");

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [already.Key, other.Key], State = "doing" }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, already.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Updated.Select(i => (i.Key, i.State)), Is.EqualTo(new[] { (already.Key, "doing"), (other.Key, "doing") }));
            Assert.That(result.Value.Failed, Is.Empty);
            Assert.That(activities.Count(a => a.Kind == ActivityKind.Transitioned), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_StateTheWorkflowDoesNotAllowForSomeItems_FailsOnlyThoseWithTheTransitionCode()
    {
        // Arrange：接案專案的交付流程只能一步一步走：「需求確認」可以到「報價」，「設計」不行直接回「報價」。
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var confirming = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "確認中" });
        var designing = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "設計中" });
        foreach (var state in new[] { "quoting", "designing" })
        {
            var step = await _host.Items.TransitionAsync(scenario.Admin, designing.Key, state, CancellationToken.None);
            Assert.That(step.Succeeded, Is.True, () => step.Failure!.Message);
        }

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [designing.Key, confirming.Key], State = "quoting" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Updated.Select(i => (i.Key, i.State)), Is.EqualTo(new[] { ("WEB-1", "quoting") }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { ("WEB-2", WorkErrorCodes.TransitionNotAllowed) }));
        }
    }

    [Test]
    public async Task UpdateAsync_DeletedItem_IsThatItemsNotFound()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var live = await scenario.CreateTaskAsync("WEB", "活著");
        var deleted = await scenario.CreateTaskAsync("WEB", "刪了");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, deleted.Key);

        // Act
        var result = await _host.Bulk.UpdateAsync(
            scenario.Admin, "WEB", new BulkItemUpdate { Keys = [deleted.Key, live.Key], Priority = Priority.Low }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Updated.Select(i => i.Key), Is.EqualTo(new[] { live.Key }));
        Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { (deleted.Key, WorkErrorCodes.ItemNotFound) }));
    }

    private async Task<Scenario> ProjectAsync(string template = Scenario.SimpleTasks)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        return scenario;
    }
}
