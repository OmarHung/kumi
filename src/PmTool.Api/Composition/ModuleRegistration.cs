using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Ai;
using PmTool.Abstractions;
using PmTool.Api.Security;
using PmTool.Blueprint;
using PmTool.Identity;
using PmTool.Knowledge;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Api.Composition;

/// <summary>啟動時要不要遷移。關掉時由 migrate 命令負責（見 <see cref="MigrateCommand"/>）。</summary>
internal sealed record DatabaseStartup(bool MigrateOnStartup);

internal static class ModuleRegistration
{
    public const string DatabaseConnectionName = "Database";
    public const string MigrationsConnectionName = "Migrations";
    public const string RuntimeRoleSetting = "Database:RuntimeRole";
    public const string MigrateOnStartupSetting = "Database:MigrateOnStartup";

    /// <summary>AI 的相容端點能不能連到私有網段與本機（自架的 Ollama 在內網時才打開）。見 <see cref="AiModuleOptions"/>。</summary>
    public const string AllowPrivateAiEndpointsSetting = "Ai:AllowPrivateEndpoints";

    /// <summary>
    /// 組合根接上各模組的地方。API 其餘的程式只認合約介面，換掉實作只要改這裡。
    /// </summary>
    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // 計畫是純邏輯、沒有狀態，整個程序共用一份。工作引擎開專案時從 DI 拿它。
        services.AddSingleton<IBlueprintPlanner, BlueprintPlanner>();

        var connections = ReadConnections(configuration);
        services.AddSingleton(new DatabaseStartup(
            SettingValues.ReadFlag(configuration, MigrateOnStartupSetting, defaultValue: true)));
        services.AddIdentityModule(connections);
        services.AddWorkModule(connections, AttachmentConfiguration.Read(configuration));
        services.AddNotificationsModule(connections);
        services.AddKnowledgeModule(connections, AttachmentConfiguration.Read(configuration));
        services.AddAiModule(connections, new AiModuleOptions(
            AllowPrivateEndpoints: SettingValues.ReadFlag(configuration, AllowPrivateAiEndpointsSetting, defaultValue: false),
            PublicUrl: TransportSecurity.From(configuration).PublicHost is { } host ? $"https://{host}" : null));

        // 本機的引擎連的 MCP 網址：依請求組（和 OpenRouter 的回呼網址、OAuth 的 issuer 同一個算法）。
        services.AddHttpContextAccessor();

        // 稽核與通知要記請求從哪裡來；身分模組自己只有「沒有請求」的版本。
        services.Replace(ServiceDescriptor.Singleton<IRequestSource, HttpRequestSource>());
        services.Replace(ServiceDescriptor.Scoped<IMcpUrlProvider, RequestMcpUrlProvider>());

        // 背景處理在主機裡才跑：migrate 命令不啟動主機，所以不會跑。測試要自己決定什麼時候處理，用設定關掉。
        if (SettingValues.ReadFlag(configuration, NotificationWorker.EnabledSetting, defaultValue: true))
        {
            services.AddSingleton<NotificationWorker>();
            services.AddHostedService(provider => provider.GetRequiredService<NotificationWorker>());
        }

        // 寄 Email 的背景工作，開關同上。
        if (SettingValues.ReadFlag(configuration, EmailWorker.EnabledSetting, defaultValue: true))
        {
            services.AddSingleton<EmailWorker>();
            services.AddHostedService(provider => provider.GetRequiredService<EmailWorker>());
        }

        // 工作階段 Cookie 的金鑰存在身分模組的資料庫，所有容器共用。名稱與憑證在 SessionAuthentication 設定。
        services.AddDataProtection().PersistKeysToIdentityModule();
        return services;
    }

    /// <summary>
    /// 沒有資料庫就什麼都做不了。在這裡就讓啟動失敗，訊息說得出缺的是哪個設定；
    /// 等到第一個查詢才失敗的話，看到的只會是連線錯誤。
    /// </summary>
    private static DatabaseConnections ReadConnections(IConfiguration configuration)
    {
        var runtime = configuration.GetConnectionString(DatabaseConnectionName);
        if (string.IsNullOrWhiteSpace(runtime))
        {
            throw new InvalidOperationException(
                $"沒有設定資料庫的連線字串（ConnectionStrings:{DatabaseConnectionName}）。");
        }

        var migrations = configuration.GetConnectionString(MigrationsConnectionName);
        var role = SettingValues.ReadText(configuration, RuntimeRoleSetting);
        if (role is not null && !RuntimeRoleGrants.IsValidRoleName(role))
        {
            // 名稱會原樣放進 GRANT 的 SQL，所以只收小寫英數字與底線；模組也會再擋一次。
            throw new InvalidOperationException(
                $"{RuntimeRoleSetting} 不合規定（{RuntimeRoleGrants.RoleNamePattern}：小寫英文字母、數字與底線，最長 63 個字），現在是「{role}」。");
        }

        return new DatabaseConnections(runtime, string.IsNullOrWhiteSpace(migrations) ? null : migrations, role);
    }
}
