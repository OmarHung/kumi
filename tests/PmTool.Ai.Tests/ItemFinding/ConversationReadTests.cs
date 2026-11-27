using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.ItemFinding;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemFinding;

/// <summary>對話讀過的專案：讀到別人的項目才記、同一個專案只記一次；記過的專案只剩自己的或看不到之後，開、列、送都把對話刪掉。</summary>
[TestFixture]
public sealed class ConversationReadTests
{
    private static readonly Guid WebId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OpsId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private AssistantHarness _h = null!;
    private FakeWorkAccess _access = null!;
    private Guid _other;

    [SetUp]
    public async Task StartAsync()
    {
        _access = new FakeWorkAccess();
        _access.Set(WebId, "WEB", FakeWorkAccess.Reader);
        _access.Set(OpsId, "OPS", FakeWorkAccess.Reader);
        _h = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<IWorkAccessQueries>(_access));
        _h.Projects.OnList = _ => [Project(WebId, "WEB", "網站改版"), Project(OpsId, "OPS", "維運")];
        _other = Guid.CreateVersion7();
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    private IAssistantItemFinder Finder => ActivatorUtilities.CreateInstance<AssistantItemFinder>(_h.Host.Services);

    [Test]
    public async Task Find_ItemsOfSomeoneElse_RecordTheirProjectOnTheConversation()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        ReturnItems(Item("WEB-1", "WEB", assignee: _other), Item("OPS-1", "OPS", assignee: _h.Actor.PersonId));

        // Act
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.EqualTo(new[] { WebId }));
    }

    [Test]
    public async Task Find_OnlyMyOwnItems_RecordNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        ReturnItems(
            Item("WEB-1", "WEB", assignee: _h.Actor.PersonId),
            Item("OPS-1", "OPS", assignee: _other, createdBy: _h.Actor.PersonId));

        // Act
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.Empty);
    }

    [Test]
    public async Task Find_SameProjectManyTimes_IsRecordedOnce()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        ReturnItems(Item("WEB-1", "WEB", assignee: _other), Item("WEB-2", "WEB", assignee: null));

        // Act
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(await ReadProjectsAsync(conversation.Id), Is.EqualTo(new[] { WebId }));
    }

    [Test]
    public async Task Find_WithoutAConversation_RecordsNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync();
        ReturnItems(Item("WEB-1", "WEB", assignee: _other));

        // Act
        await Finder.FindAsync(_h.Actor, null, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        Assert.That(await ReadCountAsync(), Is.Zero);
        Assert.That(conversation.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task Find_SomeoneElsesConversation_RecordsNothing()
    {
        // Arrange
        var conversation = await _h.NewOrganizationConversationAsync(_h.Admin);
        ReturnItems(Item("WEB-1", "WEB", assignee: _other));

        // Act
        var result = await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(await ReadCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Find_ConversationThatNoLongerExists_StillAnswersWithoutRecording()
    {
        // Arrange
        ReturnItems(Item("WEB-1", "WEB", assignee: _other));

        // Act
        var result = await Finder.FindAsync(_h.Actor, Guid.CreateVersion7(), new ItemFindQuery(10), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(await ReadCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Get_AfterTheProjectBecameOwnOnly_DeletesTheOrganizationConversation()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, null, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(await ConversationCountAsync(), Is.Zero);
            Assert.That(await ReadCountAsync(), Is.Zero);
        }
    }

    [Test]
    public async Task Get_AfterTheProjectIsNoLongerVisible_DeletesTheConversation()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();
        _access.Remove(WebId);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, null, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
    }

    [Test]
    public async Task Get_WhenPermissionsDidNotChange_KeepsTheConversation()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, null, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Succeeded, Is.True);
    }

    [Test]
    public async Task Get_WhenAnotherProjectLostAccess_OnlyConversationsThatReadIt_AreDeleted()
    {
        // Arrange
        var readWeb = await ReadSomeoneElsesWebItemAsync();
        var readOps = await _h.NewOrganizationConversationAsync();
        ReturnItems(Item("OPS-1", "OPS", assignee: _other));
        await Finder.FindAsync(_h.Actor, readOps.Id, new ItemFindQuery(10), CancellationToken.None);
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var web = await _h.Service.GetAsync(_h.Actor, null, readWeb.Id, CancellationToken.None);
        var ops = await _h.Service.GetAsync(_h.Actor, null, readOps.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(web.Succeeded, Is.False);
            Assert.That(ops.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Get_ProjectConversationThatReadAnotherProject_IsDeletedToo()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        ReturnItems(Item("OPS-1", "OPS", assignee: _other));
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);
        _access.Set(OpsId, "OPS", FakeWorkAccess.OwnOnly);

        // Act
        var read = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(read.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
    }

    [Test]
    public async Task List_AfterTheProjectBecameOwnOnly_DeletesAndDoesNotListTheConversation()
    {
        // Arrange
        var stale = await ReadSomeoneElsesWebItemAsync();
        var fresh = await _h.NewOrganizationConversationAsync();
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var listed = await _h.Service.ListAsync(_h.Actor, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value.Select(c => c.Id), Is.EqualTo(new[] { fresh.Id }));
            Assert.That(await ConversationExistsAsync(stale.Id), Is.False);
        }
    }

    [Test]
    public async Task List_ProjectConversations_AlsoHonorsReadsOfOtherProjects()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        ReturnItems(Item("OPS-1", "OPS", assignee: _other));
        await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);
        _access.Remove(OpsId);

        // Act
        var listed = await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value, Is.Empty);
            Assert.That(await ConversationExistsAsync(conversation.Id), Is.False);
        }
    }

    [Test]
    public async Task ListAll_AfterTheProjectBecameOwnOnly_DeletesAndDoesNotListTheConversation()
    {
        // Arrange
        var stale = await ReadSomeoneElsesWebItemAsync();
        var fresh = await _h.NewConversationAsync();
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var listed = await _h.Service.ListAllAsync(_h.Actor, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listed.Value.Select(c => c.Conversation.Id), Is.EqualTo(new[] { fresh.Id }));
            Assert.That(await ConversationExistsAsync(stale.Id), Is.False);
        }
    }

    [Test]
    public async Task Start_AfterTheProjectBecameOwnOnly_SaysTheConversationDoesNotExistAndDeletesIt()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var started = await _h.Service.StartAsync(
            _h.Actor, null, conversation.Id, new AssistantMessageInput("Ivan 下週有哪些工作", _h.Selection), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(started.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(await ConversationExistsAsync(conversation.Id), Is.False);
        }
    }

    [Test]
    public async Task Get_WhileAReplyIsRunning_DoesNotDeleteButTreatsItAsMissing_AndDeletesNextTime()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();
        await SetRunningUntilAsync(conversation.Id, _h.Clock.GetUtcNow().AddMinutes(5));
        _access.Set(WebId, "WEB", FakeWorkAccess.OwnOnly);

        // Act
        var during = await _h.Service.GetAsync(_h.Actor, null, conversation.Id, CancellationToken.None);
        var existsDuring = await ConversationExistsAsync(conversation.Id);
        _h.Clock.Advance(TimeSpan.FromMinutes(10));
        var after = await _h.Service.GetAsync(_h.Actor, null, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(during.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(existsDuring, Is.True);
            Assert.That(after.Failure?.Code, Is.EqualTo(AiErrorCodes.ConversationNotFound));
            Assert.That(await ConversationExistsAsync(conversation.Id), Is.False);
        }
    }

    [Test]
    public async Task Check_WithManyConversationsAndProjects_AsksTheWorkEngineOnce()
    {
        // Arrange
        await ReadSomeoneElsesWebItemAsync();
        await ReadSomeoneElsesWebItemAsync();
        ReturnItems(Item("OPS-1", "OPS", assignee: _other));
        var third = await _h.NewOrganizationConversationAsync();
        await Finder.FindAsync(_h.Actor, third.Id, new ItemFindQuery(10), CancellationToken.None);
        var before = _access.VisibleCalls;

        // Act
        await _h.Service.ListAsync(_h.Actor, null, CancellationToken.None);

        // Assert
        Assert.That(_access.VisibleCalls - before, Is.EqualTo(1));
    }

    [Test]
    public async Task List_WithoutAnyRecordedReads_DoesNotAskTheWorkEngine()
    {
        // Arrange
        await _h.NewOrganizationConversationAsync();

        // Act
        await _h.Service.ListAsync(_h.Actor, null, CancellationToken.None);

        // Assert
        Assert.That(_access.VisibleCalls, Is.Zero);
    }

    [Test]
    public async Task Delete_OfAConversation_RemovesItsReadsToo()
    {
        // Arrange
        var conversation = await ReadSomeoneElsesWebItemAsync();

        // Act
        await _h.Service.DeleteAsync(_h.Actor, null, conversation.Id, CancellationToken.None);

        // Assert
        Assert.That(await ReadCountAsync(), Is.Zero);
    }

    private async Task<AssistantConversationSummary> ReadSomeoneElsesWebItemAsync()
    {
        var conversation = await _h.NewOrganizationConversationAsync();
        ReturnItems(Item("WEB-1", "WEB", assignee: _other));
        var found = await Finder.FindAsync(_h.Actor, conversation.Id, new ItemFindQuery(10), CancellationToken.None);
        Assert.That(found.Succeeded, Is.True);
        return conversation;
    }

    private void ReturnItems(params WorkItem[] items) => _h.WorkItems.OnListAcross = (_, _) => new WorkItemPage(items, null);

    private WorkItem Item(string key, string project, Guid? assignee, Guid? createdBy = null) =>
        ItemSamples.Item(key, $"項目 {key}", project) with { AssigneeId = assignee, CreatedBy = createdBy ?? _other };

    private static ProjectSummary Project(Guid id, string key, string name) => new(id, key, name, 1, "member", DateTimeOffset.UnixEpoch);

    private async Task<IReadOnlyList<Guid>> ReadProjectsAsync(Guid conversationId)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.ConversationReads.Where(r => r.ConversationId == conversationId).Select(r => r.ProjectId).ToListAsync();
    }

    private async Task<int> ReadCountAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.ConversationReads.CountAsync();
    }

    private async Task<int> ConversationCountAsync()
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.Conversations.CountAsync();
    }

    private async Task<bool> ConversationExistsAsync(Guid id)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        return await db.Conversations.AnyAsync(c => c.Id == id);
    }

    private async Task SetRunningUntilAsync(Guid id, DateTimeOffset until)
    {
        await using var db = await _h.Host.CreateDbContextAsync();
        await db.Conversations.Where(c => c.Id == id).ExecuteUpdateAsync(set => set.SetProperty(c => c.RunningUntil, until));
    }
}
