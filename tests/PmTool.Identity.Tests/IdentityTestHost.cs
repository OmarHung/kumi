using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Tests;

/// <summary>
/// 一個接上真資料庫的模組，和組合根的接法一樣：從根容器解析、啟動時先跑所有初始化。
/// 開啟範圍與建置時的檢查，單例不小心依賴了 scoped 服務就會在這裡失敗。
/// </summary>
internal sealed class IdentityTestHost : IAsyncDisposable
{
    /// <summary>連不上的位址；不帶密碼，連線一被拒絕就失敗，不用等逾時。</summary>
    public const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=2";

    private static readonly ServiceProviderOptions StrictOptions = new() { ValidateScopes = true, ValidateOnBuild = true };

    private readonly ServiceProvider _services;

    private IdentityTestHost(ServiceProvider services, string connectionString)
    {
        _services = services;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public IServiceProvider Services => _services;

    public IAuthenticationService Authentication => _services.GetRequiredService<IAuthenticationService>();

    public IPeopleDirectory People => _services.GetRequiredService<IPeopleDirectory>();

    public IIdentityBootstrapper Bootstrapper => _services.GetRequiredService<IIdentityBootstrapper>();

    public ISessionService Sessions => _services.GetRequiredService<ISessionService>();

    public IAccessTokenService AccessTokens => _services.GetRequiredService<IAccessTokenService>();

    public ICliLoginService CliLogin => _services.GetRequiredService<ICliLoginService>();

    public IOAuthService OAuth => _services.GetRequiredService<IOAuthService>();

    public IAccountService Accounts => _services.GetRequiredService<IAccountService>();

    /// <summary>開一個新的資料庫並啟動模組。</summary>
    public static async Task<IdentityTestHost> StartAsync(Action<IServiceCollection>? configure = null) =>
        await StartAsync(await PostgresServer.CreateDatabaseAsync(), configure);

    /// <summary>對既有的資料庫啟動模組，模擬另一個程序。</summary>
    public static Task<IdentityTestHost> StartAsync(string connectionString, Action<IServiceCollection>? configure) =>
        StartAsync(new DatabaseConnections(connectionString), configure);

    /// <summary>用指定的連線啟動，例如執行的角色與資料表的擁有者分開。</summary>
    public static async Task<IdentityTestHost> StartAsync(DatabaseConnections connections, Action<IServiceCollection>? configure)
    {
        var services = new ServiceCollection();
        services.AddIdentityModule(connections);
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider(StrictOptions);

        foreach (var initializer in provider.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        return new IdentityTestHost(provider, connections.Runtime);
    }

    /// <summary>
    /// 接上連不上的資料庫、不跑初始化。用來確認某個操作根本沒碰資料庫：一碰就會丟例外，
    /// 從公開介面看不到查詢，這是最直接的觀察方式。
    /// </summary>
    public static IdentityTestHost StartWithoutDatabase(Action<IServiceCollection>? configure)
    {
        var services = new ServiceCollection();
        services.AddIdentityModule(UnreachableConnectionString);
        configure?.Invoke(services);
        return new IdentityTestHost(services.BuildServiceProvider(StrictOptions), UnreachableConnectionString);
    }

    public async Task<IdentityDbContext> CreateDbContextAsync() =>
        await _services.GetRequiredService<IDbContextFactory<IdentityDbContext>>().CreateDbContextAsync();

    /// <summary>合約沒有停用帳號的方法，測試直接把停用的時刻寫進去。</summary>
    public async Task DisableAccountAsync(Guid accountId)
    {
        await using var db = await CreateDbContextAsync();
        await db.Accounts
            .Where(account => account.Id == accountId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(account => account.DisabledAt, DateTimeOffset.UtcNow));
    }

    /// <summary>模擬別的程序撤銷了權杖：直接寫資料表，這個程序的快取不知道。</summary>
    public async Task RevokeAccessTokenBehindTheCacheAsync(Guid tokenId)
    {
        await using var db = await CreateDbContextAsync();
        await db.AccessTokens
            .Where(token => token.Id == tokenId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, (DateTimeOffset?)DateTimeOffset.UtcNow)
                .SetProperty(token => token.RevokedReason, "test"));
    }

    public async Task<AccessTokenRow> ReadAccessTokenRowAsync(Guid tokenId)
    {
        await using var db = await CreateDbContextAsync();
        return await db.AccessTokens.AsNoTracking().SingleAsync(token => token.Id == tokenId);
    }

    /// <summary>合約沒有改名稱的方法，測試直接改資料表，模擬別的地方改了這個人的資料。</summary>
    public async Task RenamePersonAsync(Guid personId, string displayName)
    {
        await using var db = await CreateDbContextAsync();
        await db.People
            .Where(person => person.Id == personId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(person => person.DisplayName, displayName));
    }

    /// <summary>合約只能連同帳號一起新增人員；沒有帳號的人（例如只記名字的外部人員）直接寫進去。</summary>
    public async Task<Guid> AddPersonWithoutAccountAsync(Guid organizationId, string displayName, OrganizationRole role)
    {
        var person = new PersonRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            DisplayName = displayName,
            Role = role,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await using var db = await CreateDbContextAsync();
        db.People.Add(person);
        await db.SaveChangesAsync();
        return person.Id;
    }

    /// <summary>
    /// 合約只能建立第一個組織；測試「不會跨組織」需要第二個，直接寫進去。
    /// 回傳那個組織的管理員身分，可以拿來在裡面新增人員。
    /// </summary>
    public async Task<Actor> AddOrganizationAsync(string name)
    {
        var organization = new OrganizationRow { Id = Guid.CreateVersion7(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
        await using var db = await CreateDbContextAsync();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync();
        return new Actor(organization.Id, Guid.CreateVersion7(), IsOrganizationAdmin: true);
    }

    /// <summary>建立第一個擁有者並登入，回傳他的身分。</summary>
    public async Task<AuthenticatedUser> StartWithOwnerAsync()
    {
        await Bootstrapper.EnsureFirstOwnerAsync(TestData.Owner, CancellationToken.None);
        return await Authentication.SignInAsync(TestData.Owner.Email, TestData.Owner.Password, CancellationToken.None)
            ?? throw new InvalidOperationException("第一個擁有者登入不了。");
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
