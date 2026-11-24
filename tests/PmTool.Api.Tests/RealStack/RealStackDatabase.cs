using Npgsql;
using Testcontainers.PostgreSql;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 這個命名空間底下的測試共用一個 PostgreSQL 容器：每個測試類別都開一個容器太慢。
/// 隔離靠每個測試類別自己的資料庫。只有這些測試需要資料庫，其他測試用假的模組，不必等容器起來。
/// </summary>
[SetUpFixture]
// 這些測試共用同一個 PostgreSQL：事件流只讀「比還在進行的最舊交易更早」的事件，而那個「最舊的交易」是整個伺服器算的，
// 別的類別在別的資料庫裡的交易也算。同時跑會讓事件晚好幾秒才讀得到、或在下一個測試才冒出來，所以這一組一個一個跑。
[NonParallelizable]
public sealed class RealStackDatabase
{
    private static PostgreSqlContainer? _container;

    public static async Task<string> CreateDatabaseAsync()
    {
        var container = _container ?? throw new InvalidOperationException("PostgreSQL 容器還沒啟動。");
        var name = $"api_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync();

            // 資料庫名稱不能用參數；它是上面用 Guid 組出來的，不含外部輸入。
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
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
}
