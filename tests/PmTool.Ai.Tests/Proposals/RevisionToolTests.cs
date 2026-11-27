using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>內建助理的 withdraw_proposal、propose_item_deletions、propose_bulk_item_changes，以及回覆開始前補上的日期與建議現況。</summary>
[TestFixture]
public sealed class RevisionToolTests : ProposalTestBase
{
    [Test]
    public async Task ProposeItemDeletions_CreatesTheProposalAndRecordsItOnTheToolPart()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_item_deletions", new { itemKeys = new[] { "WEB-45", "WEB-46" }, reason = "重複建立的" }))
            .Reply("請按刪除");

        // Act
        var reply = await H.SendAsync(conversation.Id, "把重複的刪掉");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await H.MessagesAsync(conversation.Id)).Last().Parts.OfType<AssistantToolPart>().Single();
        var result = LastToolResult();
        var draft = Creators.DeletionDrafts.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(tool.ItemDeletionProposalId, Is.Not.Null);
            Assert.That(tool.ItemBatchProposalId, Is.Null);
            Assert.That(stored.ItemDeletionProposalId, Is.EqualTo(tool.ItemDeletionProposalId));
            Assert.That(draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(draft.ItemKeys, Is.EqualTo(new[] { "WEB-45", "WEB-46" }));
            Assert.That(draft.Reason, Is.EqualTo("重複建立的"));
            Assert.That(draft.ConversationId, Is.EqualTo(conversation.Id));
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("proposalId").GetString(), Is.EqualTo(tool.ItemDeletionProposalId.ToString()));
            Assert.That(result.GetProperty("itemKeys").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "WEB-45", "WEB-46" }));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("按「刪除」才會刪除").And.Contain("不要說已經刪了"));
        }
    }

    [TestCase("{\"itemKeys\":\"WEB-1\"}")]
    [TestCase("{\"itemKeys\":[1,2]}")]
    [TestCase("{}")]
    public async Task ProposeItemDeletions_WithBadArguments_TellsTheModelAndCreatesNothing(string json)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_item_deletions", ArgumentsOf(json))).Reply("好");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("itemKeys"));
            Assert.That(Creators.DeletionDrafts, Is.Empty);
            Assert.That(reply.Final.Parts.OfType<AssistantToolPart>().Single().ItemDeletionProposalId, Is.Null);
        }
    }

    [Test]
    public async Task ProposeItemDeletions_WhenTheCreatorRefuses_GivesTheMessageAndDetailsToTheModelAsIs()
    {
        // Arrange
        Creators.DeletionFailure = new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "有檢查沒通過。")
        {
            Details = [new FailureDetail("ai.field_invalid", "itemKeys[0]", "底下還有下層：WEB-46、WEB-47。")],
        };
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_item_deletions", new { itemKeys = new[] { "WEB-45" } })).Reply("好");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("issues")[0].GetProperty("path").GetString(), Is.EqualTo("itemKeys[0]"));
            Assert.That(result.GetProperty("issues")[0].GetProperty("message").GetString(), Does.Contain("WEB-46"));
            Assert.That(reply.Final.Parts.OfType<AssistantToolPart>().Single().Status, Is.EqualTo(AssistantToolStatus.Failed));
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_CreatesOneProposalWithEachItemsOwnChanges()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_bulk_item_changes", new
            {
                items = new object[]
                {
                    new { itemKey = "WEB-1", changes = new[] { new { field = "dueDate", value = "2026-12-01" } }, comment = "順延" },
                    new { itemKey = "WEB-2", changes = new[] { new { field = "dueDate", value = "2026-12-08" } } },
                    new { itemKey = "WEB-3", changes = new[] { new { field = "description", value = "新說明" } }, expectedDescriptionRevision = "rev-1" },
                },
            }))
            .Reply("請按全部套用");

        // Act
        var reply = await H.SendAsync(conversation.Id, "把到期日往後");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var draft = Creators.BatchDrafts.Single();
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.ItemBatchProposalId, Is.Not.Null);
            Assert.That(tool.ItemDeletionProposalId, Is.Null);
            Assert.That(tool.ItemProposalId, Is.Null);
            Assert.That(draft.ConversationId, Is.EqualTo(conversation.Id));
            Assert.That(draft.Items.Select(i => i.ItemKey), Is.EqualTo(new[] { "WEB-1", "WEB-2", "WEB-3" }));
            Assert.That(draft.Items[0].Comment, Is.EqualTo("順延"));
            Assert.That(draft.Items[0].Changes.Single().Value.GetString(), Is.EqualTo("2026-12-01"));
            Assert.That(draft.Items[1].Changes.Single().Value.GetString(), Is.EqualTo("2026-12-08"));
            Assert.That(draft.Items[2].ExpectedDescriptionRevision, Is.EqualTo("rev-1"));
            Assert.That(result.GetProperty("itemKeys").GetArrayLength(), Is.EqualTo(3));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("按「全部套用」才會改").And.Contain("不要說已經改好了"));
        }
    }

    [TestCase("{\"items\":\"WEB-1\"}", "items")]
    [TestCase("{\"items\":[\"WEB-1\"]}", "items[0] 要是物件")]
    [TestCase("{\"items\":[{\"changes\":[]}]}", "items[0].itemKey")]
    [TestCase("{\"items\":[{\"itemKey\":\"WEB-1\"},{\"itemKey\":\"WEB-2\",\"changes\":\"x\"}]}", "items[1].changes")]
    [TestCase("{\"items\":[{\"itemKey\":\"WEB-1\",\"changes\":[{\"value\":1}]}]}", "items[0].changes")]
    [TestCase("{\"items\":[{\"itemKey\":\"WEB-1\",\"comment\":5}]}", "items[0].comment")]
    public async Task ProposeBulkItemChanges_WithBadArguments_NamesTheItemThatIsWrong(string json, string expected)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_bulk_item_changes", ArgumentsOf(json))).Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain(expected));
            Assert.That(Creators.BatchDrafts, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeBulkItemChanges_WhenTheUserLacksThePermission_PassesTheRefusalToTheModelAsIs()
    {
        // Arrange
        Creators.BatchFailure = new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有修改項目的權限。");
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "propose_bulk_item_changes", new { items = new[] { new { itemKey = "WEB-1" } } })).Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Is.EqualTo("你沒有修改項目的權限。"));
        }
    }

    [Test]
    public async Task WithdrawProposal_DismissesThePendingProposal_AndTheToolPartStaysWithoutAProposalId()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", new { proposalId = proposal.Id.ToString() })).Reply("撤回了");

        // Act
        var reply = await H.SendAsync(conversation.Id, "不要了");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(tool.ItemProposalId, Is.Null, "撤回不是提出，工具段落不記建議編號");
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("proposalId").GetString(), Is.EqualTo(proposal.Id.ToString()));
            Assert.That(result.GetProperty("kind").GetString(), Is.EqualTo("item_changes"));
            Assert.That(result.GetProperty("state").GetString(), Is.EqualTo("dismissed"));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("已經撤回這份建議"));
            Assert.That(Proposals.Items[proposal.Id].Status, Is.EqualTo(ItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task WithdrawProposal_ForAnAppliedProposal_TellsTheModelItIsClosed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId, ItemProposalStatus.Applied);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", new { proposalId = proposal.Id.ToString() })).Reply("撤不了");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("不能撤回"));
            Assert.That(reply.Final.Parts.OfType<AssistantToolPart>().Single().Status, Is.EqualTo(AssistantToolStatus.Failed));
        }
    }

    [TestCase("{}")]
    [TestCase("{\"proposalId\":\"\"}")]
    public async Task WithdrawProposal_WithoutAReference_AsksForIt(string json)
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", ArgumentsOf(json))).Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        Assert.That(LastToolResult().GetProperty("error").GetString(), Is.EqualTo("缺少參數 proposalId。"));
    }

    [Test]
    public async Task WithdrawProposal_ForAChangeSet_SaysOnlyApproversCanReject()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_changeset") with { ChangeSetNumber = 12 });
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", new { proposalId = "changeset:12" })).Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        Assert.That(LastToolResult().GetProperty("error").GetString(), Does.Contain("變更集不能撤回"));
    }

    [Test]
    public async Task OrganizationChat_CanWithdrawItsOwnProjectProposal()
    {
        // Arrange
        var conversation = await H.NewOrganizationConversationAsync();
        var proposal = new ProjectProposal(Guid.CreateVersion7(), "NEW", "網站", "simple_tasks", "簡單任務", false, null, ProjectProposalStatus.Pending, DateTimeOffset.UnixEpoch);
        Proposals.Add(Me.PersonId, proposal);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_project") with { ProjectProposalId = proposal.Id });
        H.Script.Enqueue(Turn.Create().Call("call-1", "withdraw_proposal", new { proposalId = proposal.Id.ToString() })).Reply("撤回了");

        // Act
        await H.SendOrganizationAsync(conversation.Id, "代號改 SITE");

        // Assert
        var result = LastToolResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("kind").GetString(), Is.EqualTo("project"));
            Assert.That(Proposals.Projects[proposal.Id].Status, Is.EqualTo(ProjectProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task Reply_TellsTheModelTheCurrentTimeInTheOrganizationTimeZone()
    {
        // Arrange：ManualClock 是 2026-11-18 08:00 UTC，台北 16:00（星期三）。
        var conversation = await H.NewConversationAsync();
        H.Script.Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        Assert.That(LastUserText(0), Does.Contain("現在是 2026-11-18（星期三）16:00，時區 Asia/Taipei"));
        Assert.That(SystemMessages(0), Has.None.Contain("現在是 20"));
    }

    [Test]
    public async Task Reply_AfterMidnightInTheOrganizationTimeZone_UsesTheNextDay()
    {
        // Arrange：UTC 16:30 已經是台北隔天 00:30。
        H.Clock.Advance(TimeSpan.FromHours(8.5));
        var conversation = await H.NewConversationAsync();
        H.Script.Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        Assert.That(LastUserText(0), Does.Contain("現在是 2026-11-19（星期四）00:30"));
    }

    [Test]
    public async Task OrganizationReply_AlsoGetsTheCurrentTime()
    {
        // Arrange
        var conversation = await H.NewOrganizationConversationAsync();
        H.Script.Reply("好");

        // Act
        await H.SendOrganizationAsync(conversation.Id);

        // Assert
        Assert.That(LastUserText(0), Does.Contain("現在是 2026-11-18（星期三）16:00"));
    }

    [Test]
    public async Task Reply_WithNoEarlierProposals_AddsNoProposalSection()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        H.Script.Reply("好");

        // Act
        await H.SendAsync(conversation.Id);

        // Assert
        Assert.That(LastUserText(0), Does.Not.Contain("它們現在的狀態"));
    }

    [Test]
    public async Task Reply_ListsTheProposalsOfTheConversationWithTheirCurrentState_InFrontOfTheLastUserMessage()
    {
        // Arrange：07:51 使用者已經按了「全部建立」；助理接著被問「那七個不要按對吧」。
        var conversation = await H.NewConversationAsync();
        var proposal = NewItemsOf(2, ["WEB-38", "WEB-39"], NewItemProposalStatus.Applied);
        Proposals.Add(Me.PersonId, proposal);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_new_items") with { NewItemProposalId = proposal.Id });
        H.Script.Reply("已經建立了");

        // Act
        await H.SendAsync(conversation.Id, "第一份不要按對吧");

        // Assert
        var last = LastUserText(0);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(last, Does.StartWith(AssistantPrompt.TurnNotePrefix));
            Assert.That(last, Does.Contain($"{proposal.Id}｜新增項目｜已套用").And.Contain("新增 2 個項目，已建立 WEB-38、WEB-39"));
            Assert.That(last, Does.EndWith("第一份不要按對吧"));
            Assert.That(SystemMessages(0), Has.None.Contain("它們現在的狀態"));
        }
    }

    [Test]
    public async Task Reply_WhenTheStateCannotBeRead_StillAnswers()
    {
        // Arrange：讀建議現況失敗不能讓整則回覆失敗。
        var conversation = await H.NewConversationAsync();
        var proposal = AddItemProposal(Me.PersonId);
        await SeedReplyAsync(conversation.Id, 1, Tool("propose_item_changes") with { ItemProposalId = proposal.Id });
        Proposals.FailReads = true;
        H.Script.Reply("好");

        // Act
        var reply = await H.SendAsync(conversation.Id);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(LastUserText(0), Does.Not.Contain("它們現在的狀態"));
            Assert.That(LastUserText(0), Does.Contain("現在是"));
        }
    }

    private string LastUserText(int call) => H.Script.Calls[call].Messages.Last(m => m.Role == ChatRole.User).Text;

    private IReadOnlyList<string> SystemMessages(int call) =>
        [.. H.Script.Calls[call].Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text)];

    private static Dictionary<string, object?> ArgumentsOf(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.ToDictionary(p => p.Key, p => (object?)p.Value);

    private JsonElement LastToolResult()
    {
        var result = H.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
