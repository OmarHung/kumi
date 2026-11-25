using Microsoft.Extensions.DependencyInjection;
using PmTool.Storage;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>歷史匯入的附件：算組織的總量，不算每人每日（上傳的人是原本的人，匯入的人不是上傳者）。</summary>
[TestFixture]
public sealed class HistoryAttachmentQuotaTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() =>
        _host = await WorkTestHost.StartAsync(services => services.AddSingleton(new StorageQuotaSettings(OrganizationBytes: 30, PersonBytesPerDay: 5)));

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportAttachmentAsync_LargerThanThePersonDailyLimit_IsNotCountedAgainstAnyPerson()
    {
        // Arrange：每人每日上限 5，匯入的檔案 12 個位元組、時間在過去。
        var (_, manager, member) = await ImportedItemAsync();

        // Act
        var first = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, content: "123456789012"), CancellationToken.None);
        var second = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-2", member.PersonId, content: "123456789012"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Succeeded, Is.True, () => first.Failure!.Message);
            Assert.That(second.Succeeded, Is.True, () => second.Failure!.Message);
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_WhenItWouldGoOverTheOrganizationLimit_IsRefusedAndTheFileIsRemoved()
    {
        // Arrange：已經匯入 24 個位元組，再 12 個會超過 30。
        var (_, manager, member) = await ImportedItemAsync();
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, content: "123456789012"), CancellationToken.None);
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-2", member.PersonId, content: "123456789012"), CancellationToken.None);
        var filesBefore = AttachmentSupport.FilesIn(_host).Length;

        // Act
        var over = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-3", member.PersonId, content: "123456789012"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
            Assert.That(AttachmentSupport.FilesIn(_host), Has.Length.EqualTo(filesBefore));
        }
    }

    [Test]
    public async Task ImportAttachmentAsync_WhenTheOrganizationIsAlreadyFull_IsRefusedWithoutReadingTheContent()
    {
        // Arrange
        var (_, manager, member) = await ImportedItemAsync();
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, content: "123456789012345678901234567890"), CancellationToken.None);
        var unreadable = new AttachmentSupport.UnreadableStream();

        // Act
        var result = await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-2", member.PersonId, stream: unreadable), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
            Assert.That(unreadable.WasRead, Is.False);
        }
    }

    [Test]
    public async Task AddAsync_AfterSomeoneElseImportedAttachmentsInTheirName_StillHasTheFullDailyAllowance()
    {
        // Arrange：匯入的附件記的是原本的人，時間是現在；他自己一個位元組也沒傳。
        var (_, manager, member) = await ImportedItemAsync();
        var now = _host.Clock.GetUtcNow();
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, content: "123456789012", at: now), CancellationToken.None);

        // Act
        var own = await _host.Attachments.AddAsync(
            member, "WEB-1", new NewAttachment { FileName = "own.txt", ContentType = "text/plain", Content = new MemoryStream(new byte[5]) }, CancellationToken.None);

        // Assert
        Assert.That(own.Succeeded, Is.True, () => own.Failure!.Message);
    }

    [Test]
    public async Task AddAsync_ImportedAttachmentsStillCountTowardTheOrganizationTotal()
    {
        // Arrange：匯入 24 個位元組，組織上限 30。
        var (setup, manager, member) = await ImportedItemAsync();
        var now = _host.Clock.GetUtcNow();
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-1", member.PersonId, content: "123456789012", at: now), CancellationToken.None);
        await _host.History.ImportAttachmentAsync(manager, "WEB-1", Attachment("a-2", member.PersonId, content: "123456789012", at: now), CancellationToken.None);
        var other = await setup.MemberAsync("WEB", "editor");

        // Act
        var over = await _host.Attachments.AddAsync(
            other, "WEB-1", new NewAttachment { FileName = "x.txt", ContentType = "text/plain", Content = new MemoryStream(new byte[7]) }, CancellationToken.None);

        // Assert
        Assert.That(over.Failure?.Code, Is.EqualTo(WorkErrorCodes.AttachmentQuotaExceeded));
    }

    private async Task<(Scenario Scenario, PmTool.Abstractions.Actor Manager, PmTool.Abstractions.Actor Member)> ImportedItemAsync()
    {
        var setup = await ProjectWithMembersAsync(_host);
        var result = await _host.History.ImportItemsAsync(setup.Manager, "WEB", Batch(Row("m-1", setup.Member.PersonId)), CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return setup;
    }
}
