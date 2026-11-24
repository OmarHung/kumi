using System.Security.Cryptography;
using System.Text;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Knowledge.Tests.Attachments;

[TestFixture]
public sealed class AttachmentTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static NewPageAttachment Upload(string fileName, string text = "內容", string? contentType = "text/plain") =>
        new() { FileName = fileName, ContentType = contentType, Content = new MemoryStream(Encoding.UTF8.GetBytes(text)) };

    [Test]
    public async Task AddAsync_WritesTheFileAndRecordsSizeHashAndUploader()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var member = org.Member();

        // Act
        var added = await _host.Attachments.AddAsync(member, "hr", page.Number, Upload(" 報告.txt ", "你好", "Text/Plain; charset=utf-8"), CancellationToken.None);

        // Assert
        Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
        var attachment = added.Value;
        Assert.That(attachment.FileName, Is.EqualTo("報告.txt"));
        Assert.That(attachment.ContentType, Is.EqualTo("text/plain"));
        Assert.That(attachment.SizeBytes, Is.EqualTo(Encoding.UTF8.GetByteCount("你好")));
        Assert.That(attachment.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("你好")))));
        Assert.That((attachment.PageId, attachment.UploadedBy), Is.EqualTo((page.Id, member.PersonId)));
    }

    [Test]
    public async Task OpenAsync_ReturnsTheStoredBytes()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("a.txt", "abc"), CancellationToken.None);

        // Act
        await using var opened = (await _host.Attachments.OpenAsync(org.Member(), added.Value.Id, CancellationToken.None)).Value;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer);

        // Assert
        Assert.That(Encoding.UTF8.GetString(buffer.ToArray()), Is.EqualTo("abc"));
        Assert.That(opened.Attachment.Id, Is.EqualTo(added.Value.Id));
    }

    [Test]
    public async Task ListAsync_ListsOldestFirstAndOnlyThePagesOwn()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "甲");
        var other = await org.PageAsync("HR", "乙");
        await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("1.txt"), CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("2.txt"), CancellationToken.None);
        await _host.Attachments.AddAsync(org.Admin, "HR", other.Number, Upload("3.txt"), CancellationToken.None);

        // Act
        var listed = await _host.Attachments.ListAsync(org.Member(), "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(listed.Value.Select(a => a.FileName), Is.EqualTo(new[] { "1.txt", "2.txt" }));
    }

    [Test]
    public async Task AddAsync_UnsafeFileName_IsRejectedWithoutReadingTheContent()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var unreadable = new UnreadableStream();

        // Act
        var added = await _host.Attachments.AddAsync(
            org.Admin, "HR", page.Number, new NewPageAttachment { FileName = "../x.txt", Content = unreadable }, CancellationToken.None);

        // Assert
        Assert.That(added.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentNameInvalid));
        Assert.That(added.Failure.Details[0].Path, Is.EqualTo("fileName"));
        Assert.That(unreadable.WasRead, Is.False);
    }

    [Test]
    public async Task AddAsync_TooLarge_IsRejectedAndLeavesNoFileBehind()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var before = Directory.Exists(_host.AttachmentDirectory) ? Directory.GetFiles(_host.AttachmentDirectory, "*", SearchOption.AllDirectories).Length : 0;
        var tooBig = new NewPageAttachment { FileName = "big.bin", Content = new ZeroStream(KnowledgeRules.MaxAttachmentBytes + 1) };

        // Act
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, tooBig, CancellationToken.None);

        // Assert
        Assert.That(added.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentTooLarge));
        var after = Directory.GetFiles(_host.AttachmentDirectory, "*", SearchOption.AllDirectories).Length;
        Assert.That(after, Is.EqualTo(before));
    }

    [Test]
    public async Task AddAsync_PageAlreadyFull_IsRejectedBeforeReadingTheContent()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        await using (var db = await _host.CreateDbContextAsync())
        {
            for (var index = 0; index < KnowledgeRules.MaxAttachmentsPerPage; index++)
            {
                db.PageAttachments.Add(new Persistence.PageAttachmentRow
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationId = org.Id,
                    PageId = page.Id,
                    FileName = $"{index}.txt",
                    ContentType = "text/plain",
                    SizeBytes = 1,
                    Sha256 = new string('0', 64),
                    UploadedBy = org.Admin.PersonId,
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
        }

        var unreadable = new UnreadableStream();

        // Act
        var added = await _host.Attachments.AddAsync(
            org.Admin, "HR", page.Number, new NewPageAttachment { FileName = "x.txt", Content = unreadable }, CancellationToken.None);

        // Assert
        Assert.That(added.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentLimitReached));
        Assert.That(unreadable.WasRead, Is.False);
    }

    [Test]
    public async Task AddAsync_ReadOnlyViewerOfAProjectSpace_IsForbiddenAndArchivedIsConflict()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Member();
        var (space, projectId) = await org.CreateProjectSpaceAsync(manager, "WEB");
        var page = await org.PageAsync("WEB", "頁面", actor: manager);
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);

        // Act
        var byViewer = await _host.Attachments.AddAsync(viewer, "WEB", page.Number, Upload("a.txt"), CancellationToken.None);
        var listedByViewer = await _host.Attachments.ListAsync(viewer, "WEB", page.Number, CancellationToken.None);
        await _host.Spaces.SetArchivedAsync(manager, space.Key, true, CancellationToken.None);
        var archived = await _host.Attachments.AddAsync(manager, "WEB", page.Number, Upload("a.txt"), CancellationToken.None);

        // Assert
        Assert.That(byViewer.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(listedByViewer.Succeeded, Is.True);
        Assert.That(archived.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
    }

    [Test]
    public async Task Attachments_OnARestrictedPage_AreNotFoundForPeopleOutsideTheList()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "機密");
        var insider = org.Member();
        var outsider = org.Member();
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("a.txt"), CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [insider.PersonId], CancellationToken.None);

        // Act
        var upload = await _host.Attachments.AddAsync(outsider, "HR", page.Number, Upload("b.txt"), CancellationToken.None);
        var list = await _host.Attachments.ListAsync(outsider, "HR", page.Number, CancellationToken.None);
        var open = await _host.Attachments.OpenAsync(outsider, added.Value.Id, CancellationToken.None);
        var insiderOpen = await _host.Attachments.OpenAsync(insider, added.Value.Id, CancellationToken.None);
        var adminOpen = await _host.Attachments.OpenAsync(org.Admin, added.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(upload.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(list.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(open.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentNotFound));
        Assert.That(insiderOpen.Succeeded && adminOpen.Succeeded, Is.True);
        await insiderOpen.Value.DisposeAsync();
        await adminOpen.Value.DisposeAsync();
    }

    [Test]
    public async Task OpenAsync_PageInTheTrash_IsNotFoundUntilRestored()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("a.txt"), CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Act
        var whileDeleted = await _host.Attachments.OpenAsync(org.Admin, added.Value.Id, CancellationToken.None);
        var listWhileDeleted = await _host.Attachments.ListAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        await _host.Pages.RestoreAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        var afterRestore = await _host.Attachments.OpenAsync(org.Admin, added.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(whileDeleted.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentNotFound));
        Assert.That(listWhileDeleted.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(afterRestore.Succeeded, Is.True);
        await afterRestore.Value.DisposeAsync();
    }

    [Test]
    public async Task OpenAsync_OtherOrganizationsAndGuestsAndUnknownIds_AreNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("a.txt"), CancellationToken.None);
        var otherOrg = new Org(_host);

        // Act
        var crossOrg = await _host.Attachments.OpenAsync(otherOrg.Member(), added.Value.Id, CancellationToken.None);
        var guest = await _host.Attachments.OpenAsync(org.Guest(), added.Value.Id, CancellationToken.None);
        var unknown = await _host.Attachments.OpenAsync(org.Admin, Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.That(
            new[] { crossOrg.Failure?.Code, guest.Failure?.Code, unknown.Failure?.Code },
            Is.All.EqualTo(KnowledgeErrorCodes.AttachmentNotFound));
    }

    [Test]
    public async Task OpenAsync_FileMissingFromTheStore_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        var added = await _host.Attachments.AddAsync(org.Admin, "HR", page.Number, Upload("a.txt"), CancellationToken.None);
        foreach (var file in Directory.GetFiles(_host.AttachmentDirectory, added.Value.Id.ToString("D") + "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }

        // Act
        var opened = await _host.Attachments.OpenAsync(org.Admin, added.Value.Id, CancellationToken.None);

        // Assert
        Assert.That(opened.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentNotFound));
    }

    [Test]
    public async Task AddAsync_UnknownSpaceOrPage_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var noSpace = await _host.Attachments.AddAsync(org.Admin, "NOPE", 1, Upload("a.txt"), CancellationToken.None);
        var noPage = await _host.Attachments.AddAsync(org.Admin, "HR", 99, Upload("a.txt"), CancellationToken.None);

        // Assert
        Assert.That(noSpace.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
        Assert.That(noPage.Failure!.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    private sealed class UnreadableStream : Stream
    {
        public bool WasRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            WasRead = true;
            throw new InvalidOperationException("不該讀內容。");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ZeroStream(long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _position);
            Array.Clear(buffer, offset, take);
            _position += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Test]
    public async Task AddAsync_AsksTheWorkEngineOnlyBeforeWritingAndOnceMoreInsideTheTransaction()
    {
        // Arrange
        var org = new Org(_host);
        var (space, _) = await org.CreateProjectSpaceAsync(org.Admin, "ATT");
        var page = await org.PageAsync(space.Key, "頁面");
        var before = _host.Work.FindVisibleProjectsCalls;

        // Act
        var added = await _host.Attachments.AddAsync(org.Admin, space.Key, page.Number, Upload("a.txt"), CancellationToken.None);

        // Assert：預檢一次、鎖住頁面之後確認一次，不是三次。
        Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
        Assert.That(_host.Work.FindVisibleProjectsCalls - before, Is.EqualTo(2));
    }
}
