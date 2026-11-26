using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 送出時就擋掉放不進去的文字附件：API 的模型每一輪重送的文字檔有字元預算，本機與主機的引擎有提示長度上限。
/// 不擋的話超過的部分會被悄悄丟掉，模型沒讀到卻照樣回答。字元用實際的數，不用位元組估（中文一個字三個位元組）。
/// </summary>
[TestFixture]
public sealed class AssistantAttachmentLimitTests
{
    private static string Ascii(int characters) => new('a', characters);

    private static string Cjk(int characters) => new('字', characters);

    private static Task<AssistantAttachment> UploadTextAsync(AssistantHarness h, string name, string content, Actor? actor = null) =>
        h.UploadAsync(name, Text(content), actor);

    private static async Task<(int Pending, int Messages)> StateAsync(AssistantHarness h, Guid conversationId)
    {
        await using var db = await h.Host.CreateDbContextAsync();
        return (await db.Attachments.CountAsync(a => a.BoundAt == null), (await h.ConversationRowAsync(conversationId)).MessageCount);
    }

    // ---- API 的模型：這一則的文字附件不能超過重送預算 ----

    [Test]
    public async Task Api_TextAttachmentsOverTheReplayBudget_AreRefusedAndNothingIsStored()
    {
        // Arrange
        await using var h = await AssistantHarness.StartAsync();
        var first = await UploadTextAsync(h, "一.txt", Ascii(250_000));
        var second = await UploadTextAsync(h, "二.txt", Ascii(250_000));
        var conversation = await h.NewConversationAsync();

        // Act
        var result = await h.TryStartAsync(conversation.Id, "看", [first.Id, second.Id]);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("400,000"));
            Assert.That(await StateAsync(h, conversation.Id), Is.EqualTo((2, 0)), "附件還是待送、什麼都沒存");
            Assert.That(h.Script.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Api_ChineseTextFilesWithFewCharactersButManyBytes_AreAccepted()
    {
        // Arrange：三份各 80,000 字（約 240 KB），合計 240,000 字，位元組合計 720 KB，用位元組估會被誤擋。
        await using var h = await AssistantHarness.StartAsync();
        var files = new List<AssistantAttachment>();
        foreach (var name in new[] { "甲.txt", "乙.txt", "丙.txt" })
        {
            files.Add(await UploadTextAsync(h, name, Cjk(80_000)));
        }

        var conversation = await h.NewConversationAsync();
        h.Script.Reply("讀完了");

        // Act
        var result = await h.SendWithAsync(conversation.Id, "整理", [.. files]);

        // Assert
        var texts = h.Script.Calls.Single().Messages.Last().Contents.OfType<TextContent>().Select(t => t.Text).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(texts, Has.Count.EqualTo(4));
            Assert.That(texts.Skip(1), Has.All.Contain("字字字").And.Not.Contain("沒有送"));
        }
    }

    // ---- 本機與主機的引擎：提示的長度上限 ----

    [Test]
    public async Task Local_TextAttachmentLongerThanThePromptLimit_IsRefusedWithTheLimit()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var big = await UploadTextAsync(h.Assistant, "大.txt", Ascii(70_000));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var result = await h.Assistant.TryStartAsync(conversation.Id, "看", [big.Id], selection: LocalAgentHarness.Select(engine));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("本機與主機上的引擎一次最多讀").And.Contain("60,000").And.Contain("含附件"));
            Assert.That(await StateAsync(h.Assistant, conversation.Id), Is.EqualTo((1, 0)));
        }
    }

    [Test]
    public async Task Local_TypedTextCountsTowardTheSameLimit()
    {
        // Arrange：打的字加上附件超過上限，光看附件的大小是看不出來的。
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var file = await UploadTextAsync(h.Assistant, "檔.txt", Ascii(53_000));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var result = await h.Assistant.TryStartAsync(
            conversation.Id, new string('字', 8000), [file.Id], selection: LocalAgentHarness.Select(engine));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
    }

    [Test]
    public async Task Local_ChineseTextFileWithFewCharactersButManyBytes_IsAccepted()
    {
        // Arrange：25,000 字約 75 KB，位元組超過上限、字數沒有。
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var file = await UploadTextAsync(h.Assistant, "中文.txt", Cjk(25_000));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "整理", [file.Id], selection: LocalAgentHarness.Select(engine));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        Assert.That(job.Prompt, Does.Contain("〔附件：中文.txt〕"));
    }

    [Test]
    public async Task Local_OldTextAttachmentThatCannotFitThePromptLimit_IsNotReadInVain()
    {
        // Arrange：舊的 200,000 字的檔案在本機的提示裡一定放不下（上限 60,000 字），不必把它讀出來再丟掉。
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var big = await UploadTextAsync(h.Assistant, "大.txt", Ascii(200_000));
        var conversation = await h.Assistant.NewConversationAsync();
        h.Assistant.Script.Reply("收到");
        await h.Assistant.SendWithAsync(conversation.Id, "先用 API 模型看", big);
        var readsBefore = h.Assistant.Store().OpensOf(big.Id);

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "換成本機", [], selection: LocalAgentHarness.Select(engine));
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(h.Assistant.Store().OpensOf(big.Id), Is.EqualTo(readsBefore), "沒有再讀那個檔案");
            Assert.That(job.Prompt, Does.Contain("〔附件：大.txt（之前附的，這次沒有再送）〕"));
        }
    }

    [Test]
    public async Task Host_TextAttachmentLongerThanThePromptLimit_IsRefusedAndNoJobIsReserved()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        var big = await UploadTextAsync(h.Assistant, "大.txt", Ascii(70_000), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var selection = new AiSelection(setup.Source.Id, "default", null);

        // Act
        var result = await h.Assistant.TryStartAsync(conversation.Id, "看", [big.Id], h.Member, selection);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentTooLarge);
        await using var db = await h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("本機與主機上的引擎一次最多讀"));
            Assert.That(await db.HostJobs.CountAsync(), Is.Zero);
            Assert.That(await db.HostUsage.CountAsync(), Is.Zero);
        }
    }

    // ---- 主機來源：讀附件失敗要馬上退還預留的工作 ----

    [Test]
    public async Task Host_WhenReadingAttachmentsFails_TheReservationIsReleasedAtOnce()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        var notes = await UploadTextAsync(h.Assistant, "筆記.md", "內容", h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var selection = new AiSelection(setup.Source.Id, "default", null);
        h.Assistant.Store().FailReads = true;

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [notes.Id], h.Member, selection);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var events = await AssistantHarness.ReadAllAsync(started.Value);
        h.Assistant.Store().FailReads = false;

        // Assert：不必等排隊逾時，名額已經還回來，同一個人馬上可以再送。
        var final = events.OfType<AssistantRunCompleted>().Single().Message;
        await using var db = await h.Host.CreateDbContextAsync();
        var usage = await db.HostUsage.AsNoTracking().SingleAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(final.Status, Is.EqualTo(AssistantMessageStatus.Failed));
            Assert.That(usage.State, Is.EqualTo(HostUsageStates.Released));
            Assert.That(await db.HostJobs.CountAsync(j => j.State == HostJobStates.Reserved || j.State == HostJobStates.Queued), Is.Zero);
        }

        var again = await h.Assistant.TryStartAsync(conversation.Id, "再試一次", [], h.Member, selection);
        Assert.That(again.Succeeded, Is.True, () => again.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync(setup.Worker);
        await h.FinishAsync(setup.Worker, job);
        await AssistantHarness.ReadAllAsync(again.Value);
    }

    // ---- 綁定失敗要退還用量預留 ----

    [Test]
    public async Task Start_WhenBindingFails_TheUsageReservationIsReleasedAndNothingIsStored()
    {
        // Arrange：存使用者的訊息時，資料庫的觸發器把待送的附件刪掉，模擬「檢查之後、綁定之前剛好被刪」。
        await using var h = await AssistantHarness.StartAsync();
        var attachment = await h.UploadAsync("圖.png", Image());
        var conversation = await h.NewConversationAsync();
        await using (var db = await h.Host.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION ai.drop_pending_attachments() RETURNS trigger AS $$
                BEGIN DELETE FROM ai.attachments WHERE bound_at IS NULL; RETURN NEW; END $$ LANGUAGE plpgsql;
                CREATE TRIGGER drop_pending AFTER INSERT ON ai.messages FOR EACH ROW EXECUTE FUNCTION ai.drop_pending_attachments();
                """);
        }

        // Act
        var result = await h.TryStartAsync(conversation.Id, "看", [attachment.Id]);

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentNotFound);
        await using var verify = await h.Host.CreateDbContextAsync();
        var usage = await verify.ConnectionUsage.AsNoTracking().ToListAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.Select(u => u.State), Is.EqualTo(new[] { "released" }), "預留的用量退還了");
            Assert.That((await h.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
            Assert.That((await h.ConversationRowAsync(conversation.Id)).RunningUntil, Is.Null, "租約也回滾了");
        }
    }
}
