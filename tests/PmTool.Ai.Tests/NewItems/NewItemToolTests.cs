using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.NewItems;

/// <summary>內建助理的 propose_new_items：建立建議、負責人的 me／none／編號、錯誤列出成員、工具段落記下編號。</summary>
[TestFixture]
public sealed class NewItemToolTests : NewItemProposalTestBase
{
    [Test]
    public async Task Tool_CreatesAProposalAndRecordsItOnTheToolPart()
    {
        // Arrange
        H.Projects.OnGet = (_, _) => Detail() with { Members = [new PmTool.Work.ProjectMember(Me.PersonId, "member")] };
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_new_items", new
            {
                items = new object[]
                {
                    new { @ref = "e", type = "epic", title = "搬到 AWS", assignee = "me" },
                    new { type = "task", title = "備份資料庫", parentRef = "e", priority = "high", fields = new { category = "資料" } },
                },
            }))
            .Reply("請按全部建立");

        // Act
        var reply = await H.SendAsync(conversation.Id, "把網站從 GCP 搬到 AWS");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await H.MessagesAsync(conversation.Id)).Last().Parts.OfType<AssistantToolPart>().Single();
        var proposal = await Proposals.GetAsync(Me, tool.NewItemProposalId!.Value, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(tool.ItemProposalId, Is.Null);
            Assert.That(stored.NewItemProposalId, Is.EqualTo(tool.NewItemProposalId));
            Assert.That(proposal.Value.Items, Has.Count.EqualTo(2));
            Assert.That(proposal.Value.Items[0].AssigneeId, Is.EqualTo(Me.PersonId));
            Assert.That(proposal.Value.Items[1].ParentRef, Is.EqualTo("e"));
            Assert.That(proposal.Value.Items[1].Fields["category"].GetString(), Is.EqualTo("資料"));
            Assert.That(proposal.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(H.WorkItems.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Tool_Result_TellsTheModelNotToSayItIsDone()
    {
        // Act
        var result = await RunToolAsync(new { items = new[] { new { type = "bug", title = "頁面壞了" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("projectKey").GetString(), Is.EqualTo("WEB"));
            Assert.That(result.GetProperty("items")[0].GetProperty("title").GetString(), Is.EqualTo("頁面壞了"));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("全部建立").And.Contain("不要說已經建好了"));
        }
    }

    [Test]
    public async Task Tool_AssigneeNoneAndAMemberId_AreResolved()
    {
        // Act
        var result = await RunToolAsync(new
        {
            items = new[]
            {
                new { type = "bug", title = "甲", assignee = "none" },
                new { type = "bug", title = "乙", assignee = ItemSamples.MemberId.ToString() },
            },
        });
        var proposal = await Proposals.GetAsync(Me, result.GetProperty("proposalId").GetGuid(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(proposal.Value.Items[0].AssigneeId, Is.Null);
            Assert.That(proposal.Value.Items[1].AssigneeId, Is.EqualTo(ItemSamples.MemberId));
        }
    }

    [Test]
    public async Task Tool_AssigneeThatIsNotRecognised_ListsTheProjectMembers()
    {
        // Act
        var result = await RunToolAsync(new { items = new[] { new { type = "bug", title = "甲", assignee = "王大明" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("不認得「王大明」").And.Contain("王小明").And.Contain(ItemSamples.MemberId.ToString()));
        }
    }

    [Test]
    public async Task Tool_AssigneeIdOfANonMember_ListsTheMembersFromTheCreator()
    {
        // Act
        var result = await RunToolAsync(new { items = new[] { new { type = "bug", title = "甲", assignee = Guid.NewGuid().ToString() } } });

        // Assert
        Assert.That(result.GetProperty("issues")[0].GetProperty("message").GetString(), Does.Contain("王小明"));
    }

    [Test]
    public async Task Tool_InvalidItems_IsOkFalseWithIssuesAndNoProposalPart()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_new_items", new { items = new[] { new { type = "story", title = "x" } } })).Reply("好");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(tool.NewItemProposalId, Is.Null);
        }
    }

    [TestCase("""{"items":[{"title":"沒有類型"}]}""", "type 與 title")]
    [TestCase("""{"items":["x"]}""", "要是物件")]
    [TestCase("""{"items":[{"type":"bug","title":"x","estimate":"三"}]}""", "estimate 要是數字")]
    [TestCase("""{"items":[{"type":"bug","title":"x","fields":[1]}]}""", "fields 要是")]
    [TestCase("""{"items":[{"type":"bug","title":"x","priority":3}]}""", "priority 要是字串")]
    [TestCase("""{"other":1}""", "items 要是")]
    public async Task Tool_MalformedArguments_AreExplainedToTheModel(string json, string expected)
    {
        // Act
        var result = await RunToolAsync(JsonDocument.Parse(json).RootElement.Clone());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain(expected));
        }
    }

    [Test]
    public async Task Tool_UnknownProperty_IsAToolErrorThatNamesTheItem()
    {
        // Act
        var result = await RunToolAsync(new { items = new object[] { new { type = "bug", title = "甲" }, new { type = "bug", title = "乙", status = "done" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("items[1]").And.Contain("status"));
        }
    }

    [Test]
    public async Task Tool_AssigneeThatIsNotAString_IsAToolErrorNotUnassigned()
    {
        // Act
        var result = await RunToolAsync(new { items = new[] { new { type = "bug", title = "甲", assignee = 5 } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("items[0].assignee 要是字串"));
        }
    }

    [Test]
    public async Task Tool_MoreItemsThanTheLimit_IsRefusedBeforeReadingAnyItem()
    {
        // Act：每一項都是壞的，若先逐項讀取，錯誤會是「要是物件」。
        var result = await RunToolAsync(new { items = Enumerable.Repeat("x", AiRules.MaxNewItemsPerProposal + 1).ToArray() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain(AiRules.MaxNewItemsPerProposal.ToString()).And.Not.Contain("要是物件"));
        }
    }

    [Test]
    public async Task Tool_ItemsGivenAsAJsonString_AreAccepted()
    {
        // Act
        var result = await RunToolAsync(new { items = """[{"type":"bug","title":"字串包起來的"}]""" });

        // Assert
        Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
    }

    private async Task<JsonElement> RunToolAsync(object arguments)
    {
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_new_items", arguments)).Reply("好");
        await H.SendAsync(conversation.Id);
        var result = H.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
