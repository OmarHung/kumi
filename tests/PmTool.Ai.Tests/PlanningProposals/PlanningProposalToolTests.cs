using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.PlanningProposals;

/// <summary>內建助理的 propose_planning_changes：參數錯誤的訊息、成功的結果帶 proposalId、帳本讀得到也撤得掉。</summary>
[TestFixture]
public sealed class PlanningProposalToolTests : ProjectWriteTestBase
{
    [Test]
    public async Task Tool_CreatesAProposalAndRecordsItOnTheToolPart()
    {
        // Arrange
        var cycle = Planning.AddCycle("舊週期", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 14));
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_planning_changes", new
            {
                changes = new object[]
                {
                    new { target = "cycle", action = "create", name = "新週期", startDate = "2027-02-01", endDate = "2027-02-14" },
                    new { target = "cycle", action = "delete", id = cycle.Id.ToString() },
                },
                reason = "重新排期",
            }))
            .Reply("請按套用");

        // Act
        var reply = await H.SendAsync(conversation.Id, "排兩週的新週期，舊的刪掉");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await H.MessagesAsync(conversation.Id)).Last().Parts.OfType<AssistantToolPart>().Single();
        var proposal = await PlanningService.GetAsync(Me, tool.ProposalId!.Value, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(stored.ProposalId, Is.EqualTo(tool.ProposalId));
            Assert.That(proposal.Value.Changes, Has.Count.EqualTo(2));
            Assert.That(proposal.Value.Reason, Is.EqualTo("重新排期"));
            Assert.That(proposal.Value.Status, Is.EqualTo(NewItemProposalStatus.Pending));
            Assert.That(Planning.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Tool_Result_CarriesTheProposalIdAndTellsTheModelNotToSayItIsDone()
    {
        // Act
        var result = await RunToolAsync("propose_planning_changes", new
        {
            changes = new[] { new { target = "milestone", action = "create", name = "上線", dueDate = "2027-03-01" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("proposalId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.GetProperty("projectKey").GetString(), Is.EqualTo("WEB"));
            Assert.That(result.GetProperty("changes")[0].GetProperty("after").GetProperty("name").GetString(), Is.EqualTo("上線"));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("按「套用」").And.Contain("不要說已經改好了"));
        }
    }

    [TestCase("""{"changes":"不是陣列"}""", "changes")]
    [TestCase("""{"changes":[1]}""", "要是物件")]
    [TestCase("""{"changes":[{"target":"cycle","action":"create","色彩":"藍"}]}""", "不認得的屬性：色彩")]
    [TestCase("""{"changes":[{"target":"week","action":"create"}]}""", "target")]
    [TestCase("""{"changes":[{"target":"cycle","action":"rename"}]}""", "action")]
    [TestCase("""{"changes":[{"target":"cycle","action":"delete","id":"不是 guid"}]}""", "id")]
    [TestCase("""{"changes":[{"target":"cycle","action":"create","name":"甲","startDate":"2027/1/1","endDate":"2027-01-02"}]}""", "startDate")]
    [TestCase("""{"changes":[{"target":"cycle","action":"create","name":5}]}""", "name 要是字串")]
    public async Task Tool_BadArguments_AreReportedAndNothingIsStored(string json, string expected)
    {
        // Arrange
        var arguments = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Act
        var result = await RunToolAsync("propose_planning_changes", arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain(expected));
        }
    }

    [Test]
    public async Task Tool_TooManyChanges_IsReportedBeforeReadingThem()
    {
        // Arrange
        var changes = Enumerable.Range(0, AiRules.MaxPlanningChangesPerProposal + 1)
            .Select(i => new { target = "milestone", action = "create", name = $"里程碑 {i}", dueDate = "2027-03-01" }).ToArray();

        // Act
        var result = await RunToolAsync("propose_planning_changes", new { changes });

        // Assert
        Assert.That(result.GetProperty("error").GetString(), Does.Contain("最多 10 筆"));
    }

    [Test]
    public async Task Tool_WithoutPermission_ReturnsTheForbiddenMessageWithWhoCanDoIt()
    {
        // Arrange
        UseProject(canManage: false, canUpdate: false);

        // Act
        var result = await RunToolAsync("propose_planning_changes", new
        {
            changes = new[] { new { target = "milestone", action = "create", name = "上線", dueDate = "2027-03-01" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("甲"));
        }
    }

    [Test]
    public async Task Ledger_ReadsAPlanningProposalAndWithdrawDismissesIt()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var created = await PlanningCreator.CreateAsync(
            Me,
            new PlanningProposalDraft("WEB", [new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Create, null) { Name = "上線", DueDate = new DateOnly(2027, 3, 1) }]),
            CancellationToken.None);
        await SeedToolReplyAsync(conversation.Id, "propose_planning_changes", created.Value.Id);
        var ledger = H.Host.Get<IAssistantProposalLedger>();

        // Act
        var listed = await ledger.ListAsync(Me, conversation.Id, CancellationToken.None);
        var withdrawn = await ledger.WithdrawAsync(Me, conversation.Id, created.Value.Id.ToString(), CancellationToken.None);
        var after = await PlanningService.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Single().Kind, Is.EqualTo(ProposalKind.Planning));
            Assert.That(listed.Single().State, Is.EqualTo(ProposalState.Pending));
            Assert.That(listed.Single().Summary, Does.Contain("新增 1 個里程碑"));
            Assert.That(withdrawn.Succeeded, Is.True, () => Describe(withdrawn.Failure));
            Assert.That(withdrawn.Value.State, Is.EqualTo(ProposalState.Dismissed));
            Assert.That(after.Value.Status, Is.EqualTo(NewItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task Ledger_WithdrawOfAnAppliedPlanningProposal_IsClosed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var created = await PlanningCreator.CreateAsync(
            Me,
            new PlanningProposalDraft("WEB", [new PlanningChangeDraft(PlanningTarget.Milestone, PlanningAction.Create, null) { Name = "上線", DueDate = new DateOnly(2027, 3, 1) }]),
            CancellationToken.None);
        await SeedToolReplyAsync(conversation.Id, "propose_planning_changes", created.Value.Id);
        await PlanningService.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Act
        var withdrawn = await H.Host.Get<IAssistantProposalLedger>().WithdrawAsync(Me, conversation.Id, created.Value.Id.ToString(), CancellationToken.None);

        // Assert
        Assert.That(withdrawn.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalClosed));
    }
}
