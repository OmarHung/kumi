using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>建立成員建議：每一條檢查、沒權限時列出誰能管理成員。</summary>
[TestFixture]
public sealed class MemberProposalCreateTests : MemberProposalTestBase
{
    [Test]
    public async Task Create_AddChangeAndRemove_StoresPendingProposalWithNames()
    {
        // Act
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "editor"), new MemberChangeDraft(Contributor, "viewer"), new MemberChangeDraft(Editor, null));

        // Assert
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Value.Status, Is.EqualTo(MemberProposalStatus.Pending));
            Assert.That(read.Value.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(read.Value.Changes[0], Is.EqualTo(new MemberChange(Outsider, "丁", "editor", "編輯者", null, null)));
            Assert.That(read.Value.Changes[1], Is.EqualTo(new MemberChange(Contributor, "乙", "viewer", "檢視者", "contributor", "執行者")));
            Assert.That(read.Value.Changes[2], Is.EqualTo(new MemberChange(Editor, "丙", null, null, "editor", "編輯者")));
            Assert.That(H.Projects.MemberWrites, Is.Empty, "建議不會改成員");
        }
    }

    [Test]
    public async Task Create_WithoutProjectManage_IsForbiddenAndNamesTheManagers()
    {
        // Arrange
        UseProject(canManage: false);

        // Act
        var created = await Creator.CreateAsync(Me, Draft(new MemberChangeDraft(Outsider, "editor")), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Forbidden));
            Assert.That(created.Failure?.Message, Is.EqualTo("你沒有管理這個專案成員的權限。能管理成員的是：甲（專案的管理者），以及組織管理員。"));
        }
    }

    [Test]
    public async Task Create_WithoutProjectManageAndNoManagers_OnlyMentionsOrganizationAdmins()
    {
        // Arrange
        UseProject(canManage: false);
        Staffing.Roles = [.. Staffing.Roles.Where(r => r.Key != "admin")];

        // Act
        var created = await Creator.CreateAsync(Me, Draft(new MemberChangeDraft(Outsider, "editor")), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Message, Does.EndWith("能管理成員的是組織管理員。"));
    }

    [Test]
    public async Task Create_DisabledManagersAreNotListedAndTheListStopsAtTen()
    {
        // Arrange：十二位管理者（含一位停用的）。
        UseProject(canManage: false);
        var many = Enumerable.Range(1, 12).Select(i => People.Add($"管理者{i}", disabled: i == 1)).ToList();
        H.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions]) with
        {
            Blueprint = Blueprint(),
            Members = [.. many.Select(id => new ProjectMember(id, "admin"))],
        };

        // Act
        var created = await Creator.CreateAsync(Me, Draft(new MemberChangeDraft(Outsider, "editor")), CancellationToken.None);

        // Assert
        var message = created.Failure!.Message;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Not.Contain("管理者1、").And.Not.Contain("：管理者1"));
            Assert.That(message, Does.Contain("管理者2").And.Contain("管理者11"));
            Assert.That(message, Does.Not.Contain("管理者12"));
        }
    }

    [Test]
    public async Task Create_ProjectNotVisible_ReturnsTheProjectFailureAsIs()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var created = await Creator.CreateAsync(Me, Draft(new MemberChangeDraft(Outsider, "editor")), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task Create_AllProblems_AreReportedTogetherWithPaths()
    {
        // Act
        var created = await Creator.CreateAsync(
            Me,
            Draft(new MemberChangeDraft(Outsider, "boss"), new MemberChangeDraft(Guid.NewGuid(), "editor"), new MemberChangeDraft(Disabled, "editor"), new MemberChangeDraft(Editor, "editor"), new MemberChangeDraft(Outsider, "viewer"), new MemberChangeDraft(Outsider, null)),
            CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.FieldInvalid));
            Assert.That(IssueAt(created, "changes[0].role"), Does.Contain("boss").And.Contain("admin（管理員）").And.Contain("viewer（檢視者）"));
            Assert.That(IssueAt(created, "changes[1].person"), Does.Contain("找不到人員"));
            Assert.That(IssueAt(created, "changes[2].person"), Does.Contain("已經停用"));
            Assert.That(IssueAt(created, "changes[3].role"), Does.Contain("已經是「編輯者」了"));
            Assert.That(IssueAt(created, "changes[4].person"), Does.Contain("只能出現一次"));
            Assert.That(IssueAt(created, "changes[5].person"), Does.Contain("只能出現一次"));
        }
    }

    [Test]
    public async Task Create_RemovingSomeoneWhoIsNotAMember_IsAnIssue()
    {
        // Act
        var created = await Creator.CreateAsync(Me, Draft(new MemberChangeDraft(Outsider, null)), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes[0].person"), Does.Contain("不是這個專案的成員"));
    }

    [Test]
    public async Task Create_RemovingADisabledMember_IsAllowed()
    {
        // Arrange
        var detail = ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, WorkPermissions.ProjectManage]) with
        {
            Blueprint = Blueprint(),
            Members = [new(Manager, "admin"), new(Disabled, "viewer")],
        };
        H.Projects.OnGet = (_, _) => detail;

        // Act
        var proposal = await CreatedAsync(new MemberChangeDraft(Disabled, null));

        // Assert
        Assert.That(proposal.Changes.Single().PreviousRoleName, Is.EqualTo("檢視者"));
    }

    [TestCase(0)]
    [TestCase(11)]
    public async Task Create_WrongNumberOfChanges_IsAnIssue(int count)
    {
        // Arrange
        var changes = Enumerable.Range(0, count).Select(_ => new MemberChangeDraft(People.Add("某人"), "viewer")).ToArray();

        // Act
        var created = await Creator.CreateAsync(Me, Draft(changes), CancellationToken.None);

        // Assert
        Assert.That(IssueAt(created, "changes"), Does.Contain("1 到 10 筆"));
    }

    [Test]
    public async Task Create_CustomRoleFromTheBlueprint_IsAccepted()
    {
        // Arrange
        Staffing.Roles = [.. Staffing.Roles, new ProjectRoleInfo("qa", "測試『員』", false, [WorkPermissions.ItemRead])];

        // Act
        var proposal = await CreatedAsync(new MemberChangeDraft(Outsider, "qa"));

        // Assert
        Assert.That(proposal.Changes.Single().RoleName, Is.EqualTo("測試『員』"));
    }
}
