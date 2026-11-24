namespace PmTool.Api.Cli;

internal static class CliRegistration
{
    /// <summary>預設是執行檔旁邊的 <c>cli</c> 資料夾（容器裡是 /app/cli）。</summary>
    public static IServiceCollection AddCliDownloads(this IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton(provider =>
        {
            var configured = configuration[CliPackageCatalog.PackagesPathSetting];
            var directory = string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "cli") : configured;
            return new CliPackageCatalog(directory, provider.GetRequiredService<ILogger<CliPackageCatalog>>());
        });
}
