using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PmTool.Knowledge.Persistence;

/// <summary>給 <c>dotnet ef migrations add</c> 用。產生遷移不連線，連線字串是假的，也刻意不放密碼。</summary>
internal sealed class KnowledgeDesignTimeFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=pm_tool_design_time";

    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
        KnowledgeDbContext.Configure(options, PlaceholderConnectionString);
        return new KnowledgeDbContext(options.Options);
    }
}
