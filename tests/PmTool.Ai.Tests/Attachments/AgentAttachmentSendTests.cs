using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 本機與主機的引擎（kumi 0.10.0 以上）也收圖片與 PDF（internal/HANDOFF-ai-attachments.md 第 8 節）：
/// 送出時依引擎與版本檢查，領到的工作帶附件清單、提示裡留位置，工作列記附件編號。
/// </summary>
[TestFixture]
public sealed class AgentAttachmentSendTests
{
    private static LocalEngineInfo Codex() => LocalAgentHarness.Codex();

    [Test]
    public async Task Local_ClaudeCodeOnNewKumi_TakesImagePdfAndTextAndTheJobListsTheBinaryOnes()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image(80));
        var pdf = await h.Assistant.UploadAsync("規格.pdf", Pdf(90));
        var notes = await h.Assistant.UploadAsync("筆記.md", Text("第一行"));
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(
            conversation.Id, "看這些", [image.Id, notes.Id, pdf.Id], selection: LocalAgentHarness.Select(engine));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync();
        var row = await h.JobRowAsync(job.JobId);
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Attachments.Select(a => (a.Id, a.FileName, a.ContentType, a.Kind, a.SizeBytes)), Is.EqualTo(new[]
            {
                (image.Id, "圖.png", "image/png", AssistantAttachmentKind.Image, 80L),
                (pdf.Id, "規格.pdf", "application/pdf", AssistantAttachmentKind.Pdf, 90L),
            }));
            Assert.That(job.Attachments.All(a => a.Sha256.Length == 64), Is.True);
            Assert.That(row.AttachmentIds, Is.EqualTo(new[] { image.Id, pdf.Id }));
            Assert.That(job.Prompt, Does.Contain("〔附件 1：圖.png（附在這則訊息的最後）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件 2：規格.pdf（附在這則訊息的最後）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件：筆記.md〕\n````\n第一行\n````"));
        }
    }

    [Test]
    public async Task Local_CodexOnNewKumi_TakesImagesButRefusesPdfWithTheReason()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync(name: "我的電腦", engines: [Codex()]);
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var pdf = await h.Assistant.UploadAsync("規格.pdf", Pdf());
        var conversation = await h.Assistant.NewConversationAsync();
        var selection = LocalAgentHarness.Select(engine);

        // Act
        var refused = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id, pdf.Id], selection: selection);
        var accepted = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: selection);
        Assert.That(accepted.Succeeded, Is.True, () => accepted.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync(engines: [Codex()]);
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(accepted.Value);

        // Assert
        AssertFailure(refused.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.Failure!.Message, Does.StartWith("Codex 讀不到 PDF（圖片可以）"));
            Assert.That(refused.Failure.Message, Does.Contain("我的電腦"));
            Assert.That(job.Attachments.Single().Id, Is.EqualTo(image.Id));
        }
    }

    [Test]
    public async Task Local_OldKumi_RefusesImagesAndSaysWhichVersionAndWhatToDo()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync(name: "辦公室的 Mac");
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var result = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: LocalAgentHarness.Select(engine));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("辦公室的 Mac"));
            Assert.That(result.Failure.Message, Does.Contain("0.9.0"));
            Assert.That(result.Failure.Message, Does.Contain("0.10.0"));
            Assert.That(result.Failure.Message, Does.Contain("kumi update"));
        }
    }

    [Test]
    public async Task Local_HistoryFromApiTurns_CodexKeepsTheImageAndNotesThePdf()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync(engines: [Codex()]);
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var pdf = await h.Assistant.UploadAsync("規格.pdf", Pdf());
        var conversation = await h.Assistant.NewConversationAsync();
        h.Assistant.Script.Reply("看到了");
        await h.Assistant.SendWithAsync(conversation.Id, "先用 API 模型看", image, pdf);

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "換成 Codex", [], selection: LocalAgentHarness.Select(engine));
        var job = await h.ClaimAsync(engines: [Codex()]);
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Attachments.Single().Id, Is.EqualTo(image.Id));
            Assert.That(job.Prompt, Does.Contain("〔附件 1：圖.png（附在這則訊息的最後）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件：規格.pdf（之前附的，這個來源這次讀不到）〕"));
        }
    }

    [Test]
    public async Task Local_TwoTurns_SecondJobNumbersTheEarlierImageFirst()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var first = await h.Assistant.UploadAsync("第一張.png", Image());
        var second = await h.Assistant.UploadAsync("第二張.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();
        var selection = LocalAgentHarness.Select(engine);
        var one = await h.Assistant.TryStartAsync(conversation.Id, "第一則", [first.Id], selection: selection);
        var firstJob = await h.ClaimAsync();
        await h.FinishAsync(firstJob.JobId);
        await AssistantHarness.ReadAllAsync(one.Value);

        // Act
        var two = await h.Assistant.TryStartAsync(conversation.Id, "第二則", [second.Id], selection: selection);
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(two.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Attachments.Select(a => a.Id), Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(job.Prompt, Does.Contain("〔附件 1：第一張.png"));
            Assert.That(job.Prompt, Does.Contain("〔附件 2：第二張.png"));
        }
    }

    [Test]
    public async Task Local_NewKumiButNoBinary_JobHasNoAttachmentsAndNoIds()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "只有字", [], selection: LocalAgentHarness.Select(engine));
        var job = await h.ClaimAsync();
        var row = await h.JobRowAsync(job.JobId);
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Attachments, Is.Empty);
            Assert.That(row.AttachmentIds, Is.Empty);
        }
    }

    [Test]
    public async Task Host_ClaudeCodeOnNewWorker_TakesImagesAndPdfAndTheJobListsThem()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = "0.10.0";
        await h.CheckInAsync(setup.Worker);
        var image = await h.Assistant.UploadAsync("圖.png", Image(70), h.Member);
        var pdf = await h.Assistant.UploadAsync("規格.pdf", Pdf(75), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);

        // Act
        var started = await h.Assistant.TryStartAsync(
            conversation.Id, "看", [image.Id, pdf.Id], h.Member, new AiSelection(setup.Source.Id, "default", null));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync(setup.Worker);
        var row = await h.JobRowAsync(job.JobId);
        await h.FinishAsync(setup.Worker, job);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Attachments.Select(a => (a.Id, a.Kind, a.SizeBytes)), Is.EqualTo(new[]
            {
                (image.Id, AssistantAttachmentKind.Image, 70L),
                (pdf.Id, AssistantAttachmentKind.Pdf, 75L),
            }));
            Assert.That(row.AttachmentIds, Is.EqualTo(new[] { image.Id, pdf.Id }));
            Assert.That(job.Prompt, Does.Contain("〔附件 1：圖.png（附在這則訊息的最後）〕"));
            Assert.That(job.Prompt, Does.Contain("〔附件 2：規格.pdf（附在這則訊息的最後）〕"));
        }
    }

    [Test]
    public async Task Host_OldWorker_RefusesWithAMessageForTheOwnerNotTheMember()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync(name: "主機上的 Claude Code");
        var image = await h.Assistant.UploadAsync("圖.png", Image(), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);

        // Act
        var result = await h.Assistant.TryStartAsync(
            conversation.Id, "看", [image.Id], h.Member, new AiSelection(setup.Source.Id, "default", null));

        // Assert
        AssertFailure(result.Failure, FailureKind.Invalid, AiErrorCodes.AttachmentUnsupported);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Message, Does.Contain("主機上的 Claude Code"));
            Assert.That(result.Failure.Message, Does.Contain("0.9.0"));
            Assert.That(result.Failure.Message, Does.Contain("組織擁有者"));
            Assert.That(result.Failure.Message, Does.Contain("ai-worker"));
            Assert.That(result.Failure.Message, Does.Not.Contain("kumi update"));
        }
    }

    [Test]
    public async Task Refused_NothingIsBoundAndTheAttachmentsStayPending()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: LocalAgentHarness.Select(engine));

        // Assert
        await using var db = await h.Host.CreateDbContextAsync();
        Assert.That(await db.Attachments.CountAsync(a => a.BoundAt == null), Is.EqualTo(1));
    }
}
