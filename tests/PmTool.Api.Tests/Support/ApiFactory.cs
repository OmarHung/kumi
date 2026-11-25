using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Composition;
using PmTool.Api.Endpoints;
using PmTool.Api.RateLimiting;
using PmTool.Blueprint;
using PmTool.Identity;
using PmTool.Knowledge;
using PmTool.Notifications;
using PmTool.Storage;
using PmTool.Work;
using IdentityAuthentication = PmTool.Identity.IAuthenticationService;

namespace PmTool.Api.Tests.Support;

internal sealed record ApiFactoryOptions
{
    public string Environment { get; init; } = "Production";

    /// <summary>讓測試用標頭指定來源 IP。TestServer 底下 RemoteIpAddress 永遠是 null，沒辦法測「依來源 IP 分開計算」。</summary>
    public bool UseTestRemoteIpHeader { get; init; }

    /// <summary>在應用程式讀到內文之前換掉內文的串流，用來量讀了多少，或模擬伺服器讀內文時丟的例外。</summary>
    public Func<Stream, Stream>? WrapRequestBody { get; init; }

    /// <summary>驗證器在建立時就丟例外，模擬真的驗證器載入 Schema 失敗。</summary>
    public bool ValidatorConstructionFails { get; init; }

    /// <summary>用 Program.cs 註冊的真的驗證器，不換成假的。</summary>
    public bool UseRealValidator { get; init; }

    /// <summary>換掉驗證端點的負載上限，測試才不必真的等十秒、開十幾條連線。null 表示用正式的數字。</summary>
    public ValidationLoadLimits? LoadLimits { get; init; }

    /// <summary>換掉就緒檢查的快取時間與期限。null 表示用正式的數字。</summary>
    public ReadinessLimits? ReadinessLimits { get; init; }

    /// <summary>
    /// 用這一組假模組，不另外建。模擬同一套資料（例如伺服器端的工作階段）前後被兩個應用程式實體用到，
    /// 例如容器重啟。null 表示每個 factory 自己一組。
    /// </summary>
    public FakeModules? SharedModules { get; init; }

    /// <summary>額外的設定值，例如第一個擁有者、轉送的標頭或 HTTPS。</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    /// <summary>web root 的目錄。null 表示建立一個空的暫存目錄，測試結束時刪掉。</summary>
    public string? WebRoot { get; init; }

    /// <summary>在換上假的模組之後再改服務註冊，給需要特殊註冊方式的測試用。</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>
    /// 用真的 Kestrel 接真的 TCP 連線，而不是 TestServer（記憶體裡的管線）。要測「讀得很慢的用戶端」這類
    /// 需要真的 socket 的行為才開；開了之後沒有 TestServer，限流器的釋放照舊靠主機關閉。
    /// </summary>
    public bool UseKestrel { get; init; }
}

/// <summary>每個測試各自建立一個，限流器的狀態跟著應用程式實體走，共用會讓測試互相影響。</summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string RemoteIpHeader = "X-Test-Remote-Ip";

    public const string ConstructionFailureDetail = "construction-detail-7c21";

    /// <summary>
    /// 模組接上之後，沒有連線字串時啟動會失敗。給一個不會真的連線的值，測試才不必跟著改；
    /// 模組的服務全部換成假的，所以不會有人拿它去連線。
    /// </summary>
    public const string UnusedConnectionString = "Host=db.invalid;Database=unused;Username=unused";

    private readonly FakeBlueprintValidator _validator;
    private readonly ApiFactoryOptions _options;
    private readonly string? _temporaryWebRoot;

    // 附件的儲存設定是啟動的必要條件；服務是假的，但啟動檢查用的是真的儲存空間，給它一個暫存資料夾。
    private readonly string _attachmentDirectory = CreateTemporaryDirectory();

    // 主機建好時記下 TestServer：關閉之後要從它的請求管線找到限流中介層的端點限流器釋放（RateLimiterCleanup）。
    private TestServer? _server;

    public ApiFactory(FakeBlueprintValidator validator, ApiFactoryOptions? options = null)
    {
        _validator = validator;
        _options = options ?? new ApiFactoryOptions();
        _temporaryWebRoot = _options.WebRoot is null ? CreateTemporaryDirectory() : null;
        Modules = _options.SharedModules ?? new FakeModules();
        if (_options.UseKestrel)
        {
            UseKestrel();
        }
    }

    public FakeModules Modules { get; }

    public IReadOnlyList<ServiceDescriptor> AppValidatorRegistrations { get; private set; } = [];

    public string WebRoot => _options.WebRoot ?? _temporaryWebRoot!;

    public static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pmtool-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>每個會改資料的請求都帶防偽標頭的用戶端，和網頁的行為一樣。</summary>
    public HttpClient CreateApiClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiRequests.CsrfHeader, "1");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_options.Environment);
        builder.UseSetting("ConnectionStrings:Database", UnusedConnectionString);
        builder.UseSetting("Attachments:Provider", "local");
        builder.UseSetting("Attachments:Directory", _attachmentDirectory);

        // 模組都是假的，沒有事件流可以讀；背景處理不跑，以免它每兩秒去連不存在的資料庫。
        builder.UseSetting(NotificationWorker.EnabledSetting, "false");
        builder.UseSetting(EmailWorker.EnabledSetting, "false");
        builder.UseSetting(AiMaintenanceWorker.EnabledSetting, "false");

        // 即時連線預設關著（和背景處理一樣）；要測它的測試用 Settings 打開，並自己決定什麼時候讀事件。
        builder.UseSetting(PmTool.Api.Live.LiveRegistration.EnabledSetting, "false");
        builder.UseSetting(PmTool.Api.Live.LiveUpdateWorker.EnabledSetting, "false");

        // 跨程序的訊號預設不經過資料庫（模組是假的，沒有資料庫可以連）；要測的用 Settings 打開。
        builder.UseSetting(PmTool.Api.Cluster.ClusterRegistration.EnabledSetting, "false");
        builder.UseSetting(WebHostDefaults.WebRootKey, WebRoot);
        foreach (var (key, value) in _options.Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(ReplaceServices);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        _server = host.Services.GetService<IServer>() as TestServer;
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _server is not null)
        {
            RateLimiterCleanup.DisposeEndpointLimiter(_server);
            _server = null;
        }

        if (disposing && _temporaryWebRoot is not null && Directory.Exists(_temporaryWebRoot))
        {
            Directory.Delete(_temporaryWebRoot, recursive: true);
        }

        if (disposing && Directory.Exists(_attachmentDirectory))
        {
            Directory.Delete(_attachmentDirectory, recursive: true);
        }
    }

    private void ReplaceServices(IServiceCollection services)
    {
        ReplaceValidator(services);
        ReplaceModules(services);

        // 登入的節流預設關掉：連錯好幾次的測試（例如限流的）不該因為它改變行為。要測節流的在 ConfigureServices 換回來。
        services.RemoveAll<SignInThrottleLimits>();
        services.AddSingleton(SignInThrottleLimits.Disabled);

        if (_options.LoadLimits is { } loadLimits)
        {
            services.RemoveAll<ValidationLoadLimits>();
            services.AddSingleton(loadLimits);
        }

        if (_options.ReadinessLimits is { } readinessLimits)
        {
            services.RemoveAll<ReadinessLimits>();
            services.AddSingleton(readinessLimits);
        }

        // 金鑰只放在記憶體裡：模組是假的，Program.cs 接上的資料庫存放連不上；
        // 啟動時預先載入金鑰環的背景服務也改讀記憶體，不去碰資料庫或使用者的家目錄。
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.Configure<KeyManagementOptions>(keys => keys.XmlRepository = new MemoryXmlRepository());

        services.AddSingleton<IStartupFilter>(new TestHooksStartupFilter(_options));
        _options.ConfigureServices?.Invoke(services);
    }

    private void ReplaceValidator(IServiceCollection services)
    {
        AppValidatorRegistrations = services.Where(d => d.ServiceType == typeof(IBlueprintValidator)).ToArray();
        if (_options.UseRealValidator)
        {
            return;
        }

        services.RemoveAll<IBlueprintValidator>();
        if (_options.ValidatorConstructionFails)
        {
            services.AddSingleton<IBlueprintValidator>(_ => throw new InvalidOperationException(ConstructionFailureDetail));
        }
        else
        {
            services.AddSingleton<IBlueprintValidator>(_validator);
        }
    }

    /// <summary>不管 Program.cs 有沒有接上真的模組，測試都只會用到假的。</summary>
    private void ReplaceModules(IServiceCollection services)
    {
        services.RemoveAll<IdentityAuthentication>();
        services.RemoveAll<ISessionService>();
        services.RemoveAll<IAccountService>();
        services.RemoveAll<IAccessTokenService>();
        services.RemoveAll<IOAuthService>();
        services.RemoveAll<ICliLoginService>();
        services.RemoveAll<IAuditLog>();
        services.RemoveAll<IPeopleDirectory>();
        services.RemoveAll<IIdentityBootstrapper>();
        services.RemoveAll<IProjectService>();
        services.RemoveAll<IProjectDescriptionService>();
        services.RemoveAll<IWorkItemService>();
        services.RemoveAll<IItemActivityService>();
        services.RemoveAll<IAttachmentService>();
        services.RemoveAll<IStorageQuota>();
        services.RemoveAll<IKnowledgeSpaceService>();
        services.RemoveAll<IKnowledgePageService>();
        services.RemoveAll<IKnowledgeAttachmentService>();
        services.RemoveAll<ISavedViewService>();
        services.RemoveAll<IPlanningService>();
        services.RemoveAll<IBlueprintChangeService>();
        services.RemoveAll<INotificationInbox>();
        services.RemoveAll<IItemSubscriptionService>();
        services.RemoveAll<IEmailSettingsService>();
        services.RemoveAll<IEmailPreferenceService>();
        services.RemoveAll<IEmailDispatcher>();
        services.RemoveAll<IAiSettingsService>();
        services.RemoveAll<IAiOptionsService>();
        services.RemoveAll<IPersonalAiConnectionService>();
        services.RemoveAll<IOpenRouterLoginService>();
        services.RemoveAll<IAssistantService>();
        services.RemoveAll<IItemProposalService>();
        services.RemoveAll<IItemProposalCreator>();
        services.RemoveAll<ILocalAgentService>();
        services.RemoveAll<ILocalAgentDirectory>();
        services.RemoveAll<IHostAgentAdminService>();
        services.RemoveAll<IHostAgentWorkerService>();
        services.RemoveAll<IHostToolGrants>();
        services.RemoveAll<IAgentToolBudget>();
        services.RemoveAll<IWorkAccessQueries>();
        services.RemoveAll<IModuleInitializer>();
        services.RemoveAll<IReadinessProbe>();
        services.RemoveAll<IMigrationStatus>();
        services.RemoveAll<TimeProvider>();

        services.AddSingleton<IdentityAuthentication>(Modules.Authentication);
        services.AddSingleton<ISessionService>(Modules.Sessions);
        services.AddSingleton<IAccountService>(Modules.Accounts);
        services.AddSingleton<IAccessTokenService>(Modules.AccessTokens);
        services.AddSingleton<IOAuthService>(Modules.OAuth);
        services.AddSingleton<ICliLoginService>(Modules.CliLogin);
        services.AddSingleton<IAuditLog>(Modules.Audit);
        services.AddSingleton<IPeopleDirectory>(Modules.People);
        services.AddSingleton<IIdentityBootstrapper>(Modules.Bootstrapper);
        services.AddSingleton<IProjectService>(Modules.Projects);
        services.AddSingleton<IProjectDescriptionService>(Modules.Descriptions);
        services.AddSingleton<IWorkItemService>(Modules.Items);
        services.AddSingleton<IBulkItemService>(Modules.Bulk);
        services.AddSingleton<IItemImportService>(Modules.Import);
        services.AddSingleton<IHistoryImportService>(Modules.History);
        services.AddSingleton<IItemActivityService>(Modules.Activity);
        services.AddSingleton<IAttachmentService>(Modules.Attachments);
        services.AddSingleton<IStorageQuota>(Modules.StorageQuota);
        services.AddSingleton<IKnowledgeSpaceService>(Modules.KnowledgeSpaces);
        services.AddSingleton<IKnowledgePageService>(Modules.KnowledgePages);
        services.AddSingleton<IKnowledgeAttachmentService>(Modules.KnowledgeAttachments);
        services.AddSingleton<ISavedViewService>(Modules.SavedViews);
        services.AddSingleton<IPlanningService>(Modules.Planning);
        services.AddSingleton<IBlueprintChangeService>(Modules.Changes);
        services.AddSingleton<INotificationInbox>(Modules.Notifications);
        services.AddSingleton<IItemSubscriptionService>(Modules.Subscriptions);
        services.AddSingleton<IEmailSettingsService>(Modules.EmailSettings);
        services.AddSingleton<IEmailPreferenceService>(Modules.EmailPreferences);
        services.AddSingleton<IAiSettingsService>(Modules.AiSettings);
        services.AddSingleton<IAiOptionsService>(Modules.AiOptions);
        services.AddSingleton<IPersonalAiConnectionService>(Modules.PersonalAi);
        services.AddSingleton<IOpenRouterLoginService>(Modules.OpenRouterLogin);
        services.AddSingleton<IAssistantService>(Modules.Assistant);
        services.AddSingleton<IItemProposalService>(Modules.ItemProposals);
        services.AddSingleton<IItemProposalCreator>(Modules.ItemProposals);
        services.AddSingleton<ILocalAgentService>(Modules.LocalAgents);
        services.AddSingleton<ILocalAgentDirectory>(Modules.LocalAgentDirectory);
        services.AddSingleton<IHostAgentAdminService>(Modules.HostAdmin);
        services.AddSingleton<IHostAgentWorkerService>(Modules.HostWorker);
        services.AddSingleton<IHostToolGrants>(Modules.HostGrants);
        services.AddSingleton<IAgentToolBudget>(Modules.AgentBudget);
        services.AddSingleton<IWorkAccessQueries>(Modules.Access);
        services.AddSingleton<TimeProvider>(Modules.Time);
        foreach (var initializer in Modules.Initializers)
        {
            services.AddSingleton(initializer);
        }

        foreach (var probe in Modules.Probes)
        {
            services.AddSingleton(probe);
        }

        foreach (var status in Modules.MigrationStatuses)
        {
            services.AddSingleton(status);
        }
    }

    private sealed class TestHooksStartupFilter(ApiFactoryOptions options) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                ApplyHooks(context);
                return nextMiddleware(context);
            });
            next(app);
        };

        private void ApplyHooks(HttpContext context)
        {
            if (options.UseTestRemoteIpHeader && context.Request.Headers.TryGetValue(RemoteIpHeader, out var ip))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(ip.ToString());
            }

            if (options.WrapRequestBody is { } wrap)
            {
                context.Request.Body = wrap(context.Request.Body);
            }
        }
    }
}
