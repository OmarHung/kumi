using PmTool.Abstractions;
using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>舊版的 kumi（0.10.0 之前）的本機與主機引擎只讀得到文字檔：圖片與 PDF 送出時回 422，歷史裡的換成一行說明。</summary>
[TestFixture]
public sealed class AttachmentTextOnlySourceTests
{
    [Test]
    public async Task Local_ImageOrPdf_IsUnsupportedAndEverythingStaysPending()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var text = await h.Assistant.UploadAsync("筆記.md", Text("內容"));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var result = await h.Assistant.TryStartAsync(
            conversation.Id, "看", [text.Id, image.Id], selection: LocalAgentHarness.Select(engine));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported);
        await using var db = await h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("圖片"));
            Assert.That(await db.Attachments.CountAsync(a => a.BoundAt == null), Is.EqualTo(2));
            Assert.That((await h.Assistant.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
        }
    }

    [Test]
    public async Task Local_TextFile_IsAppendedToTheMessageInThePrompt()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var notes = await h.Assistant.UploadAsync("筆記.md", Text("第一行\n第二行"));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "幫我整理", [notes.Id], selection: LocalAgentHarness.Select(engine));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Prompt, Does.Contain("幫我整理"));
            Assert.That(job.Prompt, Does.Contain("〔附件：筆記.md〕\n````\n第一行\n第二行\n````"));
            Assert.That(started.Value.UserMessage.Attachments.Single().FileName, Is.EqualTo("筆記.md"));
        }
    }

    [Test]
    public async Task Local_OnlyAFileWithoutText_StillProducesAPrompt()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var notes = await h.Assistant.UploadAsync("筆記.txt", Text("只有檔案"));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "", [notes.Id], selection: LocalAgentHarness.Select(engine));
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        Assert.That(job.Prompt, Does.Contain("只有檔案"));
    }

    [Test]
    public async Task Local_ImagesAndPdfsFromEarlierApiTurns_BecomeANoteInThePrompt()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var pdf = await h.Assistant.UploadAsync("規格.pdf", Pdf());
        var conversation = await h.Assistant.NewConversationAsync();
        h.Assistant.Script.Reply("看到了");
        await h.Assistant.SendWithAsync(conversation.Id, "先用 API 模型看", image, pdf);

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "換成本機", [], selection: LocalAgentHarness.Select(engine));
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Prompt, Does.Contain("〔附件：圖.png（之前附的，這個來源這次讀不到）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件：規格.pdf（之前附的，這個來源這次讀不到）〕"));
            Assert.That(job.Prompt, Does.Contain("先用 API 模型看"));
        }
    }

    [Test]
    public async Task Host_ImageIsUnsupportedAndTextFileReachesTheWorker()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image(), h.Member);
        var notes = await h.Assistant.UploadAsync("筆記.md", Text("主機要讀的內容"), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var selection = new AiSelection(setup.Source.Id, "default", null);

        // Act
        var refused = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], h.Member, selection);
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [notes.Id], h.Member, selection);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync(setup.Worker);
        await h.FinishAsync(setup.Worker, job);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        AssertFailure(refused.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported);
        Assert.That(job.Prompt, Does.Contain("〔附件：筆記.md〕\n````\n主機要讀的內容\n````"));
    }
}
