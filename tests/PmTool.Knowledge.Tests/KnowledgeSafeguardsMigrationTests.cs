using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Tests;

/// <summary>
/// 遷移 AddKnowledgeSafeguards：誰設的限制（回填）、同一個空間的上層（預先檢查與複合外鍵）、新的索引。
/// 每個測試對自己的資料庫遷移到舊的一版、造舊的資料，再遷移到最新。
/// </summary>
[TestFixture]
public sealed class KnowledgeSafeguardsMigrationTests
{
    private const string BeforeSafeguards = "20261124183912_AddKnowledgeBase";

    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [TearDown]
    public void ClosePooledConnections() => NpgsqlConnection.ClearAllPools();

    [Test]
    public async Task Migrate_EmptyDatabase_RunsToLatestAndMatchesTheModel()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();

        // Act
        await MigrateToAsync(database, null);

        // Assert
        await using var db = NewContext(database);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            Assert.That(await ConstraintNamesAsync(database), Does.Contain("fk_pages_pages_space_id_parent_id").And.Contain("ak_pages_space_id_id"));
            Assert.That(await ConstraintNamesAsync(database), Does.Not.Contain("fk_pages_pages_parent_id"));
            Assert.That(
                await IndexNamesAsync(database),
                Is.SupersetOf(new[] { "ix_page_restrictions_person", "ix_page_attachments_quota", "ix_pages_trash" }));
        }
    }

    [Test]
    public async Task Migrate_ExistingRestrictedPages_RestrictedByIsTheCreatorOnlyWhenTheCreatorIsOnTheList()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        var space = await InsertSpaceAsync(database, "HR");
        var creatorListed = await InsertPageAsync(database, space, 1, restricted: true, createdBy: Creator);
        await InsertRestrictionAsync(database, creatorListed, Creator);
        await InsertRestrictionAsync(database, creatorListed, Other);
        var creatorNotListed = await InsertPageAsync(database, space, 2, restricted: true, createdBy: Creator);
        await InsertRestrictionAsync(database, creatorNotListed, Other);
        var unrestricted = await InsertPageAsync(database, space, 3, restricted: false, createdBy: Creator);

        // Act
        await MigrateToAsync(database, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await RestrictedByAsync(database, creatorListed), Is.EqualTo(Creator));
            Assert.That(await RestrictedByAsync(database, creatorNotListed), Is.Null);
            Assert.That(await RestrictedByAsync(database, unrestricted), Is.Null);
        }
    }

    [Test]
    public async Task Migrate_ParentInTheSameSpace_KeepsTheTreeAndTheNewForeignKeyAcceptsIt()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        var space = await InsertSpaceAsync(database, "HR");
        var parent = await InsertPageAsync(database, space, 1);
        var child = await InsertPageAsync(database, space, 2, parentId: parent);

        // Act
        await MigrateToAsync(database, null);

        // Assert
        Assert.That(await ParentOfAsync(database, child), Is.EqualTo(parent));
    }

    [Test]
    public async Task Migrate_ParentInAnotherSpace_FailsAndChangesNothing()
    {
        // Arrange：舊資料裡有一頁的上層在別的空間；另外有一個本來會被回填的限制頁面，用來確認什麼都沒動。
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        var first = await InsertSpaceAsync(database, "HR");
        var second = await InsertSpaceAsync(database, "IT");
        var parent = await InsertPageAsync(database, first, 1);
        await InsertPageAsync(database, second, 1, parentId: parent);
        var restricted = await InsertPageAsync(database, first, 2, restricted: true, createdBy: Creator);
        await InsertRestrictionAsync(database, restricted, Creator);

        // Act
        var failure = Assert.CatchAsync<Exception>(async () => await MigrateToAsync(database, null));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Message, Does.Contain("同一個空間"));
            Assert.That(await ColumnExistsAsync(database, "restricted_by"), Is.False, "遷移沒有改任何東西");
            Assert.That(await ConstraintNamesAsync(database), Does.Contain("fk_pages_pages_parent_id"));
            Assert.That(await ConstraintNamesAsync(database), Does.Not.Contain("ak_pages_space_id_id"));
            Assert.That(await AppliedMigrationsAsync(database), Has.None.EndsWith("_AddKnowledgeSafeguards"));
        }
    }

    [Test]
    public async Task Migrate_AfterwardsAParentInAnotherSpaceIsRefusedByTheDatabase()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, null);
        var first = await InsertSpaceAsync(database, "HR");
        var second = await InsertSpaceAsync(database, "IT");
        var parent = await InsertPageAsync(database, first, 1);

        // Act
        var failure = Assert.CatchAsync<PostgresException>(
            async () => await InsertPageAsync(database, second, 1, parentId: parent));

        // Assert
        Assert.That(failure?.SqlState, Is.EqualTo(PostgresErrorCodes.ForeignKeyViolation));
    }

    [Test]
    public async Task Downgrade_RestoresTheSingleColumnForeignKeyAndCanBeAppliedAgain()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, null);

        // Act
        await MigrateToAsync(database, BeforeSafeguards);
        var afterDowngrade = await ConstraintNamesAsync(database);
        await MigrateToAsync(database, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDowngrade, Does.Contain("fk_pages_pages_parent_id"));
            Assert.That(afterDowngrade, Does.Not.Contain("fk_pages_pages_space_id_parent_id"));
            Assert.That(await ConstraintNamesAsync(database), Does.Contain("fk_pages_pages_space_id_parent_id"));
        }
    }

    private static async Task MigrateToAsync(string connectionString, string? migration)
    {
        await using var db = NewContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static KnowledgeDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
        KnowledgeDbContext.Configure(options, connectionString);
        return new KnowledgeDbContext(options.Options);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Guid> InsertSpaceAsync(string connectionString, string key)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO knowledge.spaces (id, organization_id, key, name, next_page_number, created_by, created_at, updated_by, updated_at)
            VALUES (@id, @org, @key, @name, 100, @by, now(), @by, now())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("org", Organization);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("name", $"空間 {key}");
        command.Parameters.AddWithValue("by", Creator);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<Guid> InsertPageAsync(
        string connectionString, Guid space, int number, bool restricted = false, Guid? createdBy = null, Guid? parentId = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO knowledge.pages
              (id, organization_id, space_id, number, parent_id, position, title, body, revision, is_restricted,
               created_by, created_at, updated_by, updated_at)
            VALUES (@id, @org, @space, @number, @parent, @number, @title, '', 1, @restricted, @by, now(), @by, now())
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("org", Organization);
        command.Parameters.AddWithValue("space", space);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("parent", (object?)parentId ?? DBNull.Value);
        command.Parameters.AddWithValue("title", $"頁面 {number}");
        command.Parameters.AddWithValue("restricted", restricted);
        command.Parameters.AddWithValue("by", createdBy ?? Creator);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task InsertRestrictionAsync(string connectionString, Guid page, Guid person)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(
            "INSERT INTO knowledge.page_restrictions (page_id, person_id, organization_id, position) VALUES (@page, @person, @org, 0)",
            connection);
        command.Parameters.AddWithValue("page", page);
        command.Parameters.AddWithValue("person", person);
        command.Parameters.AddWithValue("org", Organization);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid?> RestrictedByAsync(string connectionString, Guid page)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("SELECT restricted_by FROM knowledge.pages WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", page);
        var value = await command.ExecuteScalarAsync();
        return value is Guid guid ? guid : null;
    }

    private static async Task<Guid?> ParentOfAsync(string connectionString, Guid page)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand("SELECT parent_id FROM knowledge.pages WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", page);
        var value = await command.ExecuteScalarAsync();
        return value is Guid guid ? guid : null;
    }

    private static async Task<IReadOnlyList<string>> ConstraintNamesAsync(string connectionString) =>
        await QueryStringsAsync(
            connectionString,
            "SELECT conname FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = 'knowledge'");

    private static async Task<IReadOnlyList<string>> IndexNamesAsync(string connectionString) =>
        await QueryStringsAsync(connectionString, "SELECT indexname FROM pg_indexes WHERE schemaname = 'knowledge'");

    private static async Task<IReadOnlyList<string>> AppliedMigrationsAsync(string connectionString) =>
        await QueryStringsAsync(connectionString, "SELECT migration_id FROM knowledge.__ef_migrations_history");

    private static async Task<bool> ColumnExistsAsync(string connectionString, string column) =>
        (await QueryStringsAsync(
            connectionString,
            $"SELECT column_name FROM information_schema.columns WHERE table_schema = 'knowledge' AND table_name = 'pages' AND column_name = '{column}'")).Count > 0;

    private static async Task<IReadOnlyList<string>> QueryStringsAsync(string connectionString, string sql)
    {
        await using var connection = await OpenAsync(connectionString);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
