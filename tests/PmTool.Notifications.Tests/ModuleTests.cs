using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

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
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWorkAccessQueries>(new FakeWorkAccessQueries())
            .AddSingleton<IWorkEventStream>(new FakeWorkEventStream())
            .AddExternalFakes()
            .AddNotificationsModule(database)
            .BuildServiceProvider(Strict);
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
            Assert.That(pendingBefore, Has.Some.EndsWith("_Initial"));
            Assert.That(readyAfter, Is.True);
            Assert.That(pendingAfter, Is.Empty);
        }
    }

    [Test]
    public async Task GetPendingMigrationsAsync_DatabaseUnreachable_Throws()
    {
        // Arrange：連不上要和「都套用了」分得出來，所以丟例外，不回空的清單。
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IWorkAccessQueries>(new FakeWorkAccessQueries())
            .AddSingleton<IWorkEventStream>(new FakeWorkEventStream())
            .AddExternalFakes()
            .AddNotificationsModule(UnreachableConnectionString)
            .BuildServiceProvider(Strict);

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
    public void AddNotificationsModule_CalledTwice_RegistersEachInitializerAndProbeOnce()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging().AddSingleton<IWorkAccessQueries>(new FakeWorkAccessQueries()).AddSingleton<IWorkEventStream>(new FakeWorkEventStream());

        // Act
        NotificationsTestHost.AddExternalFakes(services);
        services.AddNotificationsModule(UnreachableConnectionString).AddNotificationsModule(UnreachableConnectionString);
        using var provider = services.BuildServiceProvider(Strict);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetServices<IModuleInitializer>().ToList(), Has.Count.EqualTo(1));
            Assert.That(provider.GetServices<IReadinessProbe>().ToList(), Has.Count.EqualTo(1));
            Assert.That(services.Count(d => d.ServiceType == typeof(IWorkEventConsumer)), Is.EqualTo(1));
        }
    }

    [TestCase("PmTool")]
    [TestCase("pmtool\"; DROP TABLE notifications.notifications; --")]
    [TestCase("")]
    public void AddNotificationsModule_InvalidRuntimeRole_Throws(string role)
    {
        // Arrange
        var connections = new DatabaseConnections(UnreachableConnectionString, RuntimeRole: role);

        // Act & Assert
        Assert.That(() => new ServiceCollection().AddNotificationsModule(connections), Throws.ArgumentException);
    }

    [Test]
    public void AddNotificationsModule_EmptyConnection_Throws()
    {
        Assert.That(() => new ServiceCollection().AddNotificationsModule(string.Empty), Throws.ArgumentException);
    }

    [Test]
    public async Task RuntimeRole_AfterMigration_CanDoEverythingTheModuleDoes()
    {
        // Arrange：遷移用擁有者，之後所有服務都用執行的角色連線。
        var (host, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;
        var item = EventBuilder.ForNewItem();
        Guid[] people = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        host.Access.Allow(item.ProjectId, people);
        var actor = new Actor(item.OrganizationId, people[1], false);

        // Act
        await host.Consumer.HandleAsync([item.Created(people[0], people[1]), item.Commented(people[0], "留言")], CancellationToken.None);
        var follow = (await host.Subscriptions.SetAsync(actor, item.ProjectId, item.ItemId, false, CancellationToken.None)).Value;
        var unread = await host.Inbox.CountUnreadAsync(actor, CancellationToken.None);
        var marked = await host.Inbox.MarkAllReadAsync(actor, CancellationToken.None);
        var position = await host.Consumer.GetPositionAsync(CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(follow.Following, Is.False);
            Assert.That((unread, marked), Is.EqualTo((2, 2)));
            Assert.That(position, Is.Not.EqualTo(WorkEventPosition.Start));
        }
    }

    [TestCase("CREATE TABLE notifications.intruder (id integer)", TestName = "RuntimeRole_CreateTable_IsRejected")]
    [TestCase("DROP TABLE notifications.notifications", TestName = "RuntimeRole_DropTable_IsRejected")]
    [TestCase("ALTER TABLE notifications.subscriptions ADD COLUMN intruder integer", TestName = "RuntimeRole_AlterTable_IsRejected")]
    [TestCase("TRUNCATE notifications.notifications", TestName = "RuntimeRole_Truncate_IsRejected")]
    [TestCase("INSERT INTO notifications.__ef_migrations_history VALUES ('99999999999999_Intruder', '10.0.0')",
        TestName = "RuntimeRole_RewriteTheMigrationHistory_IsRejected")]
    public async Task RuntimeRole_ChangingTheStructure_IsRejected(string sql)
    {
        // Arrange
        var (host, runtime) = await StartWithRuntimeRoleAsync();
        await using var _ = host;

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
        var (host, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;

        // Act & Assert
        foreach (var initializer in host.Services.GetServices<IModuleInitializer>())
        {
            Assert.That(async () => await initializer.InitializeAsync(CancellationToken.None), Throws.Nothing);
        }

        Assert.That(await host.Services.GetServices<IReadinessProbe>().Single().IsReadyAsync(CancellationToken.None), Is.True);
    }

    [Test]
    public void DesignTimeFactory_CreatesTheContext()
    {
        // Act
        using var db = new NotificationsDesignTimeFactory().CreateDbContext([]);

        // Assert
        Assert.That(db.Model.FindEntityType(typeof(NotificationRow))?.GetSchema(), Is.EqualTo("notifications"));
    }

    private static async Task<(NotificationsTestHost Host, string Runtime)> StartWithRuntimeRoleAsync()
    {
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        var host = await NotificationsTestHost.StartAsync(new DatabaseConnections(runtime, owner, role));
        return (host, runtime);
    }
}
