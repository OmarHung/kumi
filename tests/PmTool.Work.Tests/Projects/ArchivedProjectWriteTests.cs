using PmTool.Abstractions;
using PmTool.Blueprint;

namespace PmTool.Work.Tests.Projects;

/// <summary>封存的專案只能看：每一類寫入都回 project.archived（409），讀取、個人的檢視與成員管理照常。</summary>
[TestFixture]
public sealed class ArchivedProjectWriteTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task Write_EveryItemOperation_FailsWithProjectArchivedForOrganizationAdminToo()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var deleted = await scenario.CreateTaskAsync("WEB", "之前刪的");
        await _host.Items.DeleteAsync(scenario.Admin, deleted.Key, CancellationToken.None);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var admin = scenario.Admin;

        // Act
        var results = new (string Name, Failure? Failure)[]
        {
            ("create", (await _host.Items.CreateAsync(admin, "WEB", new NewWorkItem { Type = "task", Title = "新" }, CancellationToken.None)).Failure),
            ("update", (await _host.Items.UpdateAsync(admin, item.Key, new WorkItemPatch { Title = "改" }, CancellationToken.None)).Failure),
            ("transition", (await _host.Items.TransitionAsync(admin, item.Key, "doing", CancellationToken.None)).Failure),
            ("type", (await _host.Items.ChangeTypeAsync(admin, item.Key, "task", CancellationToken.None)).Failure),
            ("delete", (await _host.Items.DeleteAsync(admin, item.Key, CancellationToken.None)).Failure),
            ("restore", (await _host.Items.RestoreAsync(admin, deleted.Key, CancellationToken.None)).Failure),
            ("comment", (await _host.Activity.AddCommentAsync(admin, item.Key, "留言", null, CancellationToken.None)).Failure),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, failure) in results)
            {
                Assert.That((failure?.Kind, failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ProjectArchived)), name);
            }
        }
    }

    [Test]
    public async Task Write_Bulk_FailsTheWholeRequestWithProjectArchived()
    {
        // Arrange
        var (scenario, item) = await ArchivedWithAnItemAsync();

        // Act
        var update = await _host.Bulk.UpdateAsync(scenario.Admin, "WEB", new BulkItemUpdate { Keys = [item.Key], Priority = Priority.High }, CancellationToken.None);
        var delete = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [item.Key], CancellationToken.None);
        var restore = await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [item.Key], CancellationToken.None);

        // Assert
        Assert.That(
            new[] { update.Failure?.Code, delete.Failure?.Code, restore.Failure?.Code },
            Is.EqualTo(new[] { WorkErrorCodes.ProjectArchived, WorkErrorCodes.ProjectArchived, WorkErrorCodes.ProjectArchived }));
    }

    [Test]
    public async Task Write_Import_FailsWithProjectArchivedEvenForADryRun()
    {
        // Arrange
        var (scenario, _) = await ArchivedWithAnItemAsync();
        var import = new ItemImport { Rows = [new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "匯入" } }], DryRun = true };

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", import, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
    }

    [Test]
    public async Task Write_CyclesAndMilestones_FailWithProjectArchived()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var cycle = await _host.Planning.CreateCycleAsync(
            scenario.Admin, "WEB", new CycleDraft("衝刺一", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 13)), CancellationToken.None);
        var milestone = await _host.Planning.CreateMilestoneAsync(
            scenario.Admin, "WEB", new MilestoneDraft("上線", new DateOnly(2026, 12, 1), null), CancellationToken.None);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var draft = new CycleDraft("衝刺二", new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 27));
        var milestoneDraft = new MilestoneDraft("驗收", new DateOnly(2026, 12, 15), null);

        // Act
        var results = new[]
        {
            (await _host.Planning.CreateCycleAsync(scenario.Admin, "WEB", draft, CancellationToken.None)).Failure,
            (await _host.Planning.UpdateCycleAsync(scenario.Admin, "WEB", cycle.Value.Id, draft, CancellationToken.None)).Failure,
            (await _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Value.Id, CancellationToken.None)).Failure,
            (await _host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", milestoneDraft, CancellationToken.None)).Failure,
            (await _host.Planning.UpdateMilestoneAsync(scenario.Admin, "WEB", milestone.Value.Id, milestoneDraft, CancellationToken.None)).Failure,
            (await _host.Planning.DeleteMilestoneAsync(scenario.Admin, "WEB", milestone.Value.Id, CancellationToken.None)).Failure,
        };
        var cycles = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(f => f?.Code), Is.All.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(cycles.Value, Has.Count.EqualTo(1), "讀取照常，也沒有被改動");
        }
    }

    [Test]
    public async Task Write_ChangeSets_ProposeApplyAndRejectFailWithProjectArchived()
    {
        // Arrange：封存之前送出一份等待核准的變更集，另一份由管理員自己送出。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposer = await scenario.MemberAsync("WEB", "admin");
        var pending = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", blueprint => blueprint, proposer: proposer);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var document = await ChangeSetDrafts.DraftAsync(scenario, "WEB", blueprint => blueprint);

        // Act
        var propose = await _host.Changes.ProposeAsync(scenario.Admin, "WEB", document, DocumentFormat.Json, CancellationToken.None);
        var apply = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", pending.ChangeSet.Number, CancellationToken.None);
        var rejectByProposer = await _host.Changes.RejectAsync(proposer, "WEB", pending.ChangeSet.Number, CancellationToken.None);
        var rejectByAdmin = await _host.Changes.RejectAsync(scenario.Admin, "WEB", pending.ChangeSet.Number, CancellationToken.None);
        var read = await _host.Changes.GetAsync(scenario.Admin, "WEB", pending.ChangeSet.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(propose.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(apply.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(rejectByProposer.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived), "提案的人自己撤回也一樣");
            Assert.That(rejectByAdmin.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(read.Succeeded, Is.True, "讀取照常");
            Assert.That(read.Value.ChangeSet.Status, Is.EqualTo(ChangeSetStatus.Pending));
        }
    }

    [Test]
    public async Task Read_ArchivedProject_ItemsActivitySearchAndBlueprintStillWork()
    {
        // Arrange
        var (scenario, item) = await ArchivedWithAnItemAsync();

        // Act
        var got = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        var activity = await _host.Activity.ListAsync(scenario.Admin, item.Key, new ActivityQuery(), CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(got.Succeeded, Is.True);
            Assert.That(listed.Value.Items, Has.Count.EqualTo(1));
            Assert.That(activity.Succeeded, Is.True);
            Assert.That(detail.Value.Blueprint, Is.Not.Null);
        }
    }

    [Test]
    public async Task PersonalViewsAndMembers_ArchivedProject_StillWork()
    {
        // Arrange
        var (scenario, _) = await ArchivedWithAnItemAsync();
        var member = await MemberOfArchivedAsync(scenario);

        // Act
        var view = await _host.SavedViews.CreateAsync(
            scenario.Admin, "WEB", new SavedViewDraft("我的檢視", SavedViewLayout.Table, new SavedViewQuery(null, null, null, null, null, null, null)), CancellationToken.None);
        var removed = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", member.PersonId, CancellationToken.None);
        var added = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", member.PersonId, "viewer", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(view.Succeeded, Is.True, () => view.Failure!.Message);
            Assert.That(removed.Succeeded, Is.True);
            Assert.That(added.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Write_ViewerOnArchivedProject_GetsProjectArchivedNotForbidden()
    {
        // Arrange
        var (scenario, item) = await ArchivedWithAnItemAsync();
        var viewer = await MemberOfArchivedAsync(scenario);

        // Act
        var result = await _host.Items.UpdateAsync(viewer, item.Key, new WorkItemPatch { Title = "改" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
    }

    [Test]
    public async Task Write_ArchivedFailure_NamesTheProjectInTheMessage()
    {
        // Arrange
        var (scenario, item) = await ArchivedWithAnItemAsync();

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Message, Is.EqualTo("專案「範例專案 WEB」已封存，只能看不能改。"));
    }

    [Test]
    public async Task Write_AfterUnarchive_ItemWritesWorkAgain()
    {
        // Arrange
        var (scenario, item) = await ArchivedWithAnItemAsync();
        await _host.Projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改了" }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Title, Is.EqualTo("改了"));
    }

    private async Task<(Scenario Scenario, WorkItem Item)> ArchivedWithAnItemAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var archived = await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        Assert.That(archived.Succeeded, Is.True);
        return (scenario, item);
    }

    private async Task<Actor> MemberOfArchivedAsync(Scenario scenario)
    {
        var person = scenario.Person();
        var added = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", person.PersonId, "viewer", CancellationToken.None);
        Assert.That(added.Succeeded, Is.True, "封存的專案也能加成員");
        return person;
    }
}
