using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>
/// 助理測試的環境：真的資料庫與真的服務，模型用腳本，工作引擎用假物件。
/// 組織已經接好一個連線、開著 AI；<see cref="Actor"/> 是一般成員，<see cref="Admin"/> 是管理員。
/// </summary>
internal sealed class AssistantHarness : IAsyncDisposable
{
    public const string ProjectKey = "WEB";

    private AssistantHarness(AiTestHost host, ModelScript script, ScriptedGateway gateway, Guid organizationId, AiConnection connection)
    {
        Host = host;
        Script = script;
        Gateway = gateway;
        OrganizationId = organizationId;
        Connection = connection;
        Actor = new Actor(organizationId, Guid.CreateVersion7(), IsOrganizationAdmin: false);
        Admin = new Actor(organizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);
        Selection = new AiSelection(connection.Id, ScriptedGateway.ModelId, AiEffort.High);
    }

    public AiTestHost Host { get; }

    public ModelScript Script { get; }

    public ScriptedGateway Gateway { get; }

    public Guid OrganizationId { get; }

    public AiConnection Connection { get; }

    public Actor Actor { get; }

    public Actor Admin { get; }

    public AiSelection Selection { get; }

    public IAssistantService Service => Host.Get<IAssistantService>();

    public FakeProjectService Projects => (FakeProjectService)Host.Get<PmTool.Work.IProjectService>();

    public FakeWorkItemService WorkItems => (FakeWorkItemService)Host.Get<PmTool.Work.IWorkItemService>();

    public FakeItemActivityService Activity => (FakeItemActivityService)Host.Get<PmTool.Work.IItemActivityService>();

    public FakeBlueprintChangeService Changes => (FakeBlueprintChangeService)Host.Get<PmTool.Work.IBlueprintChangeService>();

    public FakeBlueprintUsageService Usage => (FakeBlueprintUsageService)Host.Get<PmTool.Work.IBlueprintUsageService>();

    public FakeStaffingQueries StaffingQueries => (FakeStaffingQueries)Host.Get<PmTool.Work.IProjectStaffingQueries>();

    public FakePlanning Planning => (FakePlanning)Host.Get<PmTool.Work.IPlanningService>();

    public FakePeopleDirectory People => (FakePeopleDirectory)Host.Get<PmTool.Identity.IPeopleDirectory>();

    public FakeItemFinder ItemFinder => (FakeItemFinder)Host.Get<IAssistantItemFinder>();

    public FakeProjectDescriptions Descriptions => (FakeProjectDescriptions)Host.Get<PmTool.Work.IProjectDescriptionService>();

    public FakeAttachments Attachments => (FakeAttachments)Host.Get<PmTool.Work.IAttachmentService>();

    /// <summary>直接測單一工具時用的上下文：服務都是這個主機的（工作引擎是假的、建議的建立者是真的）。</summary>
    public PmTool.Ai.Assistant.Tools.AssistantToolContext NewToolContext(string? projectKey = "WEB", Guid? conversationId = null) =>
        new(
            Actor, projectKey, Projects, Changes, Usage, new FakeTemplateLibrary(),
            WorkItems, Activity, new FakePeopleDirectory(), Host.Get<IItemProposalCreator>(), Host.Get<IProjectProposalCreator>(), Host.Get<INewItemProposalCreator>(),
            Host.Get<IStaffingBriefService>(), Host.Get<IMemberProposalCreator>(),
            Host.Get<IItemDeletionProposalCreator>(), Host.Get<IItemBatchProposalCreator>(), Host.Get<IAssistantProposalLedger>(), ItemFinder,
            Host.Get<PmTool.Abstractions.IOrganizationClock>(), Host.Get<TimeProvider>(), conversationId ?? Guid.NewGuid(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
        {
            Planning = Planning,
            Descriptions = Descriptions,
            Attachments = Attachments,
            PlanningProposals = Host.Get<IPlanningProposalCreator>(),
            CommentProposals = Host.Get<ICommentProposalCreator>(),
            RestoreProposals = Host.Get<IItemRestoreProposalCreator>(),
            ProjectChangeProposals = Host.Get<IProjectChangeProposalCreator>(),
        };

    public ManualClock Clock => (ManualClock)Host.Get<TimeProvider>();

    /// <param name="timings">換成短的逾時，測試不必真的等五分鐘。</param>
    /// <param name="configure">再多註冊幾個服務（例如本機代理的時間、MCP 網址）。</param>
    public static async Task<AssistantHarness> StartAsync(
        bool enabled = true, AssistantTimings? timings = null, Action<IServiceCollection>? configure = null)
    {
        var script = new ModelScript();
        var gateway = new ScriptedGateway(script);
        var organizationId = Guid.CreateVersion7();
        var host = await AiTestHost.StartAsync(services =>
        {
            services.AddSingleton<PmTool.Ai.Providers.IAiProviderGateway>(gateway);
            services.AddSingleton<PmTool.Work.IProjectService>(new FakeProjectService());
            services.AddSingleton<PmTool.Work.IBlueprintChangeService>(new FakeBlueprintChangeService());
            services.AddSingleton<PmTool.Work.IWorkItemService>(new FakeWorkItemService());
            services.AddSingleton<PmTool.Work.IItemActivityService>(new FakeItemActivityService());
            services.AddSingleton<PmTool.Identity.IPeopleDirectory>(new FakePeopleDirectory());
            services.AddSingleton<PmTool.Work.IBlueprintUsageService>(new FakeBlueprintUsageService());
            services.AddSingleton<PmTool.Work.ITemplateLibrary>(new FakeTemplateLibrary());
            services.AddSingleton<PmTool.Work.IProjectStaffingQueries>(new FakeStaffingQueries());
            services.AddSingleton<PmTool.Work.IPlanningService>(new FakePlanning());
            services.AddSingleton<PmTool.Work.IProjectDescriptionService>(new FakeProjectDescriptions());
            services.AddSingleton<PmTool.Work.IAttachmentService>(new FakeAttachments());
            services.AddSingleton<PmTool.Work.IBulkItemService>(new FakeBulkStub());
            services.AddSingleton<IAssistantItemFinder>(new FakeItemFinder());
            if (timings is not null)
            {
                services.AddSingleton(timings);
            }

            configure?.Invoke(services);
        });

        var admin = new Actor(organizationId, Guid.CreateVersion7(), IsOrganizationAdmin: true);
        var settings = host.Get<IAiSettingsService>();
        var created = await settings.CreateConnectionAsync(
            admin,
            new AiConnectionInput("測試連線", AiProviderKind.Anthropic, null, "sk-test-key-0001", [ScriptedGateway.ModelId], []),
            CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        var saved = await settings.SaveAsync(
            admin, new AiSettingsInput(enabled, new AiSelection(created.Value.Id, ScriptedGateway.ModelId, null)), CancellationToken.None);
        Assert.That(saved.Succeeded, Is.True, () => saved.Failure?.Message ?? string.Empty);
        return new AssistantHarness(host, script, gateway, organizationId, created.Value);
    }

    public async Task<AssistantConversationSummary> NewConversationAsync(Actor? actor = null)
    {
        var created = await Service.CreateAsync(actor ?? Actor, ProjectKey, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    /// <summary>不選專案的組織對話。</summary>
    public async Task<AssistantConversationSummary> NewOrganizationConversationAsync(Actor? actor = null)
    {
        var created = await Service.CreateAsync(actor ?? Actor, null, CancellationToken.None);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    public async Task<AssistantRun> StartOrganizationAsync(
        Guid conversationId, string text = "幫我開一個新專案", Actor? actor = null, AiSelection? selection = null)
    {
        var started = await Service.StartAsync(
            actor ?? Actor, null, conversationId, new AssistantMessageInput(text, selection ?? Selection), CancellationToken.None);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        return started.Value;
    }

    public async Task<ReplyResult> SendOrganizationAsync(
        Guid conversationId, string text = "幫我開一個新專案", Actor? actor = null, AiSelection? selection = null)
    {
        var run = await StartOrganizationAsync(conversationId, text, actor, selection);
        return new ReplyResult(run, await ReadAllAsync(run));
    }

    public async Task<IReadOnlyList<AssistantMessage>> OrganizationMessagesAsync(Guid conversationId, Actor? actor = null)
    {
        var conversation = await Service.GetAsync(actor ?? Actor, null, conversationId, CancellationToken.None);
        Assert.That(conversation.Succeeded, Is.True, () => conversation.Failure?.Message ?? string.Empty);
        return conversation.Value.Messages;
    }

    public async Task<AssistantRun> StartAsync(
        Guid conversationId, string text = "請把待辦改名", Actor? actor = null, AiSelection? selection = null, string? itemKey = null)
    {
        var started = await Service.StartAsync(
            actor ?? Actor, ProjectKey, conversationId, new AssistantMessageInput(text, selection ?? Selection) { ItemKey = itemKey }, CancellationToken.None);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        return started.Value;
    }

    /// <summary>送出一則訊息並讀到結束。</summary>
    public async Task<ReplyResult> SendAsync(
        Guid conversationId, string text = "請把待辦改名", Actor? actor = null, AiSelection? selection = null, string? itemKey = null)
    {
        var run = await StartAsync(conversationId, text, actor, selection, itemKey);
        return new ReplyResult(run, await ReadAllAsync(run));
    }

    public static async Task<IReadOnlyList<AssistantRunEvent>> ReadAllAsync(AssistantRun run, CancellationToken cancellationToken = default)
    {
        var events = new List<AssistantRunEvent>();
        await foreach (var runEvent in run.Events.WithCancellation(cancellationToken))
        {
            events.Add(runEvent);
        }

        return events;
    }

    public async Task<IReadOnlyList<AssistantMessage>> MessagesAsync(Guid conversationId, Actor? actor = null)
    {
        var conversation = await Service.GetAsync(actor ?? Actor, ProjectKey, conversationId, CancellationToken.None);
        Assert.That(conversation.Succeeded, Is.True, () => conversation.Failure?.Message ?? string.Empty);
        return conversation.Value.Messages;
    }

    public async Task<AiMessageRow> RowAsync(Guid messageId)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Messages, m => m.Id == messageId);
    }

    public async Task<AiConversationRow> ConversationRowAsync(Guid conversationId)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Conversations, c => c.Id == conversationId);
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();
}

internal sealed record ReplyResult(AssistantRun Run, IReadOnlyList<AssistantRunEvent> Events)
{
    public AssistantMessage Final => Events.OfType<AssistantRunCompleted>().Single().Message;

    public string Text => string.Concat(Events.OfType<AssistantTextDelta>().Select(e => e.Delta));

    public IReadOnlyList<AssistantToolPart> ToolUpdates => [.. Events.OfType<AssistantToolUpdate>().Select(e => e.Tool)];
}
