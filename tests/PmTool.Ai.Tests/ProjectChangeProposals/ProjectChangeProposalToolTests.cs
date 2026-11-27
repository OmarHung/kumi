using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.PlanningProposals;
using PmTool.Work;

namespace PmTool.Ai.Tests.ProjectChangeProposals;

/// <summary>內建助理的 propose_project_changes：參數的讀法（null 是清掉說明）、錯誤、成功結果帶 proposalId、帳本與撤回。</summary>
[TestFixture]
public sealed class ProjectChangeProposalToolTests : ProjectWriteTestBase
{
    [Test]
    public async Task Tool_CreatesAProposalAndRecordsItOnTheToolPart()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("舊", new DateTimeOffset(2027, 1, 5, 8, 0, 0, TimeSpan.Zero), Manager);
        var conversation = await H.NewConversationAsync();
        H.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_project_changes", new { name = "新名稱", archived = true, description = "新說明", reason = "結案" }))
            .Reply("請按套用");

        // Act
        var reply = await H.SendAsync(conversation.Id, "把專案改名並封存");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await H.MessagesAsync(conversation.Id)).Last().Parts.OfType<AssistantToolPart>().Single();
        var proposal = await ChangeService.GetAsync(Me, tool.ProposalId!.Value, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(stored.ProposalId, Is.EqualTo(tool.ProposalId));
            Assert.That(proposal.Value.Name, Is.EqualTo("新名稱"));
            Assert.That(proposal.Value.Archived, Is.True);
            Assert.That(proposal.Value.Description, Is.EqualTo("新說明"));
            Assert.That(proposal.Value.Reason, Is.EqualTo("結案"));
            Assert.That(Projects.Writes, Is.Empty);
        }
    }

    [Test]
    public async Task Tool_Result_CarriesTheProposalIdAndTellsTheModelNotToSayItIsDone()
    {
        // Act
        var result = await RunToolAsync("propose_project_changes", new { name = "新名稱" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("proposalId").GetGuid(), Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.GetProperty("name").GetString(), Is.EqualTo("新名稱"));
            Assert.That(result.GetProperty("changesDescription").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("按「套用」").And.Contain("不要說已經改好了"));
        }
    }

    [Test]
    public async Task Tool_DescriptionEmptyOrBlank_ClearsItButNullOrMissingMeansNoChange()
    {
        // Arrange
        Descriptions.Current = new ProjectDescription("有說明", new DateTimeOffset(2027, 1, 5, 8, 0, 0, TimeSpan.Zero), Manager);

        // Act
        var empty = await RunToolAsync("propose_project_changes", new { description = "" });
        var blank = await RunToolAsync("propose_project_changes", new { description = "  \n " });
        var nullWithName = await RunToolAsync("propose_project_changes", new { name = "新名稱", description = (string?)null });
        var nullOnly = await RunToolAsync("propose_project_changes", new { description = (string?)null });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty.GetProperty("clearsDescription").GetBoolean(), Is.True);
            Assert.That(blank.GetProperty("clearsDescription").GetBoolean(), Is.True);
            Assert.That(nullWithName.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(nullWithName.GetProperty("changesDescription").GetBoolean(), Is.False, "null 是不改，不是清掉");
            Assert.That(nullOnly.GetProperty("ok").GetBoolean(), Is.False, "只給 null 等於什麼都沒給");
        }
    }

    [TestCase("""{"nameX":"x"}""", "不認得的參數：nameX")]
    [TestCase("""{"name":5}""", "name 要是字串")]
    [TestCase("""{"archived":"yes"}""", "archived 要是 true 或 false")]
    [TestCase("""{"description":5}""", "description 要是字串")]
    [TestCase("""{"reason":5}""", "reason 要是字串")]
    public async Task Tool_BadArguments_AreReported(string json, string expected)
    {
        // Arrange
        var arguments = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        // Act
        var result = await RunToolAsync("propose_project_changes", arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain(expected));
        }
    }

    [Test]
    public async Task Tool_NothingToChange_ReturnsTheIssues()
    {
        // Act
        var result = await RunToolAsync("propose_project_changes", new { name = "網站改版" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("issues")[0].GetProperty("message").GetString(), Does.Contain("一樣"));
        }
    }

    [Test]
    public async Task Tool_WithoutProjectManage_ReturnsTheForbiddenMessageWithWhoCanDoIt()
    {
        // Arrange
        UseProject(canManage: false);

        // Act
        var result = await RunToolAsync("propose_project_changes", new { name = "新名稱" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("甲"));
        }
    }

    [Test]
    public async Task Ledger_ReadsTheProposalWithoutNamesAndWithdrawDismissesIt()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var created = await ChangeCreator.CreateAsync(
            Me, new ProjectChangeProposalDraft("WEB", "祕密專案名", true, default), CancellationToken.None);
        await SeedToolReplyAsync(conversation.Id, "propose_project_changes", created.Value.Id);
        var ledger = H.Host.Get<IAssistantProposalLedger>();

        // Act
        var listed = await ledger.ListAsync(Me, conversation.Id, CancellationToken.None);
        var withdrawn = await ledger.WithdrawAsync(Me, conversation.Id, created.Value.Id.ToString(), CancellationToken.None);
        var after = await ChangeService.GetAsync(Me, created.Value.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Single().Kind, Is.EqualTo(ProposalKind.ProjectChanges));
            Assert.That(listed.Single().Summary, Does.Contain("改名稱").And.Contain("封存").And.Not.Contain("祕密專案名"));
            Assert.That(withdrawn.Succeeded, Is.True, () => Describe(withdrawn.Failure));
            Assert.That(after.Value.Status, Is.EqualTo(ItemProposalStatus.Dismissed));
        }
    }

    [Test]
    public async Task Ledger_WithdrawOfAnAppliedProposal_IsClosed()
    {
        // Arrange
        var conversation = await H.NewConversationAsync();
        var created = await ChangeCreator.CreateAsync(
            Me, new ProjectChangeProposalDraft("WEB", "新名稱", null, default), CancellationToken.None);
        await SeedToolReplyAsync(conversation.Id, "propose_project_changes", created.Value.Id);
        await ChangeService.ApplyAsync(Me, created.Value.Id, CancellationToken.None);

        // Act
        var withdrawn = await H.Host.Get<IAssistantProposalLedger>().WithdrawAsync(Me, conversation.Id, created.Value.Id.ToString(), CancellationToken.None);

        // Assert
        Assert.That(withdrawn.Failure?.Code, Is.EqualTo(AiErrorCodes.ProposalClosed));
    }
}
