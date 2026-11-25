using Microsoft.EntityFrameworkCore;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>本機代理工作內容（整段對話）的清除（AG-16）：領走就清、伺服器自己結束也清、漏網的由定期清理掃掉。</summary>
[TestFixture]
public sealed class LocalAgentPayloadTests
{
    private const string Empty = "{}";

    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        _engine = await _h.RegisterAsync();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Claim_ReturnsTheConversationButTheRowNoLongerHoldsIt()
    {
        // Arrange
        await _h.SendAsync(_engine, "把待辦改名");
        await _h.WaitForJobRowsAsync(1);

        // Act
        var job = await _h.ClaimAsync();
        var row = await _h.JobRowAsync(job.JobId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Prompt, Does.Contain("把待辦改名"));
            Assert.That(job.SystemPrompt, Is.Not.Empty);
            Assert.That(row.Payload, Is.EqualTo(Empty));
        }
    }

    [Test]
    public async Task StopBeforeClaim_ServerFinishedJobHasNoConversationLeft()
    {
        // Arrange
        var run = await _h.SendAsync(_engine, "會被停掉的訊息");
        await _h.WaitForJobRowsAsync(1);

        // Act
        await run.StopAsync();
        await using var db = await _h.Host.CreateDbContextAsync();
        var rows = await db.LocalJobs.AsNoTracking().ToListAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows, Has.Count.LessThanOrEqualTo(1));
            Assert.That(rows.Select(r => r.Payload), Has.All.EqualTo(Empty));
        }
    }

    [Test]
    public async Task Sweep_OldJobWithAPayload_IsClearedAndFreshOnesAreKept()
    {
        // Arrange
        var now = _h.Assistant.Clock.GetUtcNow();
        var oldId = Guid.CreateVersion7();
        var freshId = Guid.CreateVersion7();
        await AddRowAsync(oldId, now - TimeSpan.FromHours(2));
        await AddRowAsync(freshId, now - TimeSpan.FromMinutes(5));

        // Act
        var result = await _h.Host.Get<IAiMaintenance>().SweepAsync(CancellationToken.None);
        var old = await _h.JobRowAsync(oldId);
        var fresh = await _h.JobRowAsync(freshId);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.JobPayloads, Is.EqualTo(1));
            Assert.That(old.Payload, Is.EqualTo(Empty));
            Assert.That(fresh.Payload, Does.Contain("整段對話"));
        }
    }

    [Test]
    public async Task Sweep_RunTwice_SecondFindsNothing()
    {
        // Arrange
        await AddRowAsync(Guid.CreateVersion7(), _h.Assistant.Clock.GetUtcNow() - TimeSpan.FromHours(3));
        var maintenance = _h.Host.Get<IAiMaintenance>();
        await maintenance.SweepAsync(CancellationToken.None);

        // Act
        var second = await maintenance.SweepAsync(CancellationToken.None);

        // Assert
        Assert.That(second.JobPayloads, Is.Zero);
    }

    private async Task AddRowAsync(Guid id, DateTimeOffset createdAt)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        db.LocalJobs.Add(new LocalJobRow
        {
            Id = id,
            OrganizationId = _h.Me.OrganizationId,
            PersonId = _h.Me.PersonId,
            EngineId = _engine.Id,
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            Payload = """{"prompt":"整段對話"}""",
            State = LocalJobStates.Claimed,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }
}
