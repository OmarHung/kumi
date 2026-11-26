using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>
/// 執行與遷移分成兩個角色：遷移用資料表的擁有者，遷移完把讀寫權限授予執行的角色。
/// 執行的角色要做得到工作引擎的每一件事，卻不能改資料表結構，應用程式被攻破時才不會連結構一起被改掉。
/// 每個測試各自開資料庫與角色。
/// </summary>
[TestFixture]
public sealed class WorkDatabaseRoleTests
{
    private const string InsufficientPrivilege = PostgresErrorCodes.InsufficientPrivilege;

    /// <summary>
    /// 每個測試都開自己的資料庫與角色，每一組連線字串各有一個連線池；池裡閒置的連線不會馬上關，
    /// 留著的話整個測試組件共用的 PostgreSQL 很快就沒有連線可以給後面的測試。
    /// </summary>
    [TearDown]
    public void ClosePooledConnections() => NpgsqlConnection.ClearAllPools();

    [Test]
    public async Task RuntimeRole_AfterMigration_CanDoEverythingTheEngineDoes()
    {
        // Arrange：遷移用擁有者，之後所有服務都用執行的角色連線。
        var (host, _, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.CreateProjectAsync("OPS");

        // Act：建立、修改、轉換狀態、留言、改類型、刪除，再套用一份會搬資料的變更集（直接下 SQL 更新項目並封存值）。
        var task = await scenario.CreateItemAsync("OPS", new NewWorkItem { Type = "task", Title = "範例任務", Fields = Scenario.Fields(("summary", "摘要")) });
        var doing = await host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);
        var renamed = await host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { Title = "改過的標題" }, CancellationToken.None);
        var comment = await host.Activity.AddCommentAsync(scenario.Admin, task.Key, "範例留言", null, CancellationToken.None);
        var request = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "範例需求", Fields = Scenario.Fields(("quote_amount", 1200)) });
        var changedType = await host.Items.ChangeTypeAsync(scenario.Admin, request.Key, "task", CancellationToken.None);
        var deleted = await host.Items.DeleteAsync(scenario.Admin, request.Key, CancellationToken.None);
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario,
            "OPS",
            b => b.WithoutState("task_flow", "doing").WithoutField("summary"),
            BlueprintEdits.Moves(("task_flow", "doing", "todo")) with { DropFields = ["summary"] });
        var applied = await ChangeSetDrafts.ApplyAsync(scenario, "OPS", proposed.ChangeSet.Number);
        var readBack = await host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(doing.Succeeded && renamed.Succeeded && comment.Succeeded && changedType.Succeeded && deleted.Succeeded, Is.True);
            Assert.That(applied.ChangeSet.Status, Is.EqualTo(ChangeSetStatus.Applied));
            Assert.That((readBack.Value.Title, readBack.Value.State), Is.EqualTo(("改過的標題", "todo")));
            Assert.That(await ArchivedValues.ListAsync(host, scenario), Is.Not.Empty, "改類型與變更集都封存了值");
        }
    }

    [Test]
    public async Task RuntimeRole_AfterMigration_CanManageCyclesAndMilestonesAndDeletingThemClearsTheItems()
    {
        // Arrange
        var (host, _, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");

        // Act：刪除時資料庫把項目的外鍵設成空，這一步要在執行的角色下也做得到。
        var cycle = await host.Planning.CreateCycleAsync(scenario.Admin, "WEB", new CycleDraft("衝刺 1", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 15)), CancellationToken.None);
        var milestone = await host.Planning.CreateMilestoneAsync(scenario.Admin, "WEB", new MilestoneDraft("v1", new DateOnly(2026, 12, 1), null), CancellationToken.None);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", CycleId = cycle.Value.Id, MilestoneId = milestone.Value.Id });
        var deletedCycle = await host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Value.Id, CancellationToken.None);
        var deletedMilestone = await host.Planning.DeleteMilestoneAsync(scenario.Admin, "WEB", milestone.Value.Id, CancellationToken.None);
        var readBack = await host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(deletedCycle.Succeeded && deletedMilestone.Succeeded, Is.True);
        Assert.That((readBack.Value.CycleId, readBack.Value.MilestoneId), Is.EqualTo(((Guid?)null, (Guid?)null)));
    }

    [TestCase("CREATE TABLE work.intruder (id integer)", TestName = "RuntimeRole_CreateTableInTheModuleSchema_IsRejected")]
    [TestCase("CREATE TABLE public.intruder (id integer)", TestName = "RuntimeRole_CreateTableInThePublicSchema_IsRejected")]
    [TestCase("CREATE SCHEMA intruder", TestName = "RuntimeRole_CreateSchema_IsRejected")]
    [TestCase("DROP TABLE work.items", TestName = "RuntimeRole_DropTable_IsRejected")]
    [TestCase("ALTER TABLE work.items ADD COLUMN intruder integer", TestName = "RuntimeRole_AlterTable_IsRejected")]
    [TestCase("TRUNCATE work.items", TestName = "RuntimeRole_Truncate_IsRejected")]
    [TestCase("UPDATE work.events SET kind = 'item_deleted'", TestName = "RuntimeRole_UpdateAnEvent_IsRejected")]
    [TestCase("DELETE FROM work.events", TestName = "RuntimeRole_DeleteEvents_IsRejected")]
    [TestCase("TRUNCATE work.events", TestName = "RuntimeRole_TruncateEvents_IsRejected")]
    [TestCase("DELETE FROM work.__ef_migrations_history", TestName = "RuntimeRole_RewriteTheMigrationHistory_IsRejected")]
    public async Task RuntimeRole_ChangingTheStructure_IsRejected(string sql)
    {
        // Arrange
        var (host, runtime, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;

        // Act
        var rejected = await TryExecuteAsync(runtime, sql);

        // Assert
        Assert.That(rejected?.SqlState, Is.EqualTo(InsufficientPrivilege), () => $"應該被拒絕：{rejected}");
    }

    [Test]
    public async Task RuntimeRole_TablesAddedByALaterMigration_AreGrantedWithoutGrantingAgain()
    {
        // Arrange：之後的遷移（擁有者）在 work 底下新增一張有序列的資料表。
        var (host, runtime, owner) = await StartWithRuntimeRoleAsync();
        await using var _ = host;
        Assert.That(await TryExecuteAsync(owner, "CREATE TABLE work.added_later (id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, note text)"), Is.Null);

        // Act
        var inserted = await TryExecuteAsync(runtime, "INSERT INTO work.added_later (note) VALUES ('範例')");
        var updated = await TryExecuteAsync(runtime, "UPDATE work.added_later SET note = '改過'");
        var removed = await TryExecuteAsync(runtime, "DELETE FROM work.added_later");

        // Assert
        Assert.That((inserted, updated, removed), Is.EqualTo(((PostgresException?)null, (PostgresException?)null, (PostgresException?)null)));
    }

    [Test]
    public async Task Initialize_RuntimeRoleIsTheMigrationsRole_FailsExplainingWhy()
    {
        // Arrange：只設了 RuntimeRole、沒另外設遷移用的連線：授權會把擁有者自己對遷移紀錄表的寫入也收掉。
        var database = await PostgresServer.CreateDatabaseAsync();
        var ownerRole = new NpgsqlConnectionStringBuilder(database).Username!;
        var connections = new DatabaseConnections(database, database, ownerRole);

        // Act & Assert
        var failure = Assert.CatchAsync<PostgresException>(async () => await (await WorkTestHost.StartAsync(connections)).DisposeAsync());
        Assert.That(failure?.MessageText, Does.Contain("Database:RuntimeRole").And.Contain("ConnectionStrings:Migrations"));
    }

    [Test]
    public async Task Initialize_SeveralContainersAtOnce_AllSucceed()
    {
        // Arrange：雲端一次起好幾個容器、都設了 MigrateOnStartup：同時授權不能撞到 tuple concurrently updated。
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        await (await WorkTestHost.StartAsync(new DatabaseConnections(runtime, owner, role))).DisposeAsync();

        // Act
        var starts = Enumerable.Range(0, 6)
            .Select(_ => Task.Run(async () => await (await WorkTestHost.StartAsync(new DatabaseConnections(runtime, owner, role))).DisposeAsync()))
            .ToArray();

        // Assert
        Assert.That(async () => await Task.WhenAll(starts), Throws.Nothing);
    }

    [Test]
    public async Task Initialize_RunAgainWithTheRole_ChangesNothingAndStillWorks()
    {
        // Arrange：每次部署都會跑 migrate，授權要能重跑。
        var (host, runtime, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;

        // Act
        foreach (var initializer in host.Root.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        var selected = await TryExecuteAsync(runtime, "SELECT count(*) FROM work.projects");

        // Assert
        Assert.That(selected, Is.Null);
    }

    [Test]
    public async Task Initialize_AfterARestoreOntoANewServer_NormalizesTheEventHistorySoTheRuntimeRoleCanRead()
    {
        // Arrange：還原到新的資料庫伺服器之後，事件的交易編號比新伺服器的大。執行的角色對 events 只能新增與讀（只能追加），
        // 歸一要在遷移那一步用擁有者做；不然讀的人每一輪都撞到 permission denied，通知與即時更新整個停掉。
        var (host, _, owner) = await StartWithRuntimeRoleAsync();
        await using var _ = host;
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("OPS");
        await scenario.CreateItemAsync("OPS", new NewWorkItem { Type = "task", Title = "還原前的項目" });
        Assert.That(await TryExecuteAsync(owner, "UPDATE work.events SET transaction_id = ((transaction_id::text::bigint) + 5000000000000)::text::xid8"), Is.Null);

        // Act：還原之後一定跑一次 migrate（scripts/ops/restore.sh）。
        foreach (var initializer in host.Root.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        var events = await host.Events.ReadAfterAsync(new WorkEventPosition(0, 0), 100, CancellationToken.None);

        // Assert
        Assert.That(events, Is.Not.Empty, "執行的角色讀得到還原前的事件，沒有撞到權限");
    }

    [Test]
    public async Task IsReadyAsync_AsTheRuntimeRole_IsTrue()
    {
        // Arrange：就緒檢查用執行的角色讀遷移紀錄表，它只能讀不能改。
        var (host, _, _) = await StartWithRuntimeRoleAsync();
        await using var _ = host;

        // Act
        var ready = await host.Root.GetServices<IReadinessProbe>().Single().IsReadyAsync(CancellationToken.None);

        // Assert
        Assert.That(ready, Is.True);
    }

    [Test]
    public async Task Initialize_RoleThatDoesNotExist_Fails()
    {
        // Arrange：名稱合規定，但資料庫裡沒有這個角色。設定打錯字要在遷移時就失敗，不要等到執行時才連不上。
        var database = await PostgresServer.CreateDatabaseAsync();
        var connections = new DatabaseConnections(database, database, $"missing_{Guid.NewGuid():N}");

        // Act & Assert
        var failure = Assert.CatchAsync<PostgresException>(async () => await (await WorkTestHost.StartAsync(connections)).DisposeAsync());
        Assert.That(failure?.SqlState, Is.EqualTo(PostgresErrorCodes.UndefinedObject));
    }

    [TestCase("PmTool")]
    [TestCase("pm-tool")]
    [TestCase("1pmtool")]
    [TestCase("pmtool\"; DROP TABLE work.items; --")]
    [TestCase("")]
    [TestCase("pmtool\n")]
    public void AddWorkModule_InvalidRuntimeRole_Throws(string role)
    {
        // Arrange：名稱會原樣放進 GRANT 的 SQL，只收小寫英數字與底線。
        var services = new ServiceCollection();
        var connections = new DatabaseConnections("Host=localhost;Database=example", RuntimeRole: role);

        // Act & Assert
        Assert.That(() => services.AddWorkModule(connections), Throws.ArgumentException);
    }

    [Test]
    public void AddWorkModule_RuntimeRoleLongerThanPostgresAllows_Throws()
    {
        // Arrange：PostgreSQL 的識別字最長 63 個位元組，更長的會被默默截掉，授權給錯的角色。
        var connections = new DatabaseConnections("Host=localhost;Database=example", RuntimeRole: new string('a', 64));

        // Act & Assert
        Assert.That(() => new ServiceCollection().AddWorkModule(connections), Throws.ArgumentException);
    }

    [Test]
    public async Task Migration_LeavesNoOwnerConnectionOpenAfterwards()
    {
        // Arrange：遷移的連線帶一個認得出來的名稱。遷移完程序裡不該還留著它：平常的服務用不到擁有者的連線，
        // 放在連線池裡閒置著，被攻破時卻拿得到。
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        var applicationName = $"migrations-{Guid.NewGuid():N}";
        var migrations = new NpgsqlConnectionStringBuilder(owner) { ApplicationName = applicationName }.ConnectionString;

        // Act
        await using var host = await WorkTestHost.StartAsync(new DatabaseConnections(runtime, migrations, role));

        // Assert：關連線是非同步的，伺服器那一端可能晚一點才消失，等一下下；留在池子裡的話會一直在。
        Assert.That(await CountConnectionsNamedAsync(owner, applicationName, waitForZero: TimeSpan.FromSeconds(5)), Is.Zero);
    }

    private static async Task<int> CountConnectionsNamedAsync(string connectionString, string applicationName, TimeSpan waitForZero)
    {
        var deadline = DateTime.UtcNow + waitForZero;
        while (true)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT count(*)::int FROM pg_stat_activity WHERE application_name = @name", connection);
            command.Parameters.AddWithValue("name", applicationName);
            var count = (int)(await command.ExecuteScalarAsync() ?? 0);
            if (count == 0 || DateTime.UtcNow >= deadline)
            {
                return count;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static async Task<(WorkTestHost Host, string Runtime, string Owner)> StartWithRuntimeRoleAsync()
    {
        var owner = await PostgresServer.CreateDatabaseAsync();
        var (role, runtime) = await PostgresServer.CreateRuntimeRoleAsync(owner);
        var host = await WorkTestHost.StartAsync(new DatabaseConnections(runtime, owner, role));
        return (host, runtime, owner);
    }

    /// <returns>資料庫拒絕時的錯誤；成功時是 null。</returns>
    private static async Task<PostgresException?> TryExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }
}
