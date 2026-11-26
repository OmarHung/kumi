using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>送訊息時帶附件：檢查、綁上、標題、存下的訊息、送給模型的內容與歷史的重送。</summary>
[TestFixture]
public sealed class AssistantAttachmentSendTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Send_WithAttachments_BindsThemInOrderAndTheConversationShowsThem()
    {
        // Arrange
        var first = await _h.UploadAsync("第二張.png", Image());
        var second = await _h.UploadAsync("規格.pdf", Pdf());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("看到了");

        // Act
        var result = await _h.SendWithAsync(conversation.Id, "幫我看這兩個", second, first);
        var messages = await _h.MessagesAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Run.UserMessage.Attachments.Select(a => a.Id), Is.EqualTo(new[] { second.Id, first.Id }));
            Assert.That(messages[0].Attachments.Select(a => a.FileName), Is.EqualTo(new[] { "規格.pdf", "第二張.png" }));
            Assert.That(messages[0].Attachments[0].Kind, Is.EqualTo(AssistantAttachmentKind.Pdf));
            Assert.That(messages[1].Attachments, Is.Empty, "助理的回覆沒有附件");
        }
    }

    [Test]
    public async Task Send_OnlyAttachmentsWithoutText_IsAllowedTitleIsTheFirstFileNameAndNoEmptyTextPart()
    {
        // Arrange
        var attachment = await _h.UploadAsync("會議紀錄.md", Text("# 紀錄"));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        var result = await _h.SendWithAsync(conversation.Id, "  ", attachment);

        // Assert
        var row = await _h.ConversationRowAsync(conversation.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Title, Is.EqualTo("會議紀錄.md"));
            Assert.That(result.Run.UserMessage.Parts, Is.Empty);
            var sent = _h.Script.Calls.Single().Messages.Last();
            Assert.That(sent.Contents.OfType<TextContent>().Select(t => t.Text), Is.EqualTo(new[] { PmTool.Ai.Attachments.AttachmentReplay.FormatText("會議紀錄.md", "# 紀錄") }));
        }
    }

    [Test]
    public async Task Send_NoTextAndNoAttachments_IsStillMessageInvalid()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await _h.TryStartAsync(conversation.Id, " ", []);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.MessageInvalid);
    }

    [Test]
    public async Task Send_GivesTheModelImagesAndPdfsAsDataAndTextFilesAsTextAfterTheTypedText()
    {
        // Arrange
        var image = Image(200);
        var pdf = Pdf(300);
        var png = await _h.UploadAsync("圖.png", image);
        var doc = await _h.UploadAsync("規格.pdf", pdf);
        var notes = await _h.UploadAsync("notes.md", Text("第一行\n第二行"));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendWithAsync(conversation.Id, "看看", png, doc, notes);

        // Assert
        var contents = _h.Script.Calls.Single().Messages.Last().Contents;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contents, Has.Count.EqualTo(4));
            Assert.That(((TextContent)contents[0]).Text, Is.EqualTo("看看"));
            var data = (DataContent)contents[1];
            Assert.That((data.MediaType, data.Data.ToArray()), Is.EqualTo(("image/png", image)));
            var document = (DataContent)contents[2];
            Assert.That((document.MediaType, document.Name, document.Data.ToArray()), Is.EqualTo(("application/pdf", "規格.pdf", pdf)));
            Assert.That(((TextContent)contents[3]).Text, Is.EqualTo("〔附件：notes.md〕\n````\n第一行\n第二行\n````"));
        }
    }

    [Test]
    public void FormatText_ContentWithLongBacktickRuns_UsesALongerFence()
    {
        // Act
        var text = PmTool.Ai.Attachments.AttachmentReplay.FormatText("a.md", "前\n``````\n後\n");

        // Assert
        Assert.That(text, Is.EqualTo("〔附件：a.md〕\n```````\n前\n``````\n後\n```````"));
    }

    [Test]
    public async Task Send_TheNextTurn_ReplaysEarlierAttachmentsAndKeepsThemOutOfTheNewMessage()
    {
        // Arrange
        var png = await _h.UploadAsync("圖.png", Image(100));
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("看到圖了").Reply("好");
        await _h.SendWithAsync(conversation.Id, "第一則", png);

        // Act
        await _h.SendWithAsync(conversation.Id, "第二則");

        // Assert
        var messages = _h.Script.Calls[1].Messages.Where(m => m.Role != ChatRole.System).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages.Select(m => m.Role), Is.EqualTo(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.User }));
            Assert.That(messages[0].Contents.OfType<DataContent>().Single().MediaType, Is.EqualTo("image/png"));
            Assert.That(messages[2].Contents.OfType<DataContent>(), Is.Empty);
        }
    }

    [Test]
    public async Task Send_ReplayedImagesBeyondTheBudget_AreReplacedWithANoteOldestFirst()
    {
        // Arrange：每張剛好 5 MiB，預算 15 MiB：連這一則在內放得下三張，最舊的第四張放不下。
        var conversation = await _h.NewConversationAsync();
        var big = (int)AssistantAttachmentRules.MaxImageBytes;
        _h.Script.Reply("1").Reply("2").Reply("3").Reply("4");
        for (var i = 1; i <= 4; i++)
        {
            await _h.SendWithAsync(conversation.Id, $"第 {i} 則", await _h.UploadAsync($"圖{i}.png", Image(big)));
        }

        // Assert
        var users = _h.Script.Calls[3].Messages.Where(m => m.Role == ChatRole.User).ToList();
        var oldest = users[0].Contents.OfType<TextContent>().Select(t => t.Text).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(oldest, Does.Contain("〔附件：圖1.png（之前附的，這次沒有再送）〕"));
            Assert.That(users[0].Contents.OfType<DataContent>(), Is.Empty);
            Assert.That(users.Skip(1).Select(u => u.Contents.OfType<DataContent>().Count()), Is.EqualTo(new[] { 1, 1, 1 }));
        }
    }

    [Test]
    public async Task Send_ReplayedTextFilesBeyondTheBudget_AreReplacedWithANote()
    {
        // Arrange：每份 20 萬字元，預算 40 萬：連這一則在內放得下兩份。
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("1").Reply("2").Reply("3");
        for (var i = 1; i <= 3; i++)
        {
            await _h.SendWithAsync(conversation.Id, $"第 {i} 則", await _h.UploadAsync($"檔{i}.txt", Text(new string('a', 200_000))));
        }

        // Assert
        var users = _h.Script.Calls[2].Messages.Where(m => m.Role == ChatRole.User).ToList();
        var texts = users.Select(u => string.Concat(u.Contents.OfType<TextContent>().Select(t => t.Text))).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(texts[0], Does.Contain("〔附件：檔1.txt（之前附的，這次沒有再送）〕"));
            Assert.That(texts[1], Does.Contain(new string('a', 200_000)));
            Assert.That(texts[2], Does.Contain(new string('a', 200_000)));
        }
    }

    [Test]
    public async Task Send_ReplayedAttachmentWhoseFileIsGone_BecomesANoteAndTheReplyStillWorks()
    {
        // Arrange
        var png = await _h.UploadAsync("圖.png", Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("1").Reply("2");
        await _h.SendWithAsync(conversation.Id, "第一則", png);
        _h.Store().Remove(png.Id);

        // Act
        var second = await _h.SendWithAsync(conversation.Id, "第二則");

        // Assert
        var first = _h.Script.Calls[1].Messages.First(m => m.Role == ChatRole.User);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Contents.OfType<TextContent>().Select(t => t.Text), Does.Contain("〔附件：圖.png（檔案已經不在了）〕"));
            Assert.That(second.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
        }
    }

    [Test]
    public async Task Send_AttachmentUsedOnce_CannotBeUsedAgainAndNothingIsStored()
    {
        // Arrange
        var png = await _h.UploadAsync("圖.png", Image());
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        await _h.SendWithAsync(conversation.Id, "第一則", png);
        var before = (await _h.ConversationRowAsync(conversation.Id)).MessageCount;

        // Act
        var again = await _h.TryStartAsync(conversation.Id, "第二則", [png.Id]);

        // Assert
        AssertFailure(again.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.EqualTo(before));
    }

    [Test]
    public async Task Send_SomeoneElsesUnknownOrExpiredOrOtherProjectAttachment_IsAttachmentNotFoundAndStoresNothing()
    {
        // Arrange
        var someoneElses = await _h.UploadAsync("別人.png", Image(), _h.Admin);
        var otherProject = await _h.UploadAsync("別的專案.png", Image());
        await using (var db = await _h.Host.CreateDbContextAsync())
        {
            await db.Attachments.Where(a => a.Id == otherProject.Id).ExecuteUpdateAsync(set => set.SetProperty(a => a.ProjectKey, "OTHER"));
        }

        var expired = await _h.UploadAsync("過期.png", Image());
        _h.Clock.Advance(AssistantAttachmentRules.PendingLifetime + TimeSpan.FromMinutes(1));
        var conversation = await _h.NewConversationAsync();

        // Act
        var results = new[]
        {
            await _h.TryStartAsync(conversation.Id, "看", [someoneElses.Id]),
            await _h.TryStartAsync(conversation.Id, "看", [Guid.NewGuid()]),
            await _h.TryStartAsync(conversation.Id, "看", [otherProject.Id]),
            await _h.TryStartAsync(conversation.Id, "看", [expired.Id]),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.AttachmentNotFound));
            Assert.That(results.Select(r => r.Failure?.Kind), Is.All.EqualTo(FailureKind.Invalid));
            var row = await _h.ConversationRowAsync(conversation.Id);
            Assert.That((row.MessageCount, row.RunningUntil), Is.EqualTo((0, (DateTimeOffset?)null)));
            await using var db = await _h.Host.CreateDbContextAsync();
            Assert.That(await db.Attachments.CountAsync(a => a.BoundAt != null), Is.Zero);
        }
    }

    [Test]
    public async Task Send_DuplicateTooManyOrTooLargeInTotal_AreRejectedAndTheAttachmentsStayPending()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var one = await _h.UploadAsync("一.png", Image());
        var six = new List<AssistantAttachment>();
        for (var i = 0; i < 6; i++)
        {
            six.Add(await _h.UploadAsync($"{i}.png", Image()));
        }

        var bigPdfs = new List<AssistantAttachment>();
        for (var i = 0; i < 3; i++)
        {
            bigPdfs.Add(await _h.UploadAsync($"大{i}.pdf", Pdf(6 * 1024 * 1024)));
        }

        // Act
        var duplicate = await _h.TryStartAsync(conversation.Id, "看", [one.Id, one.Id]);
        var tooMany = await _h.TryStartAsync(conversation.Id, "看", [.. six.Select(a => a.Id)]);
        var tooLarge = await _h.TryStartAsync(conversation.Id, "看", [.. bigPdfs.Select(a => a.Id)]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(duplicate.Failure, FailureKind.Invalid, AiErrorCodes.MessageInvalid);
            AssertFailure(tooMany.Failure, FailureKind.Invalid, AiErrorCodes.TooManyAttachments);
            AssertFailure(tooLarge.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
            await using var db = await _h.Host.CreateDbContextAsync();
            Assert.That(await db.Attachments.CountAsync(a => a.BoundAt == null), Is.EqualTo(10));
        }
    }

    [Test]
    public async Task Send_ExactlyFiveAttachmentsAtTheTotalLimit_IsAccepted()
    {
        // Arrange：5 個、合計剛好 15 MiB。
        var conversation = await _h.NewConversationAsync();
        var attachments = new List<AssistantAttachment>();
        for (var i = 0; i < 5; i++)
        {
            attachments.Add(await _h.UploadAsync($"{i}.pdf", Pdf(3 * 1024 * 1024)));
        }

        _h.Script.Reply("好");

        // Act
        var result = await _h.SendWithAsync(conversation.Id, "看", [.. attachments]);

        // Assert
        Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
    }

    [Test]
    public async Task Send_TwoMessagesRacingForTheSameAttachment_OnlyOneGetsIt()
    {
        // Arrange
        var png = await _h.UploadAsync("圖.png", Image());
        var first = await _h.NewConversationAsync();
        var second = await _h.NewConversationAsync();
        _h.Script.Reply("好").Reply("好");

        // Act
        var results = await Task.WhenAll(
            _h.TryStartAsync(first.Id, "甲", [png.Id]),
            _h.TryStartAsync(second.Id, "乙", [png.Id]));
        foreach (var run in results.Where(r => r.Succeeded))
        {
            await AssistantHarness.ReadAllAsync(run.Value);
        }

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
            Assert.That(results.Single(r => !r.Succeeded).Failure!.Code, Is.EqualTo(AiErrorCodes.AttachmentNotFound));
            var losers = new[] { first, second }.Zip(results).Where(p => !p.Second.Succeeded).Select(p => p.First.Id).Single();
            Assert.That((await _h.ConversationRowAsync(losers)).MessageCount, Is.Zero, "輸的那則對話什麼都沒存、租約也放了");
            Assert.That((await _h.ConversationRowAsync(losers)).RunningUntil, Is.Null);
        }
    }

    [Test]
    public async Task Bind_WhenOneAttachmentWasTakenAfterTheCheck_FailsSoTheCallerCanRollBackTheWholeTransaction()
    {
        // Arrange：檢查通過之後、綁上之前，第二個被別的訊息搶走。
        var first = await _h.UploadAsync("一.png", Image());
        var second = await _h.UploadAsync("二.png", Image());
        var conversation = await _h.NewConversationAsync();
        await using var db = await _h.Host.CreateDbContextAsync();
        var rows = await db.Attachments.AsNoTracking().OrderBy(a => a.FileName).ToListAsync();
        await db.Attachments.Where(a => a.Id == second.Id).ExecuteUpdateAsync(
            set => set.SetProperty(a => a.BoundAt, _h.Clock.GetUtcNow()));
        await using var transaction = await db.Database.BeginTransactionAsync();
        var message = new PmTool.Ai.Persistence.AiMessageRow
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            OrganizationId = _h.OrganizationId,
            Sequence = 1,
            Role = "user",
            Parts = "[]",
            Transcript = "[]",
            CreatedAt = _h.Clock.GetUtcNow(),
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        // Act
        var failure = await PmTool.Ai.Attachments.AttachmentSelection.BindAsync(
            db, _h.Actor, "WEB", conversation.Id, message.Id, rows, _h.Clock.GetUtcNow(), CancellationToken.None);
        await transaction.RollbackAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Code, Is.EqualTo(AiErrorCodes.AttachmentNotFound));
            Assert.That((await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == first.Id)).BoundAt, Is.Null, "回滾之後第一個沒有被綁走");
        }
    }
}
