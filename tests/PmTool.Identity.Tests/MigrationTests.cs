using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity.Tests;

/// <summary>啟動時的遷移：每次部署都會跑，所以從空的資料庫要能跑到最新，已經是最新時再跑也不能出錯。</summary>
[TestFixture]
public sealed class MigrationTests
{
    [Test]
    public async Task InitializeAsync_EmptyDatabase_MigratesToLatestAndCanRunAgain()
    {
        // Arrange：啟動時已經對空的資料庫跑過一次
        await using var host = await IdentityTestHost.StartAsync();
        var initializers = host.Services.GetServices<IModuleInitializer>().ToList();

        // Act
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        // Assert
        await using var db = await host.CreateDbContextAsync();
        Assert.That(initializers, Has.Count.EqualTo(1));
        Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
    }

    [Test]
    public async Task Migrations_MatchTheCurrentModel()
    {
        // Arrange
        await using var host = await IdentityTestHost.StartAsync();
        await using var db = await host.CreateDbContextAsync();

        // Act
        var hasPendingChanges = db.Database.HasPendingModelChanges();

        // Assert：改了模型卻忘了產生遷移，部署後的資料表會和程式對不上
        Assert.That(hasPendingChanges, Is.False);
    }

    private const string BeforeAccountSecurity = "20261125040519_AddCliLoginOrigin";

    [Test]
    public async Task Migrate_ExistingEmails_AreRewrittenToNfkcAndLowerCase()
    {
        // Arrange：舊版存的是 NFC 加小寫，全形與大小寫不同的寫法原樣留在資料裡
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var fullWidth = await AddAccountAsync(database, "ｕｓｅｒ＠ｅｘａｍｐｌｅ．ｃｏｍ");
        var mixedCase = await AddAccountAsync(database, "Admin@Example.com");
        var plain = await AddAccountAsync(database, "plain@example.com");

        // Act
        await using var host = await IdentityTestHost.StartAsync(database, configure: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await EmailOfAsync(database, fullWidth), Is.EqualTo("user@example.com"));
            Assert.That(await EmailOfAsync(database, mixedCase), Is.EqualTo("admin@example.com"));
            Assert.That(await EmailOfAsync(database, plain), Is.EqualTo("plain@example.com"));
        }
    }

    [Test]
    public async Task Migrate_RewrittenEmail_CanSignInWithTheFullWidthSpelling()
    {
        // Arrange：改寫之後登入也要用同一套正規化比對，這條線由別的包接上；這裡只確認資料已經是新的寫法
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var id = await AddAccountAsync(database, "ＵＳＥＲ２＠example.com");

        // Act
        await using var host = await IdentityTestHost.StartAsync(database, configure: null);

        // Assert
        Assert.That(await EmailOfAsync(database, id), Is.EqualTo("user2@example.com"));
    }

    [Test]
    public async Task Migrate_EmailsThatCollideAfterNormalizing_FailsAndChangesNothing()
    {
        // Arrange：改寫之後會變成同一個 Email 的兩個帳號，另外還有一個本來就會被改寫的
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var first = await AddAccountAsync(database, "ｕｓｅｒ＠ｅｘａｍｐｌｅ．ｃｏｍ");
        var second = await AddAccountAsync(database, "USER@example.com");
        var untouched = await AddAccountAsync(database, "Other@Example.com");

        // Act
        var failure = Assert.CatchAsync<InvalidOperationException>(async () => await (await IdentityTestHost.StartAsync(database, configure: null)).DisposeAsync());

        // Assert：訊息要說得出是人工處理的事，而且不帶任何人的 Email；一個都不改
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Message, Does.Contain("人工").And.Not.Contain("example.com"));
            Assert.That(await EmailOfAsync(database, first), Is.EqualTo("ｕｓｅｒ＠ｅｘａｍｐｌｅ．ｃｏｍ"));
            Assert.That(await EmailOfAsync(database, second), Is.EqualTo("USER@example.com"));
            Assert.That(await EmailOfAsync(database, untouched), Is.EqualTo("Other@Example.com"));
        }
    }

    [Test]
    public async Task Migrate_EmailThatCollidesWithAnUnchangedOne_FailsAndChangesNothing()
    {
        // Arrange：改寫後的值和另一個本來就不用改的撞在一起
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var changing = await AddAccountAsync(database, "Same@Example.com");
        var unchanged = await AddAccountAsync(database, "same@example.com");

        // Act
        var failure = Assert.CatchAsync<InvalidOperationException>(async () => await (await IdentityTestHost.StartAsync(database, configure: null)).DisposeAsync());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Message, Does.Contain("人工"));
            Assert.That(await EmailOfAsync(database, changing), Is.EqualTo("Same@Example.com"));
            Assert.That(await EmailOfAsync(database, unchanged), Is.EqualTo("same@example.com"));
        }
    }

    [Test]
    public async Task Migrate_EmailWithADottedCapitalI_CanStillSignInWithTheSpellingTheUserTypes()
    {
        // Arrange：資料庫的 lower 和 .NET 對這個字元的結果不一樣；改寫要用登入時同一套規則，本人才登得進去
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var typed = "\u0130stanbul@example.com";
        await AddAccountAsync(database, typed, new PasswordHasher<PasswordCredential>().HashPassword(PasswordCredential.Instance, TestData.Password));

        // Act
        await using var host = await IdentityTestHost.StartAsync(database, configure: null);
        var user = await host.Authentication.SignInAsync(typed, TestData.Password, CancellationToken.None);

        // Assert
        Assert.That(user, Is.Not.Null);
    }

    [Test]
    public async Task Migrate_EmailThatTheCurrentRulesRejectEverywhere_IsLeftAloneAndStartupContinues()
    {
        // Arrange：帶零寬字元的舊 Email 在新規則下不合格；不改它，也不擋啟動
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var stored = "zero\u200Bwidth@Example.com";
        var rejected = await AddAccountAsync(database, stored);
        var normal = await AddAccountAsync(database, "Normal@Example.com");

        // Act
        await using var host = await IdentityTestHost.StartAsync(database, configure: null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await EmailOfAsync(database, rejected), Is.EqualTo(stored));
            Assert.That(await EmailOfAsync(database, normal), Is.EqualTo("normal@example.com"));
        }
    }

    [Test]
    public async Task Migrate_EmailsAlreadyNormalized_AreNotTouchedAndRunningAgainChangesNothing()
    {
        // Arrange
        var database = await PostgresServer.CreateDatabaseAsync();
        await using var host = await IdentityTestHost.StartAsync(database, configure: null);
        var user = await host.StartWithOwnerAsync();

        // Act：每次啟動都跑
        foreach (var initializer in host.Services.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(CancellationToken.None);
        }

        // Assert
        Assert.That(await EmailOfAsync(database, user.AccountId), Is.EqualTo(user.Email));
    }

    [Test]
    public async Task Migrate_WithASeparateRuntimeRole_RewritesEmailsThroughTheOwnersConnection()
    {
        // Arrange：執行的角色在授權之前什麼權限都沒有，改寫只能用擁有者的連線
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var id = await AddAccountAsync(database, "Mixed@Example.com");
        var (role, runtimeConnection) = await PostgresServer.CreateRuntimeRoleAsync(database);

        // Act
        await using var host = await IdentityTestHost.StartAsync(new DatabaseConnections(runtimeConnection, database, role), configure: null);

        // Assert
        Assert.That(await EmailOfAsync(database, id), Is.EqualTo("mixed@example.com"));
        NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task Migration_AddAccountSecurity_NoLongerRewritesEmails()
    {
        // Arrange：只遷移到這一版（不經過模組的啟動），資料不能被 SQL 改寫
        var database = await PostgresServer.CreateDatabaseAsync();
        await MigrateToAsync(database, BeforeAccountSecurity);
        var id = await AddAccountAsync(database, "Mixed@Example.com");

        // Act
        await MigrateToAsync(database, null);

        // Assert
        Assert.That(await EmailOfAsync(database, id), Is.EqualTo("Mixed@Example.com"));
    }

    private static async Task MigrateToAsync(string connectionString, string? migration)
    {
        await using var db = NewContext(connectionString);
        await db.GetService<IMigrator>().MigrateAsync(migration);
    }

    private static IdentityDbContext NewContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.Configure(options, connectionString);
        return new IdentityDbContext(options.Options);
    }

    /// <summary>在舊的結構上直接新增一個組織、一個人與一個帳號（舊版會存成這樣的 Email），回傳帳號的編號。</summary>
    private static async Task<Guid> AddAccountAsync(string connectionString, string email, string passwordHash = "not-a-real-hash")
    {
        await using var db = NewContext(connectionString);
        var organization = new OrganizationRow { Id = Guid.CreateVersion7(), Name = "範例組織", CreatedAt = DateTimeOffset.UtcNow };
        var person = new PersonRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organization.Id,
            DisplayName = "範例使用者",
            Role = OrganizationRole.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var account = new AccountRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organization.Id,
            PersonId = person.Id,
            Email = email,
            PasswordHash = passwordHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Organizations.Add(organization);
        db.People.Add(person);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private static async Task<string?> EmailOfAsync(string connectionString, Guid accountId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT email FROM identity.accounts WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", accountId);
        return (string?)await command.ExecuteScalarAsync();
    }
}
