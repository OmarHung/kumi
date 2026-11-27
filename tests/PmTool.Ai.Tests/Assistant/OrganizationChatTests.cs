using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>不選專案的組織對話：和專案對話分開、不碰專案的權限、不收項目（附件見 OrganizationAttachmentTests）、工具與系統提示是組織版。</summary>
[TestFixture]
public sealed class OrganizationChatTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task Create_OrganizationConversation_HasNoProjectAndNeverAsksTheProjectService()
    {
        // Act
        var conversation = await _h.NewOrganizationConversationAsync();

        // Assert
        var row = await _h.ConversationRowAsync(conversation.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.ProjectId, Is.Null);
            Assert.That(row.SawAllItems, Is.False);
            Assert.That(_h.Projects.Gets, Is.Empty);
        }
    }

    [Test]
    public async Task Create_WhenTheOrganizationHasNoAi_IsUnavailable()
    {
        // Arrange
        await _h.DisposeAsync();
        _h = await AssistantHarness.StartAsync(enabled: false);

        // Act
        var created = await _h.Service.CreateAsync(_h.Actor, null, CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(AiErrorCodes.Unavailable));
    }

    [Test]
    public async Task List_OrganizationAndProjectConversations_AreKeptApart()
    {
        // Arrange
        var inProject = await _h.NewConversationAsync();
        var organization = await _h.NewOrganizationConversationAsync();

        // Act
        var organizationList = (await _h.Service.ListAsync(_h.Actor, null, CancellationToken.None)).Value;
        var projectList = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(organizationList.Select(c => c.Id), Is.EqualTo(new[] { organization.Id }));
            Assert.That(projectList.Select(c => c.Id), Is.EqualTo(new[] { inProject.Id }));
        }
    }

    [Test]
    public async Task List_OnlyMyOwnOrganizationConversations()
    {
        // Arrange
        var mine = await _h.NewOrganizationConversationAsync();
        await _h.NewOrganizationConversationAsync(_h.Admin);

        // Act
        var listed = (await _h.Service.ListAsync(_h.Actor, null, CancellationToken.None)).Value;

        // Assert
        Assert.That(listed.Select(c => c.Id), Is.EqualTo(new[] { mine.Id }));
    }

    [Test]
    public async Task Get_OrganizationConversationThroughAProject_IsNotFound()
    {
        // Arrange
        var organization = await _h.NewOrganizationConversationAsync();

        // Act
        var viaProject = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, organization.Id, CancellationToken.None);

        // Assert
        Assert.That(viaProject.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
    }

    [Test]
    public async Task Get_ProjectConversationThroughTheOrganization_IsNotFound()
    {
        // Arrange
        var inProject = await _h.NewConversationAsync();

        // Act
        var viaOrganization = await _h.Service.GetAsync(_h.Actor, null, inProject.Id, CancellationToken.None);

        // Assert
        Assert.That(viaOrganization.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
    }

    [Test]
    public async Task Get_SomeoneElsesOrganizationConversation_IsNotFound()
    {
        // Arrange
        var theirs = await _h.NewOrganizationConversationAsync(_h.Admin);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, null, theirs.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
    }

    [Test]
    public async Task Delete_OrganizationConversation_RemovesItAndLeavesProjectOnesAlone()
    {
        // Arrange
        var inProject = await _h.NewConversationAsync();
        var organization = await _h.NewOrganizationConversationAsync();

        // Act
        var deleted = await _h.Service.DeleteAsync(_h.Actor, null, organization.Id, CancellationToken.None);
        var viaProject = await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, organization.Id, CancellationToken.None);
        var viaOrganization = await _h.Service.DeleteAsync(_h.Actor, null, inProject.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(viaProject.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(viaOrganization.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That((await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Start_WithAnItem_IsRejectedAndStoresNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();

        // Act
        var started = await _h.Service.StartAsync(
            _h.Actor, null, conversation.Id, new AssistantMessageInput("看看", _h.Selection) { ItemKey = "WEB-12" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.OrganizationChatUnsupported));
            Assert.That(started.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That((await _h.ConversationRowAsync(conversation.Id)).MessageCount, Is.Zero);
        }
    }

    [Test]
    public async Task Reply_UsesTheOrganizationPromptAndOnlyTheFiveTools()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        _h.Script.Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, actor: _h.Admin);

        // Assert
        var call = _h.Script.Calls.Single();
        var system = call.Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(system, Has.Count.EqualTo(2));
            Assert.That(system[0], Is.EqualTo(AssistantPrompt.OrganizationSystemPrompt));
            Assert.That(system[1], Does.StartWith("這則對話不屬於任何專案。使用者能開新專案"));
            Assert.That(call.Options!.Tools!.Select(t => t.Name), Is.EquivalentTo(new[] { "list_templates", "get_template", "list_projects", "propose_project", "withdraw_proposal" }));
            Assert.That(_h.Projects.Gets, Is.Empty);
        }
    }

    [Test]
    public async Task Reply_ForAMember_TellsTheModelTheyCannotOpenProjects()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id);

        // Assert
        var note = _h.Script.Calls.Single().Messages.Where(m => m.Role == ChatRole.System).Select(m => m.Text).Last();
        Assert.That(note, Does.StartWith("這則對話不屬於任何專案。使用者不能開新專案"));
    }

    [Test]
    public async Task Reply_ReservesUsageWithoutAProjectKey()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        var usage = await db.ConnectionUsage.AsNoTracking().SingleAsync();
        Assert.That(usage.ProjectKey, Is.Null);
    }

    [Test]
    public async Task ListProjects_ReturnsWhatTheAskerCanSee()
    {
        // Arrange
        _h.Projects.OnList = _ =>
        [
            new ProjectSummary(Guid.NewGuid(), "WEB", "網站改版", 3, "admin", DateTimeOffset.UnixEpoch),
            new ProjectSummary(Guid.NewGuid(), "OLD", "舊專案", 1, null, DateTimeOffset.UnixEpoch) { ArchivedAt = DateTimeOffset.UnixEpoch },
        ];
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("c1", "list_projects").Finish(ChatFinishReason.ToolCalls));
        _h.Script.Reply("你有兩個專案");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "我有哪些專案");

        // Assert
        var result = _h.Script.Calls[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result!.ToString()!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That(result, Does.Contain("\"key\":\"WEB\"").And.Contain("\"key\":\"OLD\""));
            Assert.That(result, Does.Contain("\"archived\":true").And.Contain("\"archived\":false"));
        }
    }

    [Test]
    public async Task Guest_CanOpenAndSendInAnOrganizationConversation()
    {
        // Arrange：訪客不是任何專案的成員，也不是管理員；組織能用 AI 就能問模板與專案結構。
        var guest = new Actor(_h.OrganizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);
        var conversation = await _h.NewOrganizationConversationAsync(guest);
        _h.Script.Reply("可以用簡單任務");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "哪個模板適合？", guest);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
            Assert.That((await _h.OrganizationMessagesAsync(conversation.Id, guest)), Has.Count.EqualTo(2));
        }
    }
}
