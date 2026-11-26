using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ProjectProposals;

/// <summary>內建助理的 propose_project：提出的人要是管理員、一則回覆最多一份、建議編號存進訊息並在重讀時還原。</summary>
[TestFixture]
public sealed class ProposeProjectToolTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private static object Arguments(string key = "NEWP") => new { key, name = "新專案", template = "simple_tasks", includeExamples = true, description = "## 目的" };

    private string ResultOfCall(int call) =>
        _h.Script.Calls[call].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result!.ToString()!;

    [Test]
    public async Task ProposeProject_ByAnAdmin_CreatesAProposalAndRecordsItOnTheToolPart()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script.Enqueue(Turn.Create().Call("c1", "propose_project", Arguments())).Reply("請按建立專案");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "開 NEWP", _h.Admin);

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        var stored = (await _h.OrganizationMessagesAsync(conversation.Id, _h.Admin)).Last().Parts.OfType<AssistantToolPart>().Single();
        var proposal = await _h.Host.Get<IProjectProposalService>().GetAsync(_h.Admin, tool.ProjectProposalId!.Value, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Done));
            Assert.That(tool.ItemProposalId, Is.Null);
            Assert.That(stored.ProjectProposalId, Is.EqualTo(tool.ProjectProposalId), "重讀對話時建議編號還在");
            Assert.That(proposal.Value.Key, Is.EqualTo("NEWP"));
            Assert.That(proposal.Value.IncludeExamples, Is.True);
            Assert.That(proposal.Value.Status, Is.EqualTo(ProjectProposalStatus.Pending));
            Assert.That(ResultOfCall(1), Does.Contain("不要說已經建好了"));
        }
    }

    [Test]
    public async Task ProposeProject_RecordsTheConversationOnTheProposal()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script.Enqueue(Turn.Create().Call("c1", "propose_project", Arguments())).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "開 NEWP", _h.Admin);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.ProjectProposals);
        Assert.That(row.ConversationId, Is.EqualTo(conversation.Id));
    }

    [Test]
    public async Task ProposeProject_ByANonAdmin_IsAToolErrorAndStoresNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("c1", "propose_project", Arguments())).Reply("你不能開");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "開 NEWP");

        // Assert
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Status, Is.EqualTo(AssistantToolStatus.Failed));
            Assert.That(tool.Error, Does.Contain("只有組織管理員"));
            Assert.That(tool.ProjectProposalId, Is.Null);
            Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.ProjectProposals), Is.Zero);
        }
    }

    [Test]
    public async Task ProposeProject_WhenTheEngineRejectsTheKey_TheModelSeesItsMessageAndIssues()
    {
        // Arrange
        _h.Projects.OnCheckNewProject = (_, _) => WorkSamples.Failure(
            FailureKind.Invalid, WorkErrorCodes.ProjectKeyInvalid, "新專案不合格式。",
            new FailureDetail(WorkErrorCodes.ProjectKeyInvalid, "key", "專案代號不合格式。"));
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script.Enqueue(Turn.Create().Call("c1", "propose_project", Arguments("bad"))).Reply("換一個");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "開 bad", _h.Admin);

        // Assert
        Assert.That(ResultOfCall(1), Does.Contain("\"ok\":false").And.Contain("新專案不合格式。").And.Contain("專案代號不合格式。"));
    }

    [Test]
    public async Task ProposeProject_SecondOneInTheSameReply_IsRefused()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script
            .Enqueue(Turn.Create().Call("c1", "propose_project", Arguments("ONE")).Call("c2", "propose_project", Arguments("TWO")))
            .Reply("好");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "開兩個", _h.Admin);

        // Assert
        var tools = reply.Final.Parts.OfType<AssistantToolPart>().ToList();
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(t => t.Status), Is.EqualTo(new[] { AssistantToolStatus.Done, AssistantToolStatus.Failed }));
            Assert.That(tools[1].Error, Does.Contain("最多只能提出 1 份"));
            Assert.That(await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.ProjectProposals), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ProposeProject_AfterAFailedAttempt_AllowsAnotherInTheSameReply()
    {
        // Arrange
        var rejected = true;
        _h.Projects.OnCheckNewProject = (_, _) => rejected
            ? WorkSamples.Failure(FailureKind.Conflict, WorkErrorCodes.ProjectKeyTaken, "代號用掉了。")
            : Unit.Value;
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script
            .Enqueue(Turn.Create().Call("c1", "propose_project", Arguments("ONE")))
            .Enqueue(Turn.Create().Do(() => rejected = false).Call("c2", "propose_project", Arguments("TWO")))
            .Reply("好");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "開 ONE", _h.Admin);

        // Assert
        Assert.That(
            reply.Final.Parts.OfType<AssistantToolPart>().Select(t => t.Status),
            Is.EqualTo(new[] { AssistantToolStatus.Failed, AssistantToolStatus.Done }));
    }
}
