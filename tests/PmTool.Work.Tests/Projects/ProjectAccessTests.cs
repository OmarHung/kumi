using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>誰看得到哪些專案、在專案裡能做什麼、誰可以管成員。</summary>
[TestFixture]
public sealed class ProjectAccessTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetAsync_NotAMember_IsNotFoundLikeAMissingProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var hidden = await _host.Projects.GetAsync(scenario.Person(), "WEB", CancellationToken.None);
        var missing = await _host.Projects.GetAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hidden.Failure!.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(hidden.Failure.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(missing.Failure!.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task GetAsync_AdminOfAnotherOrganization_IsNotFound()
    {
        // Arrange
        await new Scenario(_host).CreateProjectAsync("WEB");
        var outsider = new Scenario(_host).Admin;

        // Act
        var result = await _host.Projects.GetAsync(outsider, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task GetAsync_LowerCaseKey_FindsTheProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.GetAsync(scenario.Admin, "web", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Project.Key, Is.EqualTo("WEB"));
    }

    [Test]
    public async Task GetAsync_SecondRead_UsesTheCachedBlueprint()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var first = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var second = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert：同一個物件表示沒有重新解析文件。
        Assert.That(second.Value.Blueprint, Is.SameAs(first.Value.Blueprint));
    }

    [Test]
    public async Task GetAsync_OrganizationAdminWhoIsNotAMember_SeesEverythingWithoutARole()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var otherAdmin = scenario.Admin with { PersonId = Guid.NewGuid() };

        // Act
        var result = await _host.Projects.GetAsync(otherAdmin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Project.MyRole, Is.Null);
        Assert.That(result.Value.MyPermissions, Has.Count.EqualTo(10).And.Contains(WorkPermissions.ProjectManage));
    }

    [TestCase("admin", new[] { "item.read", "item.create", "item.update", "item.transition", "item.delete", "comment.create", "blueprint.read", "blueprint.propose", "blueprint.apply", "project.manage" })]
    [TestCase("editor", new[] { "item.read", "item.create", "item.update", "item.transition", "item.delete", "comment.create", "blueprint.read" })]
    [TestCase("commenter", new[] { "item.read", "comment.create", "blueprint.read" })]
    [TestCase("viewer", new[] { "item.read", "blueprint.read" })]
    [TestCase("client", new[] { "item.read", "comment.create" })]
    [TestCase("project_manager", new[] { "item.read", "item.create", "item.update", "item.transition", "comment.create", "blueprint.read", "blueprint.propose", "blueprint.apply" })]
    public async Task GetAsync_Member_ListsThePermissionsOfTheirRole(string role, string[] expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var member = await scenario.MemberAsync("WEB", role);

        // Act
        var result = await _host.Projects.GetAsync(member, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Project.MyRole, Is.EqualTo(role));
        Assert.That(result.Value.MyPermissions, Is.EqualTo(expected));
    }

    [Test]
    public async Task GetAsync_RoleWithoutBlueprintRead_GetsTheBlueprintWithoutAutomationsOrBindings()
    {
        // Arrange：接案專案的客戶沒有 blueprint.read；綁定裡寫著外部系統的資源名稱。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");

        // Act
        var result = await _host.Projects.GetAsync(client, "WEB", CancellationToken.None);

        // Assert：畫面需要的部分都在。
        var blueprint = result.Value.Blueprint;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(blueprint.Automations, Is.Empty);
            Assert.That(blueprint.Bindings, Is.Empty);
            Assert.That(blueprint.ItemTypes.Select(t => t.Key), Is.EqualTo(new[] { "requirement", "change_request", "task", "subtask" }));
            Assert.That(blueprint.Fields.Select(f => f.Key), Is.EqualTo(new[] { "quote_amount", "client_signoff_date", "change_reason", "impact", "actual_hours" }));
            Assert.That(blueprint.Workflows.Select(w => w.Key), Is.EqualTo(new[] { "delivery", "task_flow" }));
            Assert.That(blueprint.Roles.Select(r => r.Key), Is.EqualTo(new[] { "project_manager", "client" }));
            Assert.That(blueprint.Views.Select(v => v.Key), Is.EqualTo(new[] { "progress_board", "requirement_table", "client_view" }));
        }
    }

    [TestCase("project_manager")]
    [TestCase("viewer")]
    [TestCase(null)]
    public async Task GetAsync_RoleWithBlueprintRead_GetsTheWholeBlueprint(string? role)
    {
        // Arrange：null 表示不是成員的組織管理員。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var caller = role is null ? scenario.Admin with { PersonId = Guid.NewGuid() } : await scenario.MemberAsync("WEB", role);

        // Act
        var result = await _host.Projects.GetAsync(caller, "WEB", CancellationToken.None);

        // Assert
        var blueprint = result.Value.Blueprint;
        Assert.That(blueprint.Automations.Select(a => a.Key), Is.EqualTo(new[] { "mr_merged_to_internal_qa" }));
        Assert.That(blueprint.Bindings.Select(b => b.Resource), Is.EqualTo(new[] { "example-group/web-app" }));
    }

    [Test]
    public async Task ListAsync_Member_SeesOnlyProjectsTheyBelongToInCreationOrder()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("AAA");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateProjectAsync("BBB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateProjectAsync("CCC");
        var member = await scenario.MemberAsync("CCC", "viewer");
        await _host.Projects.SetMemberAsync(scenario.Admin, "AAA", member.PersonId, "editor", CancellationToken.None);

        // Act
        var mine = await _host.Projects.ListAsync(member, CancellationToken.None);
        var all = await _host.Projects.ListAsync(scenario.Admin with { PersonId = Guid.NewGuid() }, CancellationToken.None);
        var outsider = await _host.Projects.ListAsync(scenario.Person(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(mine.Select(p => (p.Key, p.MyRole)), Is.EqualTo(new[] { ("AAA", "editor"), ("CCC", "viewer") }));
            Assert.That(all.Select(p => (p.Key, p.MyRole)), Is.EqualTo(new[] { ("AAA", (string?)null), ("BBB", null), ("CCC", null) }));
            Assert.That(outsider, Is.Empty);
        }
    }

    [Test]
    public async Task ListAsync_OrganizationAdmin_DoesNotSeeOtherOrganizations()
    {
        // Arrange
        await new Scenario(_host).CreateProjectAsync("WEB");
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task SetMemberAsync_ExistingMember_ChangesTheRole()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);

        // 成員依加入的時間排序；測試的時鐘不會自己走，要讓第二個人晚一點加入。
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        var member = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", member.PersonId, "project_manager", CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectMember(member.PersonId, "project_manager")));
        Assert.That(
            detail.Value.Members,
            Is.EqualTo(new[] { new ProjectMember(scenario.Admin.PersonId, "admin"), new ProjectMember(member.PersonId, "project_manager") }));
    }

    [Test]
    public async Task SetMemberAsync_ProjectAdminWhoIsNotOrganizationAdmin_CanManageMembers()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var projectAdmin = await scenario.MemberAsync("WEB", "admin");
        var newcomer = scenario.Person();

        // Act
        var result = await _host.Projects.SetMemberAsync(projectAdmin, "WEB", newcomer.PersonId, "editor", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task SetMemberAsync_EditorWithoutProjectManage_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Projects.SetMemberAsync(editor, "WEB", Guid.NewGuid(), "viewer", CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task SetMemberAsync_ProjectTheCallerCannotSee_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Person(), "WEB", Guid.NewGuid(), "viewer", CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [TestCase("owner")]
    [TestCase("project_manager")]
    [TestCase("")]
    public async Task SetMemberAsync_RoleNotInThisProject_IsInvalid(string role)
    {
        // Arrange：simple_tasks 沒有自訂角色，所以 project_manager 在這裡不存在。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", Guid.NewGuid(), role, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.MemberUnknownRole));
        Assert.That(result.Failure.Details.Single().Path, Is.EqualTo("role"));
    }

    [Test]
    public async Task SetMemberAsync_SamePersonAtTheSameTime_KeepsOneMembership()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var person = Guid.NewGuid();
        var requests = Enumerable.Range(0, 5).Select(_ => _host.NewRequest()).ToArray();

        // Act
        var results = await Task.WhenAll(requests.Select(r =>
            r.Projects.SetMemberAsync(scenario.Admin, "WEB", person, "viewer", CancellationToken.None)));
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
        Assert.That(detail.Value.Members.Count(m => m.PersonId == person), Is.EqualTo(1));
    }
}
