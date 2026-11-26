using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 本機與主機的引擎讀不了歷史裡的圖片或 PDF 時，那一輪失敗；不記下來的話，每一則之後的訊息都帶著同一張壞圖、永遠失敗
/// （API 的路徑被供應商退回時就會記 <c>replay_skipped_at</c>）。伺服器不自動重跑，只在失敗訊息說明下一則不會再送。
/// </summary>
[TestFixture]
public sealed class AgentAttachmentFailureTests
{
    private const string Note = "之前附的圖片或 PDF 下一則不會再送；如果是它們讓引擎讀不了，再送一次就好。";

    private static async Task<DateTimeOffset?> SkippedAtAsync(AiTestHost host, Guid attachmentId)
    {
        await using var db = await host.CreateDbContextAsync();
        return (await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == attachmentId)).ReplaySkippedAt;
    }

    [Test]
    public async Task Local_EngineFailsWhileCarryingAHistoryImage_MarksItSkippedAddsTheNoteAndTheNextTurnDoesNotSendIt()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var selection = LocalAgentHarness.Select(engine);
        var bad = await h.Assistant.UploadAsync("壞掉.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();
        var one = await h.Assistant.TryStartAsync(conversation.Id, "先看這張", [bad.Id], selection: selection);
        await h.FinishAsync((await h.ClaimAsync()).JobId);
        await AssistantHarness.ReadAllAsync(one.Value);

        // Act
        var two = await h.Assistant.TryStartAsync(conversation.Id, "再問一次", [], selection: selection);
        var failing = await h.ClaimAsync();
        await h.FinishAsync(failing.JobId, AssistantMessageStatus.Failed, "引擎讀不了這張圖片");
        await AssistantHarness.ReadAllAsync(two.Value);
        var messages = await h.Assistant.MessagesAsync(conversation.Id);
        var three = await h.Assistant.TryStartAsync(conversation.Id, "第三則", [], selection: selection);
        var next = await h.ClaimAsync();
        await h.FinishAsync(next.JobId);
        await AssistantHarness.ReadAllAsync(three.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failing.Attachments.Select(a => a.Id), Is.EqualTo(new[] { bad.Id }), "前提：那一輪帶著歷史的圖片");
            Assert.That(messages.Where(m => m.Status == AssistantMessageStatus.Failed).Single().Error, Is.EqualTo("引擎讀不了這張圖片\n" + Note));
            Assert.That(await SkippedAtAsync(h.Host, bad.Id), Is.Not.Null);
            Assert.That(next.Attachments, Is.Empty);
            Assert.That(next.Prompt, Does.Contain("模型讀不了"));
        }
    }

    [Test]
    public async Task Local_EngineFailsWithOnlyTheCurrentImage_DoesNotMarkOrAddTheNote()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("這一則.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();

        // Act
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: LocalAgentHarness.Select(engine));
        await h.FinishAsync((await h.ClaimAsync()).JobId, AssistantMessageStatus.Failed, "引擎壞了");
        await AssistantHarness.ReadAllAsync(started.Value);
        var messages = await h.Assistant.MessagesAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messages.Single(m => m.Status == AssistantMessageStatus.Failed).Error, Is.EqualTo("引擎壞了"));
            Assert.That(await SkippedAtAsync(h.Host, image.Id), Is.Null);
        }
    }

    [Test]
    public async Task Local_RunStoppedWhileCarryingAHistoryImage_DoesNotMarkIt()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var selection = LocalAgentHarness.Select(engine);
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();
        var one = await h.Assistant.TryStartAsync(conversation.Id, "先看", [image.Id], selection: selection);
        await h.FinishAsync((await h.ClaimAsync()).JobId);
        await AssistantHarness.ReadAllAsync(one.Value);

        // Act
        var two = await h.Assistant.TryStartAsync(conversation.Id, "再問", [], selection: selection);
        await h.FinishAsync((await h.ClaimAsync()).JobId, AssistantMessageStatus.Stopped, "使用者停止");
        await AssistantHarness.ReadAllAsync(two.Value);

        // Assert
        Assert.That(await SkippedAtAsync(h.Host, image.Id), Is.Null);
    }

    [Test]
    public async Task Host_EngineFailsWhileCarryingAHistoryImage_MarksItSkippedAndAddsTheNote()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = "0.10.0";
        await h.CheckInAsync(setup.Worker);
        var selection = new AiSelection(setup.Source.Id, "default", null);
        var bad = await h.Assistant.UploadAsync("壞掉.png", Image(), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var one = await h.Assistant.TryStartAsync(conversation.Id, "先看這張", [bad.Id], h.Member, selection);
        await h.FinishAsync(setup.Worker, await h.ClaimAsync(setup.Worker));
        await AssistantHarness.ReadAllAsync(one.Value);

        // Act
        var two = await h.Assistant.TryStartAsync(conversation.Id, "再問一次", [], h.Member, selection);
        var failing = await h.ClaimAsync(setup.Worker);
        await h.FinishAsync(setup.Worker, failing, AssistantMessageStatus.Failed, "引擎讀不了這張圖片");
        await AssistantHarness.ReadAllAsync(two.Value);
        var messages = await h.Assistant.MessagesAsync(conversation.Id, h.Member);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failing.Attachments.Select(a => a.Id), Is.EqualTo(new[] { bad.Id }));
            Assert.That(messages.Single(m => m.Status == AssistantMessageStatus.Failed).Error, Is.EqualTo("引擎讀不了這張圖片\n" + Note));
            Assert.That(await SkippedAtAsync(h.Host, bad.Id), Is.Not.Null);
        }
    }

    [Test]
    public async Task Host_JobBeingStoppedWhenTheWorkerDownloads_IsLeaseLost()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = "0.10.0";
        await h.CheckInAsync(setup.Worker);
        var image = await h.Assistant.UploadAsync("圖.png", Image(), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], h.Member, new AiSelection(setup.Source.Id, "default", null));
        var job = await h.ClaimAsync(setup.Worker);
        await using (var db = await h.Host.CreateDbContextAsync())
        {
            await db.HostJobs.Where(j => j.Id == job.JobId).ExecuteUpdateAsync(set => set.SetProperty(j => j.CancelRequested, true));
        }

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(setup.Worker.Identity, job.JobId, job.Lease, image.Id, CancellationToken.None);
        await h.FinishAsync(setup.Worker, job, AssistantMessageStatus.Stopped);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost);
    }
}
