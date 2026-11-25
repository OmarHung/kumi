using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Storage;

namespace PmTool.Knowledge.Tests.Attachments;

/// <summary>頁面附件的儲存空間上限：組織的總量與每人過去 24 小時的量。</summary>
[TestFixture]
public sealed class AttachmentQuotaTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() =>
        _host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new StorageQuotaSettings(30, 20)));

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private static NewPageAttachment Upload(string fileName, int bytes) =>
        new() { FileName = fileName, ContentType = "application/octet-stream", Content = new MemoryStream(new byte[bytes]) };

    private int StoredFileCount() => Directory.Exists(_host.AttachmentDirectory)
        ? Directory.EnumerateFiles(_host.AttachmentDirectory, "*", SearchOption.AllDirectories).Count()
        : 0;

    private async Task<(Org Org, PageDetail Page, Actor First, Actor Second)> TwoPeopleAsync()
    {
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁面");
        return (org, page, org.Member(), org.Member());
    }

    [Test]
    public async Task AddAsync_WhenTheOrganizationIsAlreadyFull_IsRefusedWithoutReadingTheContent()
    {
        // Arrange：15 + 15 = 30，剛好用滿。
        var (_, page, first, second) = await TwoPeopleAsync();
        await _host.Attachments.AddAsync(first, "HR", page.Number, Upload("a.bin", 15), CancellationToken.None);
        await _host.Attachments.AddAsync(second, "HR", page.Number, Upload("b.bin", 15), CancellationToken.None);
        var unreadable = new UnreadableStream();
        var filesBefore = StoredFileCount();

        // Act
        var added = await _host.Attachments.AddAsync(
            second, "HR", page.Number, new NewPageAttachment { FileName = "c.bin", ContentType = null, Content = unreadable }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentQuotaExceeded));
            Assert.That(added.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(added.Failure?.Message, Does.Contain("GiB"));
            Assert.That(unreadable.WasRead, Is.False);
            Assert.That(StoredFileCount(), Is.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_WhenTheFileMakesTheOrganizationGoOver_IsRefusedAndTheFileIsRemoved()
    {
        // Arrange
        var (_, page, first, second) = await TwoPeopleAsync();
        await _host.Attachments.AddAsync(first, "HR", page.Number, Upload("a.bin", 15), CancellationToken.None);
        var filesBefore = StoredFileCount();

        // Act
        var over = await _host.Attachments.AddAsync(second, "HR", page.Number, Upload("b.bin", 16), CancellationToken.None);
        var exact = await _host.Attachments.AddAsync(second, "HR", page.Number, Upload("c.bin", 15), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentQuotaExceeded));
            Assert.That(exact.Succeeded, Is.True, () => exact.Failure!.Message);
            Assert.That(StoredFileCount(), Is.EqualTo(filesBefore + 1));
        }
    }

    [Test]
    public async Task AddAsync_WhenAPersonHasUploadedTooMuchInTheLastDay_IsRefusedUntilTheWindowPasses()
    {
        // Arrange
        var (_, page, first, second) = await TwoPeopleAsync();
        await _host.Attachments.AddAsync(first, "HR", page.Number, Upload("a.bin", 20), CancellationToken.None);

        // Act
        var tooMuch = await _host.Attachments.AddAsync(first, "HR", page.Number, Upload("b.bin", 1), CancellationToken.None);
        var otherPerson = await _host.Attachments.AddAsync(second, "HR", page.Number, Upload("c.bin", 5), CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        var later = await _host.Attachments.AddAsync(first, "HR", page.Number, Upload("d.bin", 1), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tooMuch.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.AttachmentDailyQuotaExceeded));
            Assert.That(otherPerson.Succeeded, Is.True, () => otherPerson.Failure!.Message);
            Assert.That(later.Succeeded, Is.True, () => later.Failure!.Message);
        }
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
}
