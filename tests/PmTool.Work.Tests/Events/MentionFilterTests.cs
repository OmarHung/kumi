using PmTool.Abstractions;

namespace PmTool.Work.Tests.Events;

/// <summary>
/// 事件裡的提及：只留看得到專案的成員、不含做這件事的人、去重、最多 20 個。
/// 不合格的提及照文字存在留言與說明裡，不擋。
/// </summary>
[TestFixture]
public sealed class MentionFilterTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AddCommentAsync_MentioningMembersOfAnyRole_ListsThemInTheOrderTheyAppear()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, $"{EventFeed.Mention(editor)} {EventFeed.Mention(viewer)}", null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.EqualTo(new[] { editor.PersonId, viewer.PersonId }));
    }

    [Test]
    public async Task AddCommentAsync_MentioningSomeoneWhoIsNotAMember_KeepsTheTextButNotTheMention()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var outsider = scenario.Person();
        var item = await scenario.CreateTaskAsync("WEB");
        var body = $"請 {EventFeed.Mention(outsider)} 看";

        // Act
        var comment = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That(comment.Value.Comment, Is.EqualTo(body));
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.Empty);
    }

    [Test]
    public async Task AddCommentAsync_MentioningAMemberOfAnotherProject_IsNotListed()
    {
        // Arrange：他在 OPS 是成員，在 WEB 看不到。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var otherProjectMember = await scenario.MemberAsync("OPS", "editor");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, EventFeed.Mention(otherProjectMember), null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.Empty);
    }

    [Test]
    public async Task AddCommentAsync_MentioningYourself_IsNotListed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(editor, item.Key, $"{EventFeed.Mention(editor)} 我自己", null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.Empty);
    }

    [Test]
    public async Task AddCommentAsync_MentioningTheSamePersonTwice_ListsThemOnce()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act：同一個人用不同的顯示名稱提及兩次，編號大小寫也不同。
        var upper = $"@[小明](mention:{editor.PersonId.ToString("D").ToUpperInvariant()})";
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, $"{EventFeed.Mention(editor, "明")} {upper}", null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.EqualTo(new[] { editor.PersonId }));
    }

    [Test]
    public async Task AddCommentAsync_MoreThanTheLimit_ListsTheFirstOnesAndStoresAllTheText()
    {
        // Arrange：25 位成員，全都提及。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var people = new List<Actor>();
        for (var i = 0; i < WorkEventRules.MaxMentionsPerChange + 5; i++)
        {
            people.Add(await scenario.MemberAsync("WEB", "viewer"));
        }

        var item = await scenario.CreateTaskAsync("WEB");
        var body = string.Join(' ', people.Select(p => EventFeed.Mention(p)));

        // Act
        var comment = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That(comment.Value.Comment, Is.EqualTo(body));
        Assert.That(
            (await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds,
            Is.EqualTo(people.Take(WorkEventRules.MaxMentionsPerChange).Select(p => p.PersonId)));
    }

    [Test]
    public async Task AddCommentAsync_UnqualifiedMentionsBeforeTheLimit_DoNotCrowdOutTheQualifiedOnes()
    {
        // Arrange：前面 25 個不是成員，最後一個才是。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "viewer");
        var item = await scenario.CreateTaskAsync("WEB");
        var strangers = Enumerable.Range(0, 25).Select(_ => EventFeed.Mention(scenario.Person()));

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, string.Join(' ', strangers) + " " + EventFeed.Mention(member), null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.EqualTo(new[] { member.PersonId }));
    }

    [Test]
    public async Task AddCommentAsync_MalformedMentions_AreIgnored()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "@[沒有編號](mention:) @[壞編號](mention:not-a-guid) [不是提及](https://example.com)", null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_DescriptionMentioningAMemberAndAStranger_ListsOnlyTheMember()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");

        // Act
        await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Description = $"{EventFeed.Mention(scenario.Person())} {EventFeed.Mention(member)} {EventFeed.Mention(scenario.Admin)}",
        });

        // Assert：建立者自己也不算。
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.EqualTo(new[] { member.PersonId }));
    }

    [Test]
    public async Task UpdateAsync_RemovingAndReAddingAMention_CountsItAsNewTheSecondTime()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = EventFeed.Mention(member) });
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "拿掉了" }, CancellationToken.None);

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = $"又提及 {EventFeed.Mention(member)}" }, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.EqualTo(new[] { member.PersonId }));
    }
}
