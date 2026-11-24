using Microsoft.EntityFrameworkCore;
using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 身分模組的兩個連線。執行時的 DbContext 由工廠用 <see cref="DatabaseConnections.Runtime"/> 建立；
/// 遷移另外用 <see cref="DatabaseConnections.MigrationsConnection"/> 開一個，執行的角色才不需要改結構的權限。
/// 包成模組自己的型別註冊：別的模組也收同一個合約，直接註冊 <see cref="DatabaseConnections"/> 會互相蓋掉。
/// </summary>
internal sealed record IdentityDatabase(DatabaseConnections Connections)
{
    public IdentityDbContext CreateMigrationsContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.Configure(options, UnpooledMigrationsConnection);
        return new IdentityDbContext(options.Options);
    }

    /// <summary>
    /// 遷移的連線不放進連線池：用完就關。放進池子的話，遷移結束後程序裡一直留著一條擁有者的閒置連線，
    /// 平常的服務根本不需要它，被攻破時卻拿得到。
    /// </summary>
    private string UnpooledMigrationsConnection =>
        new NpgsqlConnectionStringBuilder(Connections.MigrationsConnection) { Pooling = false }.ConnectionString;
}
