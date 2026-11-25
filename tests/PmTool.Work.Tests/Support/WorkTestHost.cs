using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Storage;

namespace PmTool.Work.Tests;

/// <summary>
/// 一個測試類別的執行環境：自己的資料庫、照組合根的方式註冊模組、啟動時跑過初始化（遷移）。
/// 服務從同一個範圍取出，和 API 一個請求裡的用法一樣。
/// </summary>
internal sealed class WorkTestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly Action<IServiceCollection>? _configure;
    private readonly DatabaseConnections _connections;
    private readonly string? _attachmentDirectory;

    private WorkTestHost(
        ServiceProvider provider, ManualTimeProvider clock, DatabaseConnections connections, Action<IServiceCollection>? configure, string? attachmentDirectory)
    {
        _attachmentDirectory = attachmentDirectory;
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        _configure = configure;
        _connections = connections;
        Clock = clock;
        ConnectionString = connections.Runtime;
    }

    public ManualTimeProvider Clock { get; }

    public string ConnectionString { get; }

    public IServiceProvider Root => _provider;

    /// <summary>附件的檔案放的資料夾（local 儲存）。每個測試類別自己一個，結束時刪掉。</summary>
    public string AttachmentDirectory => _attachmentDirectory ?? throw new InvalidOperationException("這個測試環境沒有附件的資料夾。");

    public IAttachmentService Attachments => _scope.ServiceProvider.GetRequiredService<IAttachmentService>();

    public IProjectService Projects => _scope.ServiceProvider.GetRequiredService<IProjectService>();

    public IProjectDescriptionService Descriptions => _scope.ServiceProvider.GetRequiredService<IProjectDescriptionService>();

    public IWorkItemService Items => _scope.ServiceProvider.GetRequiredService<IWorkItemService>();

    public IBulkItemService Bulk => _scope.ServiceProvider.GetRequiredService<IBulkItemService>();

    public IItemImportService Import => _scope.ServiceProvider.GetRequiredService<IItemImportService>();

    public IHistoryImportService History => _scope.ServiceProvider.GetRequiredService<IHistoryImportService>();

    public IItemActivityService Activity => _scope.ServiceProvider.GetRequiredService<IItemActivityService>();

    public ISavedViewService SavedViews => _scope.ServiceProvider.GetRequiredService<ISavedViewService>();

    public IPlanningService Planning => _scope.ServiceProvider.GetRequiredService<IPlanningService>();

    public IBlueprintUsageService Usage => _scope.ServiceProvider.GetRequiredService<IBlueprintUsageService>();

    public ITemplateLibrary Templates => _scope.ServiceProvider.GetRequiredService<ITemplateLibrary>();

    public IWorkEventStream Events => _provider.GetRequiredService<IWorkEventStream>();

    /// <summary>預設每個人都在組織裡；測試用 <see cref="FakeOrganizationMembers.Exclude"/> 排除某個人。</summary>
    public FakeOrganizationMembers OrganizationMembers => _provider.GetRequiredService<FakeOrganizationMembers>();

    public IWorkAccessQueries Access => _provider.GetRequiredService<IWorkAccessQueries>();

    public IBlueprintChangeService Changes => _scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();

    /// <param name="configure">測試另外要加或換掉的服務，例如把套用的上限調小。</param>
    public static async Task<WorkTestHost> StartAsync(Action<IServiceCollection>? configure = null) =>
        await StartAsync(new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), configure);

    /// <summary>用指定的連線啟動，例如執行的角色與資料表的擁有者分開。</summary>
    public static async Task<WorkTestHost> StartAsync(DatabaseConnections connections, Action<IServiceCollection>? configure = null)
    {
        var clock = new ManualTimeProvider(StartTime);
        var attachmentDirectory = Path.Combine(Path.GetTempPath(), "pmtool-work-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attachmentDirectory);
        void WithAttachments(IServiceCollection services)
        {
            configure?.Invoke(services);
            services.TryAddSingleton(new FileStoreSettings(FileStoreSettings.LocalProvider, attachmentDirectory));
        }

        var provider = BuildProvider(connections, clock, WithAttachments);
        foreach (var initializer in provider.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        return new WorkTestHost(provider, clock, connections, WithAttachments, attachmentDirectory);
    }

    /// <param name="configure">測試另外要加的服務，例如收集 SQL 的記錄。</param>
    public static ServiceProvider BuildProvider(string connectionString, TimeProvider clock, Action<IServiceCollection>? configure = null) =>
        BuildProvider(new DatabaseConnections(connectionString), clock, configure);

    public static ServiceProvider BuildProvider(DatabaseConnections connections, TimeProvider clock, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        services.AddSingleton(clock);
        services.TryAddSingleton<IOrganizationClock, FixedOrganizationClock>();
        services.TryAddSingleton<FakeOrganizationMembers>();
        services.TryAddSingleton<IOrganizationMembers>(provider => provider.GetRequiredService<FakeOrganizationMembers>());
        services.AddSingleton<IBlueprintValidator>(BlueprintValidator.CreateDefault());
        services.AddSingleton<IBlueprintPlanner, BlueprintPlanner>();
        services.AddWorkModule(connections);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>
    /// 同一個資料庫、新的服務容器，模擬重新啟動：藍圖的快取是空的，要從資料庫讀回來。
    /// 資料表已經遷移過，不必再跑初始化。
    /// </summary>
    public WorkTestHost Restart() =>
        new(BuildProvider(_connections, Clock, _configure), Clock, _connections, _configure, attachmentDirectory: null);

    /// <summary>另一個範圍裡的服務，模擬同時進來的另一個請求。</summary>
    public (IProjectService Projects, IWorkItemService Items, AsyncServiceScope Scope) NewRequest()
    {
        var scope = _provider.CreateAsyncScope();
        return (
            scope.ServiceProvider.GetRequiredService<IProjectService>(),
            scope.ServiceProvider.GetRequiredService<IWorkItemService>(),
            scope);
    }

    /// <summary>另一個範圍裡的活動服務，模擬同時進來的另一個請求。</summary>
    public (IItemActivityService Activity, AsyncServiceScope Scope) NewActivityRequest()
    {
        var scope = _provider.CreateAsyncScope();
        return (scope.ServiceProvider.GetRequiredService<IItemActivityService>(), scope);
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        if (_attachmentDirectory is not null && Directory.Exists(_attachmentDirectory))
        {
            Directory.Delete(_attachmentDirectory, recursive: true);
        }
    }
}
