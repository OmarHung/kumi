using Npgsql;
using Testcontainers.PostgreSql;

namespace PmTool.Knowledge.Tests;

/// <summary>
/// 整個測試組件共用一個 PostgreSQL 容器：每次啟動容器要好幾秒。
/// 每個測試類別各開自己的資料庫，彼此的資料不會互相影響。
/// </summary>
[SetUpFixture]
public sealed class PostgresServer
{
    private static PostgreSqlContainer? _container;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        // 每個測試各開一個資料庫，閒置的連線池會累積；預設的 100 條在角色與遷移的測試裡碰過上限（too many clients）。
        _container = new PostgreSqlBuilder("postgres:18").WithCommand("-c", "max_connections=300").Build();
        await _container.StartAsync();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// 開一個只能登入的角色（除了 PUBLIC 的預設權限之外什麼都沒有），模擬正式環境執行用的角色。
    /// 角色屬於整個伺服器，所以名稱每次不同。回傳名稱，以及用它連到 <paramref name="databaseConnectionString"/> 那個資料庫的連線字串。
    /// </summary>
    public static async Task<(string Role, string ConnectionString)> CreateRuntimeRoleAsync(string databaseConnectionString)
    {
        var container = _container ?? throw new InvalidOperationException("PostgreSQL 容器還沒啟動。");
        var role = $"runtime_{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync();

            // CREATE ROLE 的名稱與密碼不能用參數；兩個都是上面用 Guid 組出來的，不含外部輸入。
            await using var command = new NpgsqlCommand($"CREATE ROLE \"{role}\" LOGIN PASSWORD '{password}'", connection);
            await command.ExecuteNonQueryAsync();
        }

        var runtime = new NpgsqlConnectionStringBuilder(databaseConnectionString) { Username = role, Password = password };
        return (role, runtime.ConnectionString);
    }

    /// <summary>開一個新的空資料庫，回傳連到它的連線字串。</summary>
    public static async Task<string> CreateDatabaseAsync()
    {
        var container = _container ?? throw new InvalidOperationException("PostgreSQL 容器還沒啟動。");
        var databaseName = $"knowledge_{Guid.NewGuid():N}";

        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = databaseName }.ConnectionString;
    }
}
