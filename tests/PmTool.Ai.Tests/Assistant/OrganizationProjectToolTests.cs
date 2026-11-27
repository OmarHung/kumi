using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Assistant.Tools;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Ai.Tests.ItemFinding;
using PmTool.Ai.Tests.Proposals;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>
/// 組織對話用專案的工具（每個多一個必填的 projectKey）：看得到的專案才能碰、讀過的專案記進對話（只記看得到全部項目的）、
/// 建議照舊掛在原本的對話；專案對話的工具不變。
/// </summary>
[TestFixture]
public sealed class OrganizationProjectToolTests
{
    private static readonly Guid WebId = WorkSamples.ProjectId;

    private AssistantHarness _h = null!;
    private FakeWorkAccess _access = null!;
    private FakeRevisionCreators _creators = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _access = new FakeWorkAccess();
        _access.Set(WebId, "WEB", FakeWorkAccess.Reader);
        _creators = new FakeRevisionCreators();
        _h = await AssistantHarness.StartAsync(configure: services =>
        {
            services.AddSingleton<IWorkAccessQueries>(_access);
            services.AddSingleton<IItemDeletionProposalCreator>(_creators);
        });
        _h.Projects.OnGet = (_, key) => key.Equals("WEB", StringComparison.OrdinalIgnoreCase)
            ? ItemSamples.DetailWithMember()
            : WorkSamples.Failure(FailureKind.NotFound, "work.project_not_found", "找不到這個專案。");
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public void OrganizationToolSet_HasEveryProjectToolWithARequiredProjectKey()
    {
        // Arrange
        var projectTools = AssistantToolSet.Create(_h.NewToolContext("WEB")).Select(t => t.Name!).ToHashSet();
        var organizationOwn = new[] { "list_projects", "propose_project" };

        // Act
        var tools = AssistantToolSet.Create(_h.NewToolContext(null)).OfType<AIFunction>().ToList();

        // Assert
        var names = tools.Select(t => t.Name).ToList();
        var wrapped = tools.Where(t => t.JsonSchema.GetProperty("properties").TryGetProperty("projectKey", out _)
            && t.JsonSchema.TryGetProperty("required", out var required)
            && required.EnumerateArray().Any(r => r.GetString() == "projectKey")).Select(t => t.Name).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.Unique);
            Assert.That(names, Is.SupersetOf(projectTools));
            Assert.That(names, Is.SupersetOf(organizationOwn));
            Assert.That(wrapped, Is.SupersetOf(new[] { "get_item", "propose_item_deletions", "get_project", "propose_item_changes", "get_blueprint" }));
            Assert.That(wrapped, Does.Not.Contain("list_projects"));
        }
    }

    [Test]
    public async Task GetItem_InAnOrganizationConversationWithAProjectKey_ReadsTheItem()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "get_item", new { projectKey = "WEB", itemKey = "WEB-12" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "看一下 WEB-12");

        // Assert
        var result = LastResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(result.GetProperty("item").GetProperty("key").GetString(), Is.EqualTo("WEB-12"));
        }
    }

    [Test]
    public async Task GetItem_WithoutAProjectKey_TellsTheModelToGiveOne()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "get_item", new { itemKey = "WEB-12" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "看一下 WEB-12");

        // Assert
        var result = LastResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("projectKey"));
        }
    }

    [Test]
    public async Task GetItem_ForAProjectTheUserCannotSee_IsNotFoundAndRemembersNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "get_item", new { projectKey = "SECRET", itemKey = "SECRET-1" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "看一下 SECRET-1");

        // Assert
        var result = LastResult();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("SECRET"));
            Assert.That(await ReadProjectsAsync(conversation.Id), Is.Empty);
        }
    }

    [Test]
    public async Task ProposeItemDeletions_InAnOrganizationConversation_CreatesTheProposalOnThatProjectAndConversation()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script
            .Enqueue(Turn.Create().Call("call-1", "propose_item_deletions", new { projectKey = "web", itemKeys = new[] { "WEB-12" }, reason = "重複" }))
            .Reply("請按刪除");

        // Act
        var reply = await _h.SendOrganizationAsync(conversation.Id, "刪掉 WEB-12");

        // Assert
        var draft = _creators.DeletionDrafts.Single();
        var tool = reply.Final.Parts.OfType<AssistantToolPart>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LastResult().GetProperty("ok").GetBoolean(), Is.True);
            Assert.That(draft.ProjectKey, Is.EqualTo("WEB"));
            Assert.That(draft.ConversationId, Is.EqualTo(conversation.Id));
            Assert.That(tool.ItemDeletionProposalId, Is.Not.Null);
        }
    }

    [Test]
    public async Task GetItem_ByAnUserWhoSeesEveryItem_RecordsTheProjectOnTheConversation()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "get_item", new { projectKey = "WEB", itemKey = "WEB-12" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "看一下 WEB-12");

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.EqualTo(new[] { WebId }));
    }

    [Test]
    public async Task GetItem_ByAnUserWhoOnlySeesTheirOwnItems_RecordsNothing()
    {
        // Arrange：只看自己項目的人，專案記下來只會讓對話馬上被刪。
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);
        var conversation = await _h.NewOrganizationConversationAsync();
        _h.Script.Enqueue(Turn.Create().Call("call-1", "get_item", new { projectKey = "WEB", itemKey = "WEB-12" })).Reply("好");

        // Act
        await _h.SendOrganizationAsync(conversation.Id, "看一下 WEB-12");

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.Empty);
    }

    [Test]
    public async Task ConversationReads_RememberAProjectOnSomeoneElsesConversation_WritesNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        var reads = _h.Host.Get<IAssistantConversationReads>();

        // Act
        await reads.RememberAsync(_h.Admin, conversation.Id, ["WEB"], CancellationToken.None);

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.Empty);
    }

    [Test]
    public async Task Reply_InAProjectConversation_StillOffersTheProjectToolsWithoutAProjectKey()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");

        // Act
        await _h.SendAsync(conversation.Id);

        // Assert
        var tools = _h.Script.Calls.Single().Options!.Tools!.OfType<AIFunction>().ToList();
        var getItem = tools.Single(t => t.Name == "get_item");
        Assert.That(getItem.JsonSchema.GetProperty("properties").TryGetProperty("projectKey", out _), Is.False);
    }

    private async Task<IReadOnlyList<Guid>> ReadProjectsAsync(Guid conversationId)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.ConversationReads.Where(r => r.ConversationId == conversationId).Select(r => r.ProjectId).ToListAsync();
    }

    private JsonElement LastResult()
    {
        var result = _h.Script.Calls.Last().Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last();
        using var document = JsonDocument.Parse((string)result.Result!);
        return document.RootElement.Clone();
    }
}
