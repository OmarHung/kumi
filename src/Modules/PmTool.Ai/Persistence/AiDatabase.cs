using Microsoft.EntityFrameworkCore;
using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Ai.Persistence;

/// <summary>
/// AI 模組的兩個連線。執行時的 DbContext 由工廠用 <see cref="DatabaseConnections.Runtime"/> 建立；
/// 遷移另外用 <see cref="DatabaseConnections.MigrationsConnection"/> 開一個，執行的角色才不需要改結構的權限。
/// 包成模組自己的型別註冊：別的模組也收同一個合約，直接註冊 <see cref="DatabaseConnections"/> 會互相蓋掉。
/// </summary>
internal sealed record AiDatabase(DatabaseConnections Connections)
{
    public AiDbContext CreateMigrationsContext()
    {
        var options = new DbContextOptionsBuilder<AiDbContext>();
        AiDbContext.Configure(options, UnpooledMigrationsConnection);
        return new AiDbContext(options.Options);
    }

    /// <summary>遷移的連線不放進連線池：用完就關，程序裡不留一條擁有者的閒置連線。</summary>
    private string UnpooledMigrationsConnection =>
        new NpgsqlConnectionStringBuilder(Connections.MigrationsConnection) { Pooling = false }.ConnectionString;
}
