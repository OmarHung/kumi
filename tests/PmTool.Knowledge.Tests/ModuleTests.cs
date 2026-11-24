using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Persistence;
using PmTool.Work;

namespace PmTool.Knowledge.Tests;

/// <summary>註冊入口、就緒檢查、遷移，以及執行的角色與資料表擁有者分開時的授權。</summary>
[TestFixture]
public sealed class ModuleTests
{
    private const string UnreachableConnectionString = "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=2";

    private static readonly ServiceProviderOptions Strict = new() { ValidateScopes = true, ValidateOnBuild = true };

    [TearDown]
    public void ClosePooledConnections() => NpgsqlConnection.ClearAllPools();

    [Test]
    public async Task IsReadyAsync_MigrationsNotApplied_ReturnsFalseUntilMigrated()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var services = Services(database);
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
            Assert.That(pendingBefore, Has.Some.EndsWith("_AddKnowledgeBase"));
            Assert.That(readyAfter, Is.True);
            Assert.That(pendingAfter, Is.Empty);
        }
    }

    [Test]
    public async Task GetPendingMigrationsAsync_DatabaseUnreachable_Throws()
    {
        // Arrange：連不上要和「都套用了」分得出來，所以丟例外，不回空的清單。
        await using var services = Services(UnreachableConnectionString);

        // Act
        var ready = await services.GetRequiredService<IReadinessProbe>().IsReadyAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ready, Is.False);
            await Assert.ThatAsync(
                async () => await services.GetRequiredService<IMigrationStatus>().GetPendingMigrationsAsync(CancellationToken.None),
                Throws.Exception);
        }
    }

    [Test]
    public void AddKnowledgeModule_CalledTwice_RegistersEachInitializerAndProbeOnce()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IPeopleDirectory>(new FakePeopleDirectory());
        services.AddSingleton<IWorkAccessQueries>(new FakeWorkAccessQueries());
        services.AddLogging();

        // Act
        services.AddKnowledgeModule(UnreachableConnectionString).AddKnowledgeModule(UnreachableConnectionString);
        using var provider = services.BuildServiceProvider(Strict);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetServices<IModuleInitializer>().ToList(), Has.Count.EqualTo(1));
            Assert.That(provider.GetServices<IReadinessProbe>().ToList(), Has.Count.EqualTo(1));
            Assert.That(provider.GetServices<IMigrationStatus>().ToList(), Has.Count.EqualTo(1));
        }
    }

    [TestCase("PmTool")]
    [TestCase("pmtool\"; DROP TABLE knowledge.pages; --")]
    [TestCase("")]
    public void AddKnowledgeModule_InvalidRuntimeRole_Throws(string role)
    {
        // Arrange
        var connections = new DatabaseConnections(UnreachableConnectionString, RuntimeRole: role);

        // Act & Assert
        Assert.That(() => new ServiceCollection().AddKnowledgeModule(connections), Throws.ArgumentException);
    }

    [Test]
    public void AddKnowledgeModule_EmptyConnection_Throws()
    {
        Assert.That(() => new ServiceCollection().AddKnowledgeModule(string.Empty), Throws.ArgumentException);
    }

    [Test]
    public async Task RuntimeRole_AfterMigration_CanDoEverythingTheModuleDoes()
    {
        // Arrange：遷移用擁有者，之後所有服務都用執行的角色連線。
        await using var host = await StartWithRuntimeRoleAsync();
        var org = new Org(host);

        // Act
        var space = await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁", body: "內文");
        var child = await org.PageAsync("HR", "子頁", parent: page.Number);
        var restricted = await host.Pages.SetRestrictionAsync(org.Admin, "HR", child.Number, [org.Admin.PersonId], CancellationToken.None);
        await host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "新內文", page.Revision), CancellationToken.None);
        await host.Pages.MoveAsync(org.Admin, "HR", child.Number, new PageMove(null, page.Number), CancellationToken.None);
        await host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var restored = await host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(space.Key, Is.EqualTo("HR"));
            Assert.That(restricted.Succeeded, Is.True);
            Assert.That(restored.Succeeded, Is.True);
        }
    }

    [TestCase("CREATE TABLE knowledge.intruder (id integer)", TestName = "RuntimeRole_CreateTable_IsRejected")]
    [TestCase("DROP TABLE knowledge.pages", TestName = "RuntimeRole_DropTable_IsRejected")]
    [TestCase("ALTER TABLE knowledge.spaces ADD COLUMN intruder integer", TestName = "RuntimeRole_AlterTable_IsRejected")]
    [TestCase("TRUNCATE knowledge.pages", TestName = "RuntimeRole_Truncate_IsRejected")]
    [TestCase("INSERT INTO knowledge.__ef_migrations_history VALUES ('99999999999999_Intruder', '10.0.0')",
        TestName = "RuntimeRole_RewriteTheMigrationHistory_IsRejected")]
    public async Task RuntimeRole_ChangingTheStructure_IsRejected(string sql)
    {
        // Arrange
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        await using var host = await KnowledgeTestHost.StartAsync(new DatabaseConnections(runtime, owner, role));

        // Act
        await using var connection = new NpgsqlConnection(runtime);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rejected = Assert.CatchAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync());

        // Assert
        Assert.That(rejected?.SqlState, Is.EqualTo(PostgresErrorCodes.InsufficientPrivilege));
    }

    [Test]
    public async Task Initialize_RunAgainWithTheRole_StillWorks()
    {
        // Arrange：每次部署都會跑 migrate，授權要能重跑。
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        await using var services = Services(new DatabaseConnections(runtime, owner, role));

        // Act & Assert
        foreach (var initializer in services.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
            Assert.That(async () => await initializer.InitializeAsync(CancellationToken.None), Throws.Nothing);
        }

        Assert.That(await services.GetServices<IReadinessProbe>().Single().IsReadyAsync(CancellationToken.None), Is.True);
    }

    [Test]
    public void DesignTimeFactory_CreatesTheContext()
    {
        // Act
        using var db = new KnowledgeDesignTimeFactory().CreateDbContext([]);

        // Assert
        Assert.That(db.Model.FindEntityType(typeof(PageRow))?.GetSchema(), Is.EqualTo("knowledge"));
    }

    private static ServiceProvider Services(string connectionString) => Services(new DatabaseConnections(connectionString));

    private static ServiceProvider Services(DatabaseConnections connections)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPeopleDirectory>(new FakePeopleDirectory());
        services.AddSingleton<IWorkAccessQueries>(new FakeWorkAccessQueries());
        services.AddLogging();
        services.AddKnowledgeModule(connections);
        return services.BuildServiceProvider(Strict);
    }

    private static async Task<KnowledgeTestHost> StartWithRuntimeRoleAsync()
    {
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        return await KnowledgeTestHost.StartAsync(new DatabaseConnections(runtime, owner, role));
    }
}
