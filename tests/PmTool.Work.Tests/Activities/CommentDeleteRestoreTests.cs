using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.Activities;

/// <summary>刪除與復原留言：留在原位、內容藏起來、誰能做、重送不會失敗、復原之前不能回覆。</summary>
[TestFixture]
public sealed class CommentDeleteRestoreTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteCommentAsync_ByTheAuthor_HidesTheBodyButKeepsThePlace()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        _host.Clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        var result = await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var listed = (await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).Single(a => a.Id == comment.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(
                (result.Value.Comment, result.Value.DeletedAt, result.Value.DeletedBy, result.Value.At),
                Is.EqualTo(((string?)null, (DateTimeOffset?)_host.Clock.GetUtcNow(), (Guid?)author.PersonId, comment.At)));
            Assert.That((listed.Comment, listed.DeletedAt, listed.DeletedBy), Is.EqualTo((result.Value.Comment, result.Value.DeletedAt, result.Value.DeletedBy)));
        }
    }

    [Test]
    public async Task DeleteCommentAsync_ByAProjectManager_DeletesSomeoneElsesComment()
    {
        // Arrange
        var (scenario, item, _, comment) = await SetUpAsync();
        var manager = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Activity.DeleteCommentAsync(manager, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Value.DeletedBy, result.Value.Comment), Is.EqualTo(((Guid?)manager.PersonId, (string?)null)));
    }

    [Test]
    public async Task DeleteCommentAsync_ByAnotherEditor_IsNotAuthor()
    {
        // Arrange
        var (scenario, item, _, comment) = await SetUpAsync();
        var other = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Activity.DeleteCommentAsync(other, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
    }

    [Test]
    public async Task DeleteCommentAsync_AuthorWhoCanNoLongerComment_IsForbidden()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", author.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
    }

    [Test]
    public async Task DeleteCommentAsync_InAnArchivedProject_IsProjectArchivedEvenForAManager()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var byManager = await _host.Activity.DeleteCommentAsync(scenario.Admin, item.Key, comment.Id, CancellationToken.None);
        var byAuthor = await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(
            new[] { byManager, byAuthor }.Select(r => (r.Failure?.Kind, r.Failure?.Code)),
            Is.All.EqualTo((FailureKind.Conflict, WorkErrorCodes.ProjectArchived)));
    }

    [Test]
    public async Task DeleteCommentAsync_Twice_IsANoOpThatKeepsTheFirstDeletion()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var first = await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var events = (await EventFeed.OfAsync(_host, scenario)).Count;
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await _host.Activity.DeleteCommentAsync(scenario.Admin, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((second.Value.DeletedAt, second.Value.DeletedBy), Is.EqualTo((first.Value.DeletedAt, first.Value.DeletedBy)));
            Assert.That((await EventFeed.OfAsync(_host, scenario)).Count, Is.EqualTo(events));
        }
    }

    [Test]
    public async Task DeleteCommentAsync_WritesADeletedEventWithoutExcerptOrMentions()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();

        // Act
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.ActorPersonId, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemCommentDeleted, author.PersonId, (Guid?)comment.Id)));
            Assert.That(written.Item.CommentExcerpt, Is.Null);
            Assert.That(written.Item.MentionedPersonIds, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteCommentAsync_UnknownComment_IsCommentNotFound()
    {
        // Arrange
        var (_, item, author, _) = await SetUpAsync();

        // Act
        var result = await _host.Activity.DeleteCommentAsync(author, item.Key, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.NotFound, WorkErrorCodes.CommentNotFound)));
    }

    [Test]
    public async Task RestoreCommentAsync_ByTheAuthorWhoDeletedIt_BringsBackBodyEditedAndVersions()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "改過", CancellationToken.None);
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Act
        var result = await _host.Activity.RestoreCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var revisions = await _host.Activity.ListCommentRevisionsAsync(scenario.Admin, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.Comment, result.Value.DeletedAt, result.Value.DeletedBy, result.Value.At), Is.EqualTo(("改過", (DateTimeOffset?)null, (Guid?)null, comment.At)));
            Assert.That(result.Value.EditedAt, Is.Not.Null);
            Assert.That(revisions.Value.Select(r => r.Body), Is.EqualTo(new[] { "改過", "原本的留言" }));
        }
    }

    [Test]
    public async Task RestoreCommentAsync_AuthorsOwnDeletion_AProjectManagerCanRestoreIt()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var manager = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Activity.RestoreCommentAsync(manager, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Comment, Is.EqualTo("原本的留言"));
    }

    [Test]
    public async Task RestoreCommentAsync_DeletedByAManager_OnlyAManagerCanRestoreIt()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var manager = await scenario.MemberAsync("WEB", "admin");
        await _host.Activity.DeleteCommentAsync(manager, item.Key, comment.Id, CancellationToken.None);
        var other = await scenario.MemberAsync("WEB", "editor");

        // Act
        var byAuthor = await _host.Activity.RestoreCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var byOther = await _host.Activity.RestoreCommentAsync(other, item.Key, comment.Id, CancellationToken.None);
        var byManager = await _host.Activity.RestoreCommentAsync(manager, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((byAuthor.Failure?.Kind, byAuthor.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
            Assert.That(byAuthor.Failure!.Message, Does.Contain("管理專案的人刪除"));
            Assert.That((byOther.Failure?.Kind, byOther.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
            Assert.That(byManager.Value.DeletedAt, Is.Null);
        }
    }

    [Test]
    public async Task RestoreCommentAsync_ByAnotherEditorOfTheAuthorsOwnDeletion_IsNotAuthor()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var other = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Activity.RestoreCommentAsync(other, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
    }

    [Test]
    public async Task RestoreCommentAsync_InAnArchivedProject_IsProjectArchived()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Activity.RestoreCommentAsync(scenario.Admin, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.ProjectArchived)));
    }

    [Test]
    public async Task RestoreCommentAsync_ACommentThatIsNotDeleted_IsANoOpWithoutAnEvent()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var events = (await EventFeed.OfAsync(_host, scenario)).Count;

        // Act
        var result = await _host.Activity.RestoreCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.Comment, result.Value.DeletedAt), Is.EqualTo(("原本的留言", (DateTimeOffset?)null)));
            Assert.That((await EventFeed.OfAsync(_host, scenario)).Count, Is.EqualTo(events));
        }
    }

    [Test]
    public async Task RestoreCommentAsync_WritesARestoredEvent()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Act
        await _host.Activity.RestoreCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That((written.Kind, written.Item.ActivityId, written.Item.CommentExcerpt), Is.EqualTo((WorkEventKind.ItemCommentRestored, (Guid?)comment.Id, (string?)null)));
    }

    [Test]
    public async Task DeletedComment_CannotBeEditedOrHaveItsRevisionsReadUntilRestored()
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Act
        var edit = await _host.Activity.EditCommentAsync(author, item.Key, comment.Id, "改", CancellationToken.None);
        var revisions = await _host.Activity.ListCommentRevisionsAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(
            new[] { edit.Failure, revisions.Failure }.Select(f => (f?.Kind, f?.Code)),
            Is.All.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentDeleted)));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToADeletedCommentOrToAReplyUnderIt_IsCommentDeleted()
    {
        // Arrange
        var (_, item, author, top) = await SetUpAsync();
        var reply = (await _host.Activity.AddCommentAsync(author, item.Key, "回覆", top.Id, CancellationToken.None)).Value;
        await _host.Activity.DeleteCommentAsync(author, item.Key, top.Id, CancellationToken.None);

        // Act
        var toTop = await _host.Activity.AddCommentAsync(author, item.Key, "再回", top.Id, CancellationToken.None);
        var toReply = await _host.Activity.AddCommentAsync(author, item.Key, "再回", reply.Id, CancellationToken.None);

        // Assert
        Assert.That(
            new[] { toTop.Failure, toReply.Failure }.Select(f => (f?.Kind, f?.Code)),
            Is.All.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentDeleted)));
    }

    [Test]
    public async Task AddCommentAsync_ReplyToADeletedReply_IsCommentDeleted()
    {
        // Arrange
        var (_, item, author, top) = await SetUpAsync();
        var reply = (await _host.Activity.AddCommentAsync(author, item.Key, "回覆", top.Id, CancellationToken.None)).Value;
        await _host.Activity.DeleteCommentAsync(author, item.Key, reply.Id, CancellationToken.None);

        // Act
        var result = await _host.Activity.AddCommentAsync(author, item.Key, "再回", reply.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentDeleted)));
    }

    [Test]
    public async Task DeleteCommentAsync_ATopLevelComment_LeavesItsRepliesVisible()
    {
        // Arrange
        var (scenario, item, author, top) = await SetUpAsync();
        var reply = (await _host.Activity.AddCommentAsync(author, item.Key, "回覆", top.Id, CancellationToken.None)).Value;

        // Act
        await _host.Activity.DeleteCommentAsync(author, item.Key, top.Id, CancellationToken.None);
        var listed = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(listed.Single(a => a.Id == reply.Id).Comment, Is.EqualTo("回覆"));
    }

    [Test]
    public async Task ImportCommentsAsync_RerunAfterTheImportedCommentWasDeleted_DoesNotImportItAgain()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var batch = Comments(Comment("u-1", "m-1", member.PersonId));
        var first = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
        await _host.Activity.DeleteCommentAsync(manager, "WEB-1", first.Value.Created[0].ActivityId, CancellationToken.None);

        // Act
        var rerun = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rerun.Value.Created, Is.Empty);
            Assert.That(comments, Has.Length.EqualTo(1));
            Assert.That(comments[0].DeletedAt, Is.Not.Null);
        }
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
