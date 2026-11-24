using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 刪除的項目不跟著變更集搬移，復原時才照現在的藍圖整理：類型不在了就擋下，
/// 狀態、自訂欄位、上層不合格的部分搬走或封存，並記在 Restored 活動裡。
/// </summary>
[TestFixture]
public sealed class ItemRestoreMigrationTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RestoreAsync_TypeRemovedByAChangeSet_IsTypeRemovedWithTheTypeKeyAndStaysDeleted()
    {
        // Arrange：需求變更單刪除之後，藍圖拿掉這個類型。
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ApplyAsync(scenario, WithoutChangeRequests);

        // Act
        var result = await _host.Items.RestoreAsync(scenario.Admin, item.Key, CancellationToken.None);
        var stillDeleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.ItemTypeRemoved);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Is.EqualTo("藍圖已經沒有「change_request」這個類型，無法復原。"));
            Assert.That(stillDeleted.Select(i => (i.Key, i.Type)), Is.EqualTo(new[] { ("WEB-1", "change_request") }));
        }
    }

    [Test]
    public async Task RestoreAsync_StateRemovedByAChangeSet_MovesToTheFirstStateAndRecordsFromAndTo()
    {
        // Arrange：任務停在「進行中」就刪除，之後藍圖拿掉「進行中」（還在的項目搬去「完成」）。
        var scenario = await ProjectAsync(Scenario.SimpleTasks);
        var item = await scenario.CreateTaskAsync("WEB", "做到一半");
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True);
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ApplyAsync(scenario, b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "done")));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);
        var restoredEvent = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((restored.State, restored.StateCategory), Is.EqualTo(("todo", StateCategory.NotStarted)));
            Assert.That((activity.Kind, activity.FromState, activity.ToState), Is.EqualTo((ActivityKind.Restored, "doing", "todo")));
            Assert.That(activity.Changes, Is.Empty);
            Assert.That(restoredEvent.Item.ToState!.Key, Is.EqualTo("todo"));
        }
    }

    [Test]
    public async Task RestoreAsync_FieldRemovedOrNoLongerOnTheType_ArchivesTheValuesAndRecordsOneChangeEach()
    {
        // Arrange：摘要整個刪掉，備註只從任務的欄位清單拿掉；項目兩個都有值，另一個欄位（分類）還在。
        var scenario = await ProjectAsync(Scenario.SimpleTasks);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "有欄位",
            Fields = Scenario.Fields(("summary", "短摘要"), ("notes", "很長的備註"), ("category", "feature")),
        });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ApplyAsync(
            scenario,
            b => b.WithoutField("summary").ItemType("task", t => t with { Fields = [.. t.Fields.Where(f => f != "notes")] }),
            BlueprintEdits.Drops("summary"));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);
        var archived = await ArchivedValues.ByActivityAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Fields.Keys, Is.EqualTo(new[] { "category" }));
            Assert.That(
                ActivityLog.Shapes(activity),
                Is.EqualTo(new (string, string?, string?)[] { ("notes", null, null), ("summary", "\"短摘要\"", null) }));
            Assert.That(archived.Select(a => (a.ItemNumber, a.Field, a.Value, a.ActivityId, a.ActivityKind)), Is.EqualTo(new[]
            {
                (1L, "notes", "\"很長的備註\"", activity.Id, "restored"),
                (1L, "summary", "\"短摘要\"", activity.Id, "restored"),
            }));
        }
    }

    [Test]
    public async Task RestoreAsync_ValueNoLongerFitsTheFieldDefinition_ArchivesOnlyThatValue()
    {
        // Arrange：摘要（文字）刪除之後改成數字欄位；存著的是文字，不符合新的型別，分類沒變。
        var scenario = await ProjectAsync(Scenario.SimpleTasks);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "換型別",
            Fields = Scenario.Fields(("summary", "42"), ("category", "feature")),
        });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ApplyAsync(
            scenario,
            b => b.Field("summary", f => f with { Type = FieldType.Number }),
            BlueprintEdits.Converts("summary", ConversionFailurePolicy.LeaveEmpty));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Fields.Keys, Is.EqualTo(new[] { "category" }));
            Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"42\"", null) }));
        }
    }

    [Test]
    public async Task RestoreAsync_ValueStillFitsTheFieldDefinition_KeepsItAndRecordsNoChanges()
    {
        // Arrange：藍圖改過別的欄位，這個項目的值都還合格。
        var scenario = await ProjectAsync(Scenario.SimpleTasks);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "沒變",
            Fields = Scenario.Fields(("category", "feature"), ("story_points", 5)),
        });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ApplyAsync(scenario, b => b.Field("blocked", f => f with { Name = "被卡住" }));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Fields.Keys, Is.EquivalentTo(new[] { "category", "story_points" }));
            Assert.That(activity.Changes, Is.Empty);
        }
    }

    [Test]
    public async Task RestoreAsync_ParentTypeNoLongerAllowed_RemovesTheParentAndRecordsIt()
    {
        // Arrange：任務掛在需求變更單底下就刪除；之後任務只能掛在需求底下。
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var changeRequest = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "子任務", ParentKey = changeRequest.Key });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, task.Key);
        await ApplyAsync(scenario, b => b.ItemType("task", t => t with { Parents = ["requirement"] }));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, task.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);
        var parentStillThere = await _host.Items.GetAsync(scenario.Admin, changeRequest.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.ParentKey, Is.Null);
            Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[] { ("parent", "\"WEB-1\"", null) }));
            Assert.That(parentStillThere.Succeeded, Is.True);
        }
    }

    private async Task<Scenario> ProjectAsync(string template)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        return scenario;
    }

    private static async Task ApplyAsync(Scenario scenario, Func<ProjectBlueprint, ProjectBlueprint> change, MigrationHints? hints = null)
    {
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", change, hints);
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
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
