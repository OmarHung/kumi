using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;
using static PmTool.Ai.Tests.Attachments.AttachmentKit;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 本機代理與主機的 worker 下載工作的附件：只有領走這件工作、還在執行中的才下載得到，而且附件要在這件工作的清單裡
/// （清單在建立工作時存在工作列上，領走後內容清掉也一樣）。
/// </summary>
[TestFixture]
public sealed class AgentAttachmentDownloadTests
{
    private static async Task<byte[]> ReadAsync(Result<AssistantAttachmentContent> result)
    {
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        await using var content = result.Value;
        using var buffer = new MemoryStream();
        await content.Content.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    // ---- 本機 ----

    private sealed record LocalCase(LocalAgentHarness H, LocalAgentJob Job, AssistantAttachment Image, AssistantAttachment Unlisted, IAsyncDisposable Run, byte[] Bytes);

    private static async Task<LocalCase> ClaimedLocalJobAsync(LocalAgentHarness h)
    {
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var bytes = Image(200);
        bytes[100] = 7;
        var image = await h.Assistant.UploadAsync("圖.png", bytes);
        var unlisted = await h.Assistant.UploadAsync("別則的.png", Image(50));
        var conversation = await h.Assistant.NewConversationAsync();
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: LocalAgentHarness.Select(engine));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync();
        return new LocalCase(h, job, image, unlisted, new Finisher(h, job, started.Value), bytes);
    }

    private sealed class Finisher(LocalAgentHarness h, LocalAgentJob job, AssistantRun run) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await h.Agents.FinishAsync(h.Me, job.JobId, new LocalAgentFinish(AssistantMessageStatus.Complete, null, null), CancellationToken.None);
            await AssistantHarness.ReadAllAsync(run);
        }
    }

    [Test]
    public async Task Local_ClaimedRunningJob_ReturnsTheBytesWithAMatchingHash()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await using var _ = c.Run;

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, c.Job.JobId, c.Image.Id, CancellationToken.None);
        var bytes = await ReadAsync(result);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes, Is.EqualTo(c.Bytes));
            Assert.That(Sha256Of(bytes), Is.EqualTo(c.Job.Attachments.Single().Sha256));
            Assert.That(result.Value.Sha256, Is.EqualTo(c.Job.Attachments.Single().Sha256));
        }
    }

    [Test]
    public async Task Local_AttachmentNotInTheJobsList_IsNotFoundEvenForTheSamePerson()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await using var _ = c.Run;

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, c.Job.JobId, c.Unlisted.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Local_AnotherPersonOrUnknownJob_IsNotFound()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await using var _ = c.Run;
        var other = new Actor(h.Me.OrganizationId, Guid.CreateVersion7(), false);

        // Act
        var byOther = await h.Agents.OpenJobAttachmentAsync(other, c.Job.JobId, c.Image.Id, CancellationToken.None);
        var unknown = await h.Agents.OpenJobAttachmentAsync(h.Me, Guid.NewGuid(), c.Image.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(byOther.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(unknown.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Local_FinishedJob_IsNotFound()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await h.FinishAsync(c.Job.JobId);

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, c.Job.JobId, c.Image.Id, CancellationToken.None);
        await c.Run.DisposeAsync();

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Local_JobNotClaimedYet_IsNotFound()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = "0.10.0";
        var engine = await h.RegisterAsync();
        var image = await h.Assistant.UploadAsync("圖.png", Image());
        var conversation = await h.Assistant.NewConversationAsync();
        var started = await h.Assistant.TryStartAsync(conversation.Id, "看", [image.Id], selection: LocalAgentHarness.Select(engine));
        await h.WaitForJobRowsAsync(1);
        await using var db = await h.Host.CreateDbContextAsync();
        var jobId = (await db.LocalJobs.AsNoTracking().SingleAsync()).Id;

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, jobId, image.Id, CancellationToken.None);
        var job = await h.ClaimAsync();
        await h.FinishAsync(job.JobId);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Local_JobBeingStopped_IsNotFound()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await using var db = await h.Host.CreateDbContextAsync();
        await db.LocalJobs.Where(j => j.Id == c.Job.JobId).ExecuteUpdateAsync(set => set.SetProperty(j => j.CancelRequested, true));

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, c.Job.JobId, c.Image.Id, CancellationToken.None);
        await h.FinishAsync(c.Job.JobId);
        await c.Run.DisposeAsync();

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Local_FileMissingFromTheStore_IsNotFound()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        var c = await ClaimedLocalJobAsync(h);
        await using var _ = c.Run;
        await h.Assistant.Store().DeleteAsync(c.Image.Id.ToString("D"), CancellationToken.None);

        // Act
        var result = await h.Agents.OpenJobAttachmentAsync(h.Me, c.Job.JobId, c.Image.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    // ---- 主機 ----

    private sealed record HostCase(HostHarness H, HostSetup Setup, HostJob Job, AssistantAttachment Image, AssistantAttachment Unlisted, AssistantRun Run, byte[] Bytes);

    private static async Task<HostCase> ClaimedHostJobAsync(HostHarness h)
    {
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = "0.10.0";
        await h.CheckInAsync(setup.Worker);
        var bytes = Image(300);
        bytes[120] = 9;
        var image = await h.Assistant.UploadAsync("圖.png", bytes, h.Member);
        var unlisted = await h.Assistant.UploadAsync("別則的.png", Image(50), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var started = await h.Assistant.TryStartAsync(
            conversation.Id, "看", [image.Id], h.Member, new AiSelection(setup.Source.Id, "default", null));
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        var job = await h.ClaimAsync(setup.Worker);
        return new HostCase(h, setup, job, image, unlisted, started.Value, bytes);
    }

    [Test]
    public async Task Host_RightLeaseWhileRunning_ReturnsTheBytesWithAMatchingHash()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var c = await ClaimedHostJobAsync(h);

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(c.Setup.Worker.Identity, c.Job.JobId, c.Job.Lease, c.Image.Id, CancellationToken.None);
        var bytes = await ReadAsync(result);
        await h.FinishAsync(c.Setup.Worker, c.Job);
        await AssistantHarness.ReadAllAsync(c.Run);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bytes, Is.EqualTo(c.Bytes));
            Assert.That(Sha256Of(bytes), Is.EqualTo(c.Job.Attachments.Single().Sha256));
        }
    }

    [Test]
    public async Task Host_WrongLease_IsLeaseLostAndNothingIsReturned()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var c = await ClaimedHostJobAsync(h);

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(c.Setup.Worker.Identity, c.Job.JobId, c.Job.Lease + 1, c.Image.Id, CancellationToken.None);
        await h.FinishAsync(c.Setup.Worker, c.Job);
        await AssistantHarness.ReadAllAsync(c.Run);

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost);
    }

    [Test]
    public async Task Host_FinishedJob_IsLeaseLost()
    {
        // Arrange: 回覆那一頭很久才輪詢一次，工作列在測試看完之前不會被收掉。
        await using var h = await HostHarness.StartAsync(HostHarness.FastTimings with { PollInterval = TimeSpan.FromSeconds(30) });
        var c = await ClaimedHostJobAsync(h);
        await h.FinishAsync(c.Setup.Worker, c.Job);

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(c.Setup.Worker.Identity, c.Job.JobId, c.Job.Lease, c.Image.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.HostJobLeaseLost);
    }

    [Test]
    public async Task Host_AttachmentNotInTheJobsList_IsNotFound()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var c = await ClaimedHostJobAsync(h);

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(c.Setup.Worker.Identity, c.Job.JobId, c.Job.Lease, c.Unlisted.Id, CancellationToken.None);
        await h.FinishAsync(c.Setup.Worker, c.Job);
        await AssistantHarness.ReadAllAsync(c.Run);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }

    [Test]
    public async Task Host_AnotherWorkerOrOrganization_IsNotFound()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var c = await ClaimedHostJobAsync(h);
        var other = await h.PairAsync("另一台");
        var otherOrg = new HostAgentIdentity(Guid.CreateVersion7(), c.Setup.Worker.AgentId);

        // Act
        var byOther = await h.Worker.OpenJobAttachmentAsync(other.Identity, c.Job.JobId, c.Job.Lease, c.Image.Id, CancellationToken.None);
        var byOtherOrg = await h.Worker.OpenJobAttachmentAsync(otherOrg, c.Job.JobId, c.Job.Lease, c.Image.Id, CancellationToken.None);
        var unknown = await h.Worker.OpenJobAttachmentAsync(c.Setup.Worker.Identity, Guid.NewGuid(), c.Job.Lease, c.Image.Id, CancellationToken.None);
        await h.FinishAsync(c.Setup.Worker, c.Job);
        await AssistantHarness.ReadAllAsync(c.Run);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(byOther.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(byOtherOrg.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
            AssertFailure(unknown.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
        }
    }

    [Test]
    public async Task Host_JobStillQueued_IsNotFound()
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = "0.10.0";
        await h.CheckInAsync(setup.Worker);
        var image = await h.Assistant.UploadAsync("圖.png", Image(), h.Member);
        var conversation = await h.Assistant.NewConversationAsync(h.Member);
        var started = await h.Assistant.TryStartAsync(
            conversation.Id, "看", [image.Id], h.Member, new AiSelection(setup.Source.Id, "default", null));
        await h.WaitForQueuedAsync(1);
        await using var db = await h.Host.CreateDbContextAsync();
        var jobId = (await db.HostJobs.AsNoTracking().SingleAsync()).Id;

        // Act
        var result = await h.Worker.OpenJobAttachmentAsync(setup.Worker.Identity, jobId, 0, image.Id, CancellationToken.None);
        var job = await h.ClaimAsync(setup.Worker);
        await h.FinishAsync(setup.Worker, job);
        await AssistantHarness.ReadAllAsync(started.Value);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.AttachmentNotFound);
    }
}
