using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 即時推送的權限變化與路由（真的資料庫）：加入或移出專案、改角色、停用後不等兩分鐘就生效；
/// 只看自己項目的人在刪除、復原、改類型時的路由；另一個組織的人什麼都收不到。
/// </summary>
[TestFixture]
public sealed class LiveAccessFlowTests : LiveFlowTestBase
{
    private LiveUser _contributor = null!;
    private LiveUser _otherOrganization = null!;

    [OneTimeSetUp]
    public async Task SignInTheOthersAsync()
    {
        _contributor = await NewMemberAsync("contributor");
        var email = await CreateAnotherOrganizationAsync();
        _otherOrganization = await LiveUser.SignInAsync(Factory, email, RealStackFactory.OwnerPassword);
    }

    [OneTimeTearDown]
    public void DisposeTheOthers()
    {
        _contributor?.Dispose();
        _otherOrganization?.Dispose();
    }

    [Test]
    public async Task AddingAMemberToAProject_GivesTheirOpenConnectionTheProjectWithoutWaitingForTheCheck()
    {
        // Arrange：E 開著連線，還不是 LAA 的成員。
        await using var live = await ConnectAsync(Editor);
        var project = await RealStackSteps.CreateProjectAsync(Owner.Client, "LAA", "simple_tasks");
        var projectId = project["id"]!.GetValue<Guid>();

        // Act：沒有跑重新檢查，只靠加入成員之後的立刻重算。
        await RealStackSteps.SetMemberAsync(Owner.Client, "LAA", Editor.PersonId, "editor");
        await WaitForGroupsAsync(Editor.PersonId, [LiveGroups.Project(projectId)]);
        await Factory.RunLiveOnceAsync();
        await RealStackSteps.CreateItemAsync(Owner.Client, "LAA", new { type = "task", title = "加入之後的項目" });
        await Factory.RunLiveOnceAsync();
        var message = await live.NextAsync();

        // Assert
        Assert.That(Keys(message), Is.EqualTo(new[] { "LAA-1" }));
    }

    [Test]
    public async Task RemovingAMemberFromAProject_TakesTheProjectAwayFromTheirOpenConnectionAtOnce()
    {
        // Arrange
        var project = await StartProjectAsync("LAB");
        await using var live = await ConnectAsync(Editor, [LiveGroups.Project(project)]);

        // Act
        using var removed = await Owner.Client.DeleteAsync($"/v1/projects/LAB/members/{Editor.PersonId}");
        await WaitUntilAsync(() => !GroupsOf(Editor.PersonId).Contains(LiveGroups.Project(project)));
        await Factory.RunLiveOnceAsync();
        await RealStackSteps.CreateItemAsync(Owner.Client, "LAB", new { type = "task", title = "移出之後的項目" });
        await Factory.RunLiveOnceAsync();

        // Assert
        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await live.ExpectNothingAsync();
    }

    [Test]
    public async Task CreatingAProject_GivesAnAdministratorsOpenConnectionTheNewProjectAtOnce()
    {
        // Arrange：擁有者看得到所有專案，新專案一建立，他開著的連線就要有它的群組。
        await using var live = await ConnectAsync(Owner);

        // Act
        var project = await RealStackSteps.CreateProjectAsync(Owner.Client, "LAC", "simple_tasks");
        var projectId = project["id"]!.GetValue<Guid>();

        // Assert
        await WaitForGroupsAsync(Owner.PersonId, [LiveGroups.Project(projectId)]);
    }

    [Test]
    public async Task ChangingARoleToAdministrator_GivesTheOpenConnectionEveryProjectAtOnce()
    {
        // Arrange
        var promoted = await NewMemberAsync("promoted");
        try
        {
            var project = await RealStackSteps.CreateProjectAsync(Owner.Client, "LAD", "simple_tasks");
            var projectId = project["id"]!.GetValue<Guid>();
            await using var live = await ConnectAsync(promoted);

            // Act
            using var response = await Owner.Client.PutAsync($"/v1/people/{promoted.PersonId}/role", ApiRequests.Json(new { role = "admin" }));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await WaitForGroupsAsync(promoted.PersonId, [LiveGroups.Project(projectId)]);
        }
        finally
        {
            promoted.Dispose();
        }
    }

    [Test]
    public async Task DisablingAPerson_ClosesTheirOpenConnectionsAtOnce()
    {
        // Arrange
        var leaving = await NewMemberAsync("disabled");
        try
        {
            await using var live = await ConnectAsync(leaving);

            // Act：沒有跑重新檢查。
            using var response = await Owner.Client.PostAsync($"/v1/people/{leaving.PersonId}/disable", content: null);
            await live.WaitUntilClosedAsync();

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            leaving.Dispose();
        }
    }

    [Test]
    public async Task OwnItemsOnlyMember_GetsDeleteAndRestoreOfTheirOwnItemsButNotOthers()
    {
        // Arrange
        var project = await StartProjectAsync("LAE", (_contributor, "contributor"));
        await RealStackSteps.CreateItemAsync(Owner.Client, "LAE", new { type = "task", title = "給 C", assigneeId = _contributor.PersonId });
        await RealStackSteps.CreateItemAsync(Owner.Client, "LAE", new { type = "task", title = "別人的" });
        await using var live = await ConnectAsync(_contributor, [LiveGroups.Own(project, _contributor.PersonId)]);

        // Act
        using var deletedOthers = await Owner.Client.DeleteAsync("/v1/items/LAE-2");
        await Factory.RunLiveOnceAsync();
        using var deleted = await Owner.Client.DeleteAsync("/v1/items/LAE-1");
        await Factory.RunLiveOnceAsync();
        var deletion = await live.NextAsync();
        using var restoredOthers = await Owner.Client.PostAsync("/v1/items/LAE-2/restore", content: null);
        using var restored = await Owner.Client.PostAsync("/v1/items/LAE-1/restore", content: null);
        await Factory.RunLiveOnceAsync();
        var restoration = await live.NextAsync();

        // Assert
        Assert.That(new[] { deletedOthers, deleted, restoredOthers, restored }.Select(r => r.StatusCode), Has.All.EqualTo(HttpStatusCode.OK));
        Assert.That(Keys(deletion), Is.EqualTo(new[] { "LAE-1" }));
        Assert.That(Keys(restoration), Is.EqualTo(new[] { "LAE-1" }));
        await live.ExpectNothingAsync();
    }

    [Test]
    public async Task OwnItemsOnlyMember_GetsTheTypeChangeOfTheirOwnItemOnly()
    {
        // Arrange
        var created = await RealStackSteps.CreateProjectAsync(Owner.Client, "LAF", "agency_project");
        await RealStackSteps.SetMemberAsync(Owner.Client, "LAF", Editor.PersonId, "editor");
        await RealStackSteps.SetMemberAsync(Owner.Client, "LAF", _contributor.PersonId, "contributor");
        var project = created["id"]!.GetValue<Guid>();
        await RealStackSteps.CreateItemAsync(
            Owner.Client, "LAF", new { type = "change_request", title = "C 的變更單", assigneeId = _contributor.PersonId, fields = new { change_reason = "原因", impact = "low" } });
        await RealStackSteps.CreateItemAsync(
            Owner.Client, "LAF", new { type = "change_request", title = "別人的變更單", fields = new { change_reason = "原因", impact = "low" } });
        await using var live = await ConnectAsync(_contributor, [LiveGroups.Own(project, _contributor.PersonId)]);

        // Act
        using var others = await Owner.Client.PostAsync("/v1/items/LAF-2/type", ApiRequests.Json(new { type = "task" }));
        using var mine = await Owner.Client.PostAsync("/v1/items/LAF-1/type", ApiRequests.Json(new { type = "task" }));
        await Factory.RunLiveOnceAsync();
        var message = await live.NextAsync();

        // Assert
        Assert.That((others.StatusCode, mine.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
        Assert.That(Keys(message), Is.EqualTo(new[] { "LAF-1" }));
        await live.ExpectNothingAsync();
    }

    [Test]
    public async Task PersonOfAnotherOrganization_NeverReceivesAnythingOfThisOne()
    {
        // Arrange：另一個組織的擁有者看得到「他的組織」所有的專案，但不包括這個組織的。
        var project = await StartProjectAsync("LAG");
        await using var other = await ConnectAsync(_otherOrganization);
        await using var editor = await ConnectAsync(Editor, [LiveGroups.Project(project)]);

        // Act
        await RealStackSteps.CreateItemAsync(Owner.Client, "LAG", new { type = "task", title = "這個組織的項目" });
        await RealStackSteps.PatchItemAsync(Owner.Client, "LAG-1", new { title = "改過" });
        await Factory.RunLiveOnceAsync();
        var received = await editor.NextAsync();

        // Assert
        Assert.That(received.Name, Is.EqualTo("items"));
        Assert.That(GroupsOf(_otherOrganization.PersonId), Does.Not.Contain(LiveGroups.Project(project)));
        await other.ExpectNothingAsync();
    }

    private System.Collections.Immutable.ImmutableHashSet<string> GroupsOf(Guid personId) =>
        Factory.Services.GetRequiredService<LiveConnections>().Snapshot().First(c => c.PersonId == personId).Groups;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.That(condition(), Is.True, "條件在期限內沒有成立");
    }

    /// <summary>
    /// 直接在資料庫裡建立第二個組織與它的擁有者（API 只能建立第一個組織）；密碼雜湊借第一個擁有者的，所以密碼相同。
    /// 回傳新擁有者的 Email。
    /// </summary>
    private async Task<string> CreateAnotherOrganizationAsync()
    {
        var (organizationId, personId, accountId) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var email = RealStackSteps.UniqueEmail("other-org-owner");
        await using var connection = new NpgsqlConnection(Factory.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.organizations (id, name, created_at) VALUES (@organization, 'Other Example Org', now());
            INSERT INTO identity.people (id, organization_id, display_name, role, created_at) VALUES (@person, @organization, 'Other Owner', 'owner', now());
            INSERT INTO identity.accounts (id, organization_id, person_id, email, password_hash, created_at)
            SELECT @account, @organization, @person, @email, password_hash, now() FROM identity.accounts WHERE email = @ownerEmail;
            """,
            connection);
        command.Parameters.AddWithValue("organization", organizationId);
        command.Parameters.AddWithValue("person", personId);
        command.Parameters.AddWithValue("account", accountId);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("ownerEmail", RealStackFactory.OwnerEmail);
        await command.ExecuteNonQueryAsync();
        return email;
    }
}
