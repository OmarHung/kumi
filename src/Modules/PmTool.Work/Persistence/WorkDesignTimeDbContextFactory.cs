using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PmTool.Work.Persistence;

/// <summary>
/// 給 dotnet ef 產生遷移用。產生遷移不會連線，所以連線字串是假的，也刻意不放密碼。
/// </summary>
internal sealed class WorkDesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkDbContext>
{
    private const string DesignTimeConnectionString = "Host=localhost;Database=pm_tool_design_time";

    public WorkDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WorkDbContext>();
        WorkDbContext.Configure(options, DesignTimeConnectionString);
        return new WorkDbContext(options.Options);
    }
}
