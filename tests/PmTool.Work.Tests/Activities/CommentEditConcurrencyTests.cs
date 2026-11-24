using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>同一則留言同時被改、被刪、被回覆：項目那一列的鎖讓請求排隊，不會有重複的版本號或 500。</summary>
[TestFixture]
public sealed class CommentEditConcurrencyTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await WorkTestHost.StartAsync();
        _host.Clock.AdvanceOnEveryRead(TimeSpan.FromMilliseconds(1));
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EditCommentAsync_TwoEditsAtTheSameTime_KeepConsecutiveRevisionNumbersAndBothEvents()
    {
        // Arrange
        var (scenario, item, author, comment) = await SetUpAsync();
        var eventsBefore = (await EventFeed.OfAsync(_host, scenario)).Count(e => e.Kind == WorkEventKind.ItemCommentEdited);

        // Act
        var results = await InParallelAsync(
            [(service, ct) => service.EditCommentAsync(author, item.Key, comment.Id, "甲的版本", ct),
             (service, ct) => service.EditCommentAsync(author, item.Key, comment.Id, "乙的版本", ct)]);
        var revisions = (await _host.Activity.ListCommentRevisionsAsync(author, item.Key, comment.Id, CancellationToken.None)).Value;
        var edited = (await EventFeed.OfAsync(_host, scenario)).Count(e => e.Kind == WorkEventKind.ItemCommentEdited);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Succeeded), Has.All.True);
            Assert.That(revisions.Select(r => r.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(revisions[2].Body, Is.EqualTo("原本的留言"));
            Assert.That(revisions[0].Body, Is.AnyOf("甲的版本", "乙的版本"));
            Assert.That(revisions[1].Body, Is.AnyOf("甲的版本", "乙的版本").And.Not.EqualTo(revisions[0].Body));
            Assert.That(edited - eventsBefore, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task DeleteAndReplyAtTheSameTime_NeverLeaveASuccessfulReplyThatIsAlreadyRejected()
    {
        // Arrange
        var (_, item, author, comment) = await SetUpAsync();

        // Act
        var results = await InParallelAsync(
            [(service, ct) => service.DeleteCommentAsync(author, item.Key, comment.Id, ct),
             (service, ct) => service.AddCommentAsync(author, item.Key, "回覆", comment.Id, ct)]);
        var all = await ActivityLog.AllAsync(_host, author, item.Key);
        var parent = all.Single(a => a.Id == comment.Id);
        var replies = all.Where(a => a.ReplyToId == comment.Id).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0].Succeeded, Is.True, () => results[0].Failure!.Message);
            Assert.That(parent.DeletedAt, Is.Not.Null);
            if (results[1].Succeeded)
            {
                Assert.That(replies, Has.Length.EqualTo(1));
                Assert.That(replies[0].At, Is.LessThan(parent.DeletedAt!.Value), "回覆成功就表示它在刪除之前拿到鎖");
            }
            else
            {
                Assert.That((results[1].Failure!.Kind, results[1].Failure!.Code), Is.EqualTo((FailureKind.Conflict, WorkErrorCodes.CommentDeleted)));
                Assert.That(replies, Is.Empty);
            }
        }
    }

    /// <summary>所有請求先準備好再一起放行，盡量讓它們真的同時打到資料庫。</summary>
    private async Task<Result<ItemActivity>[]> InParallelAsync(
        Func<IItemActivityService, CancellationToken, Task<Result<ItemActivity>>>[] calls)
    {
        var requests = calls.Select(_ => _host.NewActivityRequest()).ToArray();
        using var gate = new SemaphoreSlim(0, calls.Length);
        var tasks = calls.Select((call, i) => Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await call(requests[i].Activity, CancellationToken.None);
        })).ToArray();
        gate.Release(calls.Length);
        var results = await Task.WhenAll(tasks);
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        return results;
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
