using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Storage;
using PmTool.Work;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>上傳附件：看內容判斷類型、依類型的上限、檔名、待送數量、容量。對真的資料庫，檔案放在記憶體的儲存空間。</summary>
[TestFixture]
public sealed class AssistantAttachmentUploadTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Upload_Png_StoresTheFileAndReportsItAsAnImage()
    {
        // Arrange
        var content = Image(300);

        // Act
        var attachment = await _h.UploadAsync("  螢幕截圖.png ", content);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(attachment.FileName, Is.EqualTo("螢幕截圖.png"));
            Assert.That(attachment.Kind, Is.EqualTo(AssistantAttachmentKind.Image));
            Assert.That(attachment.ContentType, Is.EqualTo("image/png"));
            Assert.That(attachment.SizeBytes, Is.EqualTo(300));
            Assert.That(_h.Store().Bytes(attachment.Id), Is.EqualTo(content));
        }
    }

    [Test]
    public async Task Upload_TheDeclaredTypeAndTheExtensionAreNotTrusted()
    {
        // Arrange / Act
        var asText = await _h.UploadAsync("looks-like-an-image.png", Text("只是文字"));
        var asImage = await _h.TryUploadAsync("notes.txt", Image(), declaredType: "text/plain");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asText.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
            Assert.That(asText.ContentType, Is.EqualTo("text/plain"));
            Assert.That(asImage.Value.Kind, Is.EqualTo(AssistantAttachmentKind.Image));
            Assert.That(asImage.Value.ContentType, Is.EqualTo("image/png"));
        }
    }

    [Test]
    public async Task Upload_PdfAndSvg_ArePdfAndText()
    {
        // Act
        var pdf = await _h.UploadAsync("規格.pdf", Pdf());
        var svg = await _h.UploadAsync("logo.svg", Text("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pdf.Kind, Is.EqualTo(AssistantAttachmentKind.Pdf));
            Assert.That(svg.Kind, Is.EqualTo(AssistantAttachmentKind.Text));
        }
    }

    [TestCase(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 })]
    [TestCase(new byte[] { })]
    public async Task Upload_UnknownBinaryOrEmpty_IsTypeNotAllowedAndLeavesNoFile(byte[] content)
    {
        // Act
        var result = await _h.TryUploadAsync("報表.docx", content);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTypeNotAllowed);
        Assert.That(_h.Store().Keys, Is.Empty);
        Assert.That(await RowCountAsync(), Is.Zero);
    }

    [Test]
    public async Task Upload_SizeLimitsDependOnTheDetectedKind()
    {
        // Act
        var imageAtLimit = await _h.TryUploadAsync("a.png", Image((int)AssistantAttachmentRules.MaxImageBytes));
        var imageOver = await _h.TryUploadAsync("b.png", Image((int)AssistantAttachmentRules.MaxImageBytes + 1));
        var pdfBetween = await _h.TryUploadAsync("c.pdf", Pdf((int)AssistantAttachmentRules.MaxImageBytes + 1));
        var pdfOver = await _h.TryUploadAsync("d.pdf", Pdf((int)AssistantAttachmentRules.MaxPdfBytes + 1));
        var textOver = await _h.TryUploadAsync("e.txt", Text(new string('a', (int)AssistantAttachmentRules.MaxTextBytes + 1)));
        var textAtLimit = await _h.TryUploadAsync("f.txt", Text(new string('a', (int)AssistantAttachmentRules.MaxTextBytes)));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(imageAtLimit.Succeeded, Is.True);
            AssertFailure(imageOver.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
            Assert.That(pdfBetween.Succeeded, Is.True);
            AssertFailure(pdfOver.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
            AssertFailure(textOver.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
            Assert.That(textAtLimit.Succeeded, Is.True);
            Assert.That(_h.Store().Keys, Has.Count.EqualTo(3), "太大的不留檔案");
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("a/b.png")]
    [TestCase("a\\b.png")]
    [TestCase("..")]
    [TestCase("tab\tname.png")]
    [TestCase("nul\0.png")]
    [TestCase("zero\u200Bwidth.png")]
    [TestCase("line\u2028break.png")]
    public async Task Upload_InvalidFileName_IsRejectedBeforeAnythingIsWritten(string fileName)
    {
        // Act
        var result = await _h.TryUploadAsync(fileName, Image());

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Details.Single().Path, Is.EqualTo("fileName"));
            Assert.That(_h.Store().Keys, Is.Empty);
        }
    }

    [Test]
    public async Task Upload_FileNameWithALoneSurrogate_IsRejectedBecauseItCannotBeStored()
    {
        // Act
        var result = await _h.TryUploadAsync("lone" + '\uD800' + ".png", Image());

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid);
    }

    [Test]
    public async Task Upload_FileNameOf255CharactersIsFineAnd256IsNot()
    {
        // Act
        var fine = await _h.TryUploadAsync(new string('名', 255), Image());
        var tooLong = await _h.TryUploadAsync(new string('名', 256), Image());

        // Assert
        Assert.That(fine.Succeeded, Is.True);
        AssertFailure(tooLong.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid);
    }

    [Test]
    public async Task Upload_ProjectTheyCannotSee_IsRefusedWithoutReadingTheContent()
    {
        // Arrange
        _h.Projects.OnGet = (_, key) => WorkSamples.Failure(FailureKind.NotFound, "project.not_found", $"找不到專案 {key}。");
        await using var stream = new MemoryStream(Image());

        // Act
        var result = await _h.Attachments().UploadAsync(
            _h.Actor, "WEB", new NewAssistantAttachment { FileName = "a.png", Content = stream }, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, "project.not_found");
        Assert.That(stream.Position, Is.Zero);
    }

    [Test]
    public async Task Upload_WithoutPermissionToUseTheAssistant_IsForbidden()
    {
        // Arrange
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.ItemRead]);

        // Act
        var result = await _h.TryUploadAsync("a.png", Image());

        // Assert
        AssertFailure(result.Failure, FailureKind.Forbidden, AiErrorCodes.AssistantForbidden);
    }

    [Test]
    public async Task Upload_WhenAiIsTurnedOff_IsUnavailable()
    {
        // Arrange
        await _h.Host.Get<IAiSettingsService>().SaveAsync(
            _h.Admin, new AiSettingsInput(false, null), CancellationToken.None);

        // Act
        var result = await _h.TryUploadAsync("a.png", Image());

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.Unavailable);
    }

    [Test]
    public async Task Upload_MoreThanTheAllowedPendingAttachments_IsRefusedUntilSomeAreSentOrExpire()
    {
        // Arrange
        for (var i = 0; i < AssistantAttachmentRules.MaxPendingPerPerson; i++)
        {
            await _h.UploadAsync($"{i}.png", Image());
        }

        // Act
        var over = await _h.TryUploadAsync("one-more.png", Image());
        var someoneElse = await _h.TryUploadAsync("mine.png", Image(), _h.Admin);
        _h.Clock.Advance(AssistantAttachmentRules.PendingLifetime + TimeSpan.FromMinutes(1));
        var afterExpiry = await _h.TryUploadAsync("fresh.png", Image());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(over.Failure, FailureKind.Conflict, AiErrorCodes.TooManyAttachments);
            Assert.That(someoneElse.Succeeded, Is.True, "別人的待送不算在我頭上");
            Assert.That(afterExpiry.Succeeded, Is.True, "過期還沒被清掉的不算");
        }
    }

    [Test]
    public async Task Upload_ManyAtOnce_NeverExceedsThePendingLimit()
    {
        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => _h.TryUploadAsync($"{i}.png", Image())));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(AssistantAttachmentRules.MaxPendingPerPerson));
            Assert.That(await RowCountAsync(), Is.EqualTo(AssistantAttachmentRules.MaxPendingPerPerson));
            Assert.That(_h.Store().Keys, Has.Count.EqualTo(AssistantAttachmentRules.MaxPendingPerPerson), "被擋下的不留檔案");
        }
    }

    [Test]
    public async Task Upload_StorageFailureInTheMiddle_LeavesNoFileBehind()
    {
        // Arrange：寫檔成功、記下之前時鐘壞掉，模擬交易失敗。
        await using var stream = new MemoryStream(Image());
        _h.Clock.FailNextRead();

        // Act / Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await _h.Attachments().UploadAsync(
            _h.Actor, "WEB", new NewAssistantAttachment { FileName = "a.png", Content = stream }, CancellationToken.None));
        Assert.That(_h.Store().Keys, Is.Empty);
        Assert.That(await RowCountAsync(), Is.Zero);
    }

    [Test]
    public async Task Delete_Pending_RemovesTheRowAndTheFile()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());

        // Act
        var deleted = await _h.Attachments().DeletePendingAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);
        var again = await _h.Attachments().DeletePendingAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(_h.Store().Contains(attachment.Id), Is.False);
            Assert.That(await RowCountAsync(), Is.Zero);
            AssertFailure(again.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Delete_SomeoneElsesOrAlreadySentAttachment_IsNotFoundAndChangesNothing()
    {
        // Arrange
        var mine = await _h.UploadAsync("mine.png", Image());
        var sent = await _h.UploadAsync("sent.png", Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "看這個", sent);

        // Act
        var byOther = await _h.Attachments().DeletePendingAsync(_h.Admin, "WEB", mine.Id, CancellationToken.None);
        var bound = await _h.Attachments().DeletePendingAsync(_h.Actor, "WEB", sent.Id, CancellationToken.None);
        var unknown = await _h.Attachments().DeletePendingAsync(_h.Actor, "WEB", Guid.NewGuid(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(byOther.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(bound.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(unknown.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            Assert.That(_h.Store().Contains(mine.Id), Is.True);
            Assert.That(_h.Store().Contains(sent.Id), Is.True);
        }
    }

    [Test]
    public async Task Delete_WhenTheFileCannotBeRemoved_StillSucceedsAndTheFileIsLeftForTheSweep()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());
        _h.Store().FailDeletes = true;

        // Act
        var deleted = await _h.Attachments().DeletePendingAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True);
        Assert.That(_h.Store().Contains(attachment.Id), Is.True);
    }

    [Test]
    public async Task Open_PendingAttachment_ReadsBackTheBytesForItsOwnerOnly()
    {
        // Arrange
        var content = Image(500);
        var attachment = await _h.UploadAsync("a.png", content);

        // Act
        await using var opened = (await _h.Attachments().OpenAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None)).Value;
        using var buffer = new MemoryStream();
        await opened.Content.CopyToAsync(buffer);
        var byOther = await _h.Attachments().OpenAsync(_h.Admin, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer.ToArray(), Is.EqualTo(content));
            Assert.That(opened.Sha256, Is.EqualTo(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content))));
            Assert.That(opened.Attachment.Id, Is.EqualTo(attachment.Id));
            AssertFailure(byOther.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Open_WhenTheFileIsGone_IsNotFound()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());
        _h.Store().Remove(attachment.Id);

        // Act
        var result = await _h.Attachments().OpenAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Open_AfterTheConversationIsDeleted_IsNotFound()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "看這個", attachment);
        Assert.That((await _h.Attachments().OpenAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None)).Succeeded, Is.True);

        // Act
        await _h.Service.DeleteAsync(_h.Actor, "WEB", conversation.Id, CancellationToken.None);
        var result = await _h.Attachments().OpenAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Open_AfterLosingTheRightToSeeAllItems_IsNotFoundLikeTheConversation()
    {
        // Arrange
        var attachment = await _h.UploadAsync("a.png", Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "看這個", attachment);
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.BlueprintRead, WorkPermissions.ItemReadOwn]);

        // Act
        var result = await _h.Attachments().OpenAsync(_h.Actor, "WEB", attachment.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Usage_CountsEveryRowIncludingPendingOnes_AndAppearsInTheQuotaReport()
    {
        // Arrange
        await _h.UploadAsync("a.png", Image(1000));
        await _h.UploadAsync("b.txt", Text(new string('x', 500)));
        var quota = _h.Host.Get<IStorageQuota>();

        // Act
        var report = await quota.ReportAsync(_h.Admin, CancellationToken.None);
        var recent = await _h.Host.Get<IEnumerable<IStorageUsageSource>>().Single(s => s.Area == StorageArea.AssistantAttachments)
            .UploadedBytesSinceAsync(_h.OrganizationId, _h.Actor.PersonId, _h.Clock.GetUtcNow().AddHours(-1), CancellationToken.None);

        // Assert
        var area = report.Value.Areas.Single(a => a.Area == StorageArea.AssistantAttachments);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(area.Bytes, Is.EqualTo(1500));
            Assert.That(area.Files, Is.EqualTo(2));
            Assert.That(recent, Is.EqualTo(1500));
        }
    }

    private async Task<int> RowCountAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.Attachments.CountAsync();
    }
}

/// <summary>容量：組織的總量與每人每日的量，和項目、知識庫的附件共用同一套規則。</summary>
[TestFixture]
public sealed class AssistantAttachmentQuotaTests
{
    private static async Task<AssistantHarness> StartAsync(long organizationBytes, long personDailyBytes) =>
        await AssistantHarness.StartAsync(configure: services =>
            services.AddSingleton(new StorageQuotaSettings(organizationBytes, personDailyBytes)));

    [Test]
    public async Task Upload_BeyondTheOrganizationQuota_IsConflictAndLeavesNoFile()
    {
        // Arrange
        await using var h = await StartAsync(organizationBytes: 1000, personDailyBytes: 0);
        await h.UploadAsync("a.png", Image(600));

        // Act
        var result = await h.TryUploadAsync("b.png", Image(600));

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.AttachmentQuotaExceeded);
        Assert.That(h.Store().Keys, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Upload_BeyondThePersonsDailyQuota_UsesItsOwnCode()
    {
        // Arrange
        await using var h = await StartAsync(organizationBytes: 0, personDailyBytes: 1000);
        await h.UploadAsync("a.png", Image(600));

        // Act
        var mine = await h.TryUploadAsync("b.png", Image(600));
        var theirs = await h.TryUploadAsync("c.png", Image(600), h.Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(mine.Failure, FailureKind.Conflict, AiErrorCodes.AttachmentDailyQuotaExceeded);
            Assert.That(theirs.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Upload_WhenTheQuotaIsAlreadyFull_IsRefusedBeforeReadingTheContent()
    {
        // Arrange
        await using var h = await StartAsync(organizationBytes: 500, personDailyBytes: 0);
        await h.UploadAsync("a.png", Image(500));
        await using var stream = new MemoryStream(Image(100));

        // Act
        var result = await h.Attachments().UploadAsync(
            h.Actor, "WEB", new NewAssistantAttachment { FileName = "b.png", Content = stream }, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.AttachmentQuotaExceeded);
        Assert.That(stream.Position, Is.Zero);
    }

    [Test]
    public async Task Upload_ManyAtOnce_NeverExceedsTheOrganizationQuota()
    {
        // Arrange：每個 400 位元組、上限 1000：只放得下兩個。
        await using var h = await StartAsync(organizationBytes: 1000, personDailyBytes: 0);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => h.TryUploadAsync($"{i}.png", Image(400), i % 2 == 0 ? h.Actor : h.Admin)));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(2));
    }
}
