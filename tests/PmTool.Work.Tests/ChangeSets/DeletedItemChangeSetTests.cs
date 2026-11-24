using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 刪除的項目在變更集裡完全不算：影響的數量不含它、擋不住套用，套用時也不搬它，它停在刪除當時的樣子。
/// </summary>
[TestFixture]
public sealed class DeletedItemChangeSetTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ApplyAsync_StateRemoved_CountsAndMovesOnlyLiveItems()
    {
        // Arrange：兩個任務在進行中，刪掉其中一個；拿掉「進行中」，搬到「待辦」。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var live = await InProgressAsync(scenario, "留著的");
        var deleted = await InProgressAsync(scenario, "刪掉的");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await DeleteAsync(scenario, deleted.Key);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "todo")));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var moved = await _host.Items.GetAsync(scenario.Admin, live.Key, CancellationToken.None);
        var untouched = await DeletedItems.ReadAsync(_host, scenario, deleted.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposed.Impacts.Select(i => (i.Impact, i.Affected)), Is.EqualTo(new[] { ((DataImpact)new StateMoveImpact("task_flow", "doing", "todo"), 1L) }));
            Assert.That(moved.Value.State, Is.EqualTo("todo"));
            Assert.That(untouched.State, Is.EqualTo("doing"), "刪除的項目停在刪除當時的狀態");
            Assert.That(untouched.ActivityKinds, Is.EquivalentTo(new[] { "created", "transitioned", "deleted" }), "沒有搬移的活動");
        }
    }

    [Test]
    public async Task ApplyAsync_FieldDroppedOrDetached_CountsAndArchivesOnlyLiveItems()
    {
        // Arrange：三個任務都有摘要與備註，刪掉一個；刪除摘要這個欄位，任務的欄位清單拿掉備註。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var items = new List<WorkItem>();
        foreach (var title in new[] { "一", "二", "三" })
        {
            items.Add(await scenario.CreateItemAsync("WEB", new NewWorkItem
            {
                Type = "task",
                Title = title,
                Fields = Scenario.Fields(("summary", $"摘要{title}"), ("notes", $"備註{title}")),
            }));
        }

        await DeleteAsync(scenario, items[1].Key);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario,
            "WEB",
            b => b.WithoutField("summary").ItemType("task", t => t with { Fields = [.. t.Fields.Where(f => f != "notes")] }),
            BlueprintEdits.Drops("summary"));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var archived = await ArchivedValues.ListAsync(_host, scenario);

        // Assert
        Assert.That(
            proposed.Impacts.Select(i => (i.Impact, i.Affected)),
            Is.EqualTo(new[] { ((DataImpact)new FieldDropImpact("summary"), 2L), (new FieldDetachImpact("task", "notes"), 2L) }));
        Assert.That(archived.Select(a => (a.ItemNumber, a.Field)), Is.EqualTo(new[] { (1L, "notes"), (1L, "summary"), (3L, "notes"), (3L, "summary") }));
    }

    [Test]
    public async Task ApplyAsync_ConversionThatAbortsOnFailure_IgnoresTheValueOfADeletedItem()
    {
        // Arrange：摘要改成數字、轉不過去就擋下；轉不過去的那一個已經刪掉了。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var live = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "數字", Fields = Scenario.Fields(("summary", "42")) });
        var deleted = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "文字", Fields = Scenario.Fields(("summary", "abc")) });
        await DeleteAsync(scenario, deleted.Key);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.Field("summary", f => f with { Type = FieldType.Number }), BlueprintEdits.Converts("summary", ConversionFailurePolicy.Abort));

        // Act
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var converted = await _host.Items.GetAsync(scenario.Admin, live.Key, CancellationToken.None);

        // Assert
        Assert.That((proposed.Impacts.Single().Affected, proposed.Blockers.Count, proposed.CanApply), Is.EqualTo((1L, 0, true)));
        Assert.That(converted.Value.Fields["summary"].GetDecimal(), Is.EqualTo(42m));
    }

    [Test]
    public async Task ApplyAsync_TypeRemovedWhoseItemsAreAllDeleted_IsNotBlocked()
    {
        // Arrange：兩個需求變更單都刪掉了，其中一個底下有刪掉的任務。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var first = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        var second = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "改結帳流程" });
        var child = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "子任務", ParentKey = first.Key });
        foreach (var key in new[] { child.Key, first.Key, second.Key })
        {
            await DeleteAsync(scenario, key);
        }

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", WithoutChangeRequests);
        var applied = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(
            proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
            Is.EqualTo(new[] { ((DataImpact)new ItemTypeRemovalImpact("change_request"), 0L, false) }));
        Assert.That(applied.Succeeded, Is.True, () => ChangeSetDrafts.Describe(applied.Failure!));
    }

    [Test]
    public async Task ApplyAsync_ParentNoLongerAllowed_CountsOnlyLiveChildren()
    {
        // Arrange：任務不能再掛在需求變更單底下；掛著的兩個任務刪掉一個。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var changeRequest = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "留著的", ParentKey = changeRequest.Key });
        var deleted = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "刪掉的", ParentKey = changeRequest.Key });
        await DeleteAsync(scenario, deleted.Key);

        // Act
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.ItemType("task", t => t with { Parents = ["requirement"] }));

        // Assert
        Assert.That(
            proposed.Impacts.Select(i => (i.Impact, i.Affected, i.BlocksApply)),
            Is.EqualTo(new[] { ((DataImpact)new ParentRemovalImpact("task", "change_request"), 1L, true) }));
    }

    private async Task<WorkItem> InProgressAsync(Scenario scenario, string title)
    {
        var item = await scenario.CreateTaskAsync("WEB", title);
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        return moved.Value;
    }

    private async Task DeleteAsync(Scenario scenario, string itemKey)
    {
        var result = await _host.Items.DeleteAsync(scenario.Admin, itemKey, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    /// <summary>拿掉需求變更單這個類型，連同引用它的上層與檢視的篩選。</summary>
    private static ProjectBlueprint WithoutChangeRequests(ProjectBlueprint blueprint) =>
        blueprint with
        {
            ItemTypes = [.. blueprint.ItemTypes
                .Where(t => t.Key != "change_request")
                .Select(t => t with { Parents = [.. t.Parents.Where(p => p != "change_request")] })],
            Views = [.. blueprint.Views.Select(v => v.Filter is { } filter
                ? v with { Filter = filter with { ItemTypes = [.. filter.ItemTypes.Where(t => t != "change_request")] } }
                : v)],
        };
}
