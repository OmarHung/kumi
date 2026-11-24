using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Work.Persistence;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>同時進來的匯入、不同組織與來源的外部編號各自獨立、關注的人的處理、記下誰匯的。</summary>
[TestFixture]
public sealed class HistoryConcurrencyAndScopeTests
{
    private const int Parallel = 4;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportCommentsAsync_SameCommentFromSeveralRequests_IsStoredOnce()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var batch = Comments(Comment("u-1", "m-1", member.PersonId));

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(async _ =>
        {
            var request = _host.NewRequest();
            await using (request.Scope)
            {
                var service = request.Scope.ServiceProvider.GetRequiredService<IHistoryImportService>();
                return await service.ImportCommentsAsync(manager, "WEB", batch, CancellationToken.None);
            }
        }));
        var comments = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Where(a => a.Kind == ActivityKind.Commented).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join("; ", results.Select(r => r.Failure?.Message)));
            Assert.That(results.Sum(r => r.Value.Created.Count), Is.EqualTo(1));
            Assert.That(results.Sum(r => r.Value.Skipped.Count), Is.EqualTo(Parallel - 1));
            Assert.That(comments, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_SameAttachmentFromSeveralRequests_LosersDeleteTheirFileAndLeaveNoRow()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var filesBefore = AttachmentSupport.FilesIn(_host).Length;

        // 每個請求都先讀到內容、才有人去寫資料列：確保大家都通過「還沒匯入」的第一關，輸掉的人一定會寫過檔案。
        using var barrier = new Barrier(Parallel);
        var allArrived = true;

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, Parallel).Select(async number =>
        {
            var request = _host.NewRequest();
            await using (request.Scope)
            {
                var service = request.Scope.ServiceProvider.GetRequiredService<IHistoryImportService>();
                var stream = new GatedStream(System.Text.Encoding.UTF8.GetBytes($"內容 {number}"), () => allArrived &= barrier.SignalAndWait(TimeSpan.FromSeconds(20)));
                return await service.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, stream: stream), CancellationToken.None);
            }
        }));
        var attachments = await _host.Attachments.ListAsync(manager, "WEB-1", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(allArrived, Is.True, "四個請求都要走到寫檔");
            Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join("; ", results.Select(r => r.Failure?.Message)));
            Assert.That(results.Count(r => !r.Value.Existing), Is.EqualTo(1));
            Assert.That(results.Select(r => r.Value.Attachment.Id).Distinct().Count(), Is.EqualTo(1));
            Assert.That(attachments.Value, Has.Count.EqualTo(1));
            Assert.That(AttachmentSupport.FilesIn(_host), Has.Length.EqualTo(filesBefore + 1), "輸掉的請求把自己寫的檔案刪掉");
        }
    }

    [Test]
    public async Task ImportItemsAsync_SameExternalIdInAnotherOrganizationOrSource_IsCreatedIndependently()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var (_, otherManager, otherMember) = await ProjectWithMembersAsync(_host);

        // Act
        var first = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        var otherOrganization = await _host.History.ImportItemsAsync(otherManager, "WEB", Batch(Row("m-1", otherMember.PersonId)), CancellationToken.None);
        var otherSource = await _host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId)) with { Source = "jira" }, CancellationToken.None);
        var again = await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value.Created, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
            Assert.That(otherOrganization.Value.Created, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
            Assert.That(otherSource.Value.Created, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-2") }));
            Assert.That(again.Value.Skipped, Is.EqualTo(new[] { new HistoryItemRef("m-1", "WEB-1") }));
        }
    }

    [Test]
    public async Task ImportCommentsAndAttachments_SameExternalIdInAnotherOrganizationOrSource_AreCreatedIndependently()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var (_, otherManager, otherMember) = await ProjectWithMembersAsync(_host);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        await _host.History.ImportItemsAsync(otherManager, "WEB", Batch(Row("m-1", otherMember.PersonId)), CancellationToken.None);
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)) with { Source = "jira" }, CancellationToken.None);

        // Act
        var comment = await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None);
        var otherComment = await _host.History.ImportCommentsAsync(otherManager, "WEB", Comments(Comment("u-1", "m-1", otherMember.PersonId)), CancellationToken.None);
        var jiraComment = await _host.History.ImportCommentsAsync(
            manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)) with { Source = "jira" }, CancellationToken.None);
        var attachment = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        var otherAttachment = await _host.History.ImportAttachmentAsync(otherManager, "WEB-1", Attachment("a-1", otherMember.PersonId), CancellationToken.None);
        var jiraAttachment = await _host.History.ImportAttachmentAsync(
            manager, "WEB-2", Attachment("a-1", member.PersonId) with { Source = "jira" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new[] { comment, otherComment, jiraComment }.Select(r => r.Value.Created.Count), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(new[] { attachment, otherAttachment, jiraAttachment }.Select(r => r.Value.Existing), Is.EqualTo(new[] { false, false, false }));
        }
    }

    [Test]
    public async Task ImportItemsAsync_MoreFollowersThanTheLimit_IsAProblemOfThatRow()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var followers = Enumerable.Range(0, HistoryRulesLimit + 1).Select(_ => scenario.Person().PersonId).ToArray();

        // Act
        var result = await _host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId) with { Followers = followers }), CancellationToken.None);

        // Assert
        Assert.That(Paths(result.Failure), Is.EqualTo(new[] { "rows/0/followers" }));
    }

    [Test]
    public async Task ImportItemsAsync_ExactlyTheLimitOfFollowers_IsAccepted()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var followers = new List<Guid>();
        for (var number = 0; number < HistoryRulesLimit; number++)
        {
            followers.Add((await scenario.MemberAsync("WEB", "viewer")).PersonId);
        }

        // Act
        var result = await _host.History.ImportItemsAsync(
            manager, "WEB", Batch(Row("m-1", member.PersonId) with { Followers = followers }), CancellationToken.None);
        var created = (await EventFeed.OfAsync(_host, scenario)).Single(e => e.Kind == WorkEventKind.ItemCreated && e.Item.ItemKey == "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(created.Item.Followers, Is.EqualTo(followers));
        }
    }

    [Test]
    public async Task ImportItemsAsync_DuplicateFollowersAndTheAssignee_AreDedupedAndSkippedWithoutError()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);
        var first = await scenario.MemberAsync("WEB", "viewer");
        var second = await scenario.MemberAsync("WEB", "viewer");
        var row = Row("m-1", member.PersonId) with
        {
            Item = new NewWorkItem { Type = "task", Title = "重複關注", AssigneeId = first.PersonId },
            Followers = [second.PersonId, first.PersonId, second.PersonId, first.PersonId],
        };

        // Act
        var result = await _host.History.ImportItemsAsync(manager, "WEB", Batch(row), CancellationToken.None);
        var created = (await EventFeed.OfAsync(_host, scenario)).Single(e => e.Kind == WorkEventKind.ItemCreated && e.Item.ItemKey == "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(created.Item.Followers, Is.EqualTo(new[] { second.PersonId }));
        }
    }

    [Test]
    public async Task Import_EveryKind_RecordsWhoRanTheImport()
    {
        // Arrange
        var (scenario, manager, member) = await ProjectWithMembersAsync(_host);

        // Act
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId)), CancellationToken.None);
        await _host.History.ImportCommentsAsync(manager, "WEB", Comments(Comment("u-1", "m-1", member.PersonId)), CancellationToken.None);
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        await using var db = await _host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
        var refs = await db.HistoryRefs.AsNoTracking().Where(r => r.OrganizationId == scenario.OrganizationId).ToListAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refs.Select(r => r.Kind).Order(), Is.EqualTo(new[] { "attachment", "comment", "item" }));
            Assert.That(refs.Select(r => r.ImportedBy), Has.All.EqualTo(manager.PersonId), "署名的是原作者，匯入的人是呼叫者");
        }
    }

    private const int HistoryRulesLimit = 50;

    /// <summary>讀的時候先停在閘門：所有請求都到了這裡才一起往下，讓大家都通過「還沒匯入」的檢查。</summary>
    private sealed class GatedStream(byte[] bytes, Action gate) : MemoryStream(bytes)
    {
        private int _passed;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Pass();
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Pass();
            return base.ReadAsync(buffer, cancellationToken);
        }

        private void Pass()
        {
            if (Interlocked.Exchange(ref _passed, 1) == 0)
            {
                gate();
            }
        }
    }
}
