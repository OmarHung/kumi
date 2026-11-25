using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Persistence;
using PmTool.Storage;
using PmTool.Work;

namespace PmTool.Knowledge.Tests;

/// <summary>
/// 一個接上真資料庫的知識庫模組，和組合根的接法一樣：從根容器解析、啟動時先跑所有初始化。
/// 身分模組與工作引擎換成假的（知識庫只問它們「這個人是誰、能做什麼」）。
/// </summary>
internal sealed class KnowledgeTestHost : IAsyncDisposable
{
    public static readonly DateTimeOffset StartTime = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private KnowledgeTestHost(
        ServiceProvider provider, string connectionString, FakePeopleDirectory people, FakeWorkAccessQueries work, ManualTimeProvider clock, string attachmentDirectory)
    {
        AttachmentDirectory = attachmentDirectory;
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        ConnectionString = connectionString;
        People = people;
        Work = work;
        Clock = clock;
    }

    public string ConnectionString { get; }

    /// <summary>附件的檔案放的資料夾（local 儲存），主機結束時刪掉。</summary>
    public string AttachmentDirectory { get; }

    public IKnowledgeAttachmentService Attachments => _scope.ServiceProvider.GetRequiredService<IKnowledgeAttachmentService>();

    public FakePeopleDirectory People { get; }

    public FakeWorkAccessQueries Work { get; }

    public ManualTimeProvider Clock { get; }

    public IKnowledgeSpaceService Spaces => _scope.ServiceProvider.GetRequiredService<IKnowledgeSpaceService>();

    public IKnowledgePageService Pages => _scope.ServiceProvider.GetRequiredService<IKnowledgePageService>();

    /// <param name="configure">測試另外要換掉的服務，例如把附件的上限調小；在模組註冊之前套用。</param>
    public static async Task<KnowledgeTestHost> StartAsync(Action<IServiceCollection>? configure = null) =>
        await StartAsync(new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), configure);

    public static async Task<KnowledgeTestHost> StartAsync(DatabaseConnections connections, Action<IServiceCollection>? configure = null)
    {
        var people = new FakePeopleDirectory();
        var work = new FakeWorkAccessQueries();
        var clock = new ManualTimeProvider(StartTime);
        var services = new ServiceCollection();
        services.AddSingleton<IPeopleDirectory>(people);
        services.AddSingleton<IWorkAccessQueries>(work);
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IOrganizationClock>(new FixedOrganizationClock(TimeZoneInfo.FindSystemTimeZoneById(OrganizationTime.DefaultTimeZoneId)));
        services.AddLogging();
        configure?.Invoke(services);
        var attachmentDirectory = Path.Combine(Path.GetTempPath(), "kb-attachments-" + Guid.NewGuid().ToString("N"));
        services.AddKnowledgeModule(connections, new FileStoreSettings(FileStoreSettings.LocalProvider, attachmentDirectory));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        foreach (var initializer in provider.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        return new KnowledgeTestHost(provider, connections.Runtime, people, work, clock, attachmentDirectory);
    }

    /// <summary>直接寫資料庫的入口：只用在「一頁一頁建要花太久」的上限測試造資料，驗證仍然走公開的介面。</summary>
    public async Task<KnowledgeDbContext> CreateDbContextAsync() =>
        await _provider.GetRequiredService<IDbContextFactory<KnowledgeDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        if (Directory.Exists(AttachmentDirectory))
        {
            Directory.Delete(AttachmentDirectory, recursive: true);
        }
    }
}

/// <summary>固定時區的 IOrganizationClock：每個組織都是同一個時區，依「每天」算的規則測得準。</summary>
internal sealed class FixedOrganizationClock(TimeZoneInfo zone) : IOrganizationClock
{
    public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(zone);
}
