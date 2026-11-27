using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant.Tools;
using PmTool.Ai.Tests.Actions.DescriptionTemplates;
using PmTool.Ai.Tests.Actions.SavedViews;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Ai.Tests.Items;

/// <summary>五個讀取工具（存的檢視、說明格式、總覽、收件匣、變更集）與兩個建議工具：參數、範圍、結果的形狀。</summary>
[TestFixture]
public sealed class WorkspaceToolTests
{
    private AssistantHarness _h = null!;
    private RecordingSavedViews _views = null!;
    private RecordingTemplates _templates = null!;
    private StubInbox _inbox = null!;
    private StubChanges _changes = null!;
    private CapturingActionProposals _proposals = null!;
    private CapturingReads _reads = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public async Task StartAsync()
    {
        _h = await AssistantHarness.StartAsync();
        _views = new RecordingSavedViews();
        _templates = new RecordingTemplates();
        _inbox = new StubInbox();
        _changes = new StubChanges();
        _proposals = new CapturingActionProposals();
        _reads = new CapturingReads();
        _services = new ServiceCollection()
            .AddSingleton<ISavedViewService>(_views)
            .AddSingleton<IProjectOverviewService>(new StubOverview())
            .AddSingleton<INotificationInbox>(_inbox)
            .AddSingleton<IActionProposalCreator>(_proposals)
            .AddSingleton<IAssistantConversationReads>(_reads)
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task StopAsync()
    {
        await _services.DisposeAsync();
        await _h.DisposeAsync();
    }

    private AssistantToolContext Context(string? projectKey) =>
        _h.NewToolContext(projectKey) with { Services = _services, Descriptions = _templates, Changes = _changes };

    private static async Task<JsonElement> InvokeAsync(AIFunction tool, object arguments)
    {
        var element = JsonSerializer.SerializeToElement(arguments);
        var map = element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        var text = (string)(await tool.InvokeAsync(new AIFunctionArguments(map)))!;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    [Test]
    public async Task ListSavedViews_WithAProjectKey_ListsTheProjectViewsWithOnlyTheConditionsThatAreSet()
    {
        // Arrange
        _views.Project.Add(RecordingSavedViews.View("我的任務", SavedViewLayout.Board, new SavedViewQuery("task", null, "me", null, null, null, null)));

        // Act
        var result = await InvokeAsync(new ListSavedViewsTool(Context(null)), new { projectKey = "WEB" });

        // Assert
        var view = result.GetProperty("views")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("scope").GetString(), Is.EqualTo("project"));
            Assert.That(view.GetProperty("layout").GetString(), Is.EqualTo("board"));
            Assert.That(view.GetProperty("query").GetProperty("assignee").GetString(), Is.EqualTo("me"));
            Assert.That(view.GetProperty("query").TryGetProperty("due", out _), Is.False);
            Assert.That(result.GetProperty("notice").GetString(), Does.Contain("使用者寫的內容"));
        }
    }

    [Test]
    public async Task ListSavedViews_WithoutAProjectKey_ListsMyWork()
    {
        // Arrange
        _views.MyWork.Add(RecordingSavedViews.View("本週"));

        // Act
        var result = await InvokeAsync(new ListSavedViewsTool(Context("WEB")), new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("scope").GetString(), Is.EqualTo("my_work"));
            Assert.That(result.GetProperty("views")[0].GetProperty("name").GetString(), Is.EqualTo("本週"));
        }
    }

    [Test]
    public async Task ListSavedViews_AProjectConversationAskingForAnotherProject_IsRefused()
    {
        // Act
        var result = await InvokeAsync(new ListSavedViewsTool(Context("WEB")), new { projectKey = "OPS" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("error").GetString(), Does.Contain("WEB"));
        }
    }

    [Test]
    public async Task ListDescriptionTemplates_ReturnsIdsBodiesAndFlags()
    {
        // Arrange
        _templates.Templates.Insert(0, RecordingTemplates.Own("維運案", "## 主機", isDefault: false));

        // Act
        var result = await InvokeAsync(new ListDescriptionTemplatesTool(Context("WEB")), new { });

        // Assert
        var templates = result.GetProperty("templates");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(templates.GetArrayLength(), Is.EqualTo(2));
            Assert.That(templates[0].GetProperty("name").GetString(), Is.EqualTo("維運案"));
            Assert.That(templates[0].GetProperty("isBuiltIn").GetBoolean(), Is.False);
            Assert.That(templates[1].GetProperty("isDefault").GetBoolean(), Is.True);
        }
    }

    [Test]
    public async Task GetOverview_ReturnsTheCountsAndRejectsAnUnknownScope()
    {
        // Act
        var result = await InvokeAsync(new GetOverviewTool(Context(null)), new { scope = "managed" });
        var bad = await InvokeAsync(new GetOverviewTool(Context(null)), new { scope = "everything" });

        // Assert
        var project = result.GetProperty("projects")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(project.GetProperty("key").GetString(), Is.EqualTo("WEB"));
            Assert.That(project.GetProperty("overdue").GetInt32(), Is.EqualTo(3));
            Assert.That(project.GetProperty("ownItemsOnly").GetBoolean(), Is.False);
            Assert.That(bad.GetProperty("ok").GetBoolean(), Is.False);
        }
    }

    [Test]
    public async Task GetOverview_InAProjectConversation_OnlyReturnsThatProjectsRow_AndAnOrganizationConversationReturnsAll()
    {
        // Act
        var inProject = await InvokeAsync(new GetOverviewTool(Context("web")), new { });
        var inOrganization = await InvokeAsync(new GetOverviewTool(Context(null)), new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(inProject.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("key").GetString()), Is.EqualTo(new[] { "WEB" }));
            Assert.That(inOrganization.GetProperty("projects").GetArrayLength(), Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ListNotifications_NeverListsAccountSecurityNotifications()
    {
        // Arrange
        _inbox.Items.AddRange([
            Notification("WEB", "WEB-1", "請看"),
            new NotificationItem(Guid.NewGuid(), NotificationKind.AccountSecurity, null, null, null, Guid.NewGuid(), null, null, null, DateTimeOffset.UnixEpoch, null, null)]);

        // Act
        var result = await InvokeAsync(new ListNotificationsTool(Context(null)), new { });

        // Assert
        Assert.That(result.GetProperty("notifications").EnumerateArray().Select(n => n.GetProperty("kind").GetString()), Is.EqualTo(new[] { "mentioned" }));
    }

    [Test]
    public async Task ListNotifications_InAProjectConversation_OnlyShowsThatProjectsAndNeverMarksRead()
    {
        // Arrange
        _inbox.Items.AddRange([Notification("WEB", "WEB-1", "請看"), Notification("OPS", "OPS-9", "別的專案的留言")]);

        // Act
        var result = await InvokeAsync(new ListNotificationsTool(Context("WEB")), new { unreadOnly = true, limit = 5 });

        // Assert
        var list = result.GetProperty("notifications");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(list.GetArrayLength(), Is.EqualTo(1));
            Assert.That(list[0].GetProperty("itemKey").GetString(), Is.EqualTo("WEB-1"));
            Assert.That(_inbox.LastQuery, Is.EqualTo(new NotificationQuery(true, null, 5)));
            Assert.That(_inbox.MarkedRead, Is.Zero);
            Assert.That(_reads.Remembered, Is.Empty);
        }
    }

    [Test]
    public async Task ListNotifications_InAnOrganizationConversation_ShowsAllAndRemembersTheProjectsRead()
    {
        // Arrange
        _inbox.Items.AddRange([Notification("WEB", "WEB-1", "甲"), Notification("OPS", "OPS-9", "乙")]);

        // Act
        var result = await InvokeAsync(new ListNotificationsTool(Context(null)), new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("notifications").GetArrayLength(), Is.EqualTo(2));
            Assert.That(_reads.Remembered, Is.EquivalentTo(new[] { "WEB", "OPS" }));
        }
    }

    [Test]
    public async Task ListChangeSets_UsesTheConversationProjectByDefaultAndRefusesAnotherOne()
    {
        // Act
        var own = await InvokeAsync(new ListChangeSetsTool(Context("WEB")), new { });
        var other = await InvokeAsync(new ListChangeSetsTool(Context("WEB")), new { projectKey = "OPS" });
        var organization = await InvokeAsync(new ListChangeSetsTool(Context(null)), new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(own.GetProperty("changeSets")[0].GetProperty("status").GetString(), Is.EqualTo("pending"));
            Assert.That(own.GetProperty("changeSets")[0].GetProperty("origin").GetString(), Is.EqualTo("assistant"));
            Assert.That(_changes.Asked, Is.EqualTo(new[] { "WEB" }));
            Assert.That(other.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(organization.GetProperty("ok").GetBoolean(), Is.False, "組織對話一定要給 projectKey");
        }
    }

    [Test]
    public async Task ProposeSavedViewChanges_PassesTheChangesTheProjectAndTheReasonAndReturnsTheProposalId()
    {
        // Arrange
        var arguments = new { projectKey = "WEB", reason = "常用", changes = new[] { new { action = "create", name = "本週" } } };

        // Act
        var result = await InvokeAsync(new ProposeSavedViewChangesTool(Context(null)), arguments);

        // Assert
        var draft = _proposals.Drafts.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("proposalId").GetGuid(), Is.EqualTo(CapturingActionProposals.ProposalId));
            Assert.That(result.GetProperty("note").GetString(), Does.Contain("按「套用」").And.Contain("不要說已經改好了"));
            Assert.That((draft.Kind, draft.ProjectKey, draft.Reason), Is.EqualTo((ActionKinds.SavedViews, "WEB", "常用")));
            Assert.That(draft.Payload.GetProperty("changes")[0].GetProperty("name").GetString(), Is.EqualTo("本週"));
            Assert.That(draft.ConversationId, Is.Not.Null);
        }
    }

    [Test]
    public async Task ProposeSavedViewChanges_WithoutAProjectKey_IsAboutMyWork_AndForwardsTheCreatorsFailure()
    {
        // Arrange
        _proposals.Failure = new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "檢視建議不合規定，沒有建立。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "changes[0].name", "name 不對")],
        };

        // Act
        var result = await InvokeAsync(new ProposeSavedViewChangesTool(Context("WEB")), new { changes = new[] { new { action = "create" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_proposals.Drafts.Single().ProjectKey, Is.Null);
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(result.GetProperty("issues")[0].GetProperty("path").GetString(), Is.EqualTo("changes[0].name"));
        }
    }

    [Test]
    public async Task ProposeDescriptionTemplateChanges_BuildsADraftWithoutAProject()
    {
        // Act
        var result = await InvokeAsync(
            new ProposeDescriptionTemplateChangesTool(Context("WEB")),
            new { changes = new[] { new { action = "delete", templateId = "x" } } });

        // Assert
        var draft = _proposals.Drafts.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.True);
            Assert.That((draft.Kind, draft.ProjectKey), Is.EqualTo((ActionKinds.DescriptionTemplates, (string?)null)));
        }
    }

    [Test]
    public async Task ProposeTools_MissingChanges_AreRejectedWithoutCallingTheCreator()
    {
        // Act
        var result = await InvokeAsync(new ProposeDescriptionTemplateChangesTool(Context(null)), new { });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ok").GetBoolean(), Is.False);
            Assert.That(_proposals.Drafts, Is.Empty);
        }
    }

    private static NotificationItem Notification(string project, string itemKey, string excerpt) =>
        new(Guid.NewGuid(), NotificationKind.Mentioned, project, itemKey, "標題", Guid.NewGuid(), null, null, excerpt, DateTimeOffset.UnixEpoch, null, null);

    private sealed class CapturingActionProposals : IActionProposalCreator
    {
        public static readonly Guid ProposalId = Guid.Parse("a5a5a5a5-0000-0000-0000-0000000000c1");

        public Failure? Failure { get; set; }

        public List<ActionProposalDraft> Drafts { get; } = [];

        public Task<Result<ActionProposal>> CreateAsync(Actor actor, ActionProposalDraft draft, CancellationToken cancellationToken)
        {
            Drafts.Add(draft);
            if (Failure is { } failure)
            {
                return Task.FromResult<Result<ActionProposal>>(failure);
            }

            var step = new ActionStep("新增檢視", "本週", null, "名稱：本週", null, ActionTextFormat.Text);
            return Task.FromResult<Result<ActionProposal>>(
                new ActionProposal(ProposalId, draft.Kind, draft.ProjectKey, "建議", [step], NewItemProposalStatus.Pending, DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class CapturingReads : IAssistantConversationReads
    {
        public List<string> Remembered { get; } = [];

        public Task RememberAsync(Actor actor, Guid? conversationId, IReadOnlyCollection<string> projectKeys, CancellationToken cancellationToken)
        {
            Remembered.AddRange(projectKeys);
            return Task.CompletedTask;
        }
    }

    private sealed class StubOverview : IProjectOverviewService
    {
        public Task<IReadOnlyList<ProjectOverview>> ListAsync(Actor actor, ProjectOverviewScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProjectOverview>>(
                [Row("WEB", "網站改版", 3), Row("OPS", "維運", 9)]);

        private static ProjectOverview Row(string key, string name, int overdue) =>
            new(key, name, false, [new OverviewStateCount("todo", "待辦", StateCategory.NotStarted, 4)], 7, overdue, 1, 2, 0, 5, null, null, null);
    }

    private sealed class StubInbox : INotificationInbox
    {
        public List<NotificationItem> Items { get; } = [];

        public NotificationQuery? LastQuery { get; private set; }

        public int MarkedRead { get; private set; }

        public Task<Result<NotificationPage>> ListAsync(Actor actor, NotificationQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult<Result<NotificationPage>>(new NotificationPage(Items.ToList(), null));
        }

        public Task<int> CountUnreadAsync(Actor actor, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> MarkReadAsync(Actor actor, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken)
        {
            MarkedRead++;
            return Task.FromResult(0);
        }

        public Task<int> MarkAllReadAsync(Actor actor, CancellationToken cancellationToken)
        {
            MarkedRead++;
            return Task.FromResult(0);
        }
    }

    private sealed class StubChanges : IBlueprintChangeService
    {
        public List<string> Asked { get; } = [];

        public Task<Result<IReadOnlyList<ChangeSetSummary>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
        {
            Asked.Add(projectKey);
            IReadOnlyList<ChangeSetSummary> list =
            [
                new ChangeSetSummary(3, ChangeSetStatus.Pending, 5, "加欄位", Guid.NewGuid(), DateTimeOffset.UnixEpoch) { Origin = ChangeSetOrigin.Assistant },
            ];
            return Task.FromResult<Result<IReadOnlyList<ChangeSetSummary>>>(list.ToList());
        }

        public Task<Result<ChangeSetDetail>> ProposeAsync(Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ChangeSetDetail>> ProposeAsync(
            Actor actor, string projectKey, string document, DocumentFormat format, ChangeSetOrigin origin, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ChangeSetPreview>> PreviewAsync(Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ChangeSetDetail>> GetAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ChangeSetDetail>> ApplyAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<ChangeSetDetail>> RejectAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
