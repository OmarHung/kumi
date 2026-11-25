using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>遷移 AddAttachmentQuotaIndex：儲存空間的用量查詢用的索引。</summary>
[TestFixture]
public sealed class AttachmentQuotaIndexMigrationTests
{
    private const string BeforeQuotaIndex = "20261124084137_AddCommentEdits";

    [Test]
    public async Task Migrate_EmptyDatabase_RunsToLatestAndCreatesTheCoveringIndex()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();

        // Act
        await MigrateToAsync(database, null);

        // Assert
        var definition = await IndexDefinitionAsync(database);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(definition, Does.Contain("(organization_id, uploaded_by, uploaded_at)"));
            Assert.That(definition, Does.Contain("INCLUDE (size_bytes)"));
        }
    }

    [Test]
    public async Task Migrate_DatabaseOnThePreviousVersion_AddsTheIndexAndKeepsExistingRows()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeQuotaIndex);
        var before = await IndexDefinitionAsync(database);

        // Act
        await MigrateToAsync(database, null);

        // Assert
        await using var db = await ContextAsync(database);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before, Is.Null);
            Assert.That(await IndexDefinitionAsync(database), Is.Not.Null);
            Assert.That(db.Database.HasPendingModelChanges(), Is.False, "遷移要和目前的模型一致");
        }
    }

    [Test]
    public async Task Downgrade_DropsTheIndexAndCanBeAppliedAgain()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, null);

        // Act
        await MigrateToAsync(database, BeforeQuotaIndex);
        var afterDowngrade = await IndexDefinitionAsync(database);
        await MigrateToAsync(database, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDowngrade, Is.Null);
            Assert.That(await IndexDefinitionAsync(database), Is.Not.Null);
        }
    }

    private static async Task MigrateToAsync(string connectionString, string? migration)
    {
        await using var db = await ContextAsync(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static Task<WorkDbContext> ContextAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>();
        WorkDbContext.Configure(options, connectionString);
        return Task.FromResult(new WorkDbContext(options.Options));
    }

    private static async Task<string?> IndexDefinitionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'work' AND indexname = 'ix_attachments_quota'", connection);
        return await command.ExecuteScalarAsync() as string;
    }
}
