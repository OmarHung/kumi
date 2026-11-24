using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Ai.Tests.Settings;

namespace PmTool.Ai.Tests;

/// <summary>
/// 接上真資料庫的 AI 模組，和組合根的接法一樣：從根容器解析、啟動時先跑所有初始化。
/// 供應商換成假的（<paramref name="configure"/> 可以先註冊自己的，模組用 TryAdd）；工作引擎的介面由需要的測試自己放假的。
/// 主機骨架由主對話放好，工作包 A1 補齊（假的 gateway、常用的建立方法），A2 透過 configure 加自己的假物件。
/// 不驗證整個容器（ValidateOnBuild）：設定的測試不必為了助理的服務放工作引擎的假物件。
/// </summary>
internal sealed class AiTestHost : IAsyncDisposable
{
    private static readonly ServiceProviderOptions Options = new() { ValidateScopes = true };

    private readonly ServiceProvider _services;
    private readonly IServiceScope _scope;

    private AiTestHost(ServiceProvider services, string connectionString)
    {
        _services = services;
        _scope = services.CreateScope();
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    /// <summary>預設的假供應商（工作包 A1）；測試用 <c>configure</c> 換成別的時，這個會丟例外。</summary>
    public StubAiProviderGateway Gateway => (StubAiProviderGateway)Get<IAiProviderGateway>();

    public IServiceProvider Services => _scope.ServiceProvider;

    public T Get<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public static async Task<AiTestHost> StartAsync(
        Action<IServiceCollection>? configure = null, AiModuleOptions? options = null, TimeProvider? clock = null)
    {
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock ?? new ManualClock());
        services.AddSingleton<IOrganizationClock>(new FixedOrganizationClock());
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        configure?.Invoke(services);

        // 工作包 A1：預設的假供應商（模組用 TryAdd，configure 先註冊的會留下來）。
        services.TryAddSingleton<IAiProviderGateway>(new StubAiProviderGateway());

        // 主機代理向身分模組確認成員與擁有者：預設每個人都是一般成員。
        services.TryAddSingleton<PmTool.Identity.IPeopleDirectory>(new Assistant.Fakes.FakePeopleDirectory());
        services.AddAiModule(connectionString, options);
        var provider = services.BuildServiceProvider(Options);
        foreach (var initializer in provider.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        return new AiTestHost(provider, connectionString);
    }

    public async Task<AiDbContext> CreateDbContextAsync() =>
        await _services.GetRequiredService<IDbContextFactory<AiDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _services.DisposeAsync();

        // 每個測試各有自己的資料庫，也就各有自己的連線池；不清掉的話閒置的連線一直累積到伺服器的上限（too many clients）。
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>組織的時區一律是預設的（台北）。</summary>
internal sealed class FixedOrganizationClock : IOrganizationClock
{
    public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(TimeZoneInfo.FindSystemTimeZoneById(OrganizationTime.DefaultTimeZoneId));
}
