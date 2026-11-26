using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.HostAgents;
using PmTool.Ai.Tests.LocalAgents;

namespace PmTool.Ai.Tests.NewItems;

/// <summary>
/// 本機與主機代理的 propose_new_items：舊版 kumi 的工作不放這個工具（它會整件工作不跑）、提示改叫使用者更新；
/// 新版有；回報的 newItemProposalId 只記自己的。
/// </summary>
[TestFixture]
public sealed class NewItemAgentTests
{
    private const string NewVersion = AiRules.MinAgentVersionForNewItemProposals;

    [Test]
    public async Task LocalJob_FromAnOldKumi_HasNoNewItemsToolAndTheUpdateAdvice()
    {
        // Arrange
        await using var h = await StartLocalAsync("0.14.0");
        var engine = await h.RegisterAsync();
        await h.SendAsync(engine, "新增一個任務");

        // Act
        var job = await h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.AllowedTools, Does.Not.Contain("propose_new_items"));
            Assert.That(job.AllowedTools, Does.Contain("propose_item_changes"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("- propose_new_items："));
            Assert.That(job.SystemPrompt, Does.Contain("這一次沒有 propose_new_items 這個工具").And.Contain("kumi update"));
        }
    }

    [Test]
    public async Task LocalJob_FromAKumiThatIsNewEnough_HasTheNewItemsTool()
    {
        // Arrange
        await using var h = await StartLocalAsync(NewVersion);
        var engine = await h.RegisterAsync();
        await h.SendAsync(engine, "新增一個任務");

        // Act
        var job = await h.ClaimAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.AllowedTools, Does.Contain("propose_new_items"));
            Assert.That(job.SystemPrompt, Does.Contain("- propose_new_items：").And.Not.Contain("這一次沒有 propose_new_items"));
            Assert.That(job.SystemPrompt, Does.Not.Contain("你只能用這十個"));
        }
    }

    [Test]
    public async Task LocalReport_NewItemProposalId_IsKeptOnlyForTheOwnProposal()
    {
        // Arrange
        await using var h = await StartLocalAsync(NewVersion);
        var engine = await h.RegisterAsync();
        var mine = await ProposalOfAsync(h, h.Me);
        var others = await ProposalOfAsync(h, new PmTool.Abstractions.Actor(h.Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false));
        var run = await h.SendAsync(engine, "新增");
        var job = await h.ClaimAsync();

        // Act
        await h.ReportAsync(
            job.JobId,
            LocalAgentHarness.Tool("t1", "propose_new_items", AssistantToolStatus.Done, newItemProposalId: mine),
            LocalAgentHarness.Tool("t2", "propose_new_items", AssistantToolStatus.Done, newItemProposalId: others),
            LocalAgentHarness.Tool("t3", "propose_new_items", AssistantToolStatus.Done, newItemProposalId: Guid.NewGuid()));
        await h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        var tools = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().ToList();
        Assert.That(tools.Select(t => t.NewItemProposalId), Is.EqualTo(new Guid?[] { mine, null, null }));
    }

    [TestCase("0.14.0", false)]
    [TestCase(AiRules.MinAgentVersionForStaffingTools, true)]
    public async Task HostJob_HasTheNewItemsToolOnlyForAWorkerThatIsNewEnough(string version, bool hasTool)
    {
        // Arrange
        await using var h = await HostHarness.StartAsync();
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        var setup = await h.SetUpAsync();
        setup.Worker.AgentVersion = version;
        await h.CheckInAsync(setup.Worker);
        await h.SendAsync(h.Member, setup.Source, "新增一個任務");

        // Act
        var job = await h.ClaimAsync(setup.Worker);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.AllowedTools.Contains("propose_new_items"), Is.EqualTo(hasTool));
            Assert.That(job.SystemPrompt.Contains("- propose_new_items："), Is.EqualTo(hasTool));
            Assert.That(job.SystemPrompt.Contains("主機上的 kumi 太舊"), Is.EqualTo(!hasTool));
        }
    }

    private static async Task<LocalAgentHarness> StartLocalAsync(string version)
    {
        var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = version;
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember();
        return h;
    }

    private static async Task<Guid> ProposalOfAsync(LocalAgentHarness h, PmTool.Abstractions.Actor actor)
    {
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, PmTool.Work.WorkPermissions.ItemCreate]);
        var draft = new NewItemProposalDraft("WEB", [new NewItemDraft("task", "新任務")]);
        var created = await h.Host.Get<INewItemProposalCreator>().CreateAsync(actor, draft, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }
}
