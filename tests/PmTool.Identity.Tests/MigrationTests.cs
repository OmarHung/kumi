using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

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
}
