using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>派工查詢：專案的角色清單，以及每個負責人手上還沒結束的項目數。</summary>
[TestFixture]
public sealed class ProjectStaffingQueriesTests
{
    private const string BugTemplate = "bug_tracking";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListRolesAsync_BuiltInRolesComeFirstInFixedOrderThenCustomRolesInBlueprintOrder()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);

        // Act
        var result = await _host.Staffing.ListRolesAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        var roles = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roles.Select(r => r.Key), Is.EqualTo(new[] { "admin", "editor", "contributor", "commenter", "viewer", "project_manager", "client" }));
            Assert.That(roles.Select(r => r.IsBuiltIn), Is.EqualTo(new[] { true, true, true, true, true, false, false }));
            Assert.That(roles.Take(5).Select(r => r.Name), Is.EqualTo(new[] { "管理員", "編輯者", "執行者", "留言者", "檢視者" }));
            Assert.That(roles[5].Name, Is.EqualTo("專案經理"));
        }
    }

    [Test]
    public async Task ListRolesAsync_Permissions_FollowTheOrderOfProjectDetail()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var contributor = await scenario.MemberAsync("WEB", "contributor");
        var detail = await _host.Projects.GetAsync(contributor, "WEB", CancellationToken.None);

        // Act
        var roles = (await _host.Staffing.ListRolesAsync(scenario.Admin, "WEB", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(roles.Single(r => r.Key == "contributor").Permissions, Is.EqualTo(detail.Value.MyPermissions));
            Assert.That(roles.Single(r => r.Key == "admin").Permissions, Does.Contain(WorkPermissions.ProjectManage));
            Assert.That(roles.Single(r => r.Key == "viewer").Permissions, Is.EqualTo(new[] { WorkPermissions.ItemRead, WorkPermissions.BlueprintRead }));
            Assert.That(roles.Single(r => r.Key == "client").Permissions, Is.EqualTo(new[] { WorkPermissions.ItemRead, WorkPermissions.CommentCreate }));
        }
    }

    [Test]
    public async Task ListRolesAsync_MemberWhoOnlySeesOwnItems_CanRead()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var contributor = await scenario.MemberAsync("WEB", "contributor");

        // Act
        var result = await _host.Staffing.ListRolesAsync(contributor, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task ListRolesAsync_NonMemberAndMissingProject_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var outsider = scenario.Person();

        // Act
        var hidden = await _host.Staffing.ListRolesAsync(outsider, "WEB", CancellationToken.None);
        var missing = await _host.Staffing.ListRolesAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hidden.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(missing.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task CountOpenByAssigneeAsync_ExcludesDoneCanceledDeletedAndUnassigned()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("BUG", BugTemplate);
        var alice = await scenario.MemberAsync("BUG", "editor");
        var bob = await scenario.MemberAsync("BUG", "editor");
        await BugAsync(scenario, "進行一", alice.PersonId);
        await BugAsync(scenario, "進行二", alice.PersonId);
        await BugAsync(scenario, "進行三", bob.PersonId);
        await BugAsync(scenario, "沒人負責", null);
        var closed = await BugAsync(scenario, "已關閉", alice.PersonId);
        var wontFix = await BugAsync(scenario, "不處理", alice.PersonId);
        var deleted = await BugAsync(scenario, "已刪除", bob.PersonId);
        await MoveAsync(scenario, closed.Key, "confirmed", "fixing", "verifying", "closed");
        await MoveAsync(scenario, wontFix.Key, "wontfix");
        Assert.That((await _host.Items.DeleteAsync(scenario.Admin, deleted.Key, CancellationToken.None)).Succeeded, Is.True);

        // Act
        var result = await _host.Staffing.CountOpenByAssigneeAsync(scenario.Admin, "BUG", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value, Is.EquivalentTo(new Dictionary<Guid, int> { [alice.PersonId] = 2, [bob.PersonId] = 1 }));
    }

    [Test]
    public async Task CountOpenByAssigneeAsync_PersonWithOnlyFinishedItems_IsNotInResult()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("BUG", BugTemplate);
        var alice = await scenario.MemberAsync("BUG", "editor");
        var item = await BugAsync(scenario, "已關閉", alice.PersonId);
        await MoveAsync(scenario, item.Key, "confirmed", "fixing", "verifying", "closed");

        // Act
        var result = await _host.Staffing.CountOpenByAssigneeAsync(scenario.Admin, "BUG", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.Empty);
    }

    [Test]
    public async Task CountOpenByAssigneeAsync_MemberWhoOnlySeesOwnItems_GetsOnlyOwnNumbers()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var mine = await scenario.MemberAsync("WEB", "contributor");
        var other = await scenario.MemberAsync("WEB", "contributor");
        await TaskItemAsync(scenario, "我的一", mine.PersonId);
        await TaskItemAsync(scenario, "我的二", mine.PersonId);
        await TaskItemAsync(scenario, "別人的", other.PersonId);
        await TaskItemAsync(scenario, "我建立、別人負責", other.PersonId, mine);

        // Act
        var result = await _host.Staffing.CountOpenByAssigneeAsync(mine, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EquivalentTo(new Dictionary<Guid, int> { [mine.PersonId] = 2, [other.PersonId] = 1 }));
    }

    [Test]
    public async Task CountOpenByAssigneeAsync_NonMemberAndMissingProject_AreNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var hidden = await _host.Staffing.CountOpenByAssigneeAsync(scenario.Person(), "WEB", CancellationToken.None);
        var missing = await _host.Staffing.CountOpenByAssigneeAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hidden.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
            Assert.That(missing.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
        }
    }

    [Test]
    public async Task CountOpenByAssigneeAsync_QueryCountDoesNotGrowWithAssignees()
    {
        // Arrange
        var few = new Scenario(_host);
        await few.CreateProjectAsync("WEB");
        await TaskItemAsync(few, "一", (await few.MemberAsync("WEB", "editor")).PersonId);
        var many = new Scenario(_host);
        await many.CreateProjectAsync("WEB");
        for (var i = 0; i < 6; i++)
        {
            await TaskItemAsync(many, $"項目{i}", (await many.MemberAsync("WEB", "editor")).PersonId);
        }

        // Act
        var queriesWithFew = await CountQueriesAsync(few.Admin);
        var queriesWithMany = await CountQueriesAsync(many.Admin);

        // Assert
        Assert.That(queriesWithMany, Is.EqualTo(queriesWithFew));
    }

    private async Task<int> CountQueriesAsync(Actor actor)
    {
        using var sql = new SqlLog();
        await using var provider = WorkTestHost.BuildProvider(
            _host.ConnectionString, _host.Clock, services => services.AddLogging(logging => logging.AddProvider(sql)));
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IProjectStaffingQueries>();

        var counts = await queries.CountOpenByAssigneeAsync(actor, "WEB", CancellationToken.None);

        Assert.That(counts.Value, Is.Not.Empty);
        return sql.Commands.Count(c => c.Contains("SELECT", StringComparison.Ordinal));
    }

    private static Task<WorkItem> TaskItemAsync(Scenario scenario, string title, Guid assignee, Actor? actor = null) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = title, AssigneeId = assignee }, actor);

    private static Task<WorkItem> BugAsync(Scenario scenario, string title, Guid? assignee) =>
        scenario.CreateItemAsync("BUG", new NewWorkItem { Type = "bug", Title = title, AssigneeId = assignee });

    private async Task MoveAsync(Scenario scenario, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            var moved = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
            Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        }
    }
}
