using PmTool.Abstractions;
using PmTool.Ai.Staffing;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.Staffing;

/// <summary>
/// 派工的摘要：成員與人員名單的排序、停用的不列、項目數、能管理成員的人、週期與里程碑的日期界線（組織時區）、精簡版藍圖、訪客。
/// 服務不碰自己的資料庫，只組合工作引擎與人員目錄的回答，所以用假的下游，不需要 PostgreSQL。
/// </summary>
[TestFixture]
public sealed class StaffingBriefServiceTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    private FakeProjectService _projects = null!;
    private FakeStaffingQueries _queries = null!;
    private FakePlanning _planning = null!;
    private FakePeopleDirectory _people = null!;
    private ManualClock _clock = null!;
    private StaffingBriefService _service = null!;
    private Actor _me = null!;

    [SetUp]
    public void Start()
    {
        _projects = new FakeProjectService();
        _queries = new FakeStaffingQueries();
        _planning = new FakePlanning();
        _people = new FakePeopleDirectory();
        _clock = new ManualClock();
        _service = new StaffingBriefService(_projects, _queries, _planning, _people, new FixedOrganizationClock(), _clock);
        _me = new Actor(OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);
        _people.Set(OrganizationId, _me.PersonId, OrganizationRole.Member, name: "我自己");
    }

    [Test]
    public async Task GetProject_ListsActiveMembersByNameWithTitleScopeAndOpenItems()
    {
        // Arrange
        var amy = AddMember("阿美", "前端工程師", "WordPress 維運");
        var bob = AddMember("阿寶", null, null);
        var gone = AddMember("阿停", "舊同事", null, disabled: true);
        _queries.Open[amy] = 3;
        _queries.Open[gone] = 9;
        UseMembers((amy, "contributor"), (bob, "editor"), (gone, "editor"));

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        var members = result.Value.Members;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(members.Select(m => m.DisplayName), Is.EqualTo(new[] { "阿寶", "阿美" }));
            Assert.That(members[1].JobTitle, Is.EqualTo("前端工程師"));
            Assert.That(members[1].Responsibilities, Is.EqualTo("WordPress 維運"));
            Assert.That(members[1].OpenItems, Is.EqualTo(3));
            Assert.That(members[0].OpenItems, Is.Zero);
            Assert.That(members[1].Role, Is.EqualTo("contributor"));
        }
    }

    [Test]
    public async Task GetProject_ActorWithoutItemRead_GetsNullOpenItemsForOthersAndTheirOwnCount()
    {
        // Arrange
        var other = AddMember("阿美", null, null);
        _queries.Open[other] = 0;
        _queries.Open[_me.PersonId] = 2;
        _projects.OnGet = (_, _) => WorkSamples.Detail() with
        {
            Members = [new ProjectMember(other, "editor"), new ProjectMember(_me.PersonId, "contributor")],
            MyPermissions = [WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate],
        };

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        var members = result.Value.Members;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(members.Single(m => m.PersonId == other).OpenItems, Is.Null);
            Assert.That(members.Single(m => m.PersonId == _me.PersonId).OpenItems, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task GetProject_ReportsMyRoleAndPermissions_AndNullRoleForANonMemberAdmin()
    {
        // Arrange
        UseMembers((_me.PersonId, "editor"));
        var asMember = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);
        var admin = new Actor(OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);

        // Act
        var asAdmin = await _service.GetProjectAsync(admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asMember.Value.MyRole, Is.EqualTo("editor"));
            Assert.That(asMember.Value.MyPermissions, Does.Contain(WorkPermissions.ItemRead));
            Assert.That(asAdmin.Value.MyRole, Is.Null);
        }
    }

    [Test]
    public async Task GetProject_MemberManagers_AreTheActiveMembersWhoseRoleCanManage()
    {
        // Arrange
        var lead = AddMember("組長", null, null);
        var disabledLead = AddMember("離職的組長", null, null, disabled: true);
        var worker = AddMember("組員", null, null);
        UseMembers((lead, "admin"), (disabledLead, "admin"), (worker, "contributor"));

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value.MemberManagers, Is.EqualTo(new[] { "組長" }));
    }

    [Test]
    public async Task GetProject_Roles_AreTheOnesFromTheStaffingQueries()
    {
        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        var contributor = result.Value.Roles.Single(r => r.Key == "contributor");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Roles, Has.Count.EqualTo(FakeStaffingQueries.DefaultRoles.Count));
            Assert.That(contributor.IsBuiltIn, Is.True);
            Assert.That(contributor.Permissions, Does.Contain(WorkPermissions.ItemReadOwn));
        }
    }

    [Test]
    public async Task GetProject_CyclesAndMilestones_UseTheOrganizationsToday()
    {
        // Arrange：UTC 是 11/18 17:00，台北已經是 11/19 01:00，所以 11/18 結束的週期不再算進行中。
        _clock.Advance(TimeSpan.FromHours(9));
        _planning.AddCycle("上週", new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 18));
        _planning.AddCycle("今天結束", new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 19));
        _planning.AddCycle("下一個", new DateOnly(2026, 11, 20), new DateOnly(2026, 12, 3));
        _planning.AddMilestone("昨天到期", new DateOnly(2026, 11, 18));
        _planning.AddMilestone("今天到期", new DateOnly(2026, 11, 19));

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Cycles.Select(c => c.Name), Is.EqualTo(new[] { "今天結束", "下一個" }));
            Assert.That(result.Value.Milestones.Select(m => m.Name), Is.EqualTo(new[] { "今天到期" }));
        }
    }

    [Test]
    public async Task GetProject_CyclesAndMilestones_AreSortedAndCappedAtTen()
    {
        // Arrange
        for (var i = 0; i < 12; i++)
        {
            _planning.AddCycle($"週期{i}", new DateOnly(2026, 12, 1).AddDays(12 - i), new DateOnly(2027, 1, 1));
            _planning.AddMilestone($"里程碑{i}", new DateOnly(2026, 12, 1).AddDays(12 - i));
        }

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Cycles, Has.Count.EqualTo(10));
            Assert.That(result.Value.Cycles.Select(c => c.StartDate), Is.Ordered);
            Assert.That(result.Value.Milestones, Has.Count.EqualTo(10));
            Assert.That(result.Value.Milestones.Select(m => m.DueDate), Is.Ordered);
        }
    }

    [Test]
    public async Task GetProject_WhenPlanningCannotBeRead_StillSucceedsWithEmptyLists()
    {
        // Arrange
        _planning.AddCycle("週期", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 14));
        _planning.Failure = new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Value.Cycles, Is.Empty);
            Assert.That(result.Value.Milestones, Is.Empty);
        }
    }

    [Test]
    public async Task GetProject_ItemTypes_CarryParentsFirstStateAndFields()
    {
        // Arrange：精簡版藍圖（沒有 blueprint.read）也有類型、欄位、流程。
        _projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.ItemRead]) with { Blueprint = RichBlueprint() };

        // Act
        var result = await _service.GetProjectAsync(_me, "WEB", CancellationToken.None);

        // Assert
        var task = result.Value.ItemTypes.Single(t => t.Key == "task");
        var severity = task.Fields.Single(f => f.Key == "severity");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.ItemTypes.Select(t => t.Key), Is.EqualTo(new[] { "epic", "task" }));
            Assert.That(task.Parents, Is.EqualTo(new[] { "epic" }));
            Assert.That(task.FirstState, Is.EqualTo("待辦"));
            Assert.That(severity.Kind, Is.EqualTo("single_select"));
            Assert.That(severity.Options, Is.EqualTo(new[] { "low", "high" }));
            Assert.That(severity.Required, Is.True);
            Assert.That(task.Fields.Single(f => f.Key == "note").Required, Is.False);
        }
    }

    [Test]
    public async Task GetProject_ProjectNotFound_IsPassedThrough()
    {
        // Arrange
        _projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var result = await _service.GetProjectAsync(_me, "NOPE", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task ListPeople_SkipsDisabledSortsByNameAndFiltersByText()
    {
        // Arrange
        AddMember("阿美", "前端工程師", "WordPress 維運");
        AddMember("阿寶", "後端工程師", "資料庫");
        AddMember("阿停", "前端工程師", null, disabled: true);

        // Act
        var all = await _service.ListPeopleAsync(_me, new PeopleQuery(null, null, 50), CancellationToken.None);
        var byTitle = await _service.ListPeopleAsync(_me, new PeopleQuery(null, "前端", 50), CancellationToken.None);
        var byScope = await _service.ListPeopleAsync(_me, new PeopleQuery(null, "wordpress", 50), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Value.Select(p => p.DisplayName), Is.EqualTo(new[] { "我自己", "阿寶", "阿美" }));
            Assert.That(byTitle.Value.Select(p => p.DisplayName), Is.EqualTo(new[] { "阿美" }));
            Assert.That(byScope.Value.Select(p => p.DisplayName), Is.EqualTo(new[] { "阿美" }));
            Assert.That(all.Value.Single(p => p.DisplayName == "阿美").OrganizationRole, Is.EqualTo("member"));
        }
    }

    [Test]
    public async Task ListPeople_WithAProject_CarriesTheProjectRole_AndLimitIsApplied()
    {
        // Arrange
        var amy = AddMember("阿美", null, null);
        AddMember("阿寶", null, null);
        UseMembers((amy, "contributor"));

        // Act
        var result = await _service.ListPeopleAsync(_me, new PeopleQuery("WEB", null, 2), CancellationToken.None);

        // Assert
        Assert.That(result.Value, Has.Count.EqualTo(2));

        var all = await _service.ListPeopleAsync(_me, new PeopleQuery("WEB", null, 50), CancellationToken.None);
        Assert.That(all.Value.Single(p => p.PersonId == amy).ProjectRole, Is.EqualTo("contributor"));
        Assert.That(all.Value.Where(p => p.PersonId != amy).Select(p => p.ProjectRole), Has.All.Null);
    }

    [Test]
    public async Task ListPeople_ForAGuest_IsForbidden()
    {
        // Arrange
        _people.Set(OrganizationId, _me.PersonId, OrganizationRole.Guest);

        // Act
        var result = await _service.ListPeopleAsync(_me, new PeopleQuery(null, null, 10), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure?.Message, Is.EqualTo("訪客看不到組織的人員名單。"));
        }
    }

    [Test]
    public async Task ListPeople_ForAProjectTheUserCannotSee_IsNotFound()
    {
        // Arrange
        _projects.OnGet = (_, _) => WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        var result = await _service.ListPeopleAsync(_me, new PeopleQuery("NOPE", null, 10), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    private Guid AddMember(string name, string? title, string? scope, bool disabled = false)
    {
        var id = Guid.CreateVersion7();
        _people.Set(OrganizationId, id, OrganizationRole.Member, disabled, name, title, scope);
        return id;
    }

    private void UseMembers(params (Guid Id, string Role)[] members) =>
        _projects.OnGet = (_, _) => WorkSamples.Detail() with { Members = [.. members.Select(m => new ProjectMember(m.Id, m.Role))] };

    private static ProjectBlueprint RichBlueprint() => WorkSamples.Blueprint with
    {
        Fields =
        [
            new FieldDefinition
            {
                Key = "severity",
                Name = "嚴重度",
                Type = FieldType.SingleSelect,
                Options = [new FieldOption { Key = "low", Name = "低" }, new FieldOption { Key = "high", Name = "高" }],
            },
            new FieldDefinition { Key = "note", Name = "備註", Type = FieldType.Text },
        ],
        Workflows =
        [
            new Workflow
            {
                Key = "flow",
                Name = "流程",
                States =
                [
                    new WorkflowState { Key = "todo", Name = "待辦", Category = StateCategory.NotStarted },
                    new WorkflowState { Key = "done", Name = "完成", Category = StateCategory.Done },
                ],
                Transitions = [new WorkflowTransition { From = "todo", To = "done", RequireFields = ["severity"] }],
            },
        ],
        ItemTypes =
        [
            new ItemType { Key = "epic", Name = "史詩", Workflow = "flow" },
            new ItemType { Key = "task", Name = "任務", Workflow = "flow", Fields = ["severity", "note"], Parents = ["epic"] },
        ],
    };
}
