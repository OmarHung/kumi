using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemRestoreProposals;

/// <summary>復原建議的套用、作廢、讀取權限，以及內建工具與撤回。</summary>
[TestFixture]
public sealed class ItemRestoreProposalApplyTests : RestoreTestBase
{
    private static BulkItemFailure Refused(string key, string message) =>
        new(key, new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, message));

    [Test]
    public async Task Apply_RestoresAllItemsAsTheApplyingPerson()
    {
        // Arrange
        AddDeleted("WEB-12");
        AddDeleted("WEB-13");
        var proposal = await CreatedAsync("WEB-12", "WEB-13");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Succeeded, Is.True, () => applied.Failure?.Message ?? string.Empty);
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.DecidedAt, Is.Not.Null);
            Assert.That(applied.Value.RestoredKeys, Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
            Assert.That(applied.Value.Error, Is.Null);
            Assert.That(Bulk.Restores.Single().Actor, Is.EqualTo(Me));
            Assert.That(Bulk.Restores.Single().ProjectKey, Is.EqualTo("WEB"));
            Assert.That(Bulk.Restores.Single().Keys, Is.EqualTo(new[] { "WEB-12", "WEB-13" }));
        }
    }

    [Test]
    public async Task Apply_SomeRefused_IsAppliedWithTheFailuresInTheError()
    {
        // Arrange
        AddDeleted("WEB-12");
        AddDeleted("WEB-13");
        var proposal = await CreatedAsync("WEB-12", "WEB-13");
        Bulk.OnRestore = _ => new BulkRestoreResult([ItemSamples.Item("WEB-12")], [Refused("WEB-13", "上層還是刪除的。")]);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Value.Status, Is.EqualTo(ItemProposalStatus.Applied));
            Assert.That(applied.Value.RestoredKeys, Is.EqualTo(new[] { "WEB-12" }));
            Assert.That(applied.Value.Error, Does.Contain("WEB-13").And.Contain("上層還是刪除的。"));
        }
    }

    [Test]
    public async Task Apply_EverythingRefused_ReturnsTheFirstFailureAndStaysPending()
    {
        // Arrange
        AddDeleted("WEB-12");
        AddDeleted("WEB-13");
        var proposal = await CreatedAsync("WEB-12", "WEB-13");
        Bulk.OnRestore = _ => new BulkRestoreResult([], [Refused("WEB-12", "沒有權限。"), Refused("WEB-13", "別的原因。")]);

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(applied.Failure?.Message, Is.EqualTo("沒有權限。"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
            Assert.That(read.Value.RestoredKeys, Is.Empty);
        }
    }

    [Test]
    public async Task Apply_TheEngineRefusesTheWholeCall_ReturnsThatFailureAndStaysPending()
    {
        // Arrange
        AddDeleted("WEB-12");
        var proposal = await CreatedAsync("WEB-12");
        Bulk.OnRestore = _ => new Failure(FailureKind.Conflict, WorkErrorCodes.ProjectArchived, "專案已經封存。");

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectArchived));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task Apply_TimesOut_IsAClearFailureAndStaysPendingSoPressingAgainIsSafe()
    {
        // Arrange
        AddDeleted("WEB-12");
        var proposal = await CreatedAsync("WEB-12");
        Bulk.OnRestore = _ => throw new OperationCanceledException();

        // Act
        var applied = await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None);
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(applied.Failure?.Code, Is.EqualTo(AiErrorCodes.ApplyTimedOut));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Pending));
        }
    }

    [Test]
    public async Task ApplyAndDismiss_AfterTheProposalIsClosed_AreRefused()
    {
        // Arrange
        AddDeleted("WEB-12");
        var applied = await CreatedAsync("WEB-12");
        var dismissed = await CreatedAsync("WEB-12");
        await Proposals.ApplyAsync(Me, applied.Id, CancellationToken.None);
        await Proposals.DismissAsync(Me, dismissed.Id, CancellationToken.None);

        // Act
        var results = new[]
        {
            await Proposals.ApplyAsync(Me, applied.Id, CancellationToken.None),
            await Proposals.ApplyAsync(Me, dismissed.Id, CancellationToken.None),
            await Proposals.DismissAsync(Me, applied.Id, CancellationToken.None),
        };

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.ItemRestoreProposalClosed));
            Assert.That(Bulk.Restores, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Dismiss_MarksItDismissedWithoutRestoring()
    {
        // Arrange
        AddDeleted("WEB-12");
        var proposal = await CreatedAsync("WEB-12");

        // Act
        var dismissed = await Proposals.DismissAsync(Me, proposal.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dismissed.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
            Assert.That(dismissed.Value.DecidedAt, Is.Not.Null);
            Assert.That(Bulk.Restores, Is.Empty);
        }
    }

    [Test]
    public async Task AnotherPersonOrAnUnknownIdOrAHiddenProject_IsNotFound()
    {
        // Arrange
        AddDeleted("WEB-12");
        var proposal = await CreatedAsync("WEB-12");
        var stranger = new Actor(Me.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
        var otherOrganization = new Actor(Guid.NewGuid(), Me.PersonId, IsOrganizationAdmin: false);
        var results = new List<Result<ItemRestoreProposal>>
        {
            await Proposals.GetAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.ApplyAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.DismissAsync(stranger, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(otherOrganization, proposal.Id, CancellationToken.None),
            await Proposals.GetAsync(Me, Guid.NewGuid(), CancellationToken.None),
        };
        H.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案。");

        // Act
        results.Add(await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None));
        results.Add(await Proposals.ApplyAsync(Me, proposal.Id, CancellationToken.None));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Select(r => r.Failure?.Code), Is.All.EqualTo(AiErrorCodes.ItemRestoreProposalNotFound));
            Assert.That(Bulk.Restores, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemRestores_CreatesTheProposalAndReturnsItsIdAtTheRoot()
    {
        // Arrange
        AddDeleted("WEB-12");
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_item_restores", new { itemKeys = new[] { "WEB-12" }, reason = "刪錯了" }))
            .Reply("請按復原");

        // Act
        var reply = await H.SendAsync(conversation.Id, "把剛刪的救回來");

        // Assert
        var result = LastToolResult();
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var proposalId = Guid.Parse(result.GetProperty("proposalId").GetString()!);
        var stored = await Proposals.GetAsync(Me, proposalId, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("不要說已經復原了"));
            Assert.That(tool.ProposalId, Is.EqualTo(proposalId));
            Assert.That(stored.Value.Reason, Is.EqualTo("刪錯了"));
            Assert.That(Bulk.Restores, Is.Empty);
        }
    }

    [TestCase("{}")]
    [TestCase("{\"itemKeys\":\"WEB-1\"}")]
    [TestCase("{\"itemKeys\":[1,2]}")]
    [TestCase("{\"itemKeys\":[\"WEB-1\"],\"other\":1}")]
    public async Task ProposeItemRestores_WithBadArguments_TellsTheModelAndCreatesNothing(string json)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_item_restores", ArgumentsOf(json))).Reply("好");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LastToolResult().GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(reply.Final.Parts.OfType<AssistantToolPart>().Single().ProposalId, Is.Null);
        }
    }

    [Test]
    public async Task ProposeItemRestores_WhenTheParentIsDeleted_GivesTheParentToTheModel()
    {
        // Arrange
        AddDeleted("WEB-10");
        AddDeleted("WEB-11", parentKey: "WEB-10");
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_item_restores", new { itemKeys = new[] { "WEB-11" } })).Reply("要一起復原嗎");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.ToString(), Does.Contain("WEB-10"));
        }
    }

    [Test]
    public async Task WithdrawProposal_DismissesAPendingRestoreProposal()
    {
        // Arrange
        AddDeleted("WEB-12");
        var proposal = await CreatedAsync("WEB-12");
        var conversation = await H.NewConversationAsync();
        await SeedReplyAsync(conversation.Id, proposal.Id);
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", new { proposalId = proposal.Id.ToString() })).Reply("撤回了");

        // Act
        await H.SendAsync(conversation.Id, "不要了");

        // Assert
        var result = LastToolResult();
        var read = await Proposals.GetAsync(Me, proposal.Id, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("state").GetString(), Is.EqualTo("dismissed"));
            Assert.That(read.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
        }
    }

    private async Task SeedReplyAsync(Guid conversationId, Guid proposalId)
    {
        await using var db = await H.Host.CreateDbContextAsync();
        db.Messages.Add(new Persistence.AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            OrganizationId = H.OrganizationId,
            Sequence = 1,
            Role = AssistantMessageMapper.AssistantRoleName,
            Parts = AssistantMessageMapper.WriteParts([new AssistantToolPart("c1", "propose_item_restores", AssistantToolStatus.Done, null, null) { ProposalId = proposalId }]),
            Transcript = "[]",
            Status = "complete",
            CreatedAt = H.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        await db.Conversations.Where(c => c.Id == conversationId).ExecuteUpdateAsync(set => set.SetProperty(c => c.MessageCount, 1));
    }

    private static Dictionary<string, object?> ArgumentsOf(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.ToDictionary(p => p.Key, p => (object?)p.Value);

    private JsonElement LastToolResult()
    {
        var result = H.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
