using Npgsql;

namespace PmTool.Identity.Tests;

/// <summary>
/// 在另一條連線上先做「改密碼」會做的事（改帳號那一列、作廢工作階段）並且不提交，
/// 測試就能讓另一個請求卡在帳號的鎖後面，等到確定卡住了才提交，順序不靠時間猜。
/// </summary>
internal sealed class HeldPasswordChange : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly string _connectionString;

    private HeldPasswordChange(NpgsqlConnection connection, NpgsqlTransaction transaction, string connectionString)
    {
        _connection = connection;
        _transaction = transaction;
        _connectionString = connectionString;
    }

    public static async Task<HeldPasswordChange> BeginAsync(string connectionString, Guid accountId, Guid sessionId)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "UPDATE identity.accounts SET password_hash = password_hash WHERE id = @id", ("id", accountId));
        await ExecuteAsync(
            connection, transaction,
            "UPDATE identity.sessions SET revoked_at = now(), revoked_reason = 'password_changed' WHERE id = @id", ("id", sessionId));
        return new HeldPasswordChange(connection, transaction, connectionString);
    }

    /// <summary>在還沒提交的交易裡再執行一句；測試要在請求卡住之後才做的事用它。</summary>
    public Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters) => ExecuteAsync(_connection, _transaction, sql, parameters);

    /// <summary>等到有請求卡在鎖後面（資料庫看得到的等待），最多等十秒。</summary>
    public async Task WaitUntilSomeoneIsBlockedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'", connection);
            if ((long)(await command.ExecuteScalarAsync() ?? 0L) > 0)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("沒有請求卡在帳號的鎖後面。");
    }

    public Task CommitAsync() => _transaction.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }
}
