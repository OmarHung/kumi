using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Common;
using PmTool.Storage;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 永久刪除（知識庫唯一真的刪資料的操作）：只有空間的管理者；只刪要刪的那幾批，連同版本、名單與附件；
/// 提交之後才刪檔案；留著的頁面還指到要刪的頁面時，那一批這次不刪。
/// </summary>
[TestFixture]
public sealed class TrashPurgeTests
{
    private string _directory = null!;
    private RecordingFileStore _store = null!;
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kb-purge-" + Guid.NewGuid().ToString("N"));
        _store = new RecordingFileStore(FileStores.Create(new FileStoreSettings(FileStoreSettings.LocalProvider, _directory)));
        _host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton<IFileStore>(_store));
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        await _host.DisposeAsync();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static NewPageAttachment Upload(string name = "a.txt") =>
        new() { FileName = name, ContentType = "text/plain", Content = new MemoryStream(Encoding.UTF8.GetBytes("內容")) };

    private async Task<PageAttachment> AttachAsync(Org org, int pageNumber)
    {
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", pageNumber, Upload(), CancellationToken.None);
        Assert.That(added.Succeeded, Is.True, () => added.Failure?.Message ?? string.Empty);
        return added.Value;
    }

    private async Task<Guid> DeleteAsync(Org org, int pageNumber)
    {
        var deleted = await _host.Pages.DeleteAsync(org.Admin, "HR", pageNumber, CancellationToken.None);
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure?.Message ?? string.Empty);
        return deleted.Value.DeletionId;
    }

    private async Task<Org> ArrangeAsync()
    {
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        return org;
    }

    [Test]
    public async Task PurgeTrashAsync_WithoutABatch_RemovesOnlyBatchesOlderThanThirtyDaysWithTheirAttachments()
    {
        // Arrange
        var org = await ArrangeAsync();
        var old = await org.PageAsync("HR", "舊的");
        var oldChild = await org.PageAsync("HR", "舊的子頁", parent: old.Number);
        var recent = await org.PageAsync("HR", "新的");
        var live = await org.PageAsync("HR", "活的");
        var oldAttachment = await AttachAsync(org, oldChild.Number);
        var recentAttachment = await AttachAsync(org, recent.Number);
        var liveAttachment = await AttachAsync(org, live.Number);
        await DeleteAsync(org, old.Number);
        _host.Clock.Advance(TimeSpan.FromDays(31));
        var recentBatch = await DeleteAsync(org, recent.Number);
        _store.Deleted.Clear();

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Value, Is.EqualTo(new TrashPurge(1, 2, 1, 1)));
            Assert.That(trash.Value.Items.Select(e => e.DeletionId), Is.EqualTo(new[] { recentBatch }));
            Assert.That(_store.Deleted, Is.EqualTo(new[] { oldAttachment.Id.ToString("D") }));
            Assert.That(await _store.OpenReadAsync(recentAttachment.Id.ToString("D"), CancellationToken.None), Is.Not.Null);
            Assert.That(await _store.OpenReadAsync(liveAttachment.Id.ToString("D"), CancellationToken.None), Is.Not.Null);
            Assert.That((await _host.Pages.GetAsync(org.Admin, "HR", live.Number, CancellationToken.None)).Succeeded, Is.True);
        }
    }

    [Test]
    public async Task PurgeTrashAsync_APurgedPage_IsGoneForReadingRestoringAndDownloading()
    {
        // Arrange
        var org = await ArrangeAsync();
        var page = await org.PageAsync("HR", "要清掉的");
        var attachment = await AttachAsync(org, page.Number);
        var batch = await DeleteAsync(org, page.Number);

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);
        var read = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        var downloaded = await _host.Attachments.OpenAsync(org.Admin, attachment.Id, CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Value, Is.EqualTo(new TrashPurge(1, 1, 1, 0)));
            Assert.That(read.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(restored.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(restored.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(downloaded.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
            Assert.That(await _store.OpenReadAsync(attachment.Id.ToString("D"), CancellationToken.None), Is.Null);
            Assert.That(trash.Value.Items, Is.Empty);
        }
    }

    [Test]
    public async Task PurgeTrashAsync_OneBatch_LeavesTheOtherBatchesAndLivePagesAlone()
    {
        // Arrange
        var org = await ArrangeAsync();
        var target = await org.PageAsync("HR", "清這個");
        var other = await org.PageAsync("HR", "留著");
        var live = await org.PageAsync("HR", "活的");
        var targetBatch = await DeleteAsync(org, target.Number);
        var otherBatch = await DeleteAsync(org, other.Number);

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(targetBatch), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", other.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Value.PurgedBatches, Is.EqualTo(1));
            Assert.That(purged.Value.RemainingBatches, Is.EqualTo(1));
            Assert.That(trash.Value.Items.Select(e => e.DeletionId), Is.EqualTo(new[] { otherBatch }));
            Assert.That(restored.Succeeded, Is.True);
            Assert.That((await org.OutlineAsync("HR")).Order(), Is.EqualTo(new[] { "活的", "留著" }.Order()));
            Assert.That(live.Number, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_OnlyTouchesFilesThatBelongToThePurgedPages()
    {
        // Arrange：另一個地方（例如項目）的附件放在同一個儲存空間，鍵是它自己的編號。
        var org = await ArrangeAsync();
        var page = await org.PageAsync("HR", "頁");
        var attachment = await AttachAsync(org, page.Number);
        var foreignKey = Guid.CreateVersion7().ToString("D");
        await _store.WriteAsync(foreignKey, new MemoryStream("項目的附件"u8.ToArray()), "text/plain", 1024, CancellationToken.None);
        var batch = await DeleteAsync(org, page.Number);
        _store.Deleted.Clear();

        // Act
        await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_store.Deleted, Is.EqualTo(new[] { attachment.Id.ToString("D") }));
            Assert.That(await _store.OpenReadAsync(foreignKey, CancellationToken.None), Is.Not.Null);
        }
    }

    [Test]
    public async Task PurgeTrashAsync_AFileThatCannotBeDeleted_DoesNotFailThePurge()
    {
        // Arrange
        var org = await ArrangeAsync();
        var page = await org.PageAsync("HR", "頁");
        await AttachAsync(org, page.Number);
        var batch = await DeleteAsync(org, page.Number);
        _store.FailDeletes = true;

        // Act
        try
        {
            var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);

            // Assert
            Assert.That(purged.Value, Is.EqualTo(new TrashPurge(1, 1, 1, 0)));
        }
        finally
        {
            _store.FailDeletes = false;
        }
    }

    [Test]
    public async Task PurgeTrashAsync_NotTheSpaceManager_IsForbiddenAndDeletesNothing()
    {
        // Arrange
        var org = await ArrangeAsync();
        var page = await org.PageAsync("HR", "頁");
        var batch = await DeleteAsync(org, page.Number);

        // Act
        var byMember = await _host.Pages.PurgeTrashAsync(org.Member(), "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);
        var byGuest = await _host.Pages.PurgeTrashAsync(org.Guest(), "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byMember.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(byGuest.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
            Assert.That(trash.Value.Items, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ArchivedSpace_IsRefused()
    {
        // Arrange
        var org = await ArrangeAsync();
        var page = await org.PageAsync("HR", "頁");
        var batch = await DeleteAsync(org, page.Number);
        await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: true, CancellationToken.None);

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);

        // Assert
        Assert.That(purged.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
    }

    [Test]
    public async Task PurgeTrashAsync_ABatchThatIsNotInTheTrash_IsNotFound()
    {
        // Arrange
        var org = await ArrangeAsync();
        await org.PageAsync("HR", "活的");

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(Guid.NewGuid()), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(purged.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ABatchOfAnotherOrganization_IsNotFoundAndKeptIntact()
    {
        // Arrange
        var mine = await ArrangeAsync();
        var theirs = await ArrangeAsync();
        var page = await theirs.PageAsync("HR", "別人的");
        var batch = await DeleteAsync(theirs, page.Number);

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(mine.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);
        var theirTrash = await _host.Pages.ListTrashAsync(theirs.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        Assert.That(purged.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
        Assert.That(theirTrash.Value.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task PurgeTrashAsync_ALivePageStillPointingAtAnExpiredPage_KeepsThatBatchAndPurgesTheRest()
    {
        // Arrange：資料不一致：到期那一批的頁面底下還有一頁活的。
        var org = await ArrangeAsync();
        var broken = await org.PageAsync("HR", "上層");
        var stranded = await org.PageAsync("HR", "留著的下層");
        var fine = await org.PageAsync("HR", "沒問題");
        var brokenBatch = await DeleteAsync(org, broken.Number);
        var fineBatch = await DeleteAsync(org, fine.Number);
        _host.Clock.Advance(TimeSpan.FromDays(40));
        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Pages.Where(p => p.Id == stranded.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, broken.Id));
        }

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);
        var still = await _host.Pages.GetAsync(org.Admin, "HR", stranded.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Value.PurgedBatches, Is.EqualTo(1));
            Assert.That(trash.Value.Items.Select(e => e.DeletionId), Is.EqualTo(new[] { brokenBatch }));
            Assert.That(still.Succeeded, Is.True);
            Assert.That(fineBatch, Is.Not.EqualTo(brokenBatch));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ABatchWhoseOwnPagesAreStillReferenced_IsSkippedAndNothingIsDeleted()
    {
        // Arrange
        var org = await ArrangeAsync();
        var broken = await org.PageAsync("HR", "上層");
        var stranded = await org.PageAsync("HR", "活的");
        var batch = await DeleteAsync(org, broken.Number);
        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Pages.Where(p => p.Id == stranded.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, broken.Id));
        }

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(batch), CancellationToken.None);

        // Assert
        Assert.That(purged.Value, Is.EqualTo(new TrashPurge(0, 0, 0, 1)));
    }

    [Test]
    public async Task PurgeTrashAsync_ManyExpiredBatches_RemovesTheOldestFirstUpToTheLimit()
    {
        // Arrange
        await using var host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new KnowledgeLimits(MaxBatchesPerPurge: 2)));
        var org = new Org(host);
        await org.CreateSpaceAsync("HR");
        var batches = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var page = await org.PageAsync("HR", $"頁{index}");
            batches.Add((await host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None)).Value.DeletionId);
            host.Clock.Advance(TimeSpan.FromDays(31));
        }

        // Act
        var purged = await host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);
        var trash = await host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purged.Value.PurgedBatches, Is.EqualTo(2));
            Assert.That(purged.Value.RemainingBatches, Is.EqualTo(1));
            Assert.That(purged.Value.HasMoreExpired, Is.True, "還有到期的批沒清到");
            Assert.That(trash.Value.Items.Select(e => e.DeletionId), Is.EqualTo(new[] { batches[2] }));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ExpiredBatches_StopWhenThePageLimitWouldBeExceededButAlwaysTakeOneBatch()
    {
        // Arrange：一次最多 3 頁。批 A 兩頁、批 B 兩頁、批 C 一頁，由舊到新，全部到期。
        await using var host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new KnowledgeLimits(MaxPagesPerPurge: 3)));
        var org = new Org(host);
        await org.CreateSpaceAsync("HR");
        var batches = new List<Guid>();
        foreach (var (title, children) in new[] { ("A", 1), ("B", 1), ("C", 0) })
        {
            var top = await org.PageAsync("HR", title);
            if (children > 0)
            {
                await org.PageAsync("HR", title + "-子", parent: top.Number);
            }

            batches.Add((await host.Pages.DeleteAsync(org.Admin, "HR", top.Number, CancellationToken.None)).Value.DeletionId);
            host.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        host.Clock.Advance(TimeSpan.FromDays(31));

        // Act
        var first = await host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);
        var second = await host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value, Is.EqualTo(new TrashPurge(1, 2, 0, 2, HasMoreExpired: true)));
            Assert.That(second.Value, Is.EqualTo(new TrashPurge(2, 3, 0, 0, HasMoreExpired: false)));
            Assert.That(batches, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ABatchLargerThanThePageLimit_IsStillPurgedWhole()
    {
        // Arrange
        await using var host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new KnowledgeLimits(MaxPagesPerPurge: 1)));
        var org = new Org(host);
        await org.CreateSpaceAsync("HR");
        var top = await org.PageAsync("HR", "大的一批");
        await org.PageAsync("HR", "子一", parent: top.Number);
        await org.PageAsync("HR", "子二", parent: top.Number);
        await host.Pages.DeleteAsync(org.Admin, "HR", top.Number, CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromDays(31));

        // Act
        var purged = await host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);

        // Assert
        Assert.That(purged.Value, Is.EqualTo(new TrashPurge(1, 3, 0, 0, HasMoreExpired: false)));
    }

    [Test]
    public async Task PurgeTrashAsync_HasMoreExpired_IsFalseForBatchesThatCannotBePurged()
    {
        // Arrange：到期那一批底下還有活的頁面指著它，清不掉，不能算「還有更多」。
        var org = await ArrangeAsync();
        var broken = await org.PageAsync("HR", "上層");
        var stranded = await org.PageAsync("HR", "留著的下層");
        await DeleteAsync(org, broken.Number);
        _host.Clock.Advance(TimeSpan.FromDays(40));
        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Pages.Where(p => p.Id == stranded.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, broken.Id));
        }

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);

        // Assert
        Assert.That(purged.Value, Is.EqualTo(new TrashPurge(0, 0, 0, 1, HasMoreExpired: false)));
    }

    [Test]
    public async Task PurgeTrashAsync_AParentBatchWhileAnEarlierChildBatchIsStillThere_PurgesNothingUntilTheChildBatchIsGone()
    {
        // Arrange：子頁先刪（較早的一批），上層後刪（較晚的一批）。
        var org = await ArrangeAsync();
        var parent = await org.PageAsync("HR", "上層");
        var child = await org.PageAsync("HR", "子頁", parent: parent.Number);
        var childBatch = await DeleteAsync(org, child.Number);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var parentBatch = await DeleteAsync(org, parent.Number);

        // Act
        var parentFirst = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(parentBatch), CancellationToken.None);
        var childPurged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(childBatch), CancellationToken.None);
        var parentLast = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(parentBatch), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parentFirst.Value, Is.EqualTo(new TrashPurge(0, 0, 0, 2)));
            Assert.That(childPurged.Value, Is.EqualTo(new TrashPurge(1, 1, 0, 1)));
            Assert.That(parentLast.Value, Is.EqualTo(new TrashPurge(1, 1, 0, 0)));
        }
    }

    [Test]
    public async Task PurgeTrashAsync_ExpiredWithAParentAndItsEarlierChildBatch_PurgesBothInOneCall()
    {
        // Arrange
        var org = await ArrangeAsync();
        var parent = await org.PageAsync("HR", "上層");
        var child = await org.PageAsync("HR", "子頁", parent: parent.Number);
        await DeleteAsync(org, child.Number);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        await DeleteAsync(org, parent.Number);
        _host.Clock.Advance(TimeSpan.FromDays(31));

        // Act
        var purged = await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Expired(), CancellationToken.None);

        // Assert
        Assert.That(purged.Value, Is.EqualTo(new TrashPurge(2, 2, 0, 0)));
    }

    [Test]
    public async Task PurgeTrashAsync_InAProjectSpace_ProjectManagerCanButAnExecutorCannot()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Person(OrganizationRole.Member);
        var (space, projectId) = await org.CreateProjectSpaceAsync(manager);
        var executor = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, executor.PersonId, projectId, space.Key, FakeWorkAccessQueries.ExecutorPermissions);
        var page = await org.PageAsync(space.Key, "頁", actor: manager);
        var batch = (await _host.Pages.DeleteAsync(manager, space.Key, page.Number, CancellationToken.None)).Value.DeletionId;

        // Act
        var byExecutor = await _host.Pages.PurgeTrashAsync(executor, space.Key, TrashPurgeRequest.Batch(batch), CancellationToken.None);
        var byManager = await _host.Pages.PurgeTrashAsync(manager, space.Key, TrashPurgeRequest.Batch(batch), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byExecutor.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(byManager.Value, Is.EqualTo(new TrashPurge(1, 1, 0, 0)));
        }
    }

    /// <summary>記錄刪了哪些鍵的儲存空間：包住真的那一個，並能讓刪除失敗。</summary>
    private sealed class RecordingFileStore(IFileStore inner) : IFileStore
    {
        public ConcurrentBag<string> Deleted { get; } = [];

        public bool FailDeletes { get; set; }

        public Task<StoredFile?> WriteAsync(string key, Stream content, string contentType, long maxBytes, CancellationToken cancellationToken) =>
            inner.WriteAsync(key, content, contentType, maxBytes, cancellationToken);

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => inner.OpenReadAsync(key, cancellationToken);

        public async Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            Deleted.Add(key);
            if (FailDeletes)
            {
                throw new IOException("測試用的刪除失敗。");
            }

            await inner.DeleteAsync(key, cancellationToken);
        }

        public Task VerifyAsync(CancellationToken cancellationToken) => inner.VerifyAsync(cancellationToken);
    }
}
