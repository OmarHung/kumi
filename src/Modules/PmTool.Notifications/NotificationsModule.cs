using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Notifications.Email;
using PmTool.Notifications.Events;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications;

/// <summary>
/// 通知模組的選項。
/// </summary>
/// <param name="AllowPrivateEmailHosts">允許寄信的主機在內網或本機的位址（部署用內網的轉信主機時才設）。預設不允許。</param>
public sealed record NotificationsModuleOptions(bool AllowPrivateEmailHosts = false);

public static class NotificationsModule
{
    /// <summary>執行與遷移用同一個連線、不另外授權。本機開發與測試用。</summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString) =>
        services.AddNotificationsModule(new DatabaseConnections(connectionString));

    /// <summary>
    /// 註冊通知模組。它依賴工作引擎的 <see cref="IWorkAccessQueries"/>（誰看得到哪個專案）、身分模組的 <c>IPeopleDirectory</c>（收件人的 Email）與 <see cref="IOrganizationClock"/>（信裡的時間）、以及 Data Protection（加密密碼），都由組合根註冊。
    /// 服務是 scoped：存取查詢的實作可能是 scoped；資料庫連線一律經過工廠，初始化與就緒檢查不依賴請求範圍。
    /// </summary>
    /// <exception cref="ArgumentException">連線字串是空的，或執行的角色名稱不合 <see cref="RuntimeRoleGrants.RoleNamePattern"/>。</exception>
    public static IServiceCollection AddNotificationsModule(
        this IServiceCollection services, DatabaseConnections connections, NotificationsModuleOptions? options = null)
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
        services.TryAddSingleton(new NotificationsDatabase(connections));
        services.AddDbContextFactory<NotificationsDbContext>(options => NotificationsDbContext.Configure(options, connections.Runtime));
        services.TryAddSingleton(ObserverLimits.Default);
        services.TryAddSingleton(EmailDispatchLimits.Default);
        services.TryAddSingleton(options ?? new NotificationsModuleOptions());
        services.TryAddScoped<INotificationInbox, NotificationInbox>();
        services.TryAddScoped<IItemSubscriptionService, ItemSubscriptionService>();
        services.TryAddScoped<NotificationVisibility>();

        // 帳號事件的接收者：身分模組發事件時一個一個叫，所以也用加入清單的方式註冊。
        services.TryAddEnumerable(ServiceDescriptor.Singleton<PmTool.Identity.IAccountEventSink, AccountNotices.AccountNoticeSink>());

        // 寄信的傳送放在內部介面後面，TryAdd：測試先放假的，就不會真的連出去。
        services.TryAddSingleton<IEmailTransport>(provider =>
            new MailKitEmailTransport(allowPrivateHosts: provider.GetRequiredService<NotificationsModuleOptions>().AllowPrivateEmailHosts));
        services.TryAddSingleton<EmailPasswordProtector>();
        services.TryAddScoped<IEmailSettingsService, EmailSettingsService>();
        services.TryAddScoped<IEmailPreferenceService, EmailPreferenceService>();
        services.TryAddScoped<IEmailDispatcher, EmailDispatcher>();

        // 事件的讀者要用加入清單的方式：之後的 webhook、自動化也是讀者，不能蓋掉別人的。
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkEventConsumer, NotificationEventConsumer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleInitializer, NotificationsMigrator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, NotificationsReadinessProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationStatus, NotificationsReadinessProbe>());
        return services;
    }
}
