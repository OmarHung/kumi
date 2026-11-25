using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.Activities;

/// <summary>
/// 匯入的留言署的是被歸屬的人，但不是他自己寫的：誰都不能編輯；刪除與復原只有管理專案的人。
/// 編輯的檢查順序是：已刪除、comment.create、匯入的、不是作者。
/// </summary>
[TestFixture]
public sealed class ImportedCommentTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EditCommentAsync_ImportedComment_IsConflictEvenForTheAttributedAuthorAndTheManager()
    {
        // Arrange
        var (scenario, manager, member, comment) = await ImportedAsync();
        var other = await scenario.MemberAsync("WEB", "editor");

        // Act
        var results = new[]
        {
            await _host.Activity.EditCommentAsync(member, "WEB-1", comment.ActivityId, "原作者改", CancellationToken.None),
            await _host.Activity.EditCommentAsync(manager, "WEB-1", comment.ActivityId, "管理者改", CancellationToken.None),
            await _host.Activity.EditCommentAsync(other, "WEB-1", comment.ActivityId, "別人改", CancellationToken.None),
        };

        // Assert
        Assert.That(results.Select(r => (r.Failure?.Kind, r.Failure?.Code)), Has.All.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentImported)));
        Assert.That(results[0].Failure!.Message, Is.EqualTo("從其他系統匯入的留言不能編輯。"));
    }

    [Test]
    public async Task EditCommentAsync_ImportedCommentByAViewerWithoutCommentCreate_IsMissingThePermissionFirst()
    {
        // Arrange
        var (scenario, _, member, comment) = await ImportedAsync();
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", member.PersonId, "viewer", CancellationToken.None);

        // Act
        var result = await _host.Activity.EditCommentAsync(member, "WEB-1", comment.ActivityId, "改", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task DeleteCommentAsync_ImportedComment_OnlyTheManagerCanAndTheAuthorIsForbidden()
    {
        // Arrange
        var (_, manager, member, comment) = await ImportedAsync();

        // Act
        var byAuthor = await _host.Activity.DeleteCommentAsync(member, "WEB-1", comment.ActivityId, CancellationToken.None);
        var byManager = await _host.Activity.DeleteCommentAsync(manager, "WEB-1", comment.ActivityId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((byAuthor.Failure?.Kind, byAuthor.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
            Assert.That(byAuthor.Failure!.Message, Does.Contain("匯入的留言只有管理專案的人能刪除"));
            Assert.That(byManager.Succeeded, Is.True, () => byManager.Failure!.Message);
            Assert.That(byManager.Value.DeletedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task RestoreCommentAsync_ImportedCommentDeletedByTheManager_OnlyTheManagerCanRestoreIt()
    {
        // Arrange
        var (_, manager, member, comment) = await ImportedAsync();
        await _host.Activity.DeleteCommentAsync(manager, "WEB-1", comment.ActivityId, CancellationToken.None);

        // Act
        var byAuthor = await _host.Activity.RestoreCommentAsync(member, "WEB-1", comment.ActivityId, CancellationToken.None);
        var byManager = await _host.Activity.RestoreCommentAsync(manager, "WEB-1", comment.ActivityId, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((byAuthor.Failure?.Kind, byAuthor.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor)));
            Assert.That(byAuthor.Failure!.Message, Does.Contain("匯入的留言只有管理專案的人能復原"));
            Assert.That(byManager.Succeeded, Is.True, () => byManager.Failure!.Message);
        }
    }

    [Test]
    public async Task DeleteCommentAsync_OrdinaryCommentByItsAuthor_StillWorks()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var author = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");
        var comment = (await _host.Activity.AddCommentAsync(author, item.Key, "我的留言", null, CancellationToken.None)).Value;

        // Act
        var result = await _host.Activity.DeleteCommentAsync(author, item.Key, comment.Id, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private async Task<(Scenario Scenario, Actor Manager, Actor Member, HistoryCommentRef Comment)> ImportedAsync()
    {
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var imported = (await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None)).Value.Created[0];
        return (scenario, manager, member, imported);
    }
}
