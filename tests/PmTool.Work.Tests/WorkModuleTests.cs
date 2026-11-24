using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>組合根怎麼接上這個模組：註冊、啟動時的遷移、就緒檢查。</summary>
[TestFixture]
public sealed class WorkModuleTests
{
    [Test]
    public async Task InitializeAsync_EmptyDatabase_MigratesToTheLatestAndCanRunAgain()
    {
        // Arrange
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        await using var provider = WorkTestHost.BuildProvider(connectionString, TimeProvider.System);
        var initializer = provider.GetServices<IModuleInitializer>().Single();

        // Act
        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        // Assert
        await using var db = await provider.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
        Assert.That(await db.Database.GetAppliedMigrationsAsync(), Is.Not.Empty);
        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var admin = new Actor(Guid.NewGuid(), Guid.NewGuid(), IsOrganizationAdmin: true);
        var created = await projects.CreateAsync(admin, new NewProject("WEB", "遷移後可以用", "simple_tasks"), CancellationToken.None);
        Assert.That(created.Succeeded, Is.True);
    }

    [Test]
    public async Task Migrations_MatchTheCurrentModel()
    {
        // Arrange
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        await using var provider = WorkTestHost.BuildProvider(connectionString, TimeProvider.System);
        await using var db = await provider.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();

        // Act
        var hasPendingChanges = db.Database.HasPendingModelChanges();

        // Assert：改了模型卻忘了產生遷移，部署後的資料表會和程式對不上
        Assert.That(hasPendingChanges, Is.False);
    }

    [TestCase(false, TestName = "ArchivedValue_WithoutASource_IsRejectedByTheDatabase")]
    [TestCase(true, TestName = "ArchivedValue_WithBothSources_IsRejectedByTheDatabase")]
    public async Task ArchivedValue_NotExactlyOneSource_IsRejectedByTheDatabase(bool both)
    {
        // Arrange：封存的值不是來自變更集、就是來自改類型的活動，剛好一個；這一條由資料表把關，程式寫錯也存不進去。
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        await using var provider = WorkTestHost.BuildProvider(connectionString, TimeProvider.System);
        await provider.GetServices<IModuleInitializer>().Single().InitializeAsync(CancellationToken.None);
        await using var db = await provider.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();

        // Act
        var rejected = Assert.CatchAsync<DbUpdateException>(() => InsertArchivedValueAsync(db, both ? Guid.NewGuid() : null));

        // Assert
        Assert.That(
            (rejected?.InnerException as Npgsql.PostgresException)?.ConstraintName,
            Is.EqualTo(WorkDbContext.ArchiveSourceCheck));
    }

    [Test]
    public async Task IsReadyAsync_DatabaseReachableAndMigrated_IsTrue()
    {
        // Arrange
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        await using var provider = WorkTestHost.BuildProvider(connectionString, TimeProvider.System);
        await provider.GetServices<IModuleInitializer>().Single().InitializeAsync(CancellationToken.None);

        // Act
        var ready = await provider.GetServices<IReadinessProbe>().Single().IsReadyAsync(CancellationToken.None);

        // Assert
        Assert.That(ready, Is.True);
    }

    [Test]
    public async Task IsReadyAsync_MigrationsNotApplied_IsFalseUntilMigrated()
    {
        // Arrange：啟動時不遷移（Database:MigrateOnStartup=false）、migrate 還沒跑。資料庫連得上，但資料表對不上程式。
        var connectionString = await PostgresServer.CreateDatabaseAsync();
        await using var provider = WorkTestHost.BuildProvider(connectionString, TimeProvider.System);
        var probe = provider.GetServices<IReadinessProbe>().Single();
        var status = provider.GetServices<IMigrationStatus>().Single();

        // Act
        var readyBefore = await probe.IsReadyAsync(CancellationToken.None);
        var pendingBefore = await status.GetPendingMigrationsAsync(CancellationToken.None);
        await provider.GetServices<IModuleInitializer>().Single().InitializeAsync(CancellationToken.None);
        var readyAfter = await probe.IsReadyAsync(CancellationToken.None);
        var pendingAfter = await status.GetPendingMigrationsAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(readyBefore, Is.False);
            Assert.That(pendingBefore, Has.Some.EndsWith("_Initial"));
            Assert.That(readyAfter, Is.True);
            Assert.That(pendingAfter, Is.Empty);
        }
    }

    [Test]
    public async Task GetPendingMigrationsAsync_DatabaseUnreachable_Throws()
    {
        // Arrange：啟動時查不到要能分辨「連不上」和「都套用了」，所以連不上時丟例外，不回空的清單。
        const string nowhere = "Host=127.0.0.1;Port=1;Database=nowhere;Username=example;Timeout=2";
        await using var provider = WorkTestHost.BuildProvider(nowhere, TimeProvider.System);

        // Act & Assert
        Assert.That(
            async () => await provider.GetServices<IMigrationStatus>().Single().GetPendingMigrationsAsync(CancellationToken.None),
            Throws.InstanceOf<Exception>());
    }

    [Test]
    public async Task IsReadyAsync_DatabaseUnreachable_IsFalse()
    {
        // Arrange：沒有人在聽的連接埠。
        const string nowhere = "Host=127.0.0.1;Port=1;Database=nowhere;Username=example;Timeout=2";
        await using var provider = WorkTestHost.BuildProvider(nowhere, TimeProvider.System);

        // Act
        var ready = await provider.GetServices<IReadinessProbe>().Single().IsReadyAsync(CancellationToken.None);

        // Assert
        Assert.That(ready, Is.False);
    }

    [Test]
    public void AddWorkModule_CalledTwice_RegistersOneInitializerAndOneProbe()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddWorkModule("Host=localhost;Database=example");
        services.AddWorkModule("Host=localhost;Database=example");

        // Assert
        Assert.That(services.Count(s => s.ServiceType == typeof(IModuleInitializer)), Is.EqualTo(1));
        Assert.That(services.Count(s => s.ServiceType == typeof(IReadinessProbe)), Is.EqualTo(1));
        Assert.That(services.Count(s => s.ServiceType == typeof(IMigrationStatus)), Is.EqualTo(1));
    }

    [Test]
    public void AddWorkModule_OtherModulesInitializers_AreKept()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IModuleInitializer, OtherModuleInitializer>();

        // Act
        services.AddWorkModule("Host=localhost;Database=example");

        // Assert
        Assert.That(services.Count(s => s.ServiceType == typeof(IModuleInitializer)), Is.EqualTo(2));
    }

    [Test]
    public void AddWorkModule_NoTimeProviderRegistered_UsesTheSystemClock()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddWorkModule("Host=localhost;Database=example");
        using var provider = services.BuildServiceProvider();

        // Assert
        Assert.That(provider.GetRequiredService<TimeProvider>(), Is.SameAs(TimeProvider.System));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AddWorkModule_BlankConnectionString_Throws(string connectionString)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act & Assert
        Assert.That(() => services.AddWorkModule(connectionString), Throws.ArgumentException);
    }

    /// <summary>
    /// 一筆封存值，兩個來源都是 <paramref name="source"/>。檢查限制在外鍵之前就擋下，所以不必先建專案與項目；
    /// 交易不提交，什麼都不會留下。
    /// </summary>
    private static async Task InsertArchivedValueAsync(WorkDbContext db, Guid? source)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.ArchivedFieldValues.Add(new ArchivedFieldValueEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            ItemId = Guid.NewGuid(),
            Field = "summary",
            Value = "\"範例\"",
            ChangeSetId = source,
            ItemActivityId = source,
            ArchivedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class OtherModuleInitializer : IModuleInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
