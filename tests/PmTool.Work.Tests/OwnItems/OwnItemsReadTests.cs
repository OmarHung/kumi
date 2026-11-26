using PmTool.Abstractions;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>只看自己的項目：清單、單一項目、搜尋、跨專案、下層、規劃的計數、已刪除的清單，範圍都是「負責或建立的」。</summary>
[TestFixture]
public sealed class OwnItemsReadTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_Contributor_ListsOnlyItemsAssignedToOrCreatedByThem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAsync(world.Contributor, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(world.OwnKeys));
    }

    [Test]
    public async Task ListAsync_Contributor_PagesOverTheirOwnItemsOnly()
    {
        // Arrange：分頁與下一頁的判斷都在資料庫裡，不能被看不到的項目佔掉名額。
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var first = await _host.Items.ListAsync(world.Contributor, "WEB", new WorkItemQuery { Limit = 1 }, CancellationToken.None);
        var second = await _host.Items.ListAsync(
            world.Contributor, "WEB", new WorkItemQuery { Limit = 1, Cursor = first.Value.NextCursor }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Assigned.Key }));
            Assert.That(second.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Created.Key }));
            Assert.That(second.Value.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task ListAsync_FilteredByAnotherAssignee_DoesNotRevealOthersItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAsync(
            world.Contributor, "WEB", new WorkItemQuery { AssigneeId = world.Editor.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items, Is.Empty);
    }

    [Test]
    public async Task ListAsync_FilteredByCreatorOtherThanSelf_ShowsOnlyThoseAssignedToThem()
    {
        // Arrange：編輯者建立了「指派給執行者」與「只有編輯者」兩個項目，執行者只該看到前一個。
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAsync(
            world.Contributor, "WEB", new WorkItemQuery { CreatedBy = world.Editor.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Assigned.Key }));
    }

    [Test]
    public async Task ListAsync_FilteredByCreatorSelf_ShowsTheirUnassignedItem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAsync(
            world.Contributor, "WEB", new WorkItemQuery { CreatedBy = world.Contributor.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Created.Key }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_FilteredByCreatorOtherThanSelf_ShowsOnlyThoseAssignedToThem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAcrossProjectsAsync(
            world.Contributor, CrossProjectScope.All, new WorkItemQuery { CreatedBy = world.Editor.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Assigned.Key }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_FilteredByCreatorSelf_ShowsTheirUnassignedItem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAcrossProjectsAsync(
            world.Contributor, CrossProjectScope.All, new WorkItemQuery { CreatedBy = world.Contributor.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Created.Key }));
    }

    [Test]
    public async Task ListAsync_RoleWithItemReadAsWell_SeesEverything()
    {
        // Arrange：自訂角色同時有 item.read 與 item.read_own，以看得到全部為準。
        var world = await OwnItemsWorld.CreateAsync(_host, "editor");
        await OwnItemsWorld.PublishAsync(_host, world.Scenario, "WEB", blueprint => blueprint with
        {
            Roles = [OwnItemsWorld.Role("both", WorkPermissions.ItemRead, WorkPermissions.ItemReadOwn, WorkPermissions.CommentCreate)],
        });
        var both = await world.Scenario.MemberAsync("WEB", "both");

        // Act
        var page = await _host.Items.ListAsync(both, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task ListAsync_CustomRoleWithOnlyItemReadOwn_SeesOnlyOwnItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host, "editor");
        await OwnItemsWorld.PublishAsync(_host, world.Scenario, "WEB", blueprint => blueprint with
        {
            Roles = [OwnItemsWorld.Role("outside", WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate)],
        });
        var outside = await world.Scenario.MemberAsync("WEB", "outside");
        var mine = await world.Scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "外包建的" }, outside);

        // Act
        var page = await _host.Items.ListAsync(outside, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { mine.Key }));
    }

    [Test]
    public async Task GetAsync_OthersItem_IsNotFoundLikeAMissingOne()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var others = await _host.Items.GetAsync(world.Contributor, world.Others.Key, CancellationToken.None);
        var missing = await _host.Items.GetAsync(world.Contributor, "WEB-999", CancellationToken.None);
        var assigned = await _host.Items.GetAsync(world.Contributor, world.Assigned.Key, CancellationToken.None);
        var created = await _host.Items.GetAsync(world.Contributor, world.Created.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(others.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(others.Failure?.Kind, Is.EqualTo(missing.Failure?.Kind));
            Assert.That(assigned.Succeeded, Is.True);
            Assert.That(created.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task GetAsync_AfterTheAssigneeIsChangedAway_IsNoLongerVisible()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var before = await _host.Items.GetAsync(world.Contributor, world.Assigned.Key, CancellationToken.None);

        // Act：編輯者把負責人換成自己（建立者是編輯者，執行者兩邊都不沾了）。
        await _host.Items.UpdateAsync(
            world.Editor, world.Assigned.Key, new WorkItemPatch { AssigneeId = world.Editor.PersonId }, CancellationToken.None);
        var after = await _host.Items.GetAsync(world.Contributor, world.Assigned.Key, CancellationToken.None);
        var list = await _host.Items.ListAsync(world.Contributor, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Succeeded, Is.True);
            Assert.That(after.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(list.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Created.Key }));
        }
    }

    [Test]
    public async Task Project_Contributor_CanOpenTheProjectAndSeesItsPermissions()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var detail = await _host.Projects.GetAsync(world.Contributor, "WEB", CancellationToken.None);
        var projects = await _host.Projects.ListAsync(world.Contributor, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(detail.Value.MyPermissions, Does.Contain(WorkPermissions.ItemReadOwn).And.Not.Contain(WorkPermissions.ItemRead));
            Assert.That(detail.Value.MyPermissions, Does.Not.Contain(WorkPermissions.ItemDelete).And.Not.Contain(WorkPermissions.ProjectManage));
            Assert.That(projects.Select(p => p.Key), Is.EqualTo(new[] { "WEB" }));
        }
    }

    [Test]
    public async Task SearchAsync_Contributor_FindsOnlyOwnItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act：三個標題都含「項目」以外的字，用共同的「的」「自己」「執行者」不好分；直接搜每個標題。
        var byOthersTitle = await _host.Items.SearchAsync(world.Contributor, "只有編輯者", 20, CancellationToken.None);
        var byOthersKey = await _host.Items.SearchAsync(world.Contributor, world.Others.Key, 20, CancellationToken.None);
        var byOwnTitle = await _host.Items.SearchAsync(world.Contributor, "執行者", 20, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byOthersTitle.Value, Is.Empty);
            Assert.That(byOthersKey.Value, Is.Empty);
            Assert.That(byOwnTitle.Value.Select(i => i.Key), Is.EquivalentTo(world.OwnKeys));
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_OwnOnlyInOneProjectAndFullInAnother_MixesBothScopes()
    {
        // Arrange：WEB 只看自己的，APP 是編輯者看得到全部。
        var world = await OwnItemsWorld.CreateAsync(_host);
        await world.Scenario.CreateProjectAsync("APP");
        await _host.Projects.SetMemberAsync(world.Scenario.Admin, "APP", world.Contributor.PersonId, "editor", CancellationToken.None);
        var appItems = new[]
        {
            await world.Scenario.CreateTaskAsync("APP", "APP 的一"),
            await world.Scenario.CreateTaskAsync("APP", "APP 的二"),
        };

        // Act
        var page = await _host.Items.ListAcrossProjectsAsync(
            world.Contributor, CrossProjectScope.All, new WorkItemQuery { Limit = 3 }, CancellationToken.None);
        var rest = await _host.Items.ListAcrossProjectsAsync(
            world.Contributor, CrossProjectScope.All, new WorkItemQuery { Limit = 3, Cursor = page.Value.NextCursor }, CancellationToken.None);

        // Assert：四個看得到的（兩個 APP 加兩個 WEB 自己的），分頁沒有被看不到的佔掉。
        var all = page.Value.Items.Concat(rest.Value.Items).Select(i => i.Key).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all, Is.EquivalentTo(world.OwnKeys.Concat(appItems.Select(i => i.Key))));
            Assert.That(rest.Value.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_NoDueDateFilter_CountsOnlyVisibleItems()
    {
        // Arrange：「沒有到期日」的清單就是計數的來源。
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var page = await _host.Items.ListAcrossProjectsAsync(
            world.Contributor, CrossProjectScope.All, new WorkItemQuery { OnlyWithoutDueDate = true }, CancellationToken.None);
        var forProject = await _host.Items.ListAsync(
            world.Contributor, "WEB", new WorkItemQuery { OnlyWithoutDueDate = true }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Value.Items, Has.Count.EqualTo(2));
            Assert.That(forProject.Value.Items, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task ListAssignedToMeAsync_Contributor_ListsTheirAssignedItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);

        // Act
        var mine = await _host.Items.ListAssignedToMeAsync(world.Contributor, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { world.Assigned.Key }));
    }

    [Test]
    public async Task ListAsync_ParentKey_ListsOnlyTheVisibleChildren()
    {
        // Arrange：需求（編輯者的）底下掛兩個任務，一個指派給執行者、一個不是。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" }, editor);
        var mine = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "我的子任務", ParentKey = requirement.Key, AssigneeId = contributor.PersonId }, editor);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "別人的子任務", ParentKey = requirement.Key }, editor);

        // Act
        var children = await _host.Items.ListAsync(contributor, "WEB", new WorkItemQuery { ParentKey = requirement.Key }, CancellationToken.None);
        var detail = await _host.Items.GetAsync(contributor, mine.Key, CancellationToken.None);
        var parent = await _host.Items.GetAsync(contributor, requirement.Key, CancellationToken.None);

        // Assert：上層看不到：子任務照樣寫著上層的編號，讀上層本身是 NotFound。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(children.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { mine.Key }));
            Assert.That(detail.Value.ParentKey, Is.EqualTo(requirement.Key));
            Assert.That(parent.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
        }
    }

    [Test]
    public async Task CreateAsync_WithAParentTheyCannotSee_IsAnUnknownParent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "別人的需求" });

        // Act
        var created = await _host.Items.CreateAsync(
            contributor, "WEB", new NewWorkItem { Type = "task", Title = "想掛上去", ParentKey = requirement.Key }, CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Details.Select(d => d.Code), Does.Contain(WorkErrorCodes.ItemUnknownParent));
    }

    [Test]
    public async Task ListCyclesAsync_Progress_CountsOnlyVisibleItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var cycle = await Planning.PlanningSupport.CreateCycleAsync(_host, world.Scenario.Admin, "WEB", Planning.PlanningSupport.CycleOf("第一週", 0, 6));
        var milestone = await Planning.PlanningSupport.CreateMilestoneAsync(
            _host, world.Scenario.Admin, "WEB", Planning.PlanningSupport.MilestoneOf("上線", 10));
        foreach (var key in new[] { world.Assigned.Key, world.Others.Key })
        {
            await _host.Items.UpdateAsync(
                world.Editor, key, new WorkItemPatch { CycleId = cycle.Id, MilestoneId = milestone.Id, Estimate = 2m }, CancellationToken.None);
        }

        // Act
        var contributorCycles = await _host.Planning.ListCyclesAsync(world.Contributor, "WEB", CancellationToken.None);
        var contributorMilestones = await _host.Planning.ListMilestonesAsync(world.Contributor, "WEB", CancellationToken.None);
        var adminCycles = await _host.Planning.ListCyclesAsync(world.Scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contributorCycles.Value.Single().Progress.Total, Is.EqualTo(1));
            Assert.That(contributorCycles.Value.Single().Progress.EstimateTotal, Is.EqualTo(2m));
            Assert.That(contributorMilestones.Value.Single().Progress.Total, Is.EqualTo(1));
            Assert.That(adminCycles.Value.Single().Progress.Total, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ListDeletedAsync_RoleThatSeesOwnOnly_ListsOnlyTheirDeletedItems()
    {
        // Arrange：能看已刪除清單的只看自己的角色。刪的人是管理員，兩個項目都刪掉。
        var world = await OwnItemsWorld.CreateAsync(_host);
        await _host.Items.DeleteAsync(world.Scenario.Admin, world.Assigned.Key, CancellationToken.None);
        await _host.Items.DeleteAsync(world.Scenario.Admin, world.Others.Key, CancellationToken.None);

        // Act
        var page = await _host.Items.ListDeletedAsync(world.Contributor, "WEB", new DeletedItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { world.Assigned.Key }));
    }

    [Test]
    public async Task GetAsync_ArchivedProject_ContributorStillReadsOwnItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        await _host.Projects.ArchiveAsync(world.Scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var own = await _host.Items.GetAsync(world.Contributor, world.Assigned.Key, CancellationToken.None);
        var others = await _host.Items.GetAsync(world.Contributor, world.Others.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(own.Succeeded, Is.True);
            Assert.That(others.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
        }
    }
}
