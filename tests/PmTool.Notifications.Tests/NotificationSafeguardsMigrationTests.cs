using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 遷移 AddNotificationSafeguards：較早的通知用（項目、做的人、時刻）補上留言編號，加上換摘要的時刻與兩個索引。
/// 回填會讀工作引擎的活動表（一次性的例外）；沒有那張表的資料庫照樣跑得過。
/// </summary>
[TestFixture]
public sealed class NotificationSafeguardsMigrationTests
{
    private const string BeforeSafeguards = "20261125061247_AddAccountNotices";

    private static readonly Guid Organization = Guid.NewGuid();
    private static readonly Guid Recipient = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 11, 10, 3, 0, 0, TimeSpan.Zero);

    [TearDown]
    public void ClosePooledConnections() => NpgsqlConnection.ClearAllPools();

    [Test]
    public async Task Migrate_EmptyDatabase_RunsToLatestAddsTheIndexesAndMatchesTheModel()
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
            Assert.That(await IndexNamesAsync(database), Is.SupersetOf(new[] { "ix_notifications_comment", "ix_notifications_item" }));
        }
    }

    [Test]
    public async Task Migrate_OldCommentNotifications_GetTheCommentIdFromTheMatchingActivity()
    {
        // Arrange：舊的通知沒有留言編號；工作引擎的活動表裡有同一個項目、同一個人、同一個時刻的留言
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        await CreateActivityTableAsync(database);
        var item = Guid.NewGuid();
        var comment = await InsertActivityAsync(database, item, Actor, "commented", At);
        var otherKind = await InsertActivityAsync(database, item, Actor, "state_changed", At.AddMinutes(1));
        var commented = await InsertNotificationAsync(database, item, "commented", At);
        var mentioned = await InsertNotificationAsync(database, item, "mentioned", At);
        var noMatchingTime = await InsertNotificationAsync(database, item, "commented", At.AddHours(1));
        var notAComment = await InsertNotificationAsync(database, item, "state_changed", At.AddMinutes(1));

        // Act
        await using var host = await NotificationsTestHost.StartAsync(database);

        // Assert
        var rows = (await host.AllNotificationsAsync()).ToDictionary(n => n.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows[commented].CommentId, Is.EqualTo(comment));
            Assert.That(rows[mentioned].CommentId, Is.EqualTo(comment));
            Assert.That(rows[noMatchingTime].CommentId, Is.Null);
            Assert.That(rows[notAComment].CommentId, Is.Null);
            Assert.That(rows.Values.All(n => n.ExcerptEditedAt is null), Is.True);
            Assert.That(otherKind, Is.Not.EqualTo(comment));
        }
    }

    [Test]
    public async Task Migrate_OldNotificationAlreadyHavingACommentId_IsNotOverwritten()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        await CreateActivityTableAsync(database);
        var item = Guid.NewGuid();
        await InsertActivityAsync(database, item, Actor, "commented", At);
        var existing = Guid.NewGuid();
        var notification = await InsertNotificationAsync(database, item, "commented", At, commentId: existing);

        // Act
        await using var host = await NotificationsTestHost.StartAsync(database);

        // Assert
        var rows = (await host.AllNotificationsAsync()).ToDictionary(n => n.Id);
        Assert.That(rows[notification].CommentId, Is.EqualTo(existing));
    }

    [Test]
    public async Task Migrate_WithoutTheActivityTable_LeavesOldNotificationsAlone()
    {
        // Arrange：只有通知 schema 的資料庫（沒有 work.item_activities）
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeSafeguards);
        var notification = await InsertNotificationAsync(database, Guid.NewGuid(), "commented", At);

        // Act
        await using var host = await NotificationsTestHost.StartAsync(database);

        // Assert
        var rows = (await host.AllNotificationsAsync()).ToDictionary(n => n.Id);
        Assert.That(rows[notification].CommentId, Is.Null);
    }

    [Test]
    public async Task Downgrade_DropsTheNewColumnAndIndexesAndCanBeAppliedAgain()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, null);

        // Act
        await MigrateToAsync(database, BeforeSafeguards);
        var afterDowngrade = await IndexNamesAsync(database);
        await MigrateToAsync(database, null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDowngrade, Does.Not.Contain("ix_notifications_comment"));
            Assert.That(await IndexNamesAsync(database), Does.Contain("ix_notifications_comment"));
        }
    }

    private static async Task MigrateToAsync(string connectionString, string? migration)
    {
        await using var db = NewContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static NotificationsDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        NotificationsDbContext.Configure(options, connectionString);
        return new NotificationsDbContext(options.Options);
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>只有回填用到的欄位：真正的活動表由工作引擎的遷移建立，這個測試資料庫沒有跑它。</summary>
    private static Task CreateActivityTableAsync(string connectionString) =>
        ExecuteAsync(
            connectionString,
            """
            CREATE SCHEMA IF NOT EXISTS work;
            CREATE TABLE work.item_activities (
              id uuid PRIMARY KEY, organization_id uuid NOT NULL, item_id uuid NOT NULL,
              actor_id uuid NOT NULL, kind varchar(20) NOT NULL, at timestamptz NOT NULL);
            """);

    private static async Task<Guid> InsertActivityAsync(string connectionString, Guid item, Guid actor, string kind, DateTimeOffset at)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            connectionString,
            "INSERT INTO work.item_activities (id, organization_id, item_id, actor_id, kind, at) VALUES (@id, @org, @item, @actor, @kind, @at)",
            ("id", id), ("org", Organization), ("item", item), ("actor", actor), ("kind", kind), ("at", at));
        return id;
    }

    private static async Task<Guid> InsertNotificationAsync(
        string connectionString, Guid item, string kind, DateTimeOffset createdAt, Guid? commentId = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO notifications.notifications
              (id, organization_id, recipient_id, kind, project_id, project_key, item_id, item_key, item_title,
               actor_person_id, comment_id, event_id, created_at)
            VALUES (@id, @org, @recipient, @kind, @project, 'WEB', @item, 'WEB-1', '範例', @actor, @comment, @event, @created)
            """,
            ("id", id), ("org", Organization), ("recipient", Recipient), ("kind", kind), ("project", Guid.NewGuid()),
            ("item", item), ("actor", Actor), ("comment", (object?)commentId ?? DBNull.Value), ("event", Guid.NewGuid()), ("created", createdAt));
        return id;
    }

    private static async Task<IReadOnlyList<string>> IndexNamesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT indexname FROM pg_indexes WHERE schemaname = 'notifications'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
