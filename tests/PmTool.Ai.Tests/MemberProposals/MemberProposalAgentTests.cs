using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.LocalAgents;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>本機代理回報的 memberProposalId：只記自己的成員建議，別人的、不存在的都當作沒有。</summary>
[TestFixture]
public sealed class MemberProposalAgentTests
{
    [Test]
    public async Task LocalReport_MemberProposalId_IsKeptOnlyForTheOwnProposal()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = AiRules.MinAgentVersionForStaffingTools;
        var engine = await h.RegisterAsync();
        var mine = await ProposalOfAsync(h, h.Me);
        var others = await ProposalOfAsync(h, new PmTool.Abstractions.Actor(h.Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false));
        var run = await h.SendAsync(engine, "加人");
        var job = await h.ClaimAsync();

        // Act
        await h.ReportAsync(
            job.JobId,
            LocalAgentHarness.Tool("t1", "propose_member_changes", AssistantToolStatus.Done, memberProposalId: mine),
            LocalAgentHarness.Tool("t2", "propose_member_changes", AssistantToolStatus.Done, memberProposalId: others),
            LocalAgentHarness.Tool("t3", "propose_member_changes", AssistantToolStatus.Done, memberProposalId: Guid.NewGuid()));
        await h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        var tools = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().ToList();
        Assert.That(tools.Select(t => t.MemberProposalId), Is.EqualTo(new Guid?[] { mine, null, null }));
    }

    private static async Task<Guid> ProposalOfAsync(LocalAgentHarness h, PmTool.Abstractions.Actor actor)
    {
        var target = Guid.NewGuid();
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, WorkPermissions.ProjectManage]);
        var draft = new MemberProposalDraft("WEB", [new MemberChangeDraft(target, "viewer")]);
        var created = await h.Host.Get<IMemberProposalCreator>().CreateAsync(actor, draft, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }
}
