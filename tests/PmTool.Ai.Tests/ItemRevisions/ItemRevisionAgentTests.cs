using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.LocalAgents;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRevisions;

/// <summary>本機代理回報的 itemDeletionProposalId、itemBatchProposalId 只記自己的建議，並且存進對話的工具段落。</summary>
[TestFixture]
public sealed class ItemRevisionAgentTests
{
    [Test]
    public async Task LocalReport_DeletionAndBatchProposalIds_AreKeptOnlyForTheOwnProposals()
    {
        // Arrange
        await using var h = await LocalAgentHarness.StartAsync(LocalAgentHarness.PatientTimings);
        h.AgentVersion = AiRules.MinAgentVersionForRevisionTools;
        h.Assistant.Projects.OnGet = (_, _) => ItemSamples.DetailWithMember([.. ItemSamples.EditorPermissions, WorkPermissions.ItemDelete]);
        var engine = await h.RegisterAsync();
        var stranger = new Actor(h.Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: false);
        var myDeletion = await DeletionOfAsync(h, h.Me);
        var otherDeletion = await DeletionOfAsync(h, stranger);
        var myBatch = await BatchOfAsync(h, h.Me);
        var otherBatch = await BatchOfAsync(h, stranger);
        var run = await h.SendAsync(engine, "刪掉重複的，並把到期日往後延");
        var job = await h.ClaimAsync();

        // Act
        await h.ReportAsync(
            job.JobId,
            Tool("t1", "propose_item_deletions", deletion: myDeletion),
            Tool("t2", "propose_item_deletions", deletion: otherDeletion),
            Tool("t3", "propose_bulk_item_changes", batch: myBatch),
            Tool("t4", "propose_bulk_item_changes", batch: otherBatch),
            Tool("t5", "propose_item_deletions", deletion: Guid.NewGuid()),
            Tool("t6", "propose_bulk_item_changes", batch: myDeletion));
        await h.FinishAsync(job.JobId);
        var events = await run.ReadAllAsync();

        // Assert
        var tools = events.OfType<AssistantRunCompleted>().Single().Message.Parts.OfType<AssistantToolPart>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.ItemDeletionProposalId), Is.EqualTo(new Guid?[] { myDeletion, null, null, null, null, null }));
            Assert.That(tools.Select(t => t.ItemBatchProposalId), Is.EqualTo(new Guid?[] { null, null, myBatch, null, null, null }));
        }
    }

    private static LocalAgentTool Tool(string callId, string name, Guid? deletion = null, Guid? batch = null) =>
        new(new AssistantToolPart(callId, name, AssistantToolStatus.Done, null, null)
        {
            ItemDeletionProposalId = deletion,
            ItemBatchProposalId = batch,
        });

    private static async Task<Guid> DeletionOfAsync(LocalAgentHarness h, Actor actor)
    {
        var created = await h.Host.Get<IItemDeletionProposalCreator>().CreateAsync(
            actor, new ItemDeletionProposalDraft("WEB", ["WEB-12"]), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }

    private static async Task<Guid> BatchOfAsync(LocalAgentHarness h, Actor actor)
    {
        var change = new ItemFieldChange("dueDate", System.Text.Json.JsonSerializer.SerializeToElement("2026-12-05"));
        var created = await h.Host.Get<IItemBatchProposalCreator>().CreateAsync(
            actor, new ItemBatchProposalDraft("WEB", [new ItemBatchEntryDraft("WEB-12", [change], null, null)]), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value.Id;
    }
}
