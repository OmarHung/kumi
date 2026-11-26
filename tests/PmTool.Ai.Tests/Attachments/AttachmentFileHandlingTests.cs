using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PmTool.Abstractions;
using PmTool.Ai.Maintenance;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 檔案與資料列不一致時的處理：刪待送的附件先刪檔再處理資料列、維護每批刪不掉的不卡住後面、
/// 下載時檔案不見但資料列也不在了就當作已清掉（不記錯誤）；資料列還在才記 Error。
/// </summary>
[TestFixture]
public sealed class AttachmentFileHandlingTests
{
    private LevelLog _logs = null!;
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _logs = new LevelLog();
        _h = await AssistantHarness.StartAsync(configure: services => services.AddLogging(b => b.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace)));
    }

    [TearDown]
    public async Task StopAsync()
    {
        await _h.DisposeAsync();
        _logs.Dispose();
    }

    private IAiMaintenance Maintenance => _h.Host.Get<IAiMaintenance>();

    private async Task<AiAttachmentSnapshot?> RowAsync(Guid id)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
        return row is null ? null : new AiAttachmentSnapshot(row.BoundAt, row.MessageId, row.SweepFailedAt);
    }

    private sealed record AiAttachmentSnapshot(DateTimeOffset? BoundAt, Guid? MessageId, DateTimeOffset? SweepFailedAt);

    // ---- 刪待送的附件：先刪檔 ----

    [Test]
    public async Task DeletePending_RemovesTheFileAndTheRow()
    {
        // Arrange
        var attachment = await _h.UploadAsync("圖.png", Image());

        // Act
        var result = await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
            Assert.That(await RowAsync(attachment.Id), Is.Null);
        }
    }

    [Test]
    public async Task DeletePending_WhenTheFileCannotBeDeleted_KeepsTheRowForMaintenanceAndStillSucceeds()
    {
        // Arrange
        var attachment = await _h.UploadAsync("圖.png", Image());
        _h.Store().FailDeleteKeys.Add(attachment.Id.ToString("D"));

        // Act
        var result = await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);
        var row = await RowAsync(attachment.Id);
        var download = await _h.Attachments().OpenAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);
        var again = await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert：對使用者來說已經刪了（下載、再刪都是找不到），資料列留著當孤兒，檔案還在，容量不能先放掉。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
            Assert.That(row, Is.Not.Null, "檔案還在，資料列不能先刪");
            Assert.That(row!.MessageId, Is.Null);
            Assert.That(row.BoundAt, Is.Not.Null, "標成孤兒，維護下一輪就會清");
            Assert.That(_h.Store().Contains(attachment.Id), Is.True);
            AssertFailure(download.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(again.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }

        _h.Store().FailDeleteKeys.Clear();
        var swept = await Maintenance.SweepAsync(CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(swept.Attachments, Is.EqualTo(1));
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
            Assert.That(await RowAsync(attachment.Id), Is.Null);
        }
    }

    [Test]
    public async Task DeletePending_AnOrphanLeftByAFailedDelete_DoesNotCountAgainstThePendingLimit()
    {
        // Arrange
        var first = await _h.UploadAsync("一.png", Image());
        _h.Store().FailDeleteKeys.Add(first.Id.ToString("D"));
        await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, first.Id, CancellationToken.None);
        for (var i = 0; i < AssistantAttachmentRules.MaxPendingPerPerson; i++)
        {
            await _h.UploadAsync($"{i}.png", Image());
        }

        // Act
        var next = await _h.TryUploadAsync("多一個.png", Image());

        // Assert：20 個待送滿了才會擋；被刪掉的那個不算。
        AssertFailure(next.Failure, FailureKind.Conflict, AiErrorCodes.TooManyAttachments);
    }

    // ---- 維護：刪不掉的排到最後 ----

    private async Task<Guid> OrphanAsync(string name)
    {
        var attachment = await _h.UploadAsync(name, Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "看", attachment);
        await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        return attachment.Id;
    }

    [Test]
    public async Task Sweep_AFileThatKeepsFailing_IsMarkedAndSortedLastSoItDoesNotBlockTheRest()
    {
        // Arrange：最舊的那個一直刪不掉；每批只取一筆，沒有排序的話永遠卡在它身上。
        var stuck = await OrphanAsync("卡住.png");
        var fine = await OrphanAsync("正常.png");
        _h.Store().FailDeleteKeys.Add(stuck.ToString("D"));
        await using var db = await _h.Host.CreateDbContextAsync();
        var now = _h.Clock.GetUtcNow();

        // Act
        var first = await AttachmentRetention.SweepAsync(db, _h.Store(), now, NullLogger.Instance, CancellationToken.None, batchSize: 1);
        var second = await AttachmentRetention.SweepAsync(db, _h.Store(), now, NullLogger.Instance, CancellationToken.None, batchSize: 1);

        // Assert
        var stuckRow = await RowAsync(stuck);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.Zero, "第一批取到刪不掉的");
            Assert.That(second, Is.EqualTo(1), "第二批輪到後面的，刪得掉的沒有被卡住");
            Assert.That(await RowAsync(fine), Is.Null);
            Assert.That(stuckRow?.SweepFailedAt, Is.Not.Null, "失敗的記下時間");
            Assert.That(_h.Store().Contains(stuck), Is.True);
        }
    }

    [Test]
    public async Task Sweep_FailedOnesAreRetriedOldestFailureFirstOnceNothingElseIsLeft()
    {
        // Arrange
        var stuck = await OrphanAsync("卡住.png");
        _h.Store().FailDeleteKeys.Add(stuck.ToString("D"));
        await Maintenance.SweepAsync(CancellationToken.None);
        _h.Store().FailDeleteKeys.Clear();
        _h.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var result = await Maintenance.SweepAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Attachments, Is.EqualTo(1));
            Assert.That(await RowAsync(stuck), Is.Null);
        }
    }

    // ---- 下載時檔案不見 ----

    [Test]
    public async Task Open_FileMissingButRowStillThere_IsNotFoundAndLogsAnError()
    {
        // Arrange
        var attachment = await _h.UploadAsync("圖.png", Image());
        _h.Store().Remove(attachment.Id);

        // Act
        var result = await _h.Attachments().OpenAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        Assert.That(_logs.Count(LogLevel.Error), Is.EqualTo(1), "備份沒還原到位這類情況要有人看到");
    }

    [Test]
    public async Task Open_FileMissingBecauseTheRowWasJustDeleted_IsTreatedAsAlreadyCleanedUpWithoutAnError()
    {
        // Arrange：資料列剛好被刪（使用者刪了、維護清了），檔案隨後也不見；這不是資料不一致。
        var attachment = await _h.UploadAsync("圖.png", Image());
        _h.Store().BeforeOpen = _ =>
        {
            using var db = _h.Host.CreateDbContextAsync().GetAwaiter().GetResult();
            db.Attachments.Where(a => a.Id == attachment.Id).ExecuteDelete();
            _h.Store().Remove(attachment.Id);
        };

        // Act
        var result = await _h.Attachments().OpenAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        Assert.That(_logs.Count(LogLevel.Error), Is.Zero);
    }

    // ---- 跨組織：別的組織的附件編號一律當作不存在 ----

    [Test]
    public async Task AnotherOrganizationsAttachment_IsNotFoundForUploadSendDownloadAndDelete()
    {
        // Arrange：同一個人編號、同一個專案代號，但附件屬於別的組織。
        var attachment = await _h.UploadAsync("圖.png", Image());
        var conversation = await _h.NewConversationAsync();
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            var other = Guid.CreateVersion7();
            await db.Attachments.Where(a => a.Id == attachment.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.OrganizationId, other));
        }

        // Act
        var send = await _h.TryStartAsync(conversation.Id, "看", [attachment.Id]);
        var open = await _h.Attachments().OpenAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);
        var delete = await _h.Attachments().DeletePendingAsync(_h.Actor, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(send.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
        AssertFailure(open.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        AssertFailure(delete.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        await using var verify = await _h.Host.CreateDbContextAsync();
        Assert.That(await verify.Attachments.CountAsync(a => a.Id == attachment.Id), Is.EqualTo(1), "別的組織的附件沒被動到");
    }

    [Test]
    public async Task ActorFromAnotherOrganizationWithTheSamePersonId_CannotOpenOrDeleteMyAttachment()
    {
        // Arrange
        var attachment = await _h.UploadAsync("圖.png", Image());
        var stranger = new Actor(Guid.CreateVersion7(), _h.Actor.PersonId, IsOrganizationAdmin: false);

        // Act
        var open = await _h.Attachments().OpenAsync(stranger, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);
        var delete = await _h.Attachments().DeletePendingAsync(stranger, AssistantHarness.ProjectKey, attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(open.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        AssertFailure(delete.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        Assert.That(_h.Store().Contains(attachment.Id), Is.True);
    }

    [Test]
    public async Task AnotherOrganizationsActor_CannotUploadWhereItsOrganizationHasNoAi()
    {
        // Arrange
        var stranger = new Actor(Guid.CreateVersion7(), Guid.CreateVersion7(), IsOrganizationAdmin: false);

        // Act
        var result = await _h.TryUploadAsync("圖.png", Image(), stranger);

        // Assert
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(_h.Store().Keys, Is.Empty);
    }

    private sealed class LevelLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogLevel> _levels = new();

        public int Count(LogLevel level) => _levels.Count(l => l == level);

        public ILogger CreateLogger(string categoryName) => new Capture(_levels);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<LogLevel> levels) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                levels.Enqueue(logLevel);
        }
    }
}
