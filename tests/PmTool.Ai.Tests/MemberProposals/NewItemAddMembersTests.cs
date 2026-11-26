using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>新增項目建議的 addMembers、週期與里程碑：檢查、套用先加人再建項目。</summary>
[TestFixture]
public sealed class NewItemAddMembersTests : MemberProposalTestBase
{
    private static NewItemDraft Task(string title = "新任務") => new("task", title);

    private Task<Result<NewItemProposal>> CreateAsync(IReadOnlyList<MemberAddition>? add, params NewItemDraft[] items) =>
        NewItemCreator.CreateAsync(Me, new NewItemProposalDraft("WEB", items) { AddMembers = add ?? [] }, CancellationToken.None);

    [Test]
    public async Task Create_WithAddMembers_StoresNamesAndAllowsTheNewMemberAsAssignee()
    {
        // Act
        var created = await CreateAsync([new MemberAddition(Outsider, "contributor")], Task() with { AssigneeId = Outsider });

        // Assert
        var read = await NewItems.GetAsync(Me, created.Value.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Succeeded, Is.True, () => Describe(created.Failure));
            Assert.That(read.Value.AddMembers, Is.EqualTo(new[] { new MemberAddition(Outsider, "contributor") { DisplayName = "丁", RoleName = "執行者" } }));
            Assert.That(read.Value.Items.Single().AssigneeId, Is.EqualTo(Outsider));
        }
    }

    [Test]
    public async Task Create_AddMembersWithoutProjectManage_IsForbiddenWithTheManagers()
    {
        // Arrange
        UseProject(canManage: false);

        // Act
        var created = await CreateAsync([new MemberAddition(Outsider, "contributor")], Task());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Message, Is.EqualTo("你沒有管理這個專案成員的權限。能管理成員的是：甲（專案的管理者），以及組織管理員。"));
        }
    }

    [Test]
    public async Task Create_AddMembersProblems_AreReportedWithPaths()
    {
        // Act
        var created = await CreateAsync(
            [new(Outsider, "boss"), new(Guid.NewGuid(), "viewer"), new(Disabled, "viewer"), new(Editor, "viewer"), new(OtherOutsider, "viewer"), new(OtherOutsider, "viewer")],
            Task());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(IssueAt(created, "addMembers[0].role"), Does.Contain("boss"));
            Assert.That(IssueAt(created, "addMembers[1].person"), Does.Contain("找不到人員"));
            Assert.That(IssueAt(created, "addMembers[2].person"), Does.Contain("已經停用"));
            Assert.That(IssueAt(created, "addMembers[3].person"), Does.Contain("已經是這個專案的成員"));
            Assert.That(IssueAt(created, "addMembers[5].person"), Does.Contain("只能出現一次"));
        }
    }

    [Test]
    public async Task Create_TooManyAddMembers_IsAnIssue()
    {
        // Arrange
        var many = Enumerable.Range(0, 11).Select(_ => new MemberAddition(People.Add("某人"), "viewer")).ToList();

        // Act
        var created = await CreateAsync(many, Task());

        // Assert
        Assert.That(IssueAt(created, "addMembers"), Does.Contain("最多 10"));
    }

    [Test]
    public async Task Create_AssigneeNotAMember_WithManage_SuggestsAddMembers()
    {
        // Act
        var created = await CreateAsync(null, Task() with { AssigneeId = Outsider });

        // Assert
        Assert.That(IssueAt(created, "items[0].assigneeId"), Does.Contain("不是這個專案的成員").And.Contain("addMembers"));
    }

    [Test]
    public async Task Create_AssigneeNotAMember_WithoutManage_NamesManagersAndAlternatives()
    {
        // Arrange
        UseProject(canManage: false);

        // Act
        var created = await CreateAsync(null, Task() with { AssigneeId = Outsider });

        // Assert
        var message = IssueAt(created, "items[0].assigneeId");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("不是這個專案的成員").And.Contain("你不能把人加進專案"));
            Assert.That(message, Does.Contain("能管理成員的是：甲").And.Contain("指派給自己、不指派"));
            Assert.That(message, Does.Not.Contain("addMembers 一起加入"));
        }
    }

    [Test]
    public async Task Create_CycleAndMilestoneOfThisProject_AreStoredAndOthersAreIssues()
    {
        // Arrange
        var cycle = Guid.NewGuid();
        var milestone = Guid.NewGuid();
        var progress = new ItemProgress(0, 0, 0, null, null);
        Planning.Cycles.Add(new Cycle(cycle, "Sprint 1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 14), progress));
        Planning.Milestones.Add(new Milestone(milestone, "上線", new DateOnly(2026, 12, 31), null, progress));

        // Act
        var good = await CreateAsync(null, Task() with { CycleId = cycle, MilestoneId = milestone });
        var bad = await CreateAsync(null, Task() with { CycleId = Guid.NewGuid(), MilestoneId = Guid.NewGuid() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(good.Value.Items.Single().CycleId, Is.EqualTo(cycle));
            Assert.That(good.Value.Items.Single().MilestoneId, Is.EqualTo(milestone));
            Assert.That(IssueAt(bad, "items[0].cycleId"), Does.Contain("get_project"));
            Assert.That(IssueAt(bad, "items[0].milestoneId"), Does.Contain("get_project"));
        }
    }

    [Test]
    public async Task Create_CycleWhenPlanningIsUnreadable_SaysSo()
    {
        // Arrange
        Planning.Readable = false;

        // Act
        var created = await CreateAsync(null, Task() with { CycleId = Guid.NewGuid() });

        // Assert
        Assert.That(IssueAt(created, "items[0].cycleId"), Is.EqualTo("你讀不到這個專案的週期。"));
    }

    [Test]
    public async Task Apply_AddsTheMembersFirstThenCreatesTheItemsAssignedToThem()
    {
        // Arrange
        var cycle = Guid.NewGuid();
        Planning.Cycles.Add(new Cycle(cycle, "Sprint 1", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 14), new ItemProgress(0, 0, 0, null, null)));
        var created = await CreateAsync([new MemberAddition(Outsider, "contributor")], Task() with { AssigneeId = Outsider, CycleId = cycle });

        // Act
        var applied = await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => Describe(applied.Failure));
            Assert.That(applied.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(H.Projects.MemberWrites.Select(w => (w.PersonId, w.Role)), Is.EqualTo(new (Guid, string?)[] { (Outsider, "contributor") }));
            Assert.That(H.Projects.MemberWrites[0].Actor, Is.EqualTo(Me));
            Assert.That(H.WorkItems.Creates.Single().Item.AssigneeId, Is.EqualTo(Outsider));
            Assert.That(H.WorkItems.Creates.Single().Item.CycleId, Is.EqualTo(cycle));
        }
    }

    [Test]
    public async Task Apply_SomeoneAlreadyAMemberByNow_IsSkippedAndTheirRoleIsNotChanged()
    {
        // Arrange：建立建議時還不是成員，套用前被別人加進去了。
        var created = await CreateAsync([new MemberAddition(Outsider, "viewer"), new MemberAddition(OtherOutsider, "viewer")], Task());
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, WorkPermissions.ItemCreate, WorkPermissions.ProjectManage]) with
        {
            Blueprint = Blueprint(),
            Members = [new(Manager, "admin"), new(Outsider, "editor")],
        };

        // Act
        await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(H.Projects.MemberWrites.Select(w => w.PersonId), Is.EqualTo(new[] { OtherOutsider }));
    }

    [Test]
    public async Task Apply_AddingAMemberFails_CreatesNoItemAndGoesBackToPending()
    {
        // Arrange
        var created = await CreateAsync([new MemberAddition(Outsider, "viewer")], Task());
        H.Projects.OnMemberWrite = (_, _, _) => WorkSamples.Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var failed = await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);
        var read = await NewItems.GetAsync(Me, created.Value.Id, CancellationToken.None);
        H.Projects.OnMemberWrite = null;
        var retried = await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(read.Value.CreatedKeys, Is.All.Null);
            Assert.That(retried.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(1), "第一次沒有建任何項目");
        }
    }

    [Test]
    public async Task Apply_AddingTheSecondMemberThrows_GoesBackToPendingCreatesNothingAndCanBeAppliedAgain()
    {
        // Arrange
        var created = await CreateAsync([new MemberAddition(Outsider, "viewer"), new MemberAddition(OtherOutsider, "viewer")], Task());
        H.Projects.OnMemberWrite = (index, _, _) => index == 1 ? throw new InvalidOperationException("壞掉了") : new ProjectMember(Outsider, "viewer");

        // Act
        var failed = await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);
        var read = await NewItems.GetAsync(Me, created.Value.Id, CancellationToken.None);
        var createdBeforeRetry = H.WorkItems.Creates.Count;
        H.Projects.OnMemberWrite = null;
        var retried = await NewItems.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Failure?.Message, Does.Not.Contain("壞掉了"));
            Assert.That(read.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(createdBeforeRetry, Is.Zero);
            Assert.That(retried.Value.Status, Is.EqualTo(NewItemProposalStatus.Applied));
            Assert.That(H.WorkItems.Creates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Create_AssigneeIsADisabledMember_WithoutManage_NamesManagers()
    {
        // Arrange：己是停用的成員，不是「不是成員」；訊息仍要列出誰能加人。
        var detail = ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, WorkPermissions.ItemCreate]) with
        {
            Blueprint = Blueprint(),
            Members = [new(Manager, "admin"), new(Contributor, "contributor"), new(Editor, "editor"), new(Disabled, "editor")],
        };
        H.Projects.OnGet = (_, _) => detail;

        // Act
        var created = await CreateAsync(null, Task() with { AssigneeId = Disabled });

        // Assert
        Assert.That(IssueAt(created, "items[0].assigneeId"), Does.Contain("能管理成員的是：甲"));
    }

    [Test]
    public async Task Read_OldRowWithoutAddMembers_HasAnEmptyList()
    {
        // Arrange
        var created = await CreateAsync(null, Task());
        await using (var db = await H.Host.CreateDbContextAsync())
        {
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(
                db.Database, "UPDATE ai.new_item_proposals SET add_members = DEFAULT, items = items::text::jsonb - 0");
        }

        // Act
        var read = await NewItems.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Value.AddMembers, Is.Empty);
    }
}
