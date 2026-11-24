using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>編輯留言：只有作者、留舊版本、不動時間軸與項目、只通知新加的提及。</summary>
[TestFixture]
public sealed class CommentEditTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EditCommentAsync_ByTheAuthor_ReturnsTheNewBodyAndKeepsPositionAndItem()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var before = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;
        _host.Clock.Advance(TimeSpan.FromMinutes(7));

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "  改過的內容  ", CancellationToken.None);
        var after = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;
        var all = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That((result.Value.Comment, result.Value.At, result.Value.EditedAt), Is.EqualTo(("改過的內容", comment.At, (DateTimeOffset?)_host.Clock.GetUtcNow())));
            Assert.That((result.Value.DeletedAt, result.Value.DeletedBy), Is.EqualTo(((DateTimeOffset?)null, (Guid?)null)));
            Assert.That(after.UpdatedAt, Is.EqualTo(before.UpdatedAt));
            Assert.That(all.Count(a => a.Kind == ActivityKind.Commented), Is.EqualTo(1));
            Assert.That(all.Single(a => a.Id == comment.Id).EditedAt, Is.EqualTo(result.Value.EditedAt));
        }
    }

    [Test]
    public async Task EditCommentAsync_AReply_KeepsItsPlaceUnderTheTopLevelComment()
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();
        var reply = (await _host.Activity.AddCommentAsync(author, item.Key, "回覆", comment.Id, CancellationToken.None)).Value;

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, reply.Id, "改過的回覆", CancellationToken.None);

        // Assert
        Assert.That((result.Value.ReplyToId, result.Value.Comment), Is.EqualTo(((Guid?)comment.Id, "改過的回覆")));
    }

    [Test]
    public async Task EditCommentAsync_ByAnotherPerson_IsNotAuthorEvenForAnAdministrator()
    {
        // Arrange
        var (scenario, item, _, comment) = await SetUpAsync();

        // Act
        var result = await _host.Activity.EditCommentAsync(scenario.Admin, item.Key, comment.Id, "別人改的", CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
    }

    [Test]
    public async Task EditCommentAsync_AuthorWhoCanNoLongerComment_IsForbidden()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", author.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "降權之後", CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
    }

    [Test]
    public async Task EditCommentAsync_InAnArchivedProject_IsProjectArchived()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "封存之後", CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ProjectArchived)));
    }

    [TestCase("", WorkErrorCodes.CommentBodyRequired)]
    [TestCase("   \n ", WorkErrorCodes.CommentBodyRequired)]
    [TestCase("有\0字元", WorkErrorCodes.CommentBodyInvalid)]
    public async Task EditCommentAsync_InvalidBody_IsRejectedWithTheCommentCode(string body, string code)
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, body, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, code)));
    }

    [Test]
    public async Task EditCommentAsync_SameBodyAfterNormalizing_ChangesNothingAndWritesNoEvent()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var eventsBefore = (await EventFeed.OfAsync(_host, scenario)).Count;
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "\n  原本的留言 \r\n", CancellationToken.None);
        var revisions = await _host.Activity.ListCommentRevisionsAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.Comment, result.Value.EditedAt), Is.EqualTo(("原本的留言", (DateTimeOffset?)null)));
            Assert.That(revisions.Value, Has.Count.EqualTo(1));
            Assert.That((await EventFeed.OfAsync(_host, scenario)).Count, Is.EqualTo(eventsBefore));
        }
    }

    [Test]
    public async Task EditCommentAsync_UnknownNotACommentOrOfAnotherItem_IsCommentNotFound()
    {
        // Arrange
        var (scenario, item, author, _) = await SetUpAsync();
        var other = await scenario.CreateTaskAsync("WEB", "另一個");
        var elsewhere = (await _host.Activity.AddCommentAsync(author, other.Key, "別處", null, CancellationToken.None)).Value;
        var created = (await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).First(a => a.Kind == ActivityKind.Created);

        // Act
        var results = new[]
        {
            await _host.Activity.EditCommentAsync(author, item.Key, Guid.NewGuid(), "x", CancellationToken.None),
            await _host.Activity.EditCommentAsync(author, item.Key, created.Id, "x", CancellationToken.None),
            await _host.Activity.EditCommentAsync(author, item.Key, elsewhere.Id, "x", CancellationToken.None),
        };

        // Assert
        Assert.That(results.Select(r => (r.Failure?.Kind, r.Failure?.Code)), Is.All.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task EditCommentAsync_ItemTheCallerCannotSee_IsItemNotFound()
    {
        // Arrange
        var (scenario, item, _, comment) = await SetUpAsync();
        var stranger = scenario.Person();

        // Act
        var result = await _host.Activity.EditCommentAsync(stranger, item.Key, comment.Id, "x", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
    }

    [Test]
    public async Task EditCommentAsync_WritesAnEventWithOnlyTheNewlyAddedMentions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var author = await scenario.MemberAsync("WEB", "editor");
        var old = await scenario.MemberAsync("WEB", "editor");
        var added = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");
        var comment = (await _host.Activity.AddCommentAsync(author, item.Key, $"請看 {EventFeed.Mention(old, "舊人")}", null, CancellationToken.None)).Value;

        // Act
        await _host.Activity.EditCommentAsync(
            author, item.Key, comment.Id, $"請看 {EventFeed.Mention(old, "舊人")} 與 {EventFeed.Mention(added, "新人")}", CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.ActorPersonId, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemCommentEdited, author.PersonId, (Guid?)comment.Id)));
            Assert.That(written.Item.CommentExcerpt, Is.EqualTo("請看 @舊人 與 @新人"));
            Assert.That(written.Item.MentionedPersonIds, Is.EqualTo(new[] { added.PersonId }));
            Assert.That((written.Item.ReplyToCommentId, written.Item.ReplyToAuthorId), Is.EqualTo(((Guid?)null, (Guid?)null)));
        }
    }

    [Test]
    public async Task EditCommentAsync_NinetyNineEdits_AreAllowedAndTheHundredthIsTooManyEdits()
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();
        for (var i = 1; i < WorkRules.MaxCommentRevisions; i++)
        {
            var step = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, $"第 {i + 1} 版", CancellationToken.None);
            Assert.That(step.Succeeded, Is.True, () => $"{i}: {step.Failure!.Message}");
        }

        // Act
        var result = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "太多了", CancellationToken.None);
        var revisions = await _host.Activity.ListCommentRevisionsAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentTooManyEdits)));
            Assert.That(revisions.Value, Has.Count.EqualTo(WorkRules.MaxCommentRevisions));
            Assert.That(revisions.Value[0].Body, Is.EqualTo($"第 {WorkRules.MaxCommentRevisions} 版"));
        }
    }

    [Test]
    public async Task ListCommentRevisionsAsync_AfterTwoEdits_ListsThreeVersionsNewestFirst()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var firstEditAt = _host.Clock.GetUtcNow();
        await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "第二版", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var secondEditAt = _host.Clock.GetUtcNow();
        await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "第三版", CancellationToken.None);

        // Act
        var byAdmin = await _host.Activity.ListCommentRevisionsAsync(scenario.Admin, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(
            byAdmin.Value.Select(r => (r.Number, r.Body, r.At, r.IsCurrent)),
            Is.EqualTo(new[]
            {
                (3, "第三版", secondEditAt, true),
                (2, "第二版", firstEditAt, false),
                (1, "原本的留言", comment.At, false),
            }));
    }

    [Test]
    public async Task ListCommentRevisionsAsync_NeverEdited_IsTheCurrentVersionOnly()
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();

        // Act
        var result = await _host.Activity.ListCommentRevisionsAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(new[] { new CommentRevision(1, "原本的留言", comment.At, true) }));
    }

    [Test]
    public async Task ListCommentRevisionsAsync_UnknownComment_IsCommentNotFound()
    {
        // Arrange
        var (_, item, author, _) = await SetUpAsync();

        // Act
        var result = await _host.Activity.ListCommentRevisionsAsync(author, item.Key, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task ListCommentRevisionsAsync_ByAViewer_IsAllowedAndByAStrangerIsItemNotFound()
    {
        // Arrange
        var (scenario, item, _, comment) = await SetUpAsync();
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var seen = await _host.Activity.ListCommentRevisionsAsync(viewer, item.Key, comment.Id, CancellationToken.None);
        var hidden = await _host.Activity.ListCommentRevisionsAsync(scenario.Person(), item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((seen.Succeeded, hidden.Failure?.Code), Is.EqualTo((true, WorkErrorCodes.ItemNotFound)));
    }

    private async Task<(Scenario Scenario, WorkItem Item, Actor Author, ItemActivity Comment)> SetUpAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var author = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");
        var comment = (await _host.Activity.AddCommentAsync(author, item.Key, "原本的留言", null, CancellationToken.None)).Value;
        return (scenario, item, author, comment);
    }
}
