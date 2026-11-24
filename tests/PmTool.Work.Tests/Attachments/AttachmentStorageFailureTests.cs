using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Work.Attachments;
using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.Attachments;

/// <summary>儲存空間與資料庫對不上時的處理：交易失敗要清掉檔案，刪不掉檔案不能讓刪除失敗，找不到檔案要留下記錄。</summary>
[TestFixture]
public sealed class AttachmentStorageFailureTests
{
    private readonly CapturedLogs _logs = new();
    private WorkTestHost _host = null!;
    private HookedAttachmentStore _store = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await WorkTestHost.StartAsync(services =>
        {
            services.AddLogging(builder => builder.AddProvider(_logs));
            services.AddSingleton<IAttachmentStore>(provider =>
                new HookedAttachmentStore(AttachmentStores.Create(provider.GetRequiredService<AttachmentSettings>())));
        });
        _store = (HookedAttachmentStore)_host.Root.GetRequiredService<IAttachmentStore>();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TearDown]
    public void ResetHooks()
    {
        _store.AfterWrite = null;
        _store.FailDeletes = false;
    }

    [Test]
    public async Task AddAsync_WhenTheItemIsDeletedAfterTheFileIsWritten_FailsAndRemovesTheFile()
    {
        // Arrange：寫完檔案、提交之前，項目被別人刪了，交易裡重新確認時就失敗。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var filesBefore = FilesIn(_host).ToHashSet();
        _store.AfterWrite = async () =>
        {
            var deleted = await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);
            Assert.That(deleted.Succeeded, Is.True);
        };

        // Act
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        await _host.Items.RestoreAsync(scenario.Admin, item.Key, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
            Assert.That(listed.Value, Is.Empty);
            Assert.That(FilesIn(_host).ToHashSet(), Is.EquivalentTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_WhenTheUploaderIsRemovedFromTheProjectAfterTheFileIsWritten_FailsAndRemovesTheFile()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var filesBefore = FilesIn(_host).ToHashSet();
        _store.AfterWrite = async () =>
        {
            var removed = await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", editor.PersonId, CancellationToken.None);
            Assert.That(removed.Succeeded, Is.True);
        };

        // Act
        var added = await _host.Attachments.AddAsync(editor, item.Key, Upload("a.txt", "x"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Succeeded, Is.False);
            Assert.That(FilesIn(_host).ToHashSet(), Is.EquivalentTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_WhenTheContentStreamBreaksHalfway_FailsWithAnExceptionAndLeavesNoFile()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var filesBefore = FilesIn(_host).ToHashSet();
        var broken = new NewAttachment { FileName = "斷線.bin", Content = new BreakingStream(afterBytes: 100_000) };

        // Act / Assert
        Assert.That(
            async () => await _host.Attachments.AddAsync(scenario.Admin, item.Key, broken, CancellationToken.None),
            Throws.TypeOf<IOException>());
        Assert.That(FilesIn(_host).ToHashSet(), Is.EquivalentTo(filesBefore));
        Assert.That((await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None)).Value, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_WhenTheFileCannotBeRemoved_StillSucceedsAndLogsAWarning()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("a.txt", "x"), CancellationToken.None);
        _store.FailDeletes = true;

        // Act
        var deleted = await _host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        var warnings = _logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains(added.Value.Id.ToString(), StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(listed.Value, Is.Empty);
            Assert.That(warnings, Has.Exactly(1).Items);
            Assert.That(FilesIn(_host).Any(path => path.EndsWith(added.Value.Id.ToString("D"), StringComparison.Ordinal)), Is.True, "檔案留在儲存空間");
        }
    }

    [Test]
    public async Task OpenAsync_WhenTheFileIsMissingFromTheStorage_IsNotFoundAndLogsAnError()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await _host.Attachments.AddAsync(scenario.Admin, item.Key, Upload("遺失.txt", "x"), CancellationToken.None);
        var id = added.Value.Id.ToString("D");
        File.Delete(Path.Combine(_host.AttachmentDirectory, id[^2..], id));

        // Act
        var opened = await _host.Attachments.OpenAsync(scenario.Admin, added.Value.Id, CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        var errors = _logs.Entries.Where(e => e.Level == LogLevel.Error && e.Message.Contains(id, StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentNotFound));
            Assert.That(errors, Has.Exactly(1).Items);
            Assert.That(listed.Value, Has.Count.EqualTo(1), "資料列還在，等著從備份還原檔案");
        }
    }

    /// <summary>讀了一些之後丟例外，像上傳到一半斷線。</summary>
    private sealed class BreakingStream(int afterBytes) : Stream
    {
        private int _read;

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
            if (_read >= afterBytes)
            {
                throw new IOException("測試：連線中斷。");
            }

            var take = Math.Min(count, afterBytes - _read);
            _read += take;
            return take;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
