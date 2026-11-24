using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>歷史留言：保留作者與時間、回覆只有一層、重跑略過、不改項目的 updatedAt、事件標歷史。</summary>
[TestFixture]
public sealed class HistoryCommentImportTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportCommentsAsync_Comment_KeepsTheAuthorAndTimeAndDoesNotTouchTheItem()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var before = (await _host.Items.GetAsync(manager, "WEB-1", CancellationToken.None)).Value;
        var at = Past(5).AddMilliseconds(123);

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId, "第一則", at)), CancellationToken.None);
        var after = (await _host.Items.GetAsync(manager, "WEB-1", CancellationToken.None)).Value;
        var comment = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Single(a => a.Kind == ActivityKind.Commented);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Created.Select(c => c.ExternalId), Is.EqualTo(new[] { "u-1" }));
            Assert.That(comment.Id, Is.EqualTo(result.Value.Created[0].ActivityId));
            Assert.That((comment.ActorId, comment.At, comment.Comment, comment.ImportedFrom), Is.EqualTo((member.PersonId, at, "第一則", "monday")));
            Assert.That(after.UpdatedAt, Is.EqualTo(before.UpdatedAt));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ReplyToATopComment_IsStoredUnderIt()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();

        // Act
        var result = await _host.History.ImportCommentsAsync(
            manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId), Comment("r-1", "m-1", member.PersonId, "回覆", Past(19), "u-1")), CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(comments[1].ReplyToId, Is.EqualTo(comments[0].Id));
            Assert.That(comments[0].ReplyToId, Is.Null);
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ReplyToAReply_IsStoredUnderTheTopLevelComment()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var batch = Comments(
            Comment("u-1", "m-1", member.PersonId, at: Past(9)),
            Comment("r-1", "m-1", member.PersonId, "回覆一", Past(8), replyTo: "u-1"),
            Comment("r-2", "m-1", member.PersonId, "回覆二", Past(7), replyTo: "r-1"));

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(comments.Select(c => c.ReplyToId), Is.EqualTo(new Guid?[] { null, comments[0].Id, comments[0].Id }));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ReplyToAReplyImportedEarlier_IsStoredUnderTheTopLevelComment()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        await _host.History.ImportCommentsAsync(
            manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId, at: Past(9)), Comment("r-1", "m-1", member.PersonId, "回覆", Past(8), "u-1")), CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(
            manager, "WEB", Comments(Comment("r-2", "m-1", member.PersonId, "再回覆", Past(7), replyTo: "r-1")), CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(comments[2].ReplyToId, Is.EqualTo(comments[0].Id));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_RunAgain_SkipsEverythingAndReturnsTheSameActivityIds()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var batch = Comments(Comment("u-1", "m-1", member.PersonId), Comment("r-1", "m-1", member.PersonId, replyTo: "u-1"));
        var first = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);

        // Act
        var again = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Value.Created, Is.Empty);
            Assert.That(again.Value.Skipped, Is.EqualTo(first.Value.Created));
            Assert.That(comments, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_ReplyBeforeItsTarget_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var batch = Comments(Comment("r-1", "m-1", member.PersonId, replyTo: "u-1"), Comment("u-1", "m-1", member.PersonId));

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/reply_to_external_id" }));
    }

    [Test]
    public async Task ImportCommentsAsync_ReplyToACommentOfAnotherItem_IsAProblem()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-2", member.PersonId)), CancellationToken.None);
        await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-2", member.PersonId)), CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("r-1", "m-1", member.PersonId, replyTo: "u-1")), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/reply_to_external_id" }));
    }

    [Test]
    public async Task ImportCommentsAsync_ItemNotImportedOrInAnotherProject_IsAProblem()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        await scenario.CreateProjectAsync("OPS");
        await scenario.MemberAsync("OPS", "editor");
        await _host.History.ImportItemsAsync(scenario.Admin, "OPS", Batch(Row("o-1", member.PersonId)), CancellationToken.None);
        var batch = Comments(
            Comment("u-1", "nope", member.PersonId),
            Comment("u-2", "o-1", member.PersonId),
            Comment("u-3", "m-1", member.PersonId));

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/item_external_id", "comments/1/item_external_id" }));
    }

    [Test]
    public async Task ImportCommentsAsync_AuthorWhoIsNotAMember_IsAProblemAndNothingIsImported()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        var batch = Comments(Comment("u-1", "m-1", member.PersonId), Comment("u-2", "m-1", scenario.Person().PersonId));

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/1/author_id" }));
            Assert.That(comments, Is.Empty);
        }
    }

    [Test]
    public async Task ImportCommentsAsync_BodyAndTimeRules_AreTheOnesOfOrdinaryComments()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var batch = Comments(
            Comment("u-1", "m-1", member.PersonId, "   "),
            Comment("u-2", "m-1", member.PersonId, new string('字', 10_001)),
            Comment("u-3", "m-1", member.PersonId, "壞\0字元"),
            Comment("u-4", "m-1", member.PersonId, at: WorkTestHost.StartTime.AddHours(1)));

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "comments/0/body", "comments/1/body", "comments/2/body", "comments/3/at" }));
    }

    [Test]
    public async Task ImportCommentsAsync_WritesAHistoryEventWithoutMentionsAndTheOriginalAuthor()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        var body = $"請看 {EventFeed.Mention(manager)}";

        // Act
        await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId, body)), CancellationToken.None);
        var commented = (await EventFeed.OfAsync(_host, scenario)).Single(e => e.Kind == WorkEventKind.ItemCommented);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((commented.Item.History, commented.Item.Imported), Is.EqualTo((true, true)));
            Assert.That(commented.ActorPersonId, Is.EqualTo(member.PersonId));
            Assert.That(commented.Item.MentionedPersonIds, Is.Empty);
            Assert.That(commented.OccurredAt, Is.EqualTo(Past(20)));
        }
    }

    [Test]
    public async Task ImportCommentsAsync_WithoutProjectManage_IsForbidden()
    {
        // Arrange
        var (_, _, member) = await ImportedItemAsync();

        // Act
        var result = await _host.History.ImportCommentsAsync(member, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task ImportCommentsAsync_ArchivedProject_IsRefused()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
    }

    [Test]
    public async Task ImportCommentsAsync_NoCommentsOrBadSource_IsHistoryInvalid()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();

        // Act
        var empty = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(), CancellationToken.None);
        var badSource = await _host.History.ImportCommentsAsync(
            manager, "WEB", new HistoryComments { Source = "X", Comments = [Comment("u-1", "m-1", member.PersonId)] }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((empty.Failure?.Code, Paths(empty.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "comments" })));
            Assert.That((badSource.Failure?.Code, Paths(badSource.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "source" })));
        }
    }

    private async Task<(Scenario Scenario, Actor Manager, Actor Member)> ImportedItemAsync()
    {
        var setup = await ProjectWithMembersAsync(_host);
        var result = await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("m-1", setup.Member.PersonId)), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return setup;
    }
}
