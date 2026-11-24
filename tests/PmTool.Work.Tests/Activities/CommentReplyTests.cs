using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>留言的回覆：只有一層、只能回同一個項目的留言，事件帶著回覆的對象。</summary>
[TestFixture]
public sealed class CommentReplyTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AddCommentAsync_ReplyToATopLevelComment_StoresItAndListsItWithReplyToId()
    {
        // Arrange
        var (scenario, item, top) = await ItemWithCommentAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var reply = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", top.Id, CancellationToken.None);
        var listed = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Value.ReplyToId, Is.EqualTo(top.Id));
            Assert.That((listed.Id, listed.ReplyToId), Is.EqualTo((reply.Value.Id, (Guid?)top.Id)));
            Assert.That(top.ReplyToId, Is.Null);
        }
    }

    [Test]
    public async Task AddCommentAsync_ReplyToAReply_IsStoredUnderTheTopLevelComment()
    {
        // Arrange
        var (scenario, item, top) = await ItemWithCommentAsync();
        var first = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆一", top.Id, CancellationToken.None);

        // Act
        var second = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆二", first.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(second.Value.ReplyToId, Is.EqualTo(top.Id));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToACommentOfAnotherItem_IsCommentNotFound()
    {
        // Arrange
        var (scenario, item, _) = await ItemWithCommentAsync();
        var other = await scenario.CreateTaskAsync("WEB", "另一個");
        var elsewhere = (await _host.Activity.AddCommentAsync(scenario.Admin, other.Key, "別處的留言", null, CancellationToken.None)).Value;

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", elsewhere.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToAnUnknownId_IsCommentNotFound()
    {
        // Arrange
        var (scenario, item, _) = await ItemWithCommentAsync();

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToAnActivityThatIsNotAComment_IsCommentNotFound()
    {
        // Arrange
        var (scenario, item, _) = await ItemWithCommentAsync();
        var created = (await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).First(a => a.Kind == ActivityKind.Created);

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", created.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToACommentOfAnotherOrganization_IsCommentNotFound()
    {
        // Arrange
        var (scenario, item, _) = await ItemWithCommentAsync();
        var elsewhere = new Scenario(_host);
        await elsewhere.CreateProjectAsync("WEB");
        var foreignItem = await elsewhere.CreateTaskAsync("WEB");
        var foreign = (await _host.Activity.AddCommentAsync(elsewhere.Admin, foreignItem.Key, "別的組織", null, CancellationToken.None)).Value;

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", foreign.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.CommentNotFound));
    }

    [Test]
    public async Task AddCommentAsync_ReplyByAViewer_IsForbiddenLikeAnyComment()
    {
        // Arrange
        var (scenario, item, top) = await ItemWithCommentAsync();
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Activity.AddCommentAsync(viewer, item.Key, "回覆", top.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task AddCommentAsync_ReplyInAnArchivedProject_FailsWithProjectArchived()
    {
        // Arrange
        var (scenario, item, top) = await ItemWithCommentAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆", top.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
    }

    [Test]
    public async Task AddCommentAsync_Reply_EventCarriesTheTopLevelCommentAndItsAuthor()
    {
        // Arrange
        var (scenario, item, top) = await ItemWithCommentAsync();
        var replier = await scenario.MemberAsync("WEB", "commenter");
        var first = await _host.Activity.AddCommentAsync(replier, item.Key, "回覆一", top.Id, CancellationToken.None);

        // Act：回覆一則回覆，事件指向最上層那一則與它的作者（不是回覆的作者）。
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "回覆二", first.Value.Id, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That(
            (written.Kind, written.Item.ReplyToCommentId, written.Item.ReplyToAuthorId),
            Is.EqualTo((WorkEventKind.ItemCommented, (Guid?)top.Id, (Guid?)scenario.Admin.PersonId)));
    }

    [Test]
    public async Task AddCommentAsync_TopLevelComment_EventHasNoReplyInformation()
    {
        // Arrange / Act
        var (scenario, _, _) = await ItemWithCommentAsync();
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That((written.Item.ReplyToCommentId, written.Item.ReplyToAuthorId), Is.EqualTo(((Guid?)null, (Guid?)null)));
    }

    [Test]
    public async Task AddCommentAsync_CommentWithAttachmentReferences_ExcerptShowsPlaceholderAndFileName()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var person = await scenario.MemberAsync("WEB", "editor");
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var body = $"看圖 ![截圖.png](attachment:{id1}) 與 [規格 \\[v2\\].pdf](attachment:{id2}) 請 {EventFeed.Mention(person, "小明")}";

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.CommentExcerpt, Is.EqualTo("看圖 ［圖片］ 與 規格 [v2].pdf 請 @小明"));
    }

    private async Task<(Scenario Scenario, WorkItem Item, ItemActivity Top)> ItemWithCommentAsync()
    {
        var scenario = new Scenario(_host);
        var (item, top) = await SetUpAsync(scenario);
        return (scenario, item, top);
    }

    private async Task<(WorkItem Item, ItemActivity Top)> SetUpAsync(Scenario scenario)
    {
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var top = (await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "最上層", null, CancellationToken.None)).Value;
        return (item, top);
    }
}
