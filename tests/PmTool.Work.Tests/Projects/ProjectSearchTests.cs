using PmTool.Abstractions;
using PmTool.Work.Tests.OwnItems;

namespace PmTool.Work.Tests.Projects;

/// <summary>用代號、名稱或專案說明搜尋專案（internal/HANDOFF-project-info.md 第 6.3 節）。</summary>
[TestFixture]
public sealed class ProjectSearchTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SearchProjectsAsync_MatchesKeyNameAndDescriptionEachOnItsOwn()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "第一號");
        await CreateAsync(scenario, "BBB", "官網 zebra");
        await CreateAsync(scenario, "CCC", "第三號", description: "- 主機：zebra.example.com\n- 資料庫：db01");

        // Act
        var byKey = await SearchAsync(scenario.Admin, "aaa");
        var byName = await SearchAsync(scenario.Admin, "官網");
        var byText = await SearchAsync(scenario.Admin, "DB01");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byKey.Select(h => (h.Project.Key, h.Snippet)), Is.EqualTo(new[] { ("AAA", (string?)null) }));
            Assert.That(byName.Select(h => (h.Project.Key, h.Snippet)), Is.EqualTo(new[] { ("BBB", (string?)null) }));
            Assert.That(byText.Select(h => (h.Project.Key, h.Snippet)), Is.EqualTo(new[] { ("CCC", (string?)"資料庫：db01") }));
        }
    }

    [Test]
    public async Task SearchProjectsAsync_NameAndDescriptionBothMatch_SnippetIsNull()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "zebra 官網", description: "主機：zebra");

        // Act
        var hits = await SearchAsync(scenario.Admin, "zebra");

        // Assert
        Assert.That(hits.Single().Snippet, Is.Null);
    }

    [TestCase("a%b", "a%b", "axb")]
    [TestCase("a_b", "a_b", "axb")]
    [TestCase(@"a\b", @"a\b", "a/b")]
    public async Task SearchProjectsAsync_WildcardCharacters_AreLiteral(string query, string literal, string other)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "甲", description: $"路徑 {literal} 在這裡");
        await CreateAsync(scenario, "BBB", "乙", description: $"路徑 {other} 在這裡");

        // Act
        var hits = await SearchAsync(scenario.Admin, query);

        // Assert
        Assert.That(hits.Select(h => h.Project.Key), Is.EqualTo(new[] { "AAA" }));
    }

    [Test]
    public async Task SearchProjectsAsync_Ordering_FollowsFourTiersThenActiveFirstThenKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "ZED", "完全相同", description: "與 zed 同名的說明");
        await CreateAsync(scenario, "ZEDA", "別的名稱");
        await CreateAsync(scenario, "KEY1", "zed 開頭的名稱");
        await CreateAsync(scenario, "KEY2", "含有 zed 的名稱");
        await CreateAsync(scenario, "KEY3", "只有說明", description: "主機：zed");
        await CreateAsync(scenario, "ARC1", "封存 zed 名稱");
        await CreateAsync(scenario, "ARC2", "含有 zed 的封存");
        await _host.Projects.ArchiveAsync(scenario.Admin, "ARC2", CancellationToken.None);
        await CreateAsync(scenario, "ARC3", "只有說明（封存）", description: "zed");
        await _host.Projects.ArchiveAsync(scenario.Admin, "ARC3", CancellationToken.None);

        // Act
        var hits = await SearchAsync(scenario.Admin, "zed");

        // Assert
        Assert.That(
            hits.Select(h => h.Project.Key),
            Is.EqualTo(new[] { "ZED", "KEY1", "ZEDA", "ARC1", "KEY2", "ARC2", "KEY3", "ARC3" }));
    }

    [Test]
    public async Task SearchProjectsAsync_ArchivedProject_IsIncludedAndMarked()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "舊站 zulu");
        await _host.Projects.ArchiveAsync(scenario.Admin, "AAA", CancellationToken.None);

        // Act
        var hits = await SearchAsync(scenario.Admin, "zulu");

        // Assert
        Assert.That(hits.Single().Project.ArchivedAt, Is.Not.Null);
    }

    [Test]
    public async Task SearchProjectsAsync_Limit_CutsAfterOrdering()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "CCC", "含 yak 的名稱");
        await CreateAsync(scenario, "BBB", "yak 開頭");
        await CreateAsync(scenario, "AAA", "只有說明", description: "yak");

        // Act
        var hits = await SearchAsync(scenario.Admin, "yak", limit: 2);

        // Assert
        Assert.That(hits.Select(h => h.Project.Key), Is.EqualTo(new[] { "BBB", "CCC" }));
    }

    [Test]
    public async Task SearchProjectsAsync_Member_SeesOnlyProjectsTheyBelongTo()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "xray 甲");
        await CreateAsync(scenario, "BBB", "xray 乙", description: "xray");
        await CreateAsync(scenario, "CCC", "xray 丙");
        var contributor = await scenario.MemberAsync("AAA", "contributor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "BBB", contributor.PersonId, "viewer", CancellationToken.None);

        // Act
        var memberHits = await SearchAsync(contributor, "xray");
        var adminHits = await SearchAsync(scenario.Admin, "xray");
        var outsiderHits = await SearchAsync(scenario.Person(), "xray");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(memberHits.Select(h => (h.Project.Key, h.Project.MyRole)), Is.EquivalentTo(new[] { ("AAA", "contributor"), ("BBB", "viewer") }));
            Assert.That(adminHits.Select(h => h.Project.Key), Is.EquivalentTo(new[] { "AAA", "BBB", "CCC" }));
            Assert.That(outsiderHits, Is.Empty);
        }
    }

    [Test]
    public async Task SearchProjectsAsync_MemberWhoseRoleCannotReadItems_StillFindsTheProjectAndMatchesTheProjectList()
    {
        // Arrange：自訂角色沒有 item.read 也沒有 item.read_own。專案清單照樣列它，搜尋的範圍要和清單一致，
        // 不能換成「讀得到項目」的那份規則（ReadableProjects）。
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "tango 甲", description: "tango");
        await CreateAsync(scenario, "BBB", "tango 乙");
        await OwnItemsWorld.PublishAsync(_host, scenario, "AAA", blueprint => blueprint with
        {
            Roles = [OwnItemsWorld.Role("outsider", WorkPermissions.CommentCreate)],
        });
        var outsider = await scenario.MemberAsync("AAA", "outsider");

        // Act
        var hits = await SearchAsync(outsider, "tango");
        var listed = await _host.Projects.ListAsync(outsider, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(hits.Select(h => (h.Project.Key, h.Project.MyRole)), Is.EqualTo(new[] { ("AAA", (string?)"outsider") }));
            Assert.That(hits.Select(h => h.Project.Key), Is.EquivalentTo(listed.Select(p => p.Key)));
        }
    }

    [Test]
    public async Task SearchProjectsAsync_NotAMember_DescriptionIsNotSearchable()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "甲", description: "主機：secret-host");

        // Act
        var hits = await SearchAsync(scenario.Person(), "secret-host");

        // Assert
        Assert.That(hits, Is.Empty);
    }

    [Test]
    public async Task SearchProjectsAsync_AnotherOrganization_NeverAppears()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "whiskey 甲", description: "whiskey");
        var other = new Scenario(_host);
        await CreateAsync(other, "AAA", "whiskey 乙", description: "whiskey");

        // Act
        var hits = await SearchAsync(scenario.Admin, "whiskey");

        // Assert
        Assert.That(hits.Select(h => h.Project.Name), Is.EqualTo(new[] { "whiskey 甲" }));
    }

    [Test]
    public async Task SearchProjectsAsync_TextIsTrimmed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await CreateAsync(scenario, "AAA", "victor 甲");

        // Act
        var hits = await SearchAsync(scenario.Admin, "  victor  ");

        // Assert
        Assert.That(hits, Has.Count.EqualTo(1));
    }

    [TestCase("", 8, "q")]
    [TestCase("   ", 8, "q")]
    [TestCase("a\nb", 8, "q")]
    [TestCase("a\u0000b", 8, "q")]
    [TestCase("a\tb", 8, "q")]
    [TestCase("x", 0, "limit")]
    [TestCase("x", 21, "limit")]
    public async Task SearchProjectsAsync_InvalidInput_IsQueryInvalidWithPath(string text, int limit, string path)
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Descriptions.SearchProjectsAsync(scenario.Admin, text, limit, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.QueryInvalid)));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { path }));
        }
    }

    [Test]
    public async Task SearchProjectsAsync_TextTooLong_IsQueryInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Descriptions.SearchProjectsAsync(scenario.Admin, new string('a', 201), 8, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "q" }));
    }

    [Test]
    public async Task SearchProjectsAsync_TextOfMaxLength_IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var result = await _host.Descriptions.SearchProjectsAsync(scenario.Admin, new string('a', 200), 20, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    private async Task<IReadOnlyList<ProjectSearchHit>> SearchAsync(Actor actor, string text, int limit = 8)
    {
        var result = await _host.Descriptions.SearchProjectsAsync(actor, text, limit, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private async Task CreateAsync(Scenario scenario, string key, string name, string? description = null)
    {
        var created = await _host.Projects.CreateAsync(
            scenario.Admin, new NewProject(key, name, Scenario.SimpleTasks) { Description = description }, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
    }
}
