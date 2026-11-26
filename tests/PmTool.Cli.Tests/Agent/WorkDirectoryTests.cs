using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>每件工作的暫存資料夾：刪不掉時重試一次、啟動時掃掉殘留的、下載斷線與磁碟寫不進分開報。</summary>
[TestFixture]
public sealed class WorkDirectoryTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("kumi-workdir-test-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    private DirectoryInfo MakeStale(string name, TimeSpan age, bool privateMode = true)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name));
        File.WriteAllText(Path.Combine(directory.FullName, "attachment-1.png"), "x");
        if (!OperatingSystem.IsWindows())
        {
            directory.UnixFileMode = privateMode
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        }

        directory.LastWriteTimeUtc = DateTime.UtcNow - age;
        return directory;
    }

    [Test]
    public async Task DeleteQuietly_RetriesOnceAfterWaitingWhenTheFirstDeleteFails()
    {
        // Arrange
        var directory = new DirectoryInfo(Path.Combine(_root, "kumi-agent-a"));
        var attempts = 0;
        var waits = new List<TimeSpan>();

        // Act
        await WorkDirectories.DeleteQuietlyAsync(directory, TimeSpan.FromMilliseconds(5), (span, _) => { waits.Add(span); return Task.CompletedTask; },
            _ => { if (++attempts == 1) { throw new IOException("locked"); } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(5) }));
        }
    }

    [Test]
    public async Task DeleteQuietly_GivesUpWithoutThrowingAfterTheSecondFailure()
    {
        // Arrange
        var attempts = 0;

        // Act
        await WorkDirectories.DeleteQuietlyAsync(new DirectoryInfo(_root), TimeSpan.Zero, (_, _) => Task.CompletedTask,
            _ => { attempts++; throw new UnauthorizedAccessException("denied"); });

        // Assert
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void SweepStale_RemovesOnlyOldKumiAgentDirectoriesOwnedByUs()
    {
        // Arrange
        var old = MakeStale("kumi-agent-old", TimeSpan.FromHours(7));
        var fresh = MakeStale("kumi-agent-fresh", TimeSpan.FromHours(1));
        var other = MakeStale("other-old", TimeSpan.FromHours(7));
        var oldFile = Path.Combine(_root, "kumi-agent-file");
        File.WriteAllText(oldFile, "x");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromHours(7));

        // Act
        var removed = WorkDirectories.SweepStale(_root, DateTime.UtcNow, WorkDirectories.StaleAfter);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(old.FullName), Is.False);
            Assert.That(Directory.Exists(fresh.FullName), Is.True);
            Assert.That(Directory.Exists(other.FullName), Is.True);
            Assert.That(File.Exists(oldFile), Is.True);
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public void SweepStale_LeavesADirectoryAnotherUserCouldHaveMade()
    {
        // Arrange: 我們建的一律是 0700；不是這個權限的不是我們的。
        var open = MakeStale("kumi-agent-open", TimeSpan.FromHours(7), privateMode: false);

        // Act
        var removed = WorkDirectories.SweepStale(_root, DateTime.UtcNow, WorkDirectories.StaleAfter);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.Zero);
            Assert.That(Directory.Exists(open.FullName), Is.True);
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public void SweepStale_DoesNotFollowOrRemoveASymbolicLink()
    {
        // Arrange
        var target = Directory.CreateDirectory(Path.Combine(_root, "precious"));
        File.WriteAllText(Path.Combine(target.FullName, "keep.txt"), "x");
        var link = Path.Combine(_root, "kumi-agent-link");
        Directory.CreateSymbolicLink(link, target.FullName);
        Directory.SetLastWriteTimeUtc(link, DateTime.UtcNow - TimeSpan.FromHours(7));

        // Act
        var removed = WorkDirectories.SweepStale(_root, DateTime.UtcNow, WorkDirectories.StaleAfter);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(removed, Is.Zero);
            Assert.That(File.Exists(Path.Combine(target.FullName, "keep.txt")), Is.True);
        }
    }

    [Test]
    public void SweepStale_OnAMissingRootDoesNothing() =>
        Assert.That(WorkDirectories.SweepStale(Path.Combine(_root, "nope"), DateTime.UtcNow, WorkDirectories.StaleAfter), Is.Zero);

    // ---- 下載 ----

    private static (AgentJob Job, AgentAttachment Attachment) JobWithImage()
    {
        var (image, _) = TestAttachments.Image(size: 1000);
        return (TestAttachments.With(TestJobs.Make(), image), image);
    }

    [Test]
    public void Stage_WhenTheConnectionDropsMidDownloadSaysSoInsteadOfBlamingTheDisk()
    {
        // Arrange
        var (job, _) = JobWithImage();
        var source = new StreamAttachmentSource(_ => new BrokenStream(new byte[100], hang: false));

        // Act
        var failure = Assert.ThrowsAsync<AttachmentStagingException>(() => AttachmentStaging.StageAsync(job, _root, source, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Message, Does.Contain("連線斷了"));
            Assert.That(failure.Message, Does.Not.Contain("暫存資料夾"));
        }
    }

    [Test]
    public void Stage_WhenNoDataArrivesWithinTheIdleTimeoutFailsWithATimeoutMessage()
    {
        // Arrange
        var (job, _) = JobWithImage();
        var source = new StreamAttachmentSource(_ => new BrokenStream(new byte[100], hang: true));

        // Act
        var failure = Assert.ThrowsAsync<AttachmentStagingException>(
            () => AttachmentStaging.StageAsync(job, _root, source, CancellationToken.None, TimeSpan.FromMilliseconds(150)));

        // Assert
        Assert.That(failure.Message, Does.Contain("沒有收到資料").And.Not.Contain("暫存資料夾"));
    }

    [Test]
    public void Stage_WhenTheFileCannotBeWrittenBlamesTheTempDirectory()
    {
        // Arrange: 同名的檔案已經在，CreateNew 會失敗。
        var (job, attachment) = JobWithImage();
        File.WriteAllText(Path.Combine(_root, attachment.LocalFileName(1)), "x");
        var source = new StreamAttachmentSource(_ => new MemoryStream(new byte[1000]));

        // Act
        var failure = Assert.ThrowsAsync<AttachmentStagingException>(() => AttachmentStaging.StageAsync(job, _root, source, CancellationToken.None));

        // Assert
        Assert.That(failure.Message, Does.Contain("寫不進暫存資料夾"));
    }

    [Test]
    public void Stage_WhenCancelledWhileWaitingForDataPropagatesTheCancellation()
    {
        // Arrange
        var (job, _) = JobWithImage();
        var source = new StreamAttachmentSource(_ => new BrokenStream(new byte[100], hang: true));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act / Assert
        Assert.ThrowsAsync<TaskCanceledException>(() => AttachmentStaging.StageAsync(job, _root, source, cancel.Token));
    }
}
