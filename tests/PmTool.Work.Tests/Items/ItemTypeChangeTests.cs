using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 改類型：狀態由伺服器決定，新類型沒有的欄位值封存，上下層要符合新類型，留一筆 type_changed。
/// 用 agency_project：需求與需求變更單用交付流程，任務用任務流程、可以掛在需求或需求變更單底下。
/// </summary>
[TestFixture]
public sealed class ItemTypeChangeTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ChangeTypeAsync_WritesAnEventInTheSameTransaction_SoReadersOfTheEventStreamSeeIt()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入", AssigneeId = scenario.Admin.PersonId });
        var before = (await EventFeed.OfAsync(_host, scenario)).Count;

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "change_request", CancellationToken.None);
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That(events, Has.Count.EqualTo(before + 1));
        var written = events[^1];
        Assert.That((written.Kind, written.Item.ItemKey, written.Item.AssigneeId), Is.EqualTo((WorkEventKind.ItemUpdated, item.Key, scenario.Admin.PersonId)));
        Assert.That(written.Item.ChangedFields, Does.Contain("item_type"));
    }

    [Test]
    public async Task ChangeTypeAsync_SameType_WritesNoEvent()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var before = (await EventFeed.OfAsync(_host, scenario)).Count;

        // Act
        await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "requirement", CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Count, Is.EqualTo(before));
    }

    [Test]
    public async Task ChangeTypeAsync_SameWorkflow_KeepsTheStateAndFieldsAndRecordsOnlyTheType()
    {
        // Arrange：需求停在「報價」，報價金額兩種類型都有。
        var scenario = await AgencyAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "requirement",
            Title = "會員登入",
            Fields = Scenario.Fields(("quote_amount", 1200)),
        });
        await TransitionAsync(scenario, item.Key, "quoting");
        _host.Clock.Advance(TimeSpan.FromMinutes(3));

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "change_request", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        var changed = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((changed.Type, changed.State, changed.StateCategory), Is.EqualTo(("change_request", "quoting", StateCategory.NotStarted)));
            Assert.That(changed.Fields["quote_amount"].GetDecimal(), Is.EqualTo(1200m));
            Assert.That(changed.UpdatedAt, Is.EqualTo(_host.Clock.GetUtcNow()));
            Assert.That((activity.Kind, activity.ActorId, activity.At), Is.EqualTo((ActivityKind.TypeChanged, editor.PersonId, changed.UpdatedAt)));
            Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[] { ("item_type", "\"requirement\"", "\"change_request\"") }));
            Assert.That((activity.FromState, activity.ToState), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That(await ArchivedValues.ByActivityAsync(_host, scenario), Is.Empty);
        }
    }

    [Test]
    public async Task ChangeTypeAsync_AcrossWorkflows_MovesToTheFirstStateOfTheSameCategoryAndArchivesTheFieldsTheNewTypeLacks()
    {
        // Arrange：需求變更單停在「開發」（進行中類），填了變更原因（長文字）、影響程度與報價金額；任務一個自訂欄位都沒有。
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "change_request",
            Title = "加上社群登入",
            Fields = Scenario.Fields(("change_reason", "客戶要加社群登入"), ("impact", "high"), ("quote_amount", 1200)),
        });
        await TransitionAsync(scenario, item.Key, "quoting", "designing", "developing");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "task", CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);
        var archived = await ArchivedValues.ByActivityAsync(_host, scenario);
        var oldField = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Fields = Scenario.Fields(("quote_amount", 10)) }, CancellationToken.None);
        var done = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "done", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((readBack.Value.Type, readBack.Value.State, readBack.Value.StateCategory), Is.EqualTo(("task", "doing", StateCategory.InProgress)));
            Assert.That(readBack.Value.Fields, Is.Empty);
            Assert.That(Shape(result.Value), Is.EqualTo(Shape(readBack.Value)));
            Assert.That(activity.Kind, Is.EqualTo(ActivityKind.TypeChanged));
            Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[]
            {
                ("item_type", "\"change_request\"", "\"task\""),
                ("change_reason", null, null),
                ("impact", "\"high\"", null),
                ("quote_amount", "1200", null),
            }));
            Assert.That((activity.FromState, activity.ToState), Is.EqualTo(("developing", "doing")));
            Assert.That(archived, Is.EqualTo(new[]
            {
                (1L, "change_reason", "\"客戶要加社群登入\"", activity.Id, "type_changed"),
                (1L, "impact", "\"high\"", activity.Id, "type_changed"),
                (1L, "quote_amount", "1200", activity.Id, "type_changed"),
            }));
            Assert.That(await ArchivedValues.ListAsync(_host, scenario), Is.Empty, "不是變更集封存的");
        }

        ItemAssert.Invalid(oldField, WorkErrorCodes.ItemUnknownField, "quote_amount");
        Assert.That(done.Succeeded, Is.True, "之後照新類型的流程走");
    }

    [Test]
    public async Task ChangeTypeAsync_ToTheSameType_ReturnsTheItemAsItIsWithoutAnActivity()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        _host.Clock.Advance(TimeSpan.FromMinutes(3));

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "requirement", CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(Shape(result.Value), Is.EqualTo(Shape(item)));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [TestCase("bug")]
    [TestCase("")]
    [TestCase(null)]
    public async Task ChangeTypeAsync_TypeTheProjectDoesNotHave_IsUnknownType(string? type)
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, type!, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownType, "type");
    }

    [TestCase("commenter")]
    [TestCase("client")]
    public async Task ChangeTypeAsync_RoleWithoutItemUpdate_IsForbidden(string role)
    {
        // Arrange
        var scenario = await AgencyAsync();
        var member = await scenario.MemberAsync("WEB", role);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });

        // Act
        var result = await _host.Items.ChangeTypeAsync(member, item.Key, "change_request", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task ChangeTypeAsync_ParentTheNewTypeCannotHangUnder_IsParentNotAllowedAndChangesNothing()
    {
        // Arrange：需求只能放在最上層。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁", ParentKey = requirement.Key });

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "requirement", CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemParentNotAllowed, "parent");
        Assert.That(result.Failure!.Message, Does.Contain("「需求」"));
        Assert.That(Shape(readBack.Value), Is.EqualTo(Shape(task)));
    }

    [Test]
    public async Task ChangeTypeAsync_ChildrenTheNewTypeCannotHold_IsChildrenNotAllowedForEachLiveChild()
    {
        // Arrange：需求變更單底下三個任務，刪掉中間那個；任務底下不能再掛任務。
        var scenario = await AgencyAsync();
        var changeRequest = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        foreach (var title in new[] { "登入按鈕", "刪掉的", "帳號綁定" })
        {
            await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = title, ParentKey = changeRequest.Key });
        }

        var deleted = await _host.Items.DeleteAsync(scenario.Admin, "WEB-3", CancellationToken.None);

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, changeRequest.Key, "task", CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, changeRequest.Key, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True);
        ItemAssert.Failed(result, FailureKind.Invalid, WorkErrorCodes.ItemChildrenNotAllowed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Failure!.Details.Select(d => (d.Code, d.Path)),
                Is.EqualTo(new[] { (WorkErrorCodes.ItemChildrenNotAllowed, "children/WEB-2"), (WorkErrorCodes.ItemChildrenNotAllowed, "children/WEB-4") }));
            Assert.That(result.Failure.Details[0].Message, Does.Contain("WEB-2").And.Contain("登入按鈕"));
            Assert.That(readBack.Value.Type, Is.EqualTo("change_request"));
        }
    }

    [Test]
    public async Task ChangeTypeAsync_ChildrenTheNewTypeCanHold_KeepsThem()
    {
        // Arrange：任務也可以掛在需求變更單底下。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁", ParentKey = requirement.Key });

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, requirement.Key, "change_request", CancellationToken.None);
        var child = await _host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That(child.Value.ParentKey, Is.EqualTo(requirement.Key));
    }

    /// <summary>項目的類型、狀態、更新時間與自訂欄位；合約的自訂欄位是字典，記錄的相等比的是參考，所以寫成文字比。</summary>
    private static (string Type, string State, StateCategory Category, DateTimeOffset UpdatedAt, string Fields) Shape(WorkItem item) =>
        (item.Type, item.State, item.StateCategory, item.UpdatedAt, JsonSerializer.Serialize(item.Fields.OrderBy(f => f.Key, StringComparer.Ordinal)));

    private async Task TransitionAsync(Scenario scenario, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            var moved = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
            Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        }
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }
}
