using PmTool.Abstractions;
using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>只看自己的項目：改、改狀態、刪除、復原、留言、活動、附件、批次，看不到的項目一律當作不存在。</summary>
[TestFixture]
public sealed class OwnItemsWriteTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAndTransition_OwnItem_Succeed()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var updated = await _host.Items.UpdateAsync(world.Contributor, world.Assigned.Key, new WorkItemPatch { Title = "改過" }, CancellationToken.None);
        var moved = await _host.Items.TransitionAsync(world.Contributor, world.Created.Key, "doing", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Value.Title, Is.EqualTo("改過"));
            Assert.That(moved.Value.State, Is.EqualTo("doing"));
        }
    }

    [Test]
    public async Task UpdateTransitionAndChangeType_OthersItem_AreItemNotFound()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var updated = await _host.Items.UpdateAsync(world.Contributor, world.Others.Key, new WorkItemPatch { Title = "偷改" }, CancellationToken.None);
        var moved = await _host.Items.TransitionAsync(world.Contributor, world.Others.Key, "doing", CancellationToken.None);
        var retyped = await _host.Items.ChangeTypeAsync(world.Contributor, world.Others.Key, "task", CancellationToken.None);
        var after = await _host.Items.GetAsync(world.Editor, world.Others.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(moved.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(retyped.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(after.Value.Title, Is.EqualTo(world.Others.Title));
        }
    }

    [Test]
    public async Task Update_ReassigningAnOwnItemToSomeoneElse_StillSucceedsAndThenHidesIt()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act：自己建立的項目指派給別人之後，建立者仍然看得到。
        var handed = await _host.Items.UpdateAsync(
            world.Contributor, world.Created.Key, new WorkItemPatch { AssigneeId = world.Editor.PersonId }, CancellationToken.None);
        var stillMine = await _host.Items.GetAsync(world.Contributor, world.Created.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(handed.Succeeded, Is.True);
            Assert.That(stillMine.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task PlanningWrites_OwnOnlyRole_AreForbidden_EvenWithItemUpdate()
    {
        // Arrange：週期與里程碑是整個專案的事，只看得到自己項目的人不能新增或修改（他有 item.update，但看不到全部）
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var cycle = await _host.Planning.CreateCycleAsync(world.Contributor, "WEB", Planning.PlanningSupport.CycleOf("第一週", 0, 6), CancellationToken.None);
        var milestone = await _host.Planning.CreateMilestoneAsync(world.Contributor, "WEB", Planning.PlanningSupport.MilestoneOf("上線", 10), CancellationToken.None);

        // Assert
        Assert.That((cycle.Failure?.Kind, milestone.Failure?.Kind), Is.EqualTo((FailureKind.Forbidden, FailureKind.Forbidden)));
    }

    [Test]
    public async Task Delete_ContributorHasNoDeletePermission_IsForbiddenForAnyItem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var own = await _host.Items.DeleteAsync(world.Contributor, world.Created.Key, CancellationToken.None);
        var others = await _host.Items.DeleteAsync(world.Contributor, world.Others.Key, CancellationToken.None);

        // Assert：沒有 item.delete 是角色的事，自己的與別人的答案一樣，不透露別人的項目存不存在。
        Assert.That((own.Failure?.Kind, others.Failure?.Kind), Is.EqualTo((FailureKind.Forbidden, FailureKind.Forbidden)));
    }

    [Test]
    public async Task DeleteRestoreAndBulk_CustomRoleWithDeleteAndReadOwn_OnlyReachOwnItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host, "editor");
        await OwnItemsWorld.PublishAsync(_host, world.Scenario, "WEB", blueprint => blueprint with
        {
            Roles =
            [
                OwnItemsWorld.Role(
                    "owner_own", WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate, WorkPermissions.ItemUpdate,
                    WorkPermissions.ItemTransition, WorkPermissions.ItemDelete),
            ],
        });
        var actor = await world.Scenario.MemberAsync("WEB", "owner_own");
        var mine = await world.Scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "我建的" }, actor);
        var gone = await world.Scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "別人早就刪了" }, world.Editor);
        await _host.Items.DeleteAsync(world.Editor, gone.Key, CancellationToken.None);

        // Act
        var deleteOthers = await _host.Items.DeleteAsync(actor, world.Others.Key, CancellationToken.None);
        var restoreOthers = await _host.Items.RestoreAsync(actor, gone.Key, CancellationToken.None);
        var bulkDelete = await _host.Bulk.DeleteAsync(actor, "WEB", [mine.Key, world.Others.Key, gone.Key], CancellationToken.None);
        var bulkUpdate = await _host.Bulk.UpdateAsync(
            actor, "WEB", new BulkItemUpdate { Keys = [world.Others.Key], Priority = Priority.High }, CancellationToken.None);
        var bulkRestore = await _host.Bulk.RestoreAsync(actor, "WEB", [mine.Key, gone.Key], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleteOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(restoreOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(bulkDelete.Value.Deleted.Select(d => d.Key), Is.EqualTo(new[] { mine.Key }));
            Assert.That(bulkDelete.Value.Failed.Select(f => f.Key), Is.EquivalentTo(new[] { world.Others.Key, gone.Key }));
            Assert.That(bulkDelete.Value.Failed.Select(f => f.Failure.Code), Is.All.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(bulkUpdate.Value.Failed.Single().Failure.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(bulkRestore.Value.Restored.Select(r => r.Key), Is.EqualTo(new[] { mine.Key }));
            Assert.That(bulkRestore.Value.Failed.Single().Failure.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
        }
    }

    [Test]
    public async Task Delete_ParentWithAChildTheyCannotSee_IsBlockedWithoutNamingTheChild()
    {
        // Arrange：我建的需求底下，掛著編輯者建立、指派給編輯者的任務；我能刪、但看不到那個任務。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await OwnItemsWorld.PublishAsync(_host, scenario, "WEB", blueprint => blueprint with
        {
            Roles =
            [
                .. blueprint.Roles,
                OwnItemsWorld.Role("owner_own", WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate, WorkPermissions.ItemDelete),
            ],
        });
        var actor = await scenario.MemberAsync("WEB", "owner_own");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "我的需求" }, actor);
        var secret = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "看不到的機密任務", ParentKey = requirement.Key, AssigneeId = editor.PersonId }, editor);

        // Act
        var deleted = await _host.Items.DeleteAsync(actor, requirement.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemHasChildren));
            Assert.That(deleted.Failure!.Details, Has.Count.EqualTo(1));
            var text = deleted.Failure.Message + string.Join(" ", deleted.Failure.Details.Select(d => d.Path + d.Message));
            Assert.That(text, Does.Not.Contain(secret.Key).And.Not.Contain(secret.Title));
        }
    }

    [Test]
    public async Task Comments_OthersItem_AreNotFoundForAddingAndReadingActivity()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var addOthers = await _host.Activity.AddCommentAsync(world.Contributor, world.Others.Key, "偷留言", null, CancellationToken.None);
        var listOthers = await _host.Activity.ListAsync(world.Contributor, world.Others.Key, new ActivityQuery(), CancellationToken.None);
        var addOwn = await _host.Activity.AddCommentAsync(world.Contributor, world.Assigned.Key, "我的留言", null, CancellationToken.None);
        var listOwn = await _host.Activity.ListAsync(world.Contributor, world.Assigned.Key, new ActivityQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(addOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(listOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(addOwn.Succeeded, Is.True);
            Assert.That(listOwn.Value.Items.Select(a => a.Kind), Does.Contain(ActivityKind.Commented));
        }
    }

    [Test]
    public async Task Attachments_OthersItem_AreNotFoundForListAddOpenAndDelete()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var onOthers = await _host.Attachments.AddAsync(world.Editor, world.Others.Key, Upload("secret.txt", "x"), CancellationToken.None);
        var onOwn = await _host.Attachments.AddAsync(world.Editor, world.Assigned.Key, Upload("mine.txt", "y"), CancellationToken.None);

        // Act
        var listOthers = await _host.Attachments.ListAsync(world.Contributor, world.Others.Key, CancellationToken.None);
        var addOthers = await _host.Attachments.AddAsync(world.Contributor, world.Others.Key, Upload("b.txt", "z"), CancellationToken.None);
        var openOthers = await _host.Attachments.OpenAsync(world.Contributor, onOthers.Value.Id, CancellationToken.None);
        var deleteOthers = await _host.Attachments.DeleteAsync(world.Contributor, onOthers.Value.Id, CancellationToken.None);
        var listOwn = await _host.Attachments.ListAsync(world.Contributor, world.Assigned.Key, CancellationToken.None);
        var openOwn = await _host.Attachments.OpenAsync(world.Contributor, onOwn.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(addOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(openOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(deleteOthers.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(listOwn.Value, Has.Count.EqualTo(1));
            Assert.That(openOwn.Succeeded, Is.True);
        }

        await (openOwn.Value as IAsyncDisposable)!.DisposeAsync();
    }
}
