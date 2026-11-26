using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Attachments;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Items;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Login;
using PmTool.Ai.Persistence;
using PmTool.Ai.ProjectProposals;
using PmTool.Ai.Providers;
using PmTool.Ai.Settings;
using PmTool.Storage;

namespace PmTool.Ai;

public static class AiModule
{
    /// <summary>執行與遷移用同一個連線、不另外授權。本機開發與測試用。</summary>
    public static IServiceCollection AddAiModule(
        this IServiceCollection services, string connectionString, AiModuleOptions? options = null, FileStoreSettings? attachments = null) =>
        services.AddAiModule(new DatabaseConnections(connectionString), options ?? new AiModuleOptions(), attachments);

    /// <summary>
    /// 註冊 AI 模組。它依賴工作引擎的 <c>IProjectService</c>、<c>IBlueprintChangeService</c>、<c>IBlueprintUsageService</c>、
    /// <c>ITemplateLibrary</c>（助理的工具）、<c>IPeopleDirectory</c>（主機代理確認擁有者與成員的現在角色）、<see cref="IOrganizationClock"/>（這個月的用量）與 Data Protection（加密金鑰），都由組合根註冊。
    /// </summary>
    /// <exception cref="ArgumentException">連線字串是空的，或執行的角色名稱不合 <see cref="RuntimeRoleGrants.RoleNamePattern"/>。</exception>
    public static IServiceCollection AddAiModule(
        this IServiceCollection services, DatabaseConnections connections, AiModuleOptions options, FileStoreSettings? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connections.Runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(connections.MigrationsConnection);

        // 角色名稱等到遷移完授權時才會用到；在註冊時就擋，設定錯了啟動直接失敗，不會遷移到一半才發現。
        if (connections.RuntimeRole is { } role && !RuntimeRoleGrants.IsValidRoleName(role))
        {
            throw new ArgumentException($"執行的角色名稱不合規定（{RuntimeRoleGrants.RoleNamePattern}）。", nameof(connections));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(options);
        services.TryAddSingleton(new AiDatabase(connections));
        services.AddDbContextFactory<AiDbContext>(builder => AiDbContext.Configure(builder, connections.Runtime));

        // 工作包 A1（供應商、設定）與 A2（助理）在這裡各自註冊自己的服務，見 internal/HANDOFF-ai-assistant.md 第 8 節。

        // 工作包 A2（助理）。回覆的執行器是單例：它自己開 DI 範圍，回覆比請求活得久。
        services.TryAddSingleton(AssistantTimings.Default);
        services.TryAddSingleton<AssistantReplyRunner>();
        services.TryAddScoped<ConnectionUsageLedger>();
        services.TryAddScoped<IAssistantService, AssistantService>();

        // 助理的附件：檔案放在共用的儲存空間（和項目、知識庫的附件同一份設定與容量上限；組合根註冊過的設定留下來，沒有才用預設）。
        services.TryAddSingleton(attachments ?? new FileStoreSettings(null));
        services.TryAddSingleton<IFileStore>(provider => FileStores.Create(provider.GetRequiredService<FileStoreSettings>()));
        services.TryAddSingleton(StorageQuotaSettings.Default);
        services.TryAddSingleton<IStorageQuota, StorageQuota>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStorageUsageSource, AssistantAttachmentUsage>());
        services.TryAddSingleton<AttachmentReplay>();
        services.TryAddSingleton<AttachmentTextLimits>();
        services.TryAddSingleton<AgentAttachmentReader>();
        services.TryAddScoped<IAssistantAttachmentService, AssistantAttachmentService>();

        // 工作包 A1（供應商、設定）。gateway 與 protector 用 TryAdd：測試先放假的，就不會真的連出去。
        services.TryAddSingleton<IAiProviderGateway>(_ => new AiProviderGateway(options));
        services.TryAddSingleton<AiApiKeyProtector>();
        services.TryAddScoped<AiConnectionMapper>();
        services.TryAddScoped<AiSelectionResolver>();
        services.TryAddScoped<IAiSettingsService, AiSettingsService>();
        services.TryAddScoped<IAiOptionsService, AiOptionsService>();

        // 工作包 D1：自己的 AI 帳號與用 OpenRouter 登入。連線的實作組織與個人共用 AiConnectionStore。
        services.TryAddScoped<AiConnectionStore>();
        services.TryAddScoped<IPersonalAiConnectionService, PersonalAiConnectionService>();
        services.TryAddSingleton<IOpenRouterAuthClient>(_ => new OpenRouterAuthClient(options));
        services.TryAddScoped<IOpenRouterLoginService, OpenRouterLoginService>();

        // 工作包 E1：本機代理（網頁的 AI 助理用使用者電腦上的 Claude Code／Codex）。MCP 網址的提供者由組合根換成依請求組的。
        services.TryAddSingleton(LocalAgentTimings.Default);
        services.TryAddSingleton<LocalAgentSignals>();
        services.TryAddSingleton<LocalAgentRelay>();
        services.TryAddScoped<IMcpUrlProvider, ConfiguredMcpUrlProvider>();
        services.TryAddSingleton<IRequestSource>(NullRequestSource.Instance);
        services.TryAddScoped<LocalAgentMachineNotice>();
        services.TryAddScoped<ILocalAgentService, LocalAgentService>();
        services.TryAddScoped<ILocalAgentDirectory, LocalAgentDirectory>();

        // 主機代理：組織擁有者在 Kumi 主機上的 ai-worker 登入 Claude Code／Codex，開成組織來源，成員在網頁的助理共用。
        services.TryAddSingleton(HostTimings.Default);
        services.TryAddSingleton<HostAgentSignals>();
        services.TryAddSingleton<HostAgentRelay>();
        services.TryAddScoped<HostPeople>();
        services.TryAddScoped<HostUsageReader>();
        services.TryAddScoped<HostSourceOptions>();
        services.TryAddScoped<HostSourceResolver>();
        services.TryAddScoped<HostReservations>();
        services.TryAddScoped<IHostAgentAdminService, HostAgentAdminService>();
        services.TryAddScoped<IHostAgentWorkerService, HostAgentWorkerService>();
        services.TryAddScoped<IHostToolGrants, HostToolGrants>();
        services.TryAddScoped<IAgentToolBudget, AgentToolBudget>();

        // 修改建議：助理（內建與本機代理）只建立建議，使用者按套用時才以自己的身分寫入。
        services.TryAddScoped<IAiMaintenance, Maintenance.AiMaintenance>();
        services.TryAddScoped<IItemProposalCreator, ItemProposalCreator>();
        services.TryAddScoped<ItemProposalApplier>();
        services.TryAddScoped<IItemProposalService, ItemProposalService>();

        // 新增項目的建議：同樣只建立建議，使用者按「全部建立」才以自己的身分依序建立。
        services.TryAddScoped<INewItemProposalCreator, NewItems.NewItemProposalCreator>();
        services.TryAddScoped<NewItems.NewItemApplier>();
        services.TryAddScoped<INewItemProposalService, NewItems.NewItemProposalService>();

        // 新專案建議（不選專案的組織對話）：同樣只建立建議，使用者按「建立專案」才以自己的身分建立。
        services.TryAddScoped<IProjectProposalCreator, ProjectProposalCreator>();
        services.TryAddScoped<IProjectProposalService, ProjectProposalService>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleInitializer, AiMigrator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, AiReadinessProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationStatus, AiReadinessProbe>());
        return services;
    }
}

/// <summary>部署層級的設定（<c>Ai:*</c>），組合根讀了交進來。</summary>
/// <param name="PublicUrl">
/// 這個部署對外的網址（<c>https://主機</c>），送給 OpenRouter 的 <c>HTTP-Referer</c>，讓它認得是哪個應用。沒設就不送。
/// </param>
/// <param name="AllowPrivateEndpoints">
/// 相容端點能不能連到私有網段、本機與 link-local 的位址（<c>Ai:AllowPrivateEndpoints</c>）。預設不行：
/// 伺服器會照管理員填的網址去連，任意位址等於讓人拿它掃內網（雲端主機的中繼資料服務也在 link-local）。
/// 自架的 Ollama 在內網時才打開。
/// </param>
public sealed record AiModuleOptions(bool AllowPrivateEndpoints = false, string? PublicUrl = null);
