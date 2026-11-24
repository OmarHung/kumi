using PmTool.Abstractions;
using PmTool.Api.Endpoints;
using PmTool.Identity;
using PmTool.Storage;
using PmTool.Work;

namespace PmTool.Api.Composition;

/// <summary>
/// 開始接受請求之前要做完的事。在 app.Run() 之前呼叫：任何一步丟例外，程序就起不來，
/// 部署會停在舊版，而不是帶著還沒遷移好的資料表上線。
/// </summary>
internal static partial class StartupTasks
{
    public const string BootstrapSection = "Bootstrap";

    private static readonly string[] FirstOwnerKeys = ["OrganizationName", "AdminName", "AdminEmail", "AdminPassword"];

    public static async Task RunStartupTasksAsync(this WebApplication app)
    {
        var logger = CreateLogger(app.Services);

        // 附件的儲存空間不管要不要遷移都先確認：設定缺了或資料夾寫不進去，就在這裡讓程序起不來，不等到第一次上傳。
        await app.Services.VerifyAttachmentStorageAsync(app.Lifetime.ApplicationStopping);
        LogAttachmentStorageReady(logger, app.Configuration[FileStoreSettings.ProviderSetting] ?? string.Empty);
        if (app.Services.GetRequiredService<DatabaseStartup>().MigrateOnStartup)
        {
            await app.Services.PrepareDatabaseAsync(app.Configuration, logger, app.Lifetime.ApplicationStopping);
            return;
        }

        LogMigrationSkipped(logger, ModuleRegistration.MigrateOnStartupSetting);
        await ReportPendingMigrationsAsync(app.Services, logger, app.Lifetime.ApplicationStopping);
    }

    /// <summary>
    /// 各模組的初始化（遷移資料表、授權給執行的角色），然後建立第一個擁有者。
    /// 啟動時（<see cref="DatabaseStartup.MigrateOnStartup"/>）或 migrate 命令執行，兩邊做的事一模一樣。
    /// </summary>
    public static async Task PrepareDatabaseAsync(
        this IServiceProvider rootServices, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        // 模組的初始化多半用到 DbContext 這類 scoped 服務，自己開一個 scope，做完就釋放。
        await using var scope = rootServices.CreateAsyncScope();
        var services = scope.ServiceProvider;
        foreach (var initializer in services.GetServices<IModuleInitializer>())
        {
            await initializer.InitializeAsync(cancellationToken);
        }

        await EnsureFirstOwnerAsync(services, configuration.GetSection(BootstrapSection), logger, cancellationToken);
    }

    public static ILogger CreateLogger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StartupTasks).FullName!);

    /// <summary>
    /// 啟動時不遷移的話，還沒跑 migrate 的版本不該接流量：就緒檢查會回 503。這裡在啟動時記一筆錯誤說清楚原因，
    /// 不然部署的人只看得到就緒檢查一直失敗。查不到（例如資料庫還連不上）不讓啟動失敗，交給就緒檢查；
    /// 最多等就緒檢查的期限，連線一直等不到回應也不會卡住啟動。
    /// </summary>
    private static async Task ReportPendingMigrationsAsync(IServiceProvider rootServices, ILogger logger, CancellationToken stopping)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        deadline.CancelAfter(rootServices.GetRequiredService<ReadinessLimits>().Timeout);
        try
        {
            await using var scope = rootServices.CreateAsyncScope();
            var pending = new List<string>();
            foreach (var status in scope.ServiceProvider.GetServices<IMigrationStatus>())
            {
                pending.AddRange(await status.GetPendingMigrationsAsync(deadline.Token).WaitAsync(deadline.Token));
            }

            if (pending.Count > 0)
            {
                LogPendingMigrations(logger, pending.Count, string.Join(", ", pending), MigrateCommand.Usage);
            }
        }
        catch (Exception exception) when (!stopping.IsCancellationRequested)
        {
            LogMigrationStatusUnknown(logger, exception);
        }
    }

    private static async Task EnsureFirstOwnerAsync(
        IServiceProvider services, IConfigurationSection settings, ILogger logger, CancellationToken cancellationToken)
    {
        var values = FirstOwnerKeys.Select(key => settings[key]).ToArray();
        var missing = FirstOwnerKeys.Where((_, index) => string.IsNullOrWhiteSpace(values[index])).ToArray();
        if (missing.Length == FirstOwnerKeys.Length)
        {
            LogFirstOwnerNotConfigured(logger);
            return;
        }

        if (missing.Length > 0)
        {
            // 只記缺了哪些鍵，不記值：其中一個是密碼。
            LogFirstOwnerIncomplete(logger, string.Join(", ", missing.Select(key => $"{BootstrapSection}:{key}")));
            return;
        }

        var bootstrapper = services.GetService<IIdentityBootstrapper>()
            ?? throw new InvalidOperationException("設定了第一個擁有者，但身分模組沒有接上，沒有人能建立帳號。");
        var owner = new FirstOwner(values[0]!, values[1]!, values[2]!, values[3]!);
        var created = await bootstrapper.EnsureFirstOwnerAsync(owner, cancellationToken);
        if (created)
        {
            LogFirstOwnerCreated(logger, owner.OrganizationName);
        }
        else
        {
            LogFirstOwnerAlreadyExists(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "附件的儲存空間可以用（{Provider}）。")]
    private static partial void LogAttachmentStorageReady(ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "沒有設定第一個擁有者（Bootstrap:*），略過建立。")]
    private static partial void LogFirstOwnerNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "第一個擁有者的設定不完整，缺少 {MissingKeys}，略過建立。")]
    private static partial void LogFirstOwnerIncomplete(ILogger logger, string missingKeys);

    [LoggerMessage(Level = LogLevel.Information, Message = "已建立第一個組織「{OrganizationName}」與它的擁有者。")]
    private static partial void LogFirstOwnerCreated(ILogger logger, string organizationName);

    [LoggerMessage(Level = LogLevel.Information, Message = "資料庫裡已經有帳號，不再建立第一個擁有者。")]
    private static partial void LogFirstOwnerAlreadyExists(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Setting} 是 false：啟動時不遷移資料表、不建立第一個擁有者，由 migrate 命令負責。")]
    private static partial void LogMigrationSkipped(ILogger logger, string setting);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "資料庫還有 {Count} 個沒套用的遷移（{Migrations}）。先跑 `{Command}`；在那之前就緒檢查會回 503，這個容器不接流量。")]
    private static partial void LogPendingMigrations(ILogger logger, int count, string migrations, string command);

    [LoggerMessage(Level = LogLevel.Warning, Message = "啟動時查不到資料庫的遷移狀態，交給就緒檢查。")]
    private static partial void LogMigrationStatusUnknown(ILogger logger, Exception exception);
}
