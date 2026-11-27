using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.CommentProposals;

/// <summary>內建助理的 propose_comment_changes 與 withdraw_proposal 對留言建議的作用。</summary>
[TestFixture]
public sealed class CommentToolTests : CommentTestBase
{
    [Test]
    public async Task ProposeCommentChanges_CreatesTheProposalAndReturnsItsIdAtTheRoot()
    {
        // Arrange
        var comment = AddComment();
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_comment_changes", new
            {
                changes = new[] { new { itemKey = "WEB-12", commentId = comment.Id.ToString(), action = "edit", body = "新的" } },
            }))
            .Reply("請按套用");

        // Act
        var reply = await H.SendAsync(conversation.Id, "改我的留言");

        // Assert
        var result = LastToolResult();
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var proposalId = Guid.Parse(result.GetProperty("proposalId").GetString()!);
        var stored = await Proposals.GetAsync(Me, proposalId, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("不要說已經改好了"));
            Assert.That(tool.ProposalId, Is.EqualTo(proposalId));
            Assert.That(stored.Value.Changes.Single().Body, Is.EqualTo("新的"));
            Assert.That(Comments.Writes, Is.Empty);
        }
    }

    [TestCase("{}")]
    [TestCase("{\"changes\":\"x\"}")]
    [TestCase("{\"changes\":[1]}")]
    [TestCase("{\"changes\":[{\"itemKey\":\"WEB-12\",\"commentId\":\"nope\",\"action\":\"edit\"}]}")]
    [TestCase("{\"changes\":[{\"itemKey\":\"WEB-12\",\"commentId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"action\":\"explode\"}]}")]
    [TestCase("{\"changes\":[{\"itemKey\":\"WEB-12\",\"commentId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"action\":\"delete\",\"extra\":1}]}")]
    [TestCase("{\"changes\":[],\"other\":1}")]
    public async Task ProposeCommentChanges_WithBadArguments_TellsTheModelAndCreatesNothing(string json)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_comment_changes", ArgumentsOf(json))).Reply("好");

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
    public async Task ProposeCommentChanges_WhenTheCreatorRefuses_GivesTheDetailsToTheModel()
    {
        // Arrange
        var others = AddComment(author: Other);
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_comment_changes", new
            {
                changes = new[] { new { itemKey = "WEB-12", commentId = others.Id.ToString(), action = "edit", body = "改別人的" } },
            }))
            .Reply("不行");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.ToString(), Does.Contain("只有作者能編輯"));
        }
    }

    [Test]
    public async Task WithdrawProposal_DismissesAPendingCommentProposal()
    {
        // Arrange
        var comment = AddComment();
        var proposal = await CreatedAsync(Delete("WEB-12", comment.Id));
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
            Parts = AssistantMessageMapper.WriteParts([new AssistantToolPart("c1", "propose_comment_changes", AssistantToolStatus.Done, null, null) { ProposalId = proposalId }]),
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
