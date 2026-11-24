using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PmTool.Ai.Persistence;

/// <summary>給 <c>dotnet ef migrations add</c> 用。產生遷移不連線，連線字串是假的，也刻意不放密碼。</summary>
internal sealed class AiDesignTimeFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=pm_tool_design_time";

    public AiDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AiDbContext>();
        AiDbContext.Configure(options, PlaceholderConnectionString);
        return new AiDbContext(options.Options);
    }
}
