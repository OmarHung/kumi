using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Storage;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Attachments;
using PmTool.Work.Blueprints;
using PmTool.Work.ChangeSets;
using PmTool.Work.Events;
using PmTool.Work.History;
using PmTool.Work.Items;
using PmTool.Work.Overview;
using PmTool.Work.Persistence;
using PmTool.Work.Planning;
using PmTool.Work.Projects;
using PmTool.Work.SavedViews;
using PmTool.Work.Templates;

namespace PmTool.Work;

public static class WorkModule
{
    /// <summary>執行與遷移用同一個連線、不另外授權。本機開發與測試用。</summary>
    public static IServiceCollection AddWorkModule(
        this IServiceCollection services, string connectionString, FileStoreSettings? attachments = null) =>
        services.AddWorkModule(new DatabaseConnections(connectionString), attachments);

    /// <summary>
    /// 註冊工作引擎。<paramref name="attachments"/> 是附件的儲存設定；沒給（或設定不能用）時，附件的服務一用就丟例外，
    /// 組合根在啟動時用 <see cref="VerifyAttachmentStorageAsync"/> 把設定的問題擋在開始接請求之前。
    /// IBlueprintValidator 與 IBlueprintPlanner 由組合根註冊（validator 要是 singleton），這裡從 DI 拿。
    /// </summary>
    /// <exception cref="ArgumentException">連線字串是空的，或執行的角色名稱不合 <see cref="RuntimeRoleGrants.RoleNamePattern"/>。</exception>
    public static IServiceCollection AddWorkModule(
        this IServiceCollection services, DatabaseConnections connections, FileStoreSettings? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentException.ThrowIfNullOrWhiteSpace(connections.Runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(connections.MigrationsConnection);

        // 角色名稱等到遷移完授權時才會用到；在註冊時就擋，設定錯了啟動直接失敗，不會遷移到一半才發現。
        if (connections.RuntimeRole is { } role && !RuntimeRoleGrants.IsValidRoleName(role))
        {
            throw new ArgumentException($"執行的角色名稱不合規定（{RuntimeRoleGrants.RoleNamePattern}）。", nameof(connections));
        }

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);

        // 初始化與就緒檢查從根容器解析，不能依賴請求範圍的 DbContext，所以一律經過工廠。
        services.TryAddSingleton(new WorkDatabase(connections));
        services.AddDbContextFactory<WorkDbContext>(options => WorkDbContext.Configure(options, connections.Runtime));
        services.TryAddSingleton<TemplateCatalog>();
        services.TryAddSingleton<BlueprintCache>();
        services.TryAddScoped<IProjectService, ProjectService>();
        services.TryAddScoped<IProjectDescriptionService, ProjectDescriptionService>();
        services.TryAddScoped<IBlueprintUsageService, BlueprintUsageService>();
        services.TryAddSingleton<ITemplateLibrary, TemplateLibrary>();
        services.TryAddScoped<IWorkItemService, WorkItemService>();
        services.TryAddScoped<IBulkItemService, BulkItemService>();
        services.TryAddScoped<IItemImportService, ItemImportService>();
        services.TryAddScoped<IHistoryImportService, HistoryImportService>();
        services.TryAddScoped<IItemActivityService, ItemActivityService>();
        services.TryAddScoped<ISavedViewService, SavedViewService>();
        services.TryAddScoped<IPlanningService, PlanningService>();
        services.TryAddScoped<IProjectOverviewService, ProjectOverviewService>();
        services.TryAddScoped<IAttachmentService, AttachmentService>();

        // 儲存空間的配額：兩個模組各報自己的用量，加總的實作在 Storage。上限由組合根先註冊，沒註冊就用預設值。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageUsageSource, ItemAttachmentUsage>());
        services.TryAddSingleton(StorageQuotaSettings.Default);
        services.TryAddSingleton<IStorageQuota, StorageQuota>();

        // 用工廠建：設定不能用時，migrate 命令（不碰附件）照樣跑得起來，錯誤留到啟動檢查時才丟。
        services.TryAddSingleton(attachments ?? new FileStoreSettings(null));
        services.TryAddSingleton<IFileStore>(provider => FileStores.Create(provider.GetRequiredService<FileStoreSettings>()));

        // 事件流與存取查詢給背景工作用（它們不在請求的範圍裡），只靠單例的資料庫工廠與藍圖快取，所以是單例。
        services.TryAddSingleton<IWorkEventStream, WorkEventStream>();
        services.TryAddSingleton<IWorkAccessQueries, WorkAccessQueries>();
        services.TryAddSingleton(ChangeSetLimits.Default);
        services.TryAddSingleton<ChangeSetReader>();
        services.TryAddScoped<ChangeSetApplier>();
        services.TryAddScoped<IBlueprintChangeService, BlueprintChangeService>();

        // 其他模組也會註冊自己的初始化與就緒檢查，要用加入清單的方式，不能蓋掉別人的。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleInitializer, WorkDatabaseInitializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, WorkReadinessProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationStatus, WorkReadinessProbe>());
        return services;
    }

    /// <summary>
    /// 啟動時確認附件的儲存空間用得了：設定缺了或不認得、local 的資料夾不存在或寫不進去、gcs 的 bucket 連不上，都丟例外，
    /// 訊息說得出缺什麼。服務啟動時呼叫（不管要不要遷移資料表），失敗就起不來，部署停在舊版。
    /// </summary>
    public static Task VerifyAttachmentStorageAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<IFileStore>().VerifyAsync(cancellationToken);
    }
}
