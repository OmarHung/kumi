using Microsoft.AspNetCore.Identity;
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

    /// <summary>目前的請求來源。預設沒有請求（<see cref="FakeRequestSource.Current"/> 是 null），測試要設才有。</summary>
    public FakeRequestSource Request => _services.GetRequiredService<FakeRequestSource>();

    /// <summary>發出要求的工作階段。預設沒有（<see cref="FakeCurrentSession.Token"/> 是 null），測試要設才有。</summary>
    public FakeCurrentSession CurrentSession => _services.GetRequiredService<FakeCurrentSession>();

    /// <summary>身分模組發出的帳號事件，依發出的順序。</summary>
    public RecordingAccountEventSink Events => _services.GetRequiredService<RecordingAccountEventSink>();

    public IAuditLog Audit => _services.GetRequiredService<IAuditLog>();

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
        AddTestSupport(services);
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
        AddTestSupport(services);
        services.AddIdentityModule(UnreachableConnectionString);
        configure?.Invoke(services);
        return new IdentityTestHost(services.BuildServiceProvider(StrictOptions), UnreachableConnectionString);
    }

    /// <summary>
    /// 在模組之前先註冊，模組的預設（沒有請求）就不會蓋掉它；測試之後再註冊同一個介面照樣可以換掉。
    /// </summary>
    private static void AddTestSupport(IServiceCollection services)
    {
        services.AddSingleton<FakeRequestSource>();
        services.AddSingleton<IRequestSource>(provider => provider.GetRequiredService<FakeRequestSource>());
        services.AddSingleton<FakeCurrentSession>();
        services.AddSingleton<ICurrentSession>(provider => provider.GetRequiredService<FakeCurrentSession>());
        services.AddSingleton<RecordingAccountEventSink>();
        services.AddSingleton<IAccountEventSink>(provider => provider.GetRequiredService<RecordingAccountEventSink>());
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
    /// 回傳那個組織的擁有者身分（資料庫裡有他這個人，新增人員時會重查），可以拿來在裡面新增人員。
    /// </summary>
    public async Task<Actor> AddOrganizationAsync(string name)
    {
        var organization = new OrganizationRow { Id = Guid.CreateVersion7(), Name = name, CreatedAt = DateTimeOffset.UtcNow };
        await using var db = await CreateDbContextAsync();
        var (owner, account) = PersonRows.Create(
            organization.Id, $"{name}的擁有者", TestData.UniqueEmail(),
            _services.GetRequiredService<IPasswordHasher<PasswordCredential>>().HashPassword(PasswordCredential.Instance, TestData.Password),
            OrganizationRole.Owner, DateTimeOffset.UtcNow);
        db.Organizations.Add(organization);
        db.AddRange(owner, account);
        await db.SaveChangesAsync();
        return new Actor(organization.Id, owner.Id, IsOrganizationAdmin: true);
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
