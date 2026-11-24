using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant;

/// <summary>對話的清單、開新的、讀、刪，以及權限：只有本人、要看得到專案、要有 blueprint.read。</summary>
[TestFixture]
public sealed class AssistantConversationTests
{
    private AssistantHarness _h = null!;

    [SetUp]
    public async Task StartAsync() => _h = await AssistantHarness.StartAsync();

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    [Test]
    public async Task CreateAsync_NewConversation_IsEmptyAndNotRunning()
    {
        // Act
        var conversation = await _h.NewConversationAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(conversation.Title, Is.Null);
            Assert.That(conversation.MessageCount, Is.Zero);
            Assert.That(conversation.IsRunning, Is.False);
        }
    }

    [Test]
    public async Task ListAsync_OnlyMyConversationsInThisProject_NewestFirst()
    {
        // Arrange
        var first = await _h.NewConversationAsync();
        _h.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await _h.NewConversationAsync();
        await _h.NewConversationAsync(_h.Admin);

        // Act
        var listed = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Value;

        // Assert
        Assert.That(listed.Select(c => c.Id), Is.EqualTo(new[] { second.Id, first.Id }));
    }

    [Test]
    public async Task ListAsync_ConversationOfAnotherProject_IsNotListed()
    {
        // Arrange
        await _h.NewConversationAsync();
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail() with
        {
            Project = WorkSamples.Detail().Project with { Id = Guid.NewGuid(), Key = "OTHER" },
        };

        // Act
        var listed = (await _h.Service.ListAsync(_h.Actor, "OTHER", CancellationToken.None)).Value;

        // Assert
        Assert.That(listed, Is.Empty);
    }

    [Test]
    public async Task GetAsync_ReturnsTheMessagesOldestFirst()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("第一個回答");
        await _h.SendAsync(conversation.Id, "第一個問題");
        _h.Script.Reply("第二個回答");
        await _h.SendAsync(conversation.Id, "第二個問題");

        // Act
        var result = (await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Conversation.MessageCount, Is.EqualTo(4));
            Assert.That(result.Messages.Select(m => m.Role), Is.EqualTo(new[]
            {
                AssistantRole.User, AssistantRole.Assistant, AssistantRole.User, AssistantRole.Assistant,
            }));
            Assert.That(
                result.Messages.Select(m => ((AssistantTextPart)m.Parts.Single()).Text),
                Is.EqualTo(new[] { "第一個問題", "第一個回答", "第二個問題", "第二個回答" }));
        }
    }

    [Test]
    public async Task GetAsync_SomeoneElsesConversation_IsNotFound_EvenForAnAdmin()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await _h.Service.GetAsync(_h.Admin, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
    }

    [Test]
    public async Task GetAsync_UnknownConversation_IsNotFound()
    {
        // Act
        var result = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, Guid.NewGuid(), CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
    }

    [Test]
    public async Task GetAsync_ConversationOfAnotherOrganization_IsNotFound()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var outsider = new Actor(Guid.NewGuid(), _h.Actor.PersonId, IsOrganizationAdmin: false);

        // Act
        var result = await _h.Service.GetAsync(outsider, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
    }

    [Test]
    public async Task DeleteAsync_RemovesTheConversationAndItsMessages()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Script.Reply("好");
        var reply = await _h.SendAsync(conversation.Id);

        // Act
        var deleted = await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        await using var db = await _h.Host.CreateDbContextAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleted.Succeeded, Is.True);
            Assert.That(await db.Conversations.CountAsync(c => c.Id == conversation.Id), Is.Zero);
            Assert.That(await db.Messages.CountAsync(m => m.ConversationId == conversation.Id), Is.Zero);
            Assert.That(reply.Final.Status, Is.EqualTo(AssistantMessageStatus.Complete));
        }
    }

    [Test]
    public async Task DeleteAsync_SomeoneElsesConversation_IsNotFoundAndKeepsIt()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();

        // Act
        var result = await _h.Service.DeleteAsync(_h.Admin, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        AssertFailure(result.Failure, FailureKind.NotFound, AiErrorCodes.ConversationNotFound);
        Assert.That((await _h.ConversationRowAsync(conversation.Id)).Id, Is.EqualTo(conversation.Id));
    }

    [Test]
    public async Task DeleteAsync_WhileReplying_IsBusy()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var gate = new TaskCompletionSource();
        _h.Script.Enqueue(Turn.Create().WaitFor(gate.Task).Text("好").Finish(Microsoft.Extensions.AI.ChatFinishReason.Stop));
        var run = await _h.StartAsync(conversation.Id);

        // Act
        var result = await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);
        gate.SetResult();
        await AssistantHarness.ReadAllAsync(run);

        // Assert
        AssertFailure(result.Failure, FailureKind.Conflict, AiErrorCodes.ConversationBusy);
    }

    [Test]
    public async Task EveryOperation_WithoutBlueprintRead_IsAssistantForbidden()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        _h.Projects.OnGet = (_, _) => WorkSamples.Detail([WorkPermissions.ItemRead]);
        var input = new AssistantMessageInput("你好", _h.Selection);

        // Act
        var list = (await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Failure;
        var create = (await _h.Service.CreateAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None)).Failure;
        var get = (await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None)).Failure;
        var delete = (await _h.Service.DeleteAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None)).Failure;
        var start = (await _h.Service.StartAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, input, CancellationToken.None)).Failure;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            foreach (var failure in new[] { list, create, get, delete, start })
            {
                AssertFailure(failure, FailureKind.Forbidden, AiErrorCodes.AssistantForbidden);
            }
        }
    }

    [Test]
    public async Task EveryOperation_ProjectNotVisible_PassesThroughTheProjectNotFound()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var notFound = WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到專案");
        _h.Projects.OnGet = (_, _) => notFound;
        var input = new AssistantMessageInput("你好", _h.Selection);

        // Act
        var results = new[]
        {
            (await _h.Service.ListAsync(_h.Actor, "WEB", CancellationToken.None)).Failure,
            (await _h.Service.CreateAsync(_h.Actor, "WEB", CancellationToken.None)).Failure,
            (await _h.Service.GetAsync(_h.Actor, "WEB", conversation.Id, CancellationToken.None)).Failure,
            (await _h.Service.DeleteAsync(_h.Actor, "WEB", conversation.Id, CancellationToken.None)).Failure,
            (await _h.Service.StartAsync(_h.Actor, "WEB", conversation.Id, input, CancellationToken.None)).Failure,
        };

        // Assert
        Assert.That(results.Select(f => f?.Code), Has.All.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task CreateAsync_AiTurnedOff_IsUnavailable_ButReadingStillWorks()
    {
        // Arrange
        var conversation = await _h.NewConversationAsync();
        var off = await _h.Host.Get<IAiSettingsService>().SaveAsync(
            AdminOf(_h), new AiSettingsInput(false, null), CancellationToken.None);
        Assert.That(off.Succeeded, Is.True, () => off.Failure?.Message ?? string.Empty);

        // Act
        var create = await _h.Service.CreateAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None);
        var list = await _h.Service.ListAsync(_h.Actor, AssistantHarness.ProjectKey, CancellationToken.None);
        var get = await _h.Service.GetAsync(_h.Actor, AssistantHarness.ProjectKey, conversation.Id, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertFailure(create.Failure, FailureKind.Invalid, AiErrorCodes.Unavailable);
            Assert.That(list.Succeeded, Is.True);
            Assert.That(get.Succeeded, Is.True);
        }
    }

    private static Actor AdminOf(AssistantHarness harness) => new(harness.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

    internal static void AssertFailure(Failure? failure, FailureKind kind, string code)
    {
        Assert.That(failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Kind, Is.EqualTo(kind));
            Assert.That(failure.Code, Is.EqualTo(code));
            Assert.That(failure.Message, Is.Not.Empty);
        }
    }
}
