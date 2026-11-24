using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Work.Persistence;

namespace PmTool.Work.Tests;

/// <summary>
/// 遷移 AddMyWorkSavedViews 降版：舊的資料表每個檢視都要有專案，「我的工作」的檢視降版後沒有地方放。
/// 有的時候要整個擋下、什麼都不改；沒有時照常降版，專案的檢視不受影響，也能再升回來。每個測試各開一個資料庫。
/// </summary>
[TestFixture]
public sealed class MyWorkViewDowngradeTests
{
    private const string BeforeMyWorkViews = "20261119042144_AddCommentReplies";

    [Test]
    public async Task Downgrade_WithAMyWorkView_IsRefusedAndTheViewStays()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        var created = await host.SavedViews.CreateMyWorkAsync(
            scenario.Admin, new SavedViewDraft("我的", SavedViewLayout.Table, new SavedViewQuery(null, null, null, null, null, null, null)), CancellationToken.None);

        // Act
        var refused = await DowngradeAsync(host);
        var listed = await host.SavedViews.ListMyWorkAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
        Assert.That(refused?.MessageText, Does.Contain("我的工作"), () => $"應該擋下降版：{refused}");
        Assert.That(listed, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Downgrade_WithOnlyProjectViews_DowngradesKeepsThemAndCanComeBack()
    {
        // Arrange
        await using var host = await WorkTestHost.StartAsync();
        var scenario = new Scenario(host);
        await scenario.CreateProjectAsync("WEB");
        var empty = new SavedViewQuery(null, null, null, null, null, null, null);
        await host.SavedViews.CreateAsync(scenario.Admin, "WEB", new SavedViewDraft("專案的", SavedViewLayout.Table, empty), CancellationToken.None);

        // Act
        var refused = await DowngradeAsync(host);
        await using (var db = await ContextAsync(host))
        {
            await db.Database.MigrateAsync();
        }

        var afterRoundTrip = await host.SavedViews.ListAsync(scenario.Admin, "WEB", CancellationToken.None);
        var myWork = await host.SavedViews.CreateMyWorkAsync(scenario.Admin, new SavedViewDraft("我的", SavedViewLayout.Table, empty), CancellationToken.None);

        // Assert
        Assert.That(refused, Is.Null);
        Assert.That(afterRoundTrip.Value.Select(v => v.Name), Is.EqualTo(new[] { "專案的" }));
        Assert.That(myWork.Succeeded, Is.True);
    }

    private static async Task<PostgresException?> DowngradeAsync(WorkTestHost host)
    {
        await using var db = await ContextAsync(host);
        try
        {
            await db.Database.MigrateAsync(BeforeMyWorkViews);
            return null;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.RaiseException)
        {
            return exception;
        }
    }

    private static Task<WorkDbContext> ContextAsync(WorkTestHost host) =>
        host.Root.GetRequiredService<IDbContextFactory<WorkDbContext>>().CreateDbContextAsync();
}
