using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>本機代理與項目：允許的工具、系統提示知道在看哪個項目、回報建議的編號時只記自己的。</summary>
[TestFixture]
public sealed class LocalAgentItemTests
{
    private LocalAgentHarness _h = null!;
    private AiConnectionOption _engine = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        _h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        _engine = await _h.RegisterAsync();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Job_AllowsTheThreeItemToolsAndNamesTheItemInTheSystemPrompt()
    {
        // Arrange
        await _h.SendAsync(_engine, "整理重點", itemKey: "web-12");

        // Act
        var job = await _h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.AllowedTools, Is.SupersetOf(new[] { "get_item", "search_items", "propose_item_changes" }));
            Assert.That(job.AllowedTools, Is.SupersetOf(new[] { "get_blueprint", "propose_changeset" }));
            Assert.That(job.SystemPrompt, Does.Contain("使用者正在看項目 WEB-12").And.Not.Contain("改版首頁"));
            Assert.That(job.SystemPrompt, Does.Contain("propose_item_changes"));
        }
    }

    [Test]
    public async Task Job_WithoutAnItem_DoesNotMentionOne()
    {
        // Arrange
        await _h.SendAsync(_engine, "問");

        // Act
        var job = await _h.ClaimAsync();

        // Assert
        Assert.That(job.SystemPrompt, Does.Not.Contain("使用者正在看項目 WEB"));
    }

    [Test]
    public async Task Job_ItemNotInTheProject_IsRejectedBeforeAJobExists()
    {
        // Arrange
        var conversation = await _h.Assistant.NewConversationAsync();

        // Act
        var started = await _h.Assistant.Service.StartAsync(
            _h.Me, "WEB", conversation.Id,
            new AssistantMessageInput("問", LocalAgentHarness.Select(_engine)) { ItemKey = "WEB-404" }, CancellationToken.None);

        // Assert
        Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.ItemNotFound));
    }

    [Test]
    public async Task Report_OwnProposalId_IsKeptOnTheToolPartAndTheStoredMessage()
    {
        // Arrange
        var mine = await ProposalOfAsync(_h.Me);
        var run = await _h.SendAsync(_engine, "改狀態", itemKey: "WEB-12");
        var job = await _h.ClaimAsync();

        // Act
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("t1", "propose_item_changes", AssistantToolStatus.Running));
        await _h.ReportAsync(job.JobId, LocalAgentHarness.Tool("t1", "propose_item_changes", AssistantToolStatus.Done, itemProposalId: mine));
        await _h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        var tool = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await _h.Assistant.MessagesAsync(run.ConversationId)).Last().Parts.OfType<AssistantToolPart>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.ItemProposalId, Is.EqualTo(mine));
            Assert.That(stored.ItemProposalId, Is.EqualTo(mine));
            Assert.That(events.OfType<AssistantToolUpdate>().Last().Tool.ItemProposalId, Is.EqualTo(mine));
        }
    }

    [Test]
    public async Task Report_SomeoneElsesOrUnknownProposalId_IsDropped()
    {
        // Arrange
        var others = await ProposalOfAsync(new Actor(_h.Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false));
        var run = await _h.SendAsync(_engine, "改狀態");
        var job = await _h.ClaimAsync();

        // Act
        await _h.ReportAsync(
            job.JobId,
            LocalAgentHarness.Tool("t1", "propose_item_changes", AssistantToolStatus.Done, itemProposalId: others),
            LocalAgentHarness.Tool("t2", "propose_item_changes", AssistantToolStatus.Done, itemProposalId: Guid.NewGuid()));
        await _h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        var tools = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().ToList();
        Assert.That(tools.Select(t => t.ItemProposalId), Is.All.Null);
    }

    private async Task<Guid> ProposalOfAsync(Actor actor)
    {
        var draft = new ItemProposalDraft("WEB", "WEB-12", [new ItemFieldChange("title", JsonSerializer.SerializeToElement("新標題"))], null);
        var created = await _h.Host.Get<IItemProposalCreator>().CreateAsync(actor, draft, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }
}
