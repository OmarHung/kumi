using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Notifications.Email;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 一個接上真資料庫的通知模組，和組合根的接法一樣：從根容器解析、啟動時先跑所有初始化。
/// 工作引擎的存取查詢換成假的。
/// </summary>
internal sealed class NotificationsTestHost : IAsyncDisposable
{
    private static readonly ServiceProviderOptions StrictOptions = new() { ValidateScopes = true, ValidateOnBuild = true };

    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    private NotificationsTestHost(
        ServiceProvider services, string connectionString, FakeWorkAccessQueries access, CapturedLogs logs, ExternalFakes fakes)
    {
        OrganizationClock = fakes.Clock;
        var (people, transport) = (fakes.People, fakes.Transport);
        People = people;
        Transport = transport;
        Logs = logs;
        _services = services;
        _scope = services.CreateScope();
        ConnectionString = connectionString;
        Access = access;
    }

    public string ConnectionString { get; }

    public FakeWorkAccessQueries Access { get; }

    public CapturedLogs Logs { get; }

    public FakePeopleDirectory People { get; }

    public FakeEmailTransport Transport { get; }

    public FakeOrganizationClock OrganizationClock { get; }

    public IEmailSettingsService EmailSettings => _scope.ServiceProvider.GetRequiredService<IEmailSettingsService>();

    public IEmailPreferenceService EmailPreferences => _scope.ServiceProvider.GetRequiredService<IEmailPreferenceService>();

    public IEmailDispatcher Dispatcher => _scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();

    /// <summary>模組向外要的東西（組合根註冊）：人員目錄、組織時鐘、Data Protection。測試放假的，ModuleTests 也要。</summary>
    public static ExternalFakes AddExternalFakes(IServiceCollection services, bool useRealEmailTransport = false)
    {
        var people = new FakePeopleDirectory();
        var transport = new FakeEmailTransport();
        var clock = new FakeOrganizationClock();
        services.AddSingleton<IPeopleDirectory>(people);
        services.AddSingleton<IOrganizationClock>(clock);
        if (!useRealEmailTransport)
        {
            services.AddSingleton<IEmailTransport>(transport);
        }

        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return new ExternalFakes(people, transport, clock);
    }

    public IServiceProvider Services => _services;

    public IWorkEventConsumer Consumer => _scope.ServiceProvider.GetRequiredService<IWorkEventConsumer>();

    public INotificationInbox Inbox => _scope.ServiceProvider.GetRequiredService<INotificationInbox>();

    public IItemSubscriptionService Subscriptions => _scope.ServiceProvider.GetRequiredService<IItemSubscriptionService>();

    public static async Task<NotificationsTestHost> StartAsync(TimeProvider? clock = null) =>
        await StartAsync(new DatabaseConnections(await PostgresServer.CreateDatabaseAsync()), null, clock);

    /// <summary>對既有的資料庫啟動，模擬另一個程序。</summary>
    public static Task<NotificationsTestHost> StartAsync(string connectionString, FakeWorkAccessQueries? access = null) =>
        StartAsync(new DatabaseConnections(connectionString), access);

    /// <param name="useRealEmailTransport">用 MailKit 真的寄（整合測試對 Mailpit 用）；預設換成假的傳送。</param>
    public static async Task<NotificationsTestHost> StartAsync(
        DatabaseConnections connections,
        FakeWorkAccessQueries? access = null,
        TimeProvider? clock = null,
        IWorkEventStream? stream = null,
        bool useRealEmailTransport = false,
        IReadOnlyList<INotificationObserver>? observers = null,
        TimeSpan? observerTimeout = null)
    {
        access ??= new FakeWorkAccessQueries();
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs));
        services.AddSingleton<IWorkAccessQueries>(access);
        services.AddSingleton(stream ?? new FakeWorkEventStream());
        var fakes = AddExternalFakes(services, useRealEmailTransport);
        if (clock is not null)
        {
            // 模組用 TryAdd，先註冊的假時鐘會留下來。
            services.AddSingleton(clock);
        }

        if (observerTimeout is { } timeout)
        {
            services.AddSingleton(new PmTool.Notifications.Events.ObserverLimits(timeout));
        }

        foreach (var observer in observers ?? [])
        {
            services.AddSingleton(observer);
        }

        services.AddNotificationsModule(connections);
        var provider = services.BuildServiceProvider(StrictOptions);
        foreach (var initializer in provider.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        return new NotificationsTestHost(provider, connections.Runtime, access, logs, fakes);
    }

    /// <summary>另一個範圍裡的讀者，模擬同一時間另一個程序也在處理。</summary>
    public IServiceScope CreateScope() => _services.CreateScope();

    public async Task<NotificationsDbContext> CreateDbContextAsync() =>
        await _services.GetRequiredService<IDbContextFactory<NotificationsDbContext>>().CreateDbContextAsync();

    public async Task<IReadOnlyList<NotificationRow>> AllNotificationsAsync()
    {
        await using var db = await CreateDbContextAsync();
        return await db.Notifications.AsNoTracking().OrderBy(n => n.CreatedAt).ThenBy(n => n.Id).ToListAsync();
    }

    public async Task<IReadOnlyList<SubscriptionRow>> AllSubscriptionsAsync()
    {
        await using var db = await CreateDbContextAsync();
        return await db.Subscriptions.AsNoTracking().ToListAsync();
    }

    /// <summary>收件人在某個事件收到的通知種類，依種類排序。</summary>
    public async Task<IReadOnlyList<NotificationKind>> KindsForAsync(Guid recipient)
    {
        var all = await AllNotificationsAsync();
        return [.. all.Where(n => n.RecipientId == recipient).Select(n => n.Kind)];
    }

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return _services.DisposeAsync();
    }
}

internal sealed record ExternalFakes(FakePeopleDirectory People, FakeEmailTransport Transport, FakeOrganizationClock Clock);

internal static class NotificationsTestServices
{
    /// <summary>流式的寫法，給只建服務容器、不啟動整個測試主機的測試用。</summary>
    public static IServiceCollection AddExternalFakes(this IServiceCollection services)
    {
        NotificationsTestHost.AddExternalFakes(services);
        return services;
    }
}
