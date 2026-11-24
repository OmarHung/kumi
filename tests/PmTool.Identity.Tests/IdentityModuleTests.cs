using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Tests;

/// <summary>註冊入口、就緒檢查與產生遷移用的工廠。</summary>
[TestFixture]
public sealed class IdentityModuleTests
{
    private const string UnreachableConnectionString = IdentityTestHost.UnreachableConnectionString;

    [Test]
    public async Task IsReadyAsync_DatabaseReachable_ReturnsTrue()
    {
        // Arrange
        await using var host = await IdentityTestHost.StartAsync();
        var probes = host.Services.GetServices<IReadinessProbe>().ToList();

        // Act
        var ready = await probes.Single().IsReadyAsync(CancellationToken.None);

        // Assert
        Assert.That(ready, Is.True);
    }

    [Test]
    public async Task IsReadyAsync_MigrationsNotApplied_ReturnsFalseUntilMigrated()
    {
        // Arrange：啟動時不遷移（Database:MigrateOnStartup=false）、migrate 還沒跑。資料庫連得上，但資料表對不上程式。
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var services = new ServiceCollection()
            .AddIdentityModule(database)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var probe = services.GetRequiredService<IReadinessProbe>();
        var status = services.GetRequiredService<IMigrationStatus>();

        // Act
        var readyBefore = await probe.IsReadyAsync(CancellationToken.None);
        var pendingBefore = await status.GetPendingMigrationsAsync(CancellationToken.None);
        await services.GetRequiredService<IModuleInitializer>().InitializeAsync(CancellationToken.None);
        var readyAfter = await probe.IsReadyAsync(CancellationToken.None);
        var pendingAfter = await status.GetPendingMigrationsAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(readyBefore, Is.False);
            Assert.That(pendingBefore, Has.Some.EndsWith("_AddDataProtectionKeys"));
            Assert.That(readyAfter, Is.True);
            Assert.That(pendingAfter, Is.Empty);
        }
    }

    [Test]
    public async Task GetPendingMigrationsAsync_DatabaseUnreachable_Throws()
    {
        // Arrange：啟動時查不到要能分辨「連不上」和「都套用了」，所以連不上時丟例外，不回空的清單。
        await using var services = new ServiceCollection()
            .AddIdentityModule(UnreachableConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Act & Assert
        Assert.That(
            async () => await services.GetRequiredService<IMigrationStatus>().GetPendingMigrationsAsync(CancellationToken.None),
            Throws.InstanceOf<Exception>());
    }

    [Test]
    public async Task IsReadyAsync_DatabaseUnreachable_ReturnsFalse()
    {
        // Arrange：就緒檢查是從根容器解析的，啟動時不會先跑遷移
        await using var services = new ServiceCollection()
            .AddIdentityModule(UnreachableConnectionString)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var probe = services.GetRequiredService<IReadinessProbe>();

        // Act
        var ready = await probe.IsReadyAsync(CancellationToken.None);

        // Assert
        Assert.That(ready, Is.False);
    }

    [Test]
    public async Task AddIdentityModule_CalledTwice_RegistersEachStartupServiceOnce()
    {
        // Arrange
        var services = new ServiceCollection().AddIdentityModule(UnreachableConnectionString).AddIdentityModule(UnreachableConnectionString);

        // Act
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Assert：組合根會依序執行所有初始化，重複註冊就會遷移兩次
        Assert.That(provider.GetServices<IModuleInitializer>().ToList(), Has.Count.EqualTo(1));
        Assert.That(provider.GetServices<IReadinessProbe>().ToList(), Has.Count.EqualTo(1));
        Assert.That(provider.GetServices<IMigrationStatus>().ToList(), Has.Count.EqualTo(1));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AddIdentityModule_BlankConnectionString_Throws(string connectionString)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddIdentityModule(connectionString));
    }

    [Test]
    public void DesignTimeFactory_CreatesContextWithTheRuntimeSettings()
    {
        // Act
        using var db = new DesignTimeIdentityDbContextFactory().CreateDbContext([]);

        // Assert：產生遷移時的模型要和執行時一樣
        Assert.That(db.Model.GetDefaultSchema(), Is.EqualTo(IdentityDbContext.Schema));
        Assert.That(db.Database.ProviderName, Is.EqualTo("Npgsql.EntityFrameworkCore.PostgreSQL"));
    }
}
