using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Access;
using PmTool.Work.Items;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>只看自己的項目：變更集（整個專案的影響數量與編號）、批次、改類型、匯入不能透露看不到的項目。</summary>
[TestFixture]
public sealed class OwnItemsLeakTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ChangeSetPreview_ContributorWithConversionAndFieldDrop_IsForbiddenAndRevealsNothing()
    {
        // Arrange：編輯者建了一個摘要是「abc」的項目；文件把摘要改成數字（轉不過去就擋下），並刪掉另一個欄位。
        var world = await OwnItemsWorld.CreateAsync(_host);
        await _host.Items.UpdateAsync(
            world.Editor, world.Others.Key, new WorkItemPatch { Fields = Scenario.Fields(("summary", "abc")) }, CancellationToken.None);
        var document = await ChangeSetDrafts.DraftAsync(
            world.Scenario,
            "WEB",
            b => b.Field("summary", f => f with { Type = FieldType.Number }).WithoutField("notes"),
            new MigrationHints
            {
                ConvertFields = [new FieldConversion { Field = "summary", OnFailure = ConversionFailurePolicy.Abort }],
                DropFields = ["notes"],
            });

        // Act
        var contributor = await _host.Changes.PreviewAsync(world.Contributor, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var editor = await _host.Changes.PreviewAsync(world.Editor, "WEB", document, DocumentFormat.Json, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((contributor.Failure?.Kind, contributor.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
            Assert.That(contributor.Failure!.Message, Does.Contain("所有的項目"));
            Assert.That(contributor.Failure.Details, Is.Empty);
            Assert.That(contributor.Failure.Message, Does.Not.Contain(world.Others.Key));
            Assert.That(editor.Succeeded, Is.True, () => ChangeSetDrafts.Describe(editor.Failure!));
            Assert.That(editor.Value.Impacts, Is.Not.Empty);
        }
    }

    [Test]
    public async Task ChangeSetListGetAndLifecycle_Contributor_AreForbidden_WhileEditorStillReads()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            world.Scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中" }));

        // Act
        var list = await _host.Changes.ListAsync(world.Contributor, "WEB", CancellationToken.None);
        var get = await _host.Changes.GetAsync(world.Contributor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var reject = await _host.Changes.RejectAsync(world.Contributor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var apply = await _host.Changes.ApplyAsync(world.Contributor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var propose = await ChangeSetDrafts.TryProposeAsync(
            world.Scenario, "WEB", b => b.State("task_flow", "doing", s => s with { Name = "處理中二" }), proposer: world.Contributor);
        var editorList = await _host.Changes.ListAsync(world.Editor, "WEB", CancellationToken.None);
        var editorGet = await _host.Changes.GetAsync(world.Editor, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var project = await _host.Projects.GetAsync(world.Contributor, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(get.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(reject.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(apply.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(propose.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(editorList.Succeeded && editorGet.Succeeded, Is.True);
            Assert.That(project.Value.Blueprint, Is.Not.Null, "藍圖本身照舊給");
        }
    }

    [Test]
    public async Task BulkUpdate_StateAndFieldsOnOthersItems_AreItemNotFoundAndChangeNothing()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var state = await _host.Bulk.UpdateAsync(
            world.Contributor, "WEB", new BulkItemUpdate { Keys = [world.Others.Key, world.Created.Key], State = "doing" }, CancellationToken.None);
        var fields = await _host.Bulk.UpdateAsync(
            world.Contributor, "WEB", new BulkItemUpdate { Keys = [world.Others.Key], Priority = Priority.High, AssigneeId = world.Contributor.PersonId }, CancellationToken.None);
        var after = await _host.Items.GetAsync(world.Editor, world.Others.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Value.Failed.Single().Key, Is.EqualTo(world.Others.Key));
            Assert.That(state.Value.Failed.Single().Failure.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(state.Value.Updated.Select(u => u.Key), Is.EqualTo(new[] { world.Created.Key }));
            Assert.That(fields.Value.Failed.Single().Failure.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That((after.Value.State, after.Value.AssigneeId), Is.EqualTo((world.Others.State, world.Editor.PersonId)));
        }
    }

    [Test]
    public async Task ChangeType_ChildrenTheyCannotSee_AreBlockedWithoutNamingThem()
    {
        // Arrange：我建的需求底下掛著編輯者的任務；需求改成任務，任務底下不能再掛任務。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "我的需求" }, contributor);
        var secret = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "看不到的機密任務", ParentKey = requirement.Key, AssigneeId = editor.PersonId }, editor);

        // Act
        var result = await _host.Items.ChangeTypeAsync(contributor, requirement.Key, "task", CancellationToken.None);
        var after = await _host.Items.GetAsync(contributor, requirement.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemChildrenNotAllowed));
            Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "children" }));
            var text = result.Failure.Message + string.Join(" ", result.Failure.Details.Select(d => d.Path + d.Message));
            Assert.That(text, Does.Not.Contain(secret.Key).And.Not.Contain(secret.Title));
            Assert.That(after.Value.Type, Is.EqualTo("requirement"));
        }
    }

    [Test]
    public async Task ChangeType_HiddenParentOfAnotherType_DoesNotNameTheParentsType()
    {
        // Arrange：需求變更單只能掛在需求底下（自訂藍圖）；我的任務掛在編輯者的需求變更單底下，我看不到那一份。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", b => b.ItemType("change_request", t => t with { Parents = ["requirement"] }));
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "編輯者的需求" }, editor);
        var changeRequest = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "change_request", Title = "編輯者的變更單", ParentKey = requirement.Key }, editor);
        var task = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "我的任務", ParentKey = changeRequest.Key, AssigneeId = contributor.PersonId }, editor);

        // Act
        var result = await _host.Items.ChangeTypeAsync(contributor, task.Key, "change_request", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemParentNotAllowed));
            Assert.That(result.Failure!.Message, Does.Not.Contain("「需求變更單」").Or.Contain("你看不到的上層"));
            Assert.That(result.Failure.Message, Does.Contain("你看不到的上層"));
        }
    }

    [Test]
    public async Task Import_ParentTheyCannotSee_IsAnUnknownParentAndCreatesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "requirement", Title = "編輯者的需求", AssigneeId = editor.PersonId }, editor);
        var import = new ItemImport
        {
            Rows = [new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "匯入的任務", ParentKey = requirement.Key } }],
        };

        // Act
        var result = await _host.Import.ImportAsync(contributor, "WEB", import, CancellationToken.None);
        var listed = await _host.Items.ListAsync(contributor, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ImportInvalid));
            Assert.That(result.Failure!.Details.Select(d => (d.Path, d.Code)), Is.EqualTo(new[] { ("rows/0/parent", WorkErrorCodes.ItemUnknownParent) }));
            Assert.That(listed.Value.Items, Is.Empty);
        }
    }

    [Test]
    public async Task Delete_ParentWithHiddenAndVisibleChildren_MessageDoesNotCountTheHiddenOnes()
    {
        // Arrange：我的需求底下一個我建的任務、兩個編輯者的任務。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", b => b with
        {
            Roles = [.. b.Roles, OwnItemsWorld.Role("owner_own", WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate, WorkPermissions.ItemDelete)],
        });
        var actor = await scenario.MemberAsync("WEB", "owner_own");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "我的需求" }, actor);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "我的任務", ParentKey = requirement.Key }, actor);
        foreach (var title in new[] { "別人一", "別人二" })
        {
            await scenario.CreateItemAsync(
                "WEB", new NewWorkItem { Type = "task", Title = title, ParentKey = requirement.Key, AssigneeId = editor.PersonId }, editor);
        }

        // Act
        var deleted = await _host.Items.DeleteAsync(actor, requirement.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemHasChildren));
            Assert.That(deleted.Failure!.Message, Does.Contain("1 個項目").And.Contain("你看不到的項目"));
            Assert.That(deleted.Failure.Message, Does.Not.Contain("3"));
        }
    }

    [Test]
    public void ItemScope_NoReadPermission_ContainsNothing_AndOwnershipHasOneDefinition()
    {
        // Arrange
        var person = Guid.NewGuid();
        var other = Guid.NewGuid();
        var item = new PmTool.Work.Persistence.ItemEntity
        {
            Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), ProjectId = Guid.NewGuid(), Number = 1, Type = "task", Title = "範例",
            State = "todo", StateCategory = "not_started", Priority = 0, Fields = "{}", CreatedBy = other,
            CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch, AssigneeId = person,
        };

        // Act / Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(default(ItemScope).Contains(item), Is.False);
            Assert.That(new ItemScope(ItemScopeKind.None, person).Contains(item), Is.False);
            Assert.That(new ItemScope(ItemScopeKind.None, person).Apply(new[] { item }.AsQueryable()), Is.Empty);
            Assert.That(new ItemScope(ItemScopeKind.OwnOnly, person).Contains(item), Is.True);
            Assert.That(new ItemScope(ItemScopeKind.OwnOnly, other).Contains(item), Is.True);
            Assert.That(new ItemScope(ItemScopeKind.OwnOnly, Guid.NewGuid()).Contains(item), Is.False);
            Assert.That(ItemScope.All(Guid.NewGuid()).Contains(item), Is.True);
            Assert.That(ItemScope.IsOwned(person, other, null), Is.False);
        }
    }
}
