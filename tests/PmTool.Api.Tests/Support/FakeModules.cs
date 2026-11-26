using System.Collections.Concurrent;
using PmTool.Abstractions;

namespace PmTool.Api.Tests.Support;

/// <summary>一個 factory 用的全部假模組。測試在建立用戶端之前或之後都可以改它們的行為。</summary>
internal sealed class FakeModules
{
    public FakeModules()
    {
        Sessions = new FakeSessionService(Authentication, Time);
        Accounts = new FakeAccountService(Authentication, Sessions);
        AccessTokens = new FakeAccessTokenService(Authentication, Time);
    }

    public FakeAuthenticationService Authentication { get; } = new();

    public FakeSessionService Sessions { get; }

    public FakeAccountService Accounts { get; }

    public FakeAccessTokenService AccessTokens { get; }

    public FakeOAuthService OAuth { get; } = new();

    public FakeCliLoginService CliLogin { get; } = new();

    public FakeAuditLog Audit { get; } = new();

    public FakePeopleDirectory People { get; } = new();

    public FakeIdentityBootstrapper Bootstrapper { get; } = new();

    public FakeProjectService Projects { get; } = new();

    public FakeProjectDescriptionService Descriptions { get; } = new();

    public FakeWorkItemService Items { get; } = new();

    public FakeBulkItemService Bulk { get; } = new();

    public FakeItemImportService Import { get; } = new();

    public FakeHistoryImportService History { get; } = new();

    public FakeItemActivityService Activity { get; } = new();

    public FakeAttachmentService Attachments { get; } = new();

    public FakeStorageQuota StorageQuota { get; } = new();

    public FakeKnowledgeSpaceService KnowledgeSpaces { get; } = new();

    public FakeKnowledgePageService KnowledgePages { get; } = new();

    public FakeKnowledgeAttachmentService KnowledgeAttachments { get; } = new();

    public FakeSavedViewService SavedViews { get; } = new();

    public FakePlanningService Planning { get; } = new();

    public FakeBlueprintChangeService Changes { get; } = new();

    public FakeNotificationInbox Notifications { get; } = new();

    public FakeItemSubscriptionService Subscriptions { get; } = new();

    public FakeEmailSettingsService EmailSettings { get; } = new();

    public FakeEmailPreferenceService EmailPreferences { get; } = new();

    public FakeItemProposals ItemProposals { get; } = new();

    public FakeProjectProposals ProjectProposals { get; } = new();

    public FakeAiSettingsService AiSettings { get; } = new();

    public FakeAiOptionsService AiOptions { get; } = new();

    public FakePersonalAiConnectionService PersonalAi { get; } = new();

    public FakeOpenRouterLoginService OpenRouterLogin { get; } = new();

    public FakeAssistantService Assistant { get; } = new();

    public FakeAssistantAttachmentService AssistantAttachments { get; } = new();

    public FakeLocalAgentService LocalAgents { get; } = new();

    public FakeLocalAgentDirectory LocalAgentDirectory { get; } = new();

    public FakeHostAgentAdminService HostAdmin { get; } = new();

    public FakeHostAgentWorkerService HostWorker { get; } = new();

    public FakeHostToolGrants HostGrants { get; } = new();

    public FakeAgentToolBudget AgentBudget { get; } = new();

    public FakeWorkAccessQueries Access { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    /// <summary>啟動時依序執行的初始化，以及就緒檢查會問的對象。預設都沒有。</summary>
    public List<IModuleInitializer> Initializers { get; } = [];

    public List<IReadinessProbe> Probes { get; } = [];

    /// <summary>啟動時不遷移（Database:MigrateOnStartup=false）時，啟動會問它們還差哪些遷移。預設沒有。</summary>
    public List<IMigrationStatus> MigrationStatuses { get; } = [];
}

/// <summary>可以手動前進的時鐘，測試才能不等真的時間就跨過工作階段的期限。</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _utcTicks = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}

/// <summary>記錄自己被呼叫的初始化，用一份共用的清單看出多個初始化的先後。</summary>
internal sealed class RecordingInitializer(string name, ConcurrentQueue<string> log, Exception? failure = null)
    : IModuleInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        log.Enqueue(name);
        return failure is null ? Task.CompletedTask : Task.FromException(failure);
    }
}

internal sealed class FakeMigrationStatus(Func<Task<IReadOnlyList<string>>> respond) : IMigrationStatus
{
    public static FakeMigrationStatus Pending(params string[] migrations) => new(() => Task.FromResult<IReadOnlyList<string>>(migrations));

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) => respond();
}

internal sealed class FakeReadinessProbe(Func<Task<bool>> respond) : IReadinessProbe
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public static FakeReadinessProbe Ready() => new(() => Task.FromResult(true));

    public static FakeReadinessProbe NotReady() => new(() => Task.FromResult(false));

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return respond();
    }
}
