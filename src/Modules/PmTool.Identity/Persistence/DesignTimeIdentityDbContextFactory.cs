using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 給 <c>dotnet ef migrations add</c> 用。產生遷移不需要連線，連線字串只是讓提供者建得起來，
/// 所以故意不放密碼，也不讀任何設定檔。
/// </summary>
internal sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=pm_tool;Username=pm_tool";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.Configure(options, PlaceholderConnectionString);
        return new IdentityDbContext(options.Options);
    }
}
