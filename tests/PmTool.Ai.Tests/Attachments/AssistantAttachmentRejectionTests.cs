using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 供應商不收歷史裡的圖片或 PDF（加密、太多頁、損毀、尺寸太大）：不能讓整則對話永遠送不出去。
/// 請求被判「不合法」而且帶了歷史的圖片或 PDF 時，拿掉重試一次；重試成功就記下，之後直接用說明。
/// 這一則自己的附件被拒時照舊失敗、顯示原因，使用者可以換檔案。
/// </summary>
[TestFixture]
public sealed class AssistantAttachmentRejectionTests
{
    private const string SkippedNote = "〔附件：壞掉.pdf（之前附的，模型讀不了，這次沒有再送）〕";

    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static Turn Rejected(HttpStatusCode status = HttpStatusCode.BadRequest) =>
        Turn.Create().Throw(new HttpRequestException("這份 PDF 讀不了", null, status));

    private async Task<(Guid ConversationId, AssistantAttachment Pdf)> ConversationWithBadPdfAsync()
    {
        var pdf = await _h.UploadAsync("壞掉.pdf", Pdf());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("收到");
        await _h.SendWithAsync(conversation.Id, "先看這份", pdf);
        return (conversation.Id, pdf);
    }

    private static bool HasBinary(ModelCall call) => call.Messages.Any(m => m.Contents.OfType<DataContent>().Any());

    private static IEnumerable<string> TextsOf(ModelCall call) => call.Messages.SelectMany(m => m.Contents.OfType<TextContent>()).Select(t => t.Text);

    private async Task<DateTimeOffset?> SkippedAtAsync(Guid attachmentId)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return (await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId)).ReplaySkippedAt;
    }

    private async Task<int> CountedUsageAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.ConnectionUsage.CountAsync(u => u.State != "released");
    }

    [Test]
    public async Task Reply_ProviderRejectsTheHistoryPdf_RetriesWithoutItAndRemembersTheSkip()
    {
        // Arrange
        var (conversationId, pdf) = await ConversationWithBadPdfAsync();
        _h.Script.Enqueue(Rejected()).Reply("好，不看那份了");

        // Act
        var second = await _h.SendAsync(conversationId, "繼續");
        var calls = _h.Script.Calls;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(second.Text, Is.EqualTo("好，不看那份了"));
            Assert.That(calls, Has.Count.EqualTo(3), "第一輪 1 次、這一輪被拒 1 次加重試 1 次");
            Assert.That(HasBinary(calls[1]), Is.True, "第一次照舊帶歷史的 PDF");
            Assert.That(HasBinary(calls[2]), Is.False, "重試把歷史的圖片與 PDF 換成說明");
            Assert.That(TextsOf(calls[2]), Does.Contain(SkippedNote));
            Assert.That(await SkippedAtAsync(pdf.Id), Is.Not.Null, "重試成功之後記下來");
        }
    }

    [Test]
    public async Task Reply_AfterTheSkipWasRecorded_LaterTurnsUseTheNoteDirectlyWithoutAnotherRejection()
    {
        // Arrange
        var (conversationId, _) = await ConversationWithBadPdfAsync();
        _h.Script.Enqueue(Rejected()).Reply("第二輪");
        await _h.SendAsync(conversationId, "繼續");
        _h.Script.Reply("第三輪");

        // Act
        var third = await _h.SendAsync(conversationId, "再繼續");
        var last = _h.Script.Calls[^1];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(third.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(4), "第三輪只呼叫一次");
            Assert.That(HasBinary(last), Is.False);
            Assert.That(TextsOf(last), Does.Contain(SkippedNote));
        }
    }

    [Test]
    public async Task Reply_Retry_CountsUsageByTheCallsActuallyMade()
    {
        // Arrange
        var (conversationId, _) = await ConversationWithBadPdfAsync();
        var before = await CountedUsageAsync();
        _h.Script.Enqueue(Rejected()).Reply("好");

        // Act
        await _h.SendAsync(conversationId, "繼續");

        // Assert
        Assert.That(await CountedUsageAsync() - before, Is.EqualTo(2), "被拒的一次與重試的一次");
    }

    [Test]
    public async Task Reply_TheCurrentMessagesOwnAttachmentIsRejected_FailsWithTheReasonAndDoesNotRetry()
    {
        // Arrange
        var pdf = await _h.UploadAsync("加密.pdf", Pdf());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Enqueue(Rejected());

        // Act
        var result = await _h.SendWithAsync(conversation.Id, "看這份", pdf);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(result.Final.Error, Does.Contain("供應商不接受").And.Contain("這份 PDF 讀不了"));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(1));
            Assert.That(await SkippedAtAsync(pdf.Id), Is.Null);
        }
    }

    [Test]
    public async Task Reply_RetryIsRejectedToo_FailsAndDoesNotRecordTheSkip()
    {
        // Arrange：這一則自己的圖片也不被收，拿掉歷史的之後還是 400。
        var (conversationId, pdf) = await ConversationWithBadPdfAsync();
        var image = await _h.UploadAsync("壞圖.png", Image());
        _h.Script.Enqueue(Rejected()).Enqueue(Rejected());

        // Act
        var result = await _h.SendWithAsync(conversationId, "再看這張", image);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(3));
            Assert.That(await SkippedAtAsync(pdf.Id), Is.Null, "沒有證據是歷史的檔案壞了，不記");
        }
    }

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task Reply_FailureThatIsNotAnInvalidRequest_IsNotRetried(HttpStatusCode status)
    {
        // Arrange
        var (conversationId, pdf) = await ConversationWithBadPdfAsync();
        _h.Script.Enqueue(Rejected(status));

        // Act
        var result = await _h.SendAsync(conversationId, "繼續");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(2), "第一輪 1 次加這一輪 1 次，沒有重試");
            Assert.That(await SkippedAtAsync(pdf.Id), Is.Null);
        }
    }

    [TestCase(HttpStatusCode.RequestEntityTooLarge)]
    [TestCase(HttpStatusCode.UnprocessableEntity)]
    public async Task Reply_OtherInvalidRequestStatuses_AreRetriedToo(HttpStatusCode status)
    {
        // Arrange
        var (conversationId, _) = await ConversationWithBadPdfAsync();
        _h.Script.Enqueue(Rejected(status)).Reply("好");

        // Act
        var result = await _h.SendAsync(conversationId, "繼續");

        // Assert
        Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Reply_RejectedButHistoryHasOnlyTextFiles_IsNotRetried()
    {
        // Arrange
        var notes = await _h.UploadAsync("筆記.md", Text("內容"));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("收到");
        await _h.SendWithAsync(conversation.Id, "先看", notes);
        _h.Script.Enqueue(Rejected());

        // Act
        var result = await _h.SendAsync(conversation.Id, "繼續");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public async Task Reply_RejectedAfterTheModelAlreadyAnswered_IsNotRetried()
    {
        // Arrange：已經有輸出送給使用者了，重試會讓同一段文字重複出現。
        var (conversationId, _) = await ConversationWithBadPdfAsync();
        _h.Script.Enqueue(Turn.Create().Text("先說一半").Throw(new HttpRequestException("壞了", null, HttpStatusCode.BadRequest)));

        // Act
        var result = await _h.SendAsync(conversationId, "繼續");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(_h.Script.Calls, Has.Count.EqualTo(2));
        }
    }
}
