using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>留言的編輯、刪除、復原、版本與「只看自己的項目」「歷史匯入」「權限變動」的交會。</summary>
[TestFixture]
public sealed class CommentEditAccessTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CommentActions_OnAnItemAnOwnOnlyMemberCannotSee_AreAllItemNotFound()
    {
        // Arrange
        var world = await OwnItems.OwnItemsWorld.CreateAsync(_host);
        var comment = (await _host.Activity.AddCommentAsync(world.Editor, world.Others.Key, "別人的項目", null, CancellationToken.None)).Value;
        var deleted = (await _host.Activity.AddCommentAsync(world.Editor, world.Others.Key, "已刪除的", null, CancellationToken.None)).Value;
        await _host.Activity.DeleteCommentAsync(world.Editor, world.Others.Key, deleted.Id, CancellationToken.None);
        var key = world.Others.Key;

        // Act
        var results = new[]
        {
            (await _host.Activity.EditCommentAsync(world.Contributor, key, comment.Id, "改", CancellationToken.None)).Failure,
            (await _host.Activity.DeleteCommentAsync(world.Contributor, key, comment.Id, CancellationToken.None)).Failure,
            (await _host.Activity.RestoreCommentAsync(world.Contributor, key, deleted.Id, CancellationToken.None)).Failure,
            (await _host.Activity.ListCommentRevisionsAsync(world.Contributor, key, comment.Id, CancellationToken.None)).Failure,
        };

        // Assert
        Assert.That(results.Select(f => (f?.Kind, f?.Code)), Has.All.EqualTo((FailureKind.NotFound, WorkErrorCodes.ItemNotFound)));
    }

    [Test]
    public async Task EditCommentAsync_OwnOnlyMemberOnAnItemTheyCanSee_CanEditTheirOwnComment()
    {
        // Arrange
        var world = await OwnItems.OwnItemsWorld.CreateAsync(_host);
        var comment = (await _host.Activity.AddCommentAsync(world.Contributor, world.Created.Key, "我的留言", null, CancellationToken.None)).Value;

        // Act
        var result = await _host.Activity.EditCommentAsync(world.Contributor, world.Created.Key, comment.Id, "改過", CancellationToken.None);
        var revisions = await _host.Activity.ListCommentRevisionsAsync(world.Contributor, world.Created.Key, comment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(result.Value.Comment, Is.EqualTo("改過"));
            Assert.That(revisions.Value.Select(r => r.Body), Is.EqualTo(new[] { "改過", "我的留言" }));
        }
    }

    [Test]
    public async Task RestoreCommentAsync_AuthorWhoDeletedItThenLostCommentCreate_IsMissingThePermissionNotNotAuthor()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var author = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");
        var comment = (await _host.Activity.AddCommentAsync(author, item.Key, "留言", null, CancellationToken.None)).Value;
        await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", author.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.Activity.RestoreCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
    }
}
