using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Attachments;
using PmTool.Knowledge.Pages;
using PmTool.Knowledge.Persistence;
using PmTool.Knowledge.Spaces;
using PmTool.Storage;

namespace PmTool.Knowledge;

public static class KnowledgeModule
{
    /// <summary>執行與遷移用同一個連線、不另外授權。本機開發與測試用。</summary>
    public static IServiceCollection AddKnowledgeModule(
        this IServiceCollection services, string connectionString, FileStoreSettings? attachments = null) =>
        services.AddKnowledgeModule(new DatabaseConnections(connectionString), attachments);

    /// <summary>
    /// 註冊知識庫模組。它依賴身分模組的 <c>IPeopleDirectory</c>（組織空間看角色）與工作引擎的 <c>IWorkAccessQueries</c>（專案空間看專案的權限），
    /// 由組合根註冊。服務是 scoped：存取查詢的實作可能是 scoped；資料庫連線一律經過工廠，初始化與就緒檢查不依賴請求範圍。
    /// </summary>
    /// <exception cref="ArgumentException">連線字串是空的，或執行的角色名稱不合 <see cref="RuntimeRoleGrants.RoleNamePattern"/>。</exception>
    public static IServiceCollection AddKnowledgeModule(
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

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new KnowledgeDatabase(connections));
        services.AddDbContextFactory<KnowledgeDbContext>(options => KnowledgeDbContext.Configure(options, connections.Runtime));
        services.TryAddScoped<SpaceAccess>();
        services.TryAddScoped<IKnowledgeSpaceService, KnowledgeSpaceService>();
        services.TryAddScoped<IKnowledgePageService, KnowledgePageService>();
        services.TryAddScoped<IKnowledgeAttachmentService, KnowledgeAttachmentService>();

        // 儲存空間的配額：兩個模組各報自己的用量，加總的實作在 Storage。上限由組合根先註冊，沒註冊就用預設值。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageUsageSource, PageAttachmentUsage>());
        services.TryAddSingleton(StorageQuotaSettings.Default);
        services.TryAddSingleton<IStorageQuota, StorageQuota>();

        // 和工作引擎共用同一個儲存空間：兩邊都用 TryAdd，先註冊的贏，拿到的是同一個實例。
        // 用工廠建：設定不能用時，migrate 命令（不碰附件）照樣跑得起來，錯誤留到啟動檢查時才丟。
        services.TryAddSingleton(attachments ?? new FileStoreSettings(null));
        services.TryAddSingleton<IFileStore>(provider => FileStores.Create(provider.GetRequiredService<FileStoreSettings>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleInitializer, KnowledgeMigrator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, KnowledgeReadinessProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationStatus, KnowledgeReadinessProbe>());
        return services;
    }
}
