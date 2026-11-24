using PmTool.Abstractions;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>歷史附件：保留上傳的人與時間、已經匯入過時不讀內容、一般上傳的規則照樣適用。</summary>
[TestFixture]
public sealed class HistoryAttachmentImportTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportAttachmentAsync_NewFile_StoresItWithTheOriginalUploaderAndTime()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var at = Past(10).AddMilliseconds(250);

        // Act
        var result = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, "圖.png", "圖片內容", at), CancellationToken.None);
        var content = await _host.Attachments.OpenAsync(manager, result.Value.Attachment.Id, CancellationToken.None);
        var activity = (await ActivityLog.AllAsync(_host, manager, "WEB-1")).Single(a => a.Kind == ActivityKind.AttachmentAdded);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Existing, Is.False);
            Assert.That((result.Value.Attachment.UploadedBy, result.Value.Attachment.UploadedAt, result.Value.Attachment.FileName), Is.EqualTo((member.PersonId, at, "圖.png")));
            Assert.That(System.Text.Encoding.UTF8.GetString(await AttachmentSupport.ReadAllAsync(content.Value)), Is.EqualTo("圖片內容"));
            Assert.That((activity.ActorId, activity.At, activity.ImportedFrom), Is.EqualTo((member.PersonId, at, "monday")));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_AlreadyImported_ReturnsTheSameAttachmentWithoutReadingTheContent()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var first = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        var unreadable = new AttachmentSupport.UnreadableStream();
        var filesBefore = AttachmentSupport.FilesIn(_host).Length;

        // Act
        var again = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, stream: unreadable), CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(manager, "WEB-1", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(again.Succeeded, Is.True, () => again.Failure!.Message);
            Assert.That(again.Value.Existing, Is.True);
            Assert.That(again.Value.Attachment.Id, Is.EqualTo(first.Value.Attachment.Id));
            Assert.That(unreadable.WasRead, Is.False);
            Assert.That(listed.Value, Has.Count.EqualTo(1));
            Assert.That(AttachmentSupport.FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_UploaderWhoIsNotAMemberOrFutureTime_IsHistoryInvalidAndWritesNoFile()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        var filesBefore = AttachmentSupport.FilesIn(_host).Length;

        // Act
        var outsider = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", scenario.Person().PersonId), CancellationToken.None);
        var future = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-2", member.PersonId, at: WorkTestHost.StartTime.AddHours(1)), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((outsider.Failure?.Code, Paths(outsider.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "uploadedBy" })));
            Assert.That((future.Failure?.Code, Paths(future.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "uploadedAt" })));
            Assert.That(AttachmentSupport.FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_BadFileNameOrTooLarge_FailsLikeAnOrdinaryUpload()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        var big = Attachment("a-2", member.PersonId, stream: new AttachmentSupport.ZeroStream(AttachmentRules.MaxBytes + 1));
        var filesBefore = AttachmentSupport.FilesIn(_host).Length;

        // Act
        var badName = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, "../x.png"), CancellationToken.None);
        var tooLarge = await _host.History.ImportAttachmentAsync(manager, "WEB-1", big, CancellationToken.None);
        var files = AttachmentSupport.FilesIn(_host);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(badName.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNameInvalid));
            Assert.That(tooLarge.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentTooLarge));
            Assert.That(files, Has.Length.EqualTo(filesBefore), "沒有留下寫了一半的檔案");
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_ItemAlreadyHasTheMaximum_IsRefused()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        for (var number = 0; number < AttachmentRules.MaxPerItem; number++)
        {
            var added = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment($"a-{number}", member.PersonId, content: "x"), CancellationToken.None);
            Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
        }

        // Act
        var result = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-over", member.PersonId), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentLimitReached));
    }

    [Test]
    public async Task ImportAttachmentAsync_WritesAHistoryEvent()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();

        // Act
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        var added = (await EventFeed.OfAsync(_host, scenario)).Single(e => e.Kind == WorkEventKind.ItemAttachmentAdded);

        // Assert
        Assert.That((added.Item.History, added.ActorPersonId, added.OccurredAt), Is.EqualTo((true, member.PersonId, Past(10))));
    }

    [Test]
    public async Task ImportAttachmentAsync_WithoutProjectManageOrArchivedOrUnknownItem_IsRefused()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();

        // Act
        var forbidden = await _host.History.ImportAttachmentAsync(member, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);
        var unknown = await _host.History.ImportAttachmentAsync(manager, "WEB-99", Attachment("a-2", member.PersonId), CancellationToken.None);
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var archived = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-3", member.PersonId), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forbidden.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(unknown.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(archived.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_ExternalIdImportedIntoAnotherProject_IsHistoryInvalid()
    {
        // Arrange
        var (scenario, manager, member) = await ImportedItemAsync();
        await scenario.CreateProjectAsync("OPS");
        await _host.Projects.SetMemberAsync(scenario.Admin, "OPS", member.PersonId, "editor", CancellationToken.None);
        await _host.History.ImportItemsAsync(scenario.Admin, "OPS", Batch(Row("o-1", member.PersonId)), CancellationToken.None);
        await _host.History.ImportAttachmentAsync(scenario.Admin, "OPS-1", Attachment("a-1", member.PersonId), CancellationToken.None);

        // Act
        var result = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId), CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Code, Paths(result.Failure)), Is.EqualTo((WorkErrorCodes.HistoryInvalid, new[] { "externalId" })));
    }

    private async Task<(Scenario Scenario, Actor Manager, Actor Member)> ImportedItemAsync()
    {
        var setup = await ProjectWithMembersAsync(_host);
        var result = await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("m-1", setup.Member.PersonId)), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return setup;
    }
}
