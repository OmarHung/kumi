using PmTool.Work.Attachments;

namespace PmTool.Work.Tests.Attachments;

/// <summary>主機資料夾的儲存：分一層子資料夾、先寫暫存檔再 rename、權限 600、鍵一定是編號。</summary>
[TestFixture]
public sealed class LocalAttachmentStoreTests
{
    private string _directory = null!;
    private LocalAttachmentStore _store = null!;

    [SetUp]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "pmtool-local-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new LocalAttachmentStore(_directory);
    }

    [TearDown]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            // 有的測試把資料夾改成唯讀，先放開才刪得掉。
            SetMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task WriteAsync_StoresTheContentAndReportsSizeAndSha256()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");
        var bytes = "你好，附件"u8.ToArray();

        // Act
        var stored = await _store.WriteAsync(key, new MemoryStream(bytes), "text/plain", 1024, CancellationToken.None);
        await using var read = await _store.OpenReadAsync(key, CancellationToken.None);
        using var copy = new MemoryStream();
        await read!.CopyToAsync(copy);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Is.EqualTo(new StoredFile(bytes.Length, AttachmentSupport.Sha256Of(bytes))));
            Assert.That(copy.ToArray(), Is.EqualTo(bytes));
        }
    }

    [Test]
    public async Task WriteAsync_PutsTheFileInASubfolderNamedByTheLastTwoCharactersAndLeavesNoTemporaryFile()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        await _store.WriteAsync(key, new MemoryStream([1, 2, 3]), "application/octet-stream", 1024, CancellationToken.None);

        // Assert
        var files = Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).ToArray();
        Assert.That(files, Is.EqualTo(new[] { Path.Combine(_directory, key[^2..], key) }));
    }

    [Test]
    public async Task WriteAsync_CreatesTheFileReadableOnlyByTheOwner()
    {
        // Arrange
        Assume.That(OperatingSystem.IsWindows(), Is.False);
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        await _store.WriteAsync(key, new MemoryStream([1]), "application/octet-stream", 1024, CancellationToken.None);

        // Assert
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(File.GetUnixFileMode(Path.Combine(_directory, key[^2..], key)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
    }

    [Test]
    public async Task WriteAsync_OverTheLimit_ReturnsNullAndLeavesNothingBehind()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");

        // Act
        var stored = await _store.WriteAsync(key, new AttachmentSupport.ZeroStream(2_000_000), "application/zip", 1_000_000, CancellationToken.None);
        var read = await _store.OpenReadAsync(key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored, Is.Null);
            Assert.That(read, Is.Null);
            Assert.That(Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories), Is.Empty);
        }
    }

    [Test]
    public async Task OpenReadAsync_AMissingFile_IsNull()
    {
        // Act
        var read = await _store.OpenReadAsync(Guid.CreateVersion7().ToString("D"), CancellationToken.None);

        // Assert
        Assert.That(read, Is.Null);
    }

    [Test]
    public async Task DeleteAsync_RemovesTheFileAndAMissingFileIsNotAnError()
    {
        // Arrange
        var key = Guid.CreateVersion7().ToString("D");
        await _store.WriteAsync(key, new MemoryStream([1]), "application/octet-stream", 1024, CancellationToken.None);

        // Act
        await _store.DeleteAsync(key, CancellationToken.None);
        await _store.DeleteAsync(key, CancellationToken.None);
        var read = await _store.OpenReadAsync(key, CancellationToken.None);

        // Assert
        Assert.That(read, Is.Null);
    }

    [Test]
    public void DeleteAsync_WhenTheSubfolderWasNeverCreated_IsNotAnError()
    {
        // Act / Assert：全新的資料夾裡什麼都沒有，連子資料夾都不存在。
        Assert.That(async () => await _store.DeleteAsync(Guid.CreateVersion7().ToString("D"), CancellationToken.None), Throws.Nothing);
    }

    [TestCase("../escape")]
    [TestCase("..")]
    [TestCase("/etc/passwd")]
    [TestCase("not-a-guid")]
    [TestCase("")]
    public void Keys_ThatAreNotIds_AreRefusedSoThePathCannotEscape(string key)
    {
        // Act / Assert
        Assert.That(
            async () => await _store.WriteAsync(key, new MemoryStream([1]), "application/octet-stream", 1024, CancellationToken.None),
            Throws.ArgumentException);
        Assert.That(async () => await _store.OpenReadAsync(key, CancellationToken.None), Throws.ArgumentException);
        Assert.That(async () => await _store.DeleteAsync(key, CancellationToken.None), Throws.ArgumentException);
    }

    [Test]
    public async Task VerifyAsync_AWritableDirectory_PassesAndLeavesNoProbeFileBehind()
    {
        // Act
        await _store.VerifyAsync(CancellationToken.None);

        // Assert
        Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Empty);
    }

    [Test]
    public void VerifyAsync_AMissingDirectory_FailsAndNamesTheSetting()
    {
        // Arrange
        var store = new LocalAttachmentStore(Path.Combine(_directory, "不存在"));

        // Act / Assert
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => store.VerifyAsync(CancellationToken.None));
        Assert.That(failure.Message, Does.Contain("Attachments:Directory").And.Contain("不存在"));
    }

    [Test]
    public void VerifyAsync_AReadOnlyDirectory_FailsAndNamesTheSetting()
    {
        // Arrange：本機測試用的帳號不是 root，唯讀才有意義。
        Assume.That(OperatingSystem.IsWindows(), Is.False);
        Assume.That(Environment.UserName, Is.Not.EqualTo("root"));
        SetMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        // Act / Assert
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => _store.VerifyAsync(CancellationToken.None));
        Assert.That(failure.Message, Does.Contain("Attachments:Directory").And.Contain("寫不進去"));
    }

    private static void SetMode(string path, UnixFileMode mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, mode);
        }
    }
}
