using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 頁面在「找到之後、讀內容之前」被永久刪除（資料列真的不見了）時，回應是找不到，不是 500。
/// 用一個資料庫的攔截器在指定的那一個指令之前刪掉頁面，重現兩個步驟之間剛好發生的永久刪除。
/// </summary>
[TestFixture]
public sealed class PagePurgedDuringRequestTests
{
    private PurgeBeforeCommand _purger = null!;
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _purger = new PurgeBeforeCommand();
        _host = await KnowledgeTestHost.StartAsync(services =>
            services.AddSingleton<IDbContextOptionsConfiguration<KnowledgeDbContext>>(new InterceptorConfiguration(_purger)));
        _purger.ConnectionString = _host.ConnectionString;
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetAsync_WhenThePageIsPurgedBeforeItsContentIsRead_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁", body: "內容");
        _purger.Arm(page.Id, isCommand: IsPageContentRead, skip: 0);

        // Act
        var read = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(read.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
        }
    }

    [Test]
    public async Task UpdateAsync_WhenThePageIsPurgedBeforeItIsLocked_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁", body: "內容");
        _purger.Arm(page.Id, isCommand: IsPageLock, skip: 0);

        // Act
        var updated = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges("新標題", null, page.Revision), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task UpdateAsync_WhenThePageIsPurgedRightAfterTheSave_IsNotFoundInsteadOfFailing()
    {
        // Arrange：存檔本身在交易裡完成，提交之後讀回內容時頁面已經不見。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁", body: "內容");
        _purger.Arm(page.Id, isCommand: IsPageContentRead, skip: 1);

        // Act
        var updated = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges("新標題", null, page.Revision), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task RestoreAsync_AfterThePageWasPurged_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁");
        var deleted = await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        await _host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(deleted.Value.DeletionId), CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
            Assert.That(restored.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
        }
    }

    private static bool IsPageContentRead(string sql) => sql.StartsWith("SELECT", StringComparison.Ordinal) && sql.Contains("FROM knowledge.pages", StringComparison.Ordinal) && sql.Contains(".body", StringComparison.Ordinal);

    private static bool IsPageLock(string sql) => sql.Contains("FOR UPDATE", StringComparison.Ordinal);

    private sealed class InterceptorConfiguration(IInterceptor interceptor) : IDbContextOptionsConfiguration<KnowledgeDbContext>
    {
        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) => optionsBuilder.AddInterceptors(interceptor);
    }

    /// <summary>在符合條件的第 (skip + 1) 個指令送出之前，用另一條連線把指定的頁面刪掉，只做一次。</summary>
    private sealed class PurgeBeforeCommand : DbCommandInterceptor
    {
        private readonly object _gate = new();
        private Guid _pageId;
        private Func<string, bool>? _isCommand;
        private int _skip;

        public string ConnectionString { get; set; } = string.Empty;

        public void Arm(Guid pageId, Func<string, bool> isCommand, int skip)
        {
            lock (_gate)
            {
                (_pageId, _isCommand, _skip) = (pageId, isCommand, skip);
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await PurgeIfDueAsync(command.CommandText);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await PurgeIfDueAsync(command.CommandText);
            return result;
        }

        private async Task PurgeIfDueAsync(string sql)
        {
            Guid pageId;
            lock (_gate)
            {
                if (_isCommand is null || !_isCommand(sql))
                {
                    return;
                }

                if (_skip > 0)
                {
                    _skip--;
                    return;
                }

                (pageId, _isCommand) = (_pageId, null);
            }

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var delete = new NpgsqlCommand("DELETE FROM knowledge.pages WHERE id = @id", connection);
            delete.Parameters.AddWithValue("id", pageId);
            await delete.ExecuteNonQueryAsync();
        }
    }
}
