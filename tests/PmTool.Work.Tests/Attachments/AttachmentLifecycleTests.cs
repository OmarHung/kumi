using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.Attachments;

/// <summary>上傳、列出、下載、刪除：資料列、儲存空間的檔案、活動與事件一起對得上。</summary>
[TestFixture]
public sealed class AttachmentLifecycleTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AddAsync_ReturnsTheAttachmentWithTheSizeAndSha256OfTheContent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var bytes = "報價單 v2\n合計 1,200 元"u8.ToArray();
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, "web-1", Upload("報價單.pdf", bytes, "application/pdf"), CancellationToken.None);

        // Assert
        var attachment = added.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(attachment.ItemKey, Is.EqualTo(item.Key));
            Assert.That(attachment.FileName, Is.EqualTo("報價單.pdf"));
            Assert.That(attachment.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(attachment.SizeBytes, Is.EqualTo(bytes.Length));
            Assert.That(attachment.Sha256, Is.EqualTo(Sha256Of(bytes)));
            Assert.That(attachment.UploadedBy, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(attachment.UploadedAt, Is.EqualTo(item.CreatedAt.AddMinutes(5)));
        }
    }

    [Test]
    public async Task AddAsync_AnEmptyFile_IsAllowedAndHasTheSha256OfNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("空的.txt", []), CancellationToken.None);

        // Assert
        Assert.That((added.Value.SizeBytes, added.Value.Sha256), Is.EqualTo((0L, Sha256Of([]))));
    }

    [Test]
    public async Task ListAsync_ReturnsTheAttachmentsOldestFirst()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var other = await scenario.CreateTaskAsync("WEB", "另一個項目");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            _host.Clock.Advance(TimeSpan.FromMinutes(1));
            await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload(name, name), CancellationToken.None);
        }

        await _host.Attachments.AddAsync(scenario.Admin, other.Key, Upload("別人的.txt", "x"), CancellationToken.None);

        // Act
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(a => a.FileName), Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt" }));
    }

    [Test]
    public async Task ListAsync_AnItemWithoutAttachments_IsEmpty()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(listed.Value, Is.Empty);
    }

    [Test]
    public async Task OpenAsync_ReturnsTheSameBytesAndTheAttachment()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("資料.bin", bytes), CancellationToken.None);

        // Act
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var attachment = opened.Value.Attachment;
        var read = await ReadAllAsync(opened.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment, Is.EqualTo(added.Value));
            Assert.That(read, Is.EqualTo(bytes));
        }
    }

    [Test]
    public async Task DeleteAsync_RemovesTheRowAndTheFileAndReturnsTheDeletedAttachment()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "內容"), CancellationToken.None);
        var filesBefore = FilesIn(_host).Length;

        // Act
        var deleted = await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var deletedAgain = await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Value, Is.EqualTo(added.Value));
            Assert.That(listed.Value, Is.Empty);
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(deletedAgain.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore - 1));
        }
    }

    [Test]
    public async Task AddAsync_AndDeleteAsync_LeaveActivitiesWithTheFileNameAndLeaveTheItemUnchanged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("規格.pdf", "x", "application/pdf"), CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        var (addedActivity, removedActivity) = (activities[^2], activities[^1]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(addedActivity.Kind, Is.EqualTo(ActivityKind.AttachmentAdded));
            Assert.That(addedActivity.ActorId, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(addedActivity.At, Is.EqualTo(added.Value.UploadedAt));
            Assert.That(ActivityLog.Shapes(addedActivity), Is.EqualTo(new[] { ("attachment", (string?)null, (string?)"\"規格.pdf\"") }));
            Assert.That(removedActivity.Kind, Is.EqualTo(ActivityKind.AttachmentRemoved));
            Assert.That(ActivityLog.Shapes(removedActivity), Is.EqualTo(new[] { ("attachment", (string?)"\"規格.pdf\"", (string?)null) }));
            Assert.That(readBack.Value.UpdatedAt, Is.EqualTo(item.UpdatedAt), "附件不算項目內容的修改");
        }
    }

    [Test]
    public async Task AddAsync_AndDeleteAsync_WriteEventsThatCarryTheItemAndTheActivity()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "有附件的項目");

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        var addedEvent = await EventFeed.LastAsync(_host, scenario);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var removedEvent = await EventFeed.LastAsync(_host, scenario);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(addedEvent.Kind, Is.EqualTo(WorkEventKind.ItemAttachmentAdded));
            Assert.That(addedEvent.ActorPersonId, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That((addedEvent.Item.ItemKey, addedEvent.Item.Title, addedEvent.Item.ProjectKey), Is.EqualTo((item.Key, "有附件的項目", "WEB")));
            Assert.That(addedEvent.Item.ChangedFields, Is.EqualTo(new[] { "attachments" }));
            Assert.That(addedEvent.Item.ActivityId, Is.EqualTo(activities[^2].Id));
            Assert.That(removedEvent.Kind, Is.EqualTo(WorkEventKind.ItemAttachmentRemoved));
            Assert.That(removedEvent.Item.ChangedFields, Is.EqualTo(new[] { "attachments" }));
            Assert.That(removedEvent.Item.ActivityId, Is.EqualTo(activities[^1].Id));
        }
    }

    [Test]
    public async Task AddAsync_TheFileIsStoredUnderASubfolderNamedByTheLastTwoCharactersOfTheId()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);

        // Assert
        var id = added.Value.Id.ToString("D");
        Assert.That(FilesIn(_host), Does.Contain(Path.Combine(_host.AttachmentDirectory, id[^2..], id)));
    }

    [Test]
    public async Task DeleteAsync_ThenRestoringTheItem_DoesNotBringTheAttachmentBack()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var kept = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("留著.txt", "x"), CancellationToken.None);
        var removed = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("刪掉.txt", "y"), CancellationToken.None);
        await _host.Attachments.DeleteAsync(scenario.Admin, removed.Value.Id, CancellationToken.None);

        // Act
        await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);
        await _host.Items.RestoreAsync(scenario.Admin, item.Key, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(a => a.Id), Is.EqualTo(new[] { kept.Value.Id }));
    }
}
