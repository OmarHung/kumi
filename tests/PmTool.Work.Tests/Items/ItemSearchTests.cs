using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>跨專案搜尋：看得到的範圍、排序、跳脫、驗證。</summary>
[TestFixture]
public sealed class ItemSearchTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SearchAsync_AcrossProjects_FindsItemsInEveryReadableProject()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await scenario.CreateTaskAsync("WEB", "會員登入");
        await scenario.CreateTaskAsync("APP", "登入頁面");
        await scenario.CreateTaskAsync("APP", "無關的");

        // Act
        var found = await SearchAsync(scenario.Admin, "登入");

        // Assert
        Assert.That(found.Select(i => i.Key), Is.EquivalentTo(new[] { "WEB-1", "APP-1" }));
    }

    [Test]
    public async Task SearchAsync_ProjectTheCallerIsNotAMemberOf_IsNotSearched()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await scenario.CreateTaskAsync("WEB", "登入流程");
        await scenario.CreateTaskAsync("APP", "登入畫面");
        var member = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var found = await SearchAsync(member, "登入");

        // Assert
        Assert.That(found.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
    }

    [Test]
    public async Task SearchAsync_ProjectWhereTheRoleCannotReadItems_IsNotSearched()
    {
        // Arrange：自訂角色只能留言、讀不到項目，和 ReadableProjectIdsAsync 同一套判斷。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var current = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        await StoredBlueprints.PublishAsync(_host, scenario, "WEB", current.Value.Blueprint with
        {
            Roles = [new RoleDefinition { Key = "guest", Name = "訪客", Permissions = [WorkPermissions.CommentCreate] }],
        });
        await scenario.CreateTaskAsync("WEB", "祕密");
        var guest = await scenario.MemberAsync("WEB", "guest");

        // Act
        var found = await SearchAsync(guest, "祕密");

        // Assert
        Assert.That(found, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_DeletedItems_AreNotFound()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var gone = await scenario.CreateTaskAsync("WEB", "要刪的登入");
        await scenario.CreateTaskAsync("WEB", "留著的登入");
        var deleted = await _host.Items.DeleteAsync(scenario.Admin, gone.Key, CancellationToken.None);
        Assert.That(deleted.Succeeded, Is.True);

        // Act
        var found = await SearchAsync(scenario.Admin, "登入");

        // Assert
        Assert.That(found.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2" }));
    }

    [Test]
    public async Task SearchAsync_AnotherOrganization_IsNotSearched()
    {
        // Arrange
        var mine = await TwoProjectsAsync();
        var theirs = await TwoProjectsAsync();
        await theirs.CreateTaskAsync("WEB", "別家的登入");
        await mine.CreateTaskAsync("WEB", "我家的登入");

        // Act
        var found = await SearchAsync(mine.Admin, "登入");

        // Assert
        Assert.That(found.Select(i => i.Title), Is.EqualTo(new[] { "我家的登入" }));
    }

    [Test]
    public async Task SearchAsync_Ranking_ExactKeyThenPrefixThenContainsThenMostRecentlyUpdated()
    {
        // Arrange：WEB-2 的標題包含 web-1；WEB-1 的編號完全符合。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "一般");
        await scenario.CreateTaskAsync("WEB", "see web-1 here");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateTaskAsync("WEB", "web-1 開頭較新");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateTaskAsync("WEB", "web-1 開頭最新");

        // Act
        var found = await SearchAsync(scenario.Admin, "web-1");

        // Assert
        Assert.That(found.Select(i => i.Number), Is.EqualTo(new long[] { 1, 4, 3, 2 }));
    }

    [Test]
    public async Task SearchAsync_SameRank_MoreRecentlyUpdatedFirst()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "A 報表");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateTaskAsync("WEB", "B 報表");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-1", new WorkItemPatch { Title = "A 報表改" }, CancellationToken.None);

        // Act
        var found = await SearchAsync(scenario.Admin, "報表");

        // Assert
        Assert.That(found.Select(i => i.Number), Is.EqualTo(new long[] { 1, 2 }));
    }

    [TestCase("%", new long[] { 1 })]
    [TestCase("_", new long[] { 2 })]
    [TestCase("\\", new long[] { 3 })]
    [TestCase("a%b", new long[] { })]
    public async Task SearchAsync_Wildcards_AreLiteralCharacters(string text, long[] expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "完成 100%");
        await scenario.CreateTaskAsync("WEB", "snake_case 命名");
        await scenario.CreateTaskAsync("WEB", "路徑 C:\\temp");
        await scenario.CreateTaskAsync("WEB", "aXb 測試");

        // Act
        var found = await SearchAsync(scenario.Admin, text);

        // Assert
        Assert.That(found.Select(i => i.Number).Order(), Is.EqualTo(expected));
    }

    [Test]
    public async Task SearchAsync_TextWithSurroundingSpaces_IsTrimmed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "月報");

        // Act
        var found = await SearchAsync(scenario.Admin, "  月報  ");

        // Assert
        Assert.That(found, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SearchAsync_MoreMatchesThanTheLimit_ReturnsAtMostLimit()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var n in Enumerable.Range(1, 5))
        {
            await scenario.CreateTaskAsync("WEB", $"搜尋 {n}");
        }

        // Act
        var found = await SearchAsync(scenario.Admin, "搜尋", limit: 3);

        // Assert
        Assert.That(found, Has.Count.EqualTo(3));
    }

    [TestCase("", 8, "q")]
    [TestCase("   ", 8, "q")]
    [TestCase("a\u0000b", 8, "q")]
    [TestCase("a\nb", 8, "q")]
    [TestCase("a\u001Bb", 8, "q")]
    [TestCase("ok", 0, "limit")]
    [TestCase("ok", 21, "limit")]
    public async Task SearchAsync_InvalidInput_IsQueryInvalidAtTheParameter(string text, int limit, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Items.SearchAsync(scenario.Admin, text, limit, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, path);
    }

    [Test]
    public async Task SearchAsync_NullTooLongOrLoneSurrogateText_IsQueryInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var tooLong = await _host.Items.SearchAsync(scenario.Admin, new string('字', 201), 8, CancellationToken.None);
        var missing = await _host.Items.SearchAsync(scenario.Admin, null!, 8, CancellationToken.None);
        var lone = await _host.Items.SearchAsync(scenario.Admin, "x" + (char)0xD800, 8, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(tooLong, WorkErrorCodes.QueryInvalid, "q");
        ItemAssert.Invalid(missing, WorkErrorCodes.QueryInvalid, "q");
        ItemAssert.Invalid(lone, WorkErrorCodes.QueryInvalid, "q");
    }

    private async Task<Scenario> TwoProjectsAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("APP");
        return scenario;
    }

    private async Task<IReadOnlyList<WorkItem>> SearchAsync(Actor actor, string text, int limit = 8)
    {
        var result = await _host.Items.SearchAsync(actor, text, limit, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }
}
