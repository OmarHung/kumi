using Microsoft.Extensions.DependencyInjection;
using PmTool.Storage;
using static PmTool.Work.Tests.AttachmentSupport;

namespace PmTool.Work.Tests.Attachments;

/// <summary>項目附件的儲存空間上限：組織的總量與每人過去 24 小時的量。</summary>
[TestFixture]
public sealed class AttachmentQuotaTests
{
    private const long OrganizationLimit = 30;
    private const long PersonDailyLimit = 20;

    private WorkTestHost _host = null!;
    private HookedAttachmentStore _store = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await WorkTestHost.StartAsync(services =>
        {
            services.AddSingleton(new StorageQuotaSettings(OrganizationLimit, PersonDailyLimit));
            services.AddSingleton<IFileStore>(provider =>
                new HookedAttachmentStore(FileStores.Create(provider.GetRequiredService<FileStoreSettings>())));
        });
        _store = (HookedAttachmentStore)_host.Root.GetRequiredService<IFileStore>();
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TearDown]
    public void ResetHooks() => _store.AfterWrite = null;

    [Test]
    public async Task AddAsync_WhenTheOrganizationIsAlreadyFull_IsRefusedWithoutReadingTheContent()
    {
        // Arrange：兩個人各傳 15 個位元組，剛好用滿 30。
        var (scenario, item, first, second) = await FullOrganizationAsync();
        var unreadable = new UnreadableStream();
        var filesBefore = FilesIn(_host).Length;

        // Act
        var added = await _host.Attachments.AddAsync(
            second, item.Key, new NewAttachment { FileName = "c.txt", ContentType = "text/plain", Content = unreadable }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
            Assert.That(added.Failure?.Kind, Is.EqualTo(PmTool.Abstractions.FailureKind.Conflict));
            Assert.That(added.Failure?.Message, Does.Contain("GiB"));
            Assert.That(unreadable.WasRead, Is.False);
            Assert.That(FilesIn(_host), Has.Length.EqualTo(filesBefore));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(scenario.OrganizationId, Is.EqualTo(second.OrganizationId));
        }
    }

    [Test]
    public async Task AddAsync_WhenTheFileMakesTheOrganizationGoOver_IsRefusedAndTheFileIsRemoved()
    {
        // Arrange：已用 15，再傳 16 個位元組會超過 30。
        var (_, item, _, second) = await FullOrganizationAsync(secondBytes: 0);
        var filesBefore = FilesIn(_host).ToHashSet();

        // Act
        var added = await _host.Attachments.AddAsync(second, item.Key, Upload("big.bin", new byte[16]), CancellationToken.None);
        var listed = await _host.Attachments.ListAsync(second, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
            Assert.That(listed.Value, Has.Count.EqualTo(1));
            Assert.That(FilesIn(_host).ToHashSet(), Is.EquivalentTo(filesBefore));
        }
    }

    [Test]
    public async Task AddAsync_AFileThatFillsTheOrganizationExactly_IsAllowed()
    {
        // Arrange
        var (_, item, _, second) = await FullOrganizationAsync(secondBytes: 0);

        // Act
        var added = await _host.Attachments.AddAsync(second, item.Key, Upload("exact.bin", new byte[15]), CancellationToken.None);

        // Assert
        Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
    }

    [Test]
    public async Task AddAsync_WhenAPersonHasUploadedTooMuchInTheLastDay_IsRefusedUntilTheWindowPasses()
    {
        // Arrange：同一個人 20 個位元組已經是每日上限。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var uploader = await scenario.MemberAsync("WEB", "editor");
        var other = await scenario.MemberAsync("WEB", "editor");
        Assert.That((await _host.Attachments.AddAsync(uploader, item.Key, Upload("a.bin", new byte[20]), CancellationToken.None)).Succeeded, Is.True);

        // Act
        var tooMuch = await _host.Attachments.AddAsync(uploader, item.Key, Upload("b.bin", new byte[1]), CancellationToken.None);
        var otherPerson = await _host.Attachments.AddAsync(other, item.Key, Upload("c.bin", new byte[5]), CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        var later = await _host.Attachments.AddAsync(uploader, item.Key, Upload("d.bin", new byte[1]), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tooMuch.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentDailyQuotaExceeded));
            Assert.That(otherPerson.Succeeded, Is.True, () => otherPerson.Failure!.Message);
            Assert.That(later.Succeeded, Is.True, () => later.Failure!.Message);
        }
    }

    [Test]
    public async Task AddAsync_TwoUploadsThatTogetherGoOver_OnlyOneSucceedsEvenWhenBothFilesAreWrittenFirst()
    {
        // Arrange：各 20 個位元組、上限 30；兩邊都寫完檔案之後才一起進交易。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var itemA = await scenario.CreateTaskAsync("WEB", "甲");
        var itemB = await scenario.CreateTaskAsync("WEB", "乙");
        var userA = await scenario.MemberAsync("WEB", "editor");
        var userB = await scenario.MemberAsync("WEB", "editor");
        var written = 0;
        var bothWritten = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.AfterWrite = async () =>
        {
            if (Interlocked.Increment(ref written) == 2)
            {
                bothWritten.SetResult();
            }

            await bothWritten.Task;
        };

        // Act
        var results = await Task.WhenAll(
            _host.Attachments.AddAsync(userA, itemA.Key, Upload("a.bin", new byte[20]), CancellationToken.None),
            _host.Attachments.AddAsync(userB, itemB.Key, Upload("b.bin", new byte[20]), CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
        }
    }

    private async Task<(Scenario Scenario, WorkItem Item, PmTool.Abstractions.Actor First, PmTool.Abstractions.Actor Second)> FullOrganizationAsync(
        int secondBytes = 15)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var first = await scenario.MemberAsync("WEB", "editor");
        var second = await scenario.MemberAsync("WEB", "editor");
        Assert.That((await _host.Attachments.AddAsync(first, item.Key, Upload("a.bin", new byte[15]), CancellationToken.None)).Succeeded, Is.True);
        if (secondBytes > 0)
        {
            Assert.That((await _host.Attachments.AddAsync(second, item.Key, Upload("b.bin", new byte[secondBytes]), CancellationToken.None)).Succeeded, Is.True);
        }

        return (scenario, item, first, second);
    }
}
