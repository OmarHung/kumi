using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Projects;

/// <summary>
/// 移除成員與最後一位 admin：移除之後他看不到專案；不是組織管理員的人不能讓專案沒有 admin（移除或換成別的角色都一樣擋下），
/// 組織管理員不擋（他們在每個專案都有全部權限，單人使用時要能把自己換成別的角色）；
/// 他負責的項目照舊，只有之後要把他填進去時才擋。
/// </summary>
[TestFixture]
public sealed class ProjectMemberRemovalTests
{
    private const int Rounds = 5;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RemoveMemberAsync_Member_ReturnsTheMembershipAndHeNoLongerSeesTheProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "他負責的", AssigneeId = editor.PersonId });

        // Act
        var result = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", editor.PersonId, CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var seen = await _host.Projects.GetAsync(editor, "WEB", CancellationToken.None);
        var item = await _host.Items.GetAsync(editor, "WEB-1", CancellationToken.None);
        var projects = await _host.Projects.ListAsync(editor, CancellationToken.None);
        var mine = await _host.Items.ListAssignedToMeAsync(editor, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new ProjectMember(editor.PersonId, "editor")));
            Assert.That(detail.Value.Members, Is.EqualTo(new[] { new ProjectMember(scenario.Admin.PersonId, "admin") }));
            Assert.That(seen.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(item.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(projects, Is.Empty);
            Assert.That(mine, Is.Empty);
        }
    }

    [Test]
    public async Task RemoveMemberAsync_PersonWhoIsNotAMember_IsMemberNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", Guid.NewGuid(), CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.MemberNotFound);
    }

    [Test]
    public async Task RemoveMemberAsync_TheOnlyAdminByAProjectAdminWhoIsNotAnOrganizationAdmin_IsLastAdminAndKeepsHim()
    {
        // Arrange：唯一的 admin 不是組織管理員，另外有一位編輯者。
        var scenario = new Scenario(_host);
        var soleAdmin = await SoleProjectAdminAsync(scenario);
        await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(soleAdmin, "WEB", soleAdmin.PersonId, CancellationToken.None);
        var detail = await _host.Projects.GetAsync(soleAdmin, "WEB", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.LastAdmin);
        Assert.That(detail.Value.Members.Select(m => m.Role), Is.EquivalentTo(new[] { "admin", "editor" }));
    }

    [Test]
    public async Task RemoveMemberAsync_TheOnlyAdminByAnOrganizationAdmin_IsRemovedAndHeStillManagesTheProject()
    {
        // Arrange：組織管理員開的專案，他是唯一的 admin，另外有一位編輯者。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", scenario.Admin.PersonId, CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var promoted = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", editor.PersonId, "admin", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new ProjectMember(scenario.Admin.PersonId, "admin")));
            Assert.That(detail.Value.Members, Is.EqualTo(new[] { new ProjectMember(editor.PersonId, "editor") }));
            Assert.That(detail.Value.MyPermissions, Does.Contain(WorkPermissions.ProjectManage), "組織管理員不是成員也有全部權限");
            Assert.That(promoted.Succeeded, Is.True, "之後還能指定新的 admin");
        }
    }

    [Test]
    public async Task RemoveMemberAsync_OneOfTwoAdmins_IsRemovedEvenWhenItIsTheCallerHimself()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var projectAdmin = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(projectAdmin, "WEB", projectAdmin.PersonId, CancellationToken.None);
        var seen = await _host.Projects.GetAsync(projectAdmin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectMember(projectAdmin.PersonId, "admin")));
        Assert.That(seen.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task RemoveMemberAsync_EditorWithoutProjectManage_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(editor, "WEB", viewer.PersonId, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task RemoveMemberAsync_ProjectTheCallerCannotSee_IsProjectNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RemoveMemberAsync(scenario.Person(), "WEB", scenario.Admin.PersonId, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task SetMemberAsync_TheOnlyAdminToAnotherRoleByAProjectAdminWhoIsNotAnOrganizationAdmin_IsLastAdmin()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var soleAdmin = await SoleProjectAdminAsync(scenario);

        // Act
        var demoted = await _host.Projects.SetMemberAsync(soleAdmin, "WEB", soleAdmin.PersonId, "editor", CancellationToken.None);
        var same = await _host.Projects.SetMemberAsync(soleAdmin, "WEB", soleAdmin.PersonId, "admin", CancellationToken.None);
        var detail = await _host.Projects.GetAsync(soleAdmin, "WEB", CancellationToken.None);

        // Assert
        ItemAssert.Failed(demoted, FailureKind.Conflict, WorkErrorCodes.LastAdmin);
        Assert.That(same.Succeeded, Is.True, "換成同樣的 admin 不會少一位");
        Assert.That(detail.Value.Members, Is.EqualTo(new[] { new ProjectMember(soleAdmin.PersonId, "admin") }));
    }

    [Test]
    public async Task SetMemberAsync_OrganizationAdminWhoIsTheOnlyAdminToACustomRole_ChangesTheRoleAndKeepsAllPermissions()
    {
        // Arrange：單人使用的接案專案，擁有者要把自己換成專案經理，才能走限專案經理的轉換。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);

        // Act
        var result = await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", scenario.Admin.PersonId, "project_manager", CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new ProjectMember(scenario.Admin.PersonId, "project_manager")));
            Assert.That(detail.Value.Project.MyRole, Is.EqualTo("project_manager"));
            Assert.That(detail.Value.MyPermissions, Does.Contain(WorkPermissions.ProjectManage));
        }
    }

    [Test]
    public async Task SetMemberAsync_OneOfTwoAdminsToAnotherRole_ChangesTheRole()
    {
        // Arrange：不是組織管理員的 admin 把另一位 admin 換成檢視者，自己還是 admin。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var projectAdmin = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Projects.SetMemberAsync(projectAdmin, "WEB", scenario.Admin.PersonId, "viewer", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new ProjectMember(scenario.Admin.PersonId, "viewer")));
        var detail = await _host.Projects.GetAsync(projectAdmin, "WEB", CancellationToken.None);
        Assert.That(detail.Value.Members.Count(m => m.Role == "admin"), Is.EqualTo(1));
    }

    [Test]
    public async Task RemoveMemberAsync_BothAdminsAtTheSameTime_RemovesExactlyOne()
    {
        // Arrange：每一輪開新的專案，兩位 admin 同時被不是組織管理員的管理者移除；不管誰先，都要留下一位。
        for (var round = 0; round < Rounds; round++)
        {
            var scenario = new Scenario(_host);
            var steward = await StewardAsync(scenario);
            var second = await scenario.MemberAsync("WEB", "admin");
            Guid[] admins = [scenario.Admin.PersonId, second.PersonId];

            // Act
            var results = await InParallelAsync(admins.Length, (projects, i) =>
                projects.RemoveMemberAsync(steward, "WEB", admins[i], CancellationToken.None));
            var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

            // Assert
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1), $"第 {round + 1} 輪");
            Assert.That(results.Single(r => !r.Succeeded).Failure!.Code, Is.EqualTo(WorkErrorCodes.LastAdmin));
            Assert.That(detail.Value.Members.Count(m => m.Role == "admin"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DemoteAndRemoveTheTwoAdminsAtTheSameTime_KeepsOneAdmin()
    {
        // Arrange：一位被換成編輯者、另一位被移除，兩件事同時由不是組織管理員的管理者送進來。
        for (var round = 0; round < Rounds; round++)
        {
            var scenario = new Scenario(_host);
            var steward = await StewardAsync(scenario);
            var second = await scenario.MemberAsync("WEB", "admin");

            // Act
            var results = await InParallelAsync(2, (projects, i) => i == 0
                ? projects.SetMemberAsync(steward, "WEB", scenario.Admin.PersonId, "editor", CancellationToken.None)
                : projects.RemoveMemberAsync(steward, "WEB", second.PersonId, CancellationToken.None));
            var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

            // Assert
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1), $"第 {round + 1} 輪");
            Assert.That(detail.Value.Members.Count(m => m.Role == "admin"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task UpdateAsync_ItemOfARemovedMember_ChangesOtherFieldsButCannotAssignHimAgain()
    {
        // Arrange：被移除的人是負責人，也是成員欄位「審查者」的值。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var other = await scenario.CreateTaskAsync("WEB", "別的任務");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "他負責的",
            AssigneeId = editor.PersonId,
            Fields = Scenario.Fields(("reviewer", editor.PersonId)),
        });
        await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", editor.PersonId, CancellationToken.None);

        // Act
        var retitled = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Title = "改了標題", Fields = Scenario.Fields(("summary", "補上摘要")) }, CancellationToken.None);
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        var reassigned = await _host.Items.UpdateAsync(scenario.Admin, other.Key, new WorkItemPatch { AssigneeId = editor.PersonId }, CancellationToken.None);
        var reviewer = await _host.Items.UpdateAsync(
            scenario.Admin, other.Key, new WorkItemPatch { Fields = Scenario.Fields(("reviewer", editor.PersonId)) }, CancellationToken.None);

        // Assert
        Assert.That(retitled.Succeeded, Is.True, () => retitled.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(retitled.Value.AssigneeId, Is.EqualTo(editor.PersonId), "負責人照舊");
            Assert.That(retitled.Value.Fields["reviewer"].GetString(), Is.EqualTo(editor.PersonId.ToString()));
            Assert.That(moved.Succeeded, Is.True);
        }

        ItemAssert.Invalid(reassigned, WorkErrorCodes.ItemUnknownAssignee, "assignee");
        ItemAssert.Invalid(reviewer, WorkErrorCodes.ItemUnknownAssignee, "reviewer");
    }

    /// <summary>
    /// 開專案、加一位不是組織管理員的 admin，再把開專案的組織管理員移出去：他就是專案唯一的 admin。
    /// </summary>
    private async Task<Actor> SoleProjectAdminAsync(Scenario scenario)
    {
        await scenario.CreateProjectAsync("WEB");
        var soleAdmin = await scenario.MemberAsync("WEB", "admin");
        var removed = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", scenario.Admin.PersonId, CancellationToken.None);
        Assert.That(removed.Succeeded, Is.True, () => removed.Failure!.Message);
        return soleAdmin;
    }

    /// <summary>
    /// 開專案並加一位自訂角色「成員管理」的人：有 project.manage、不是 admin、也不是組織管理員。
    /// 同時移除兩位 admin 時由他送出，他自己的權限不受影響，結果只看最後一位 admin 的規則。
    /// </summary>
    private static async Task<Actor> StewardAsync(Scenario scenario)
    {
        await scenario.CreateProjectAsync("WEB");
        var steward = new RoleDefinition { Key = "steward", Name = "成員管理", Permissions = [WorkPermissions.ItemRead, WorkPermissions.ProjectManage] };
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Roles = [.. b.Roles, steward] });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        return await scenario.MemberAsync("WEB", steward.Key);
    }

    /// <summary>所有請求先準備好再一起放行，盡量讓它們真的同時打到資料庫。</summary>
    private async Task<Result<ProjectMember>[]> InParallelAsync(int count, Func<IProjectService, int, Task<Result<ProjectMember>>> call)
    {
        var requests = Enumerable.Range(0, count).Select(_ => _host.NewRequest()).ToArray();
        using var gate = new SemaphoreSlim(0, count);
        var tasks = requests.Select((request, i) => Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await call(request.Projects, i);
        })).ToArray();
        gate.Release(count);
        var results = await Task.WhenAll(tasks);
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        return results;
    }
}
