using System.Diagnostics;
using Npgsql;

namespace PmTool.Work.Tests;

/// <summary>
/// 測試自己的另一條資料庫連線，在自己的交易裡鎖住項目或專案那一列，模擬同時進來、還沒提交的另一個寫入。
/// 只用來做出等鎖與互鎖的情況，不拿來讀資料驗證結果。
/// 它的 deadlock_timeout 設得很長：和被測的請求互鎖時，一定是先開始等的被測請求先發現互鎖、被 PostgreSQL 退回。
/// </summary>
internal sealed class RowLockHolder : IAsyncDisposable
{
    public const string ForUpdate = "UPDATE";
    public const string ForNoKeyUpdate = "NO KEY UPDATE";
    public const string ForShare = "SHARE";

    // 被測的請求幾毫秒就會開始等鎖；等這麼久還沒看到，表示測試的前提沒有成立。
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    private RowLockHolder(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public static async Task<RowLockHolder> BeginAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var holder = new RowLockHolder(connection, await connection.BeginTransactionAsync());
        await holder.ExecuteAsync("SET LOCAL deadlock_timeout = '1min'");
        return holder;
    }

    /// <param name="mode">鎖的強度，用這個類別的常數；它直接寫進 SQL，不接受別的字串。</param>
    public Task LockItemsAsync(Guid projectId, string mode, params long[] numbers)
    {
        Assert.That(mode, Is.AnyOf(ForUpdate, ForNoKeyUpdate, ForShare));
        return ExecuteAsync(
            $"SELECT 1 FROM work.items WHERE project_id = @project AND number = ANY(@numbers) ORDER BY number FOR {mode}",
            new NpgsqlParameter("project", projectId),
            new NpgsqlParameter("numbers", numbers));
    }

    /// <summary>專案那一列的排他鎖：和每個項目寫入一開始對專案取的共用鎖衝突。</summary>
    public Task LockProjectAsync(Guid projectId) =>
        ExecuteAsync("SELECT 1 FROM work.projects WHERE id = @project FOR UPDATE", new NpgsqlParameter("project", projectId));

    /// <summary>專案那一列的共用鎖（FOR KEY SHARE）：進行中的項目寫入拿的那一種，和封存的排他鎖衝突。</summary>
    public Task LockProjectSharedAsync(Guid projectId) =>
        ExecuteAsync("SELECT 1 FROM work.projects WHERE id = @project FOR KEY SHARE", new NpgsqlParameter("project", projectId));

    /// <summary>週期那一列的共用鎖（FOR KEY SHARE）：項目指到週期時取的那一種，和刪除週期衝突。</summary>
    public Task LockCycleAsync(Guid cycleId) =>
        ExecuteAsync("SELECT 1 FROM work.cycles WHERE id = @id FOR KEY SHARE", new NpgsqlParameter("id", cycleId));

    /// <summary>里程碑那一列的共用鎖，用途同 <see cref="LockCycleAsync"/>。</summary>
    public Task LockMilestoneAsync(Guid milestoneId) =>
        ExecuteAsync("SELECT 1 FROM work.milestones WHERE id = @id FOR KEY SHARE", new NpgsqlParameter("id", milestoneId));

    /// <summary>儲存的檢視那一列的排他鎖：換掉的請求讀得到它，但更新要等。</summary>
    public Task LockSavedViewAsync(Guid viewId) =>
        ExecuteAsync("SELECT 1 FROM work.saved_views WHERE id = @id FOR UPDATE", new NpgsqlParameter("id", viewId));

    /// <summary>在自己的交易裡刪掉那一列，提交前別人看不到。</summary>
    public Task DeleteSavedViewAsync(Guid viewId) =>
        ExecuteAsync("DELETE FROM work.saved_views WHERE id = @id", new NpgsqlParameter("id", viewId));

    public Task CommitAsync() => _transaction.CommitAsync();

    /// <summary>等到這個資料庫裡有 <paramref name="count"/> 個連線在等鎖。</summary>
    public static async Task WaitForLockWaitersAsync(string connectionString, int count)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var waited = Stopwatch.StartNew();
        while (await LockWaitersAsync(connection) < count)
        {
            if (waited.Elapsed > Patience)
            {
                Assert.Fail($"等了 {Patience.TotalSeconds} 秒，還沒有 {count} 個連線在等鎖。");
            }

            await Task.Delay(PollInterval);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task<long> LockWaitersAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private async Task ExecuteAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, _connection, _transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
