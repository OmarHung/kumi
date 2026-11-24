using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>
/// 遷移 ItemArchiveAndTypeChange 降版：舊的資料表記不住刪除（封存）的項目，也記不住改類型封存的值。
/// 有這些資料時降版要整個擋下、什麼都不改，不能默默刪掉值或讓刪除的項目重新出現；沒有時照常降版，也能再升回來。
/// 每個測試降版會改到資料表，所以各自開一個資料庫。
/// </summary>
[TestFixture]
public sealed class MigrationDowngradeTests
{
    private const string BeforeArchive = "20261115153621_ChangeSets";
    private const string BeforeProjectArchive = "20261118101414_AddCyclesAndMilestones";
    private const string BeforeAttachments = "20261118160517_AddProjectArchive";
    private const string Archive = "20261116025056_ItemArchiveAndTypeChange";

    [Test]
    public async Task Downgrade_WithADeletedItem_IsRefusedAndTheItemStaysDeleted()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "要刪掉的");
        await host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Act
        var refused = await DowngradeAsync(host);
        var latestAfterRefusal = await LatestMigrationAsync(host);

        // 比它新的遷移已經先降下去了（週期與里程碑拿掉了項目的兩個欄位）；升回來才讀得了項目。
        await using (var db = await ContextAsync(host))
        {
            await db.Database.MigrateAsync();
        }

        var stored = await DeletedItems.ReadAsync(host, scenario, item.Key);
        var readBack = await host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(refused?.MessageText, Does.Contain("刪除"), () => $"應該擋下降版：{refused}");
        Assert.That(latestAfterRefusal, Is.EqualTo(Archive));
        Assert.That(stored.DeletedAt, Is.Not.Null);
        Assert.That(readBack.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound), "刪除的項目沒有重新出現");
    }

    [Test]
    public async Task Downgrade_WithAValueArchivedByATypeChange_IsRefusedAndKeepsTheValue()
    {
        // Arrange：需求變更單改成任務，封存了報價金額。
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "change_request",
            Title = "加上社群登入",
            Fields = Scenario.Fields(("quote_amount", 1200)),
        });
        await host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "task", CancellationToken.None);

        // Act
        var refused = await DowngradeAsync(host);
        var archived = await ArchivedValues.ByActivityAsync(host, scenario);

        // Assert
        Assert.That(refused?.MessageText, Does.Contain("改類型"), () => $"應該擋下降版：{refused}");
        Assert.That(await LatestMigrationAsync(host), Is.EqualTo(Archive));
        Assert.That(archived.Select(a => (a.Field, a.Value)), Is.EqualTo(new[] { ("quote_amount", "1200") }));
    }

    [Test]
    public async Task Downgrade_WithNothingTheOldSchemaCannotHold_SucceedsAndCanBeUpgradedAgain()
    {
        // Arrange：只有沒刪除的項目。
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "留著的");

        // Act
        var refused = await DowngradeAsync(host);
        var downgraded = await LatestMigrationAsync(host);
        await using (var db = await ContextAsync(host))
        {
            await db.Database.MigrateAsync();
        }

        var readBack = await host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(refused, Is.Null);
        Assert.That(downgraded, Is.EqualTo(BeforeArchive));
        Assert.That(readBack.Value.Title, Is.EqualTo("留著的"));
    }

    [Test]
    public async Task Downgrade_WithAnArchivedProject_IsRefusedAndTheProjectStaysArchived()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        await host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var refused = await DowngradeToAsync(host, BeforeProjectArchive);
        var latest = await LatestMigrationAsync(host);
        var project = (await host.Projects.ListAsync(scenario.Admin, CancellationToken.None)).Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused?.MessageText, Does.Contain("封存的專案"), () => $"應該擋下降版：{refused}");
            Assert.That(latest, Is.Not.EqualTo(BeforeProjectArchive));
            Assert.That(project.ArchivedAt, Is.Not.Null);
        }
    }

    [Test]
    public async Task Downgrade_WithNoArchivedProject_DowngradesAndCanComeBack()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        await host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        await host.Projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var refused = await DowngradeToAsync(host, BeforeProjectArchive);
        var latest = await LatestMigrationAsync(host);
        await using (var db = await ContextAsync(host))
        {
            await db.Database.MigrateAsync();
        }

        var archived = await host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused, Is.Null);
            Assert.That(latest, Is.EqualTo(BeforeProjectArchive));
            Assert.That(archived.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task Downgrade_WithAnAttachment_IsRefusedAndTheAttachmentStays()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await host.Attachments.AddAsync(scenario.Admin, item.Key, AttachmentSupport.Upload("a.txt", "x"), CancellationToken.None);

        // Act
        var refused = await DowngradeToAsync(host, BeforeAttachments);
        var latest = await LatestMigrationAsync(host);
        var listed = await host.Attachments.ListAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused?.MessageText, Does.Contain("附件"), () => $"應該擋下降版：{refused}");
            Assert.That(latest, Is.Not.EqualTo(BeforeAttachments));
            Assert.That(listed.Value, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Downgrade_WithNoAttachments_DowngradesAndCanComeBack()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var added = await host.Attachments.AddAsync(scenario.Admin, item.Key, AttachmentSupport.Upload("a.txt", "x"), CancellationToken.None);
        await host.Attachments.DeleteAsync(scenario.Admin, added.Value.Id, CancellationToken.None);

        // Act
        var refused = await DowngradeToAsync(host, BeforeAttachments);
        var latest = await LatestMigrationAsync(host);
        await using (var db = await ContextAsync(host))
        {
            await db.Database.MigrateAsync();
        }

        var addedAgain = await host.Attachments.AddAsync(scenario.Admin, item.Key, AttachmentSupport.Upload("b.txt", "y"), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused, Is.Null);
            Assert.That(latest, Is.EqualTo(BeforeAttachments));
            Assert.That(addedAgain.Succeeded, Is.True);
        }
    }

    /// <returns>降版被資料庫擋下時的錯誤；成功降版時是 null。</returns>
    private static Task<PostgresException?> DowngradeAsync(WorkTestHost host) => DowngradeToAsync(host, BeforeArchive);

    private static async Task<PostgresException?> DowngradeToAsync(WorkTestHost host, string target)
    {
        await using var db = await ContextAsync(host);
        try
        {
            await db.Database.MigrateAsync(target);
            return null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.RaiseException)
        {
            return exception;
        }
    }

    private static async Task<string> LatestMigrationAsync(WorkTestHost host)
    {
        await using var db = await ContextAsync(host);
        return (await db.Database.GetAppliedMigrationsAsync()).Order(StringComparer.Ordinal).Last();
    }

    private static Task<WorkDbContext> ContextAsync(WorkTestHost host) =>
        host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
}
