using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PmTool.Abstractions;
using PmTool.Identity.AccessTokens;
using PmTool.Identity.Audit;
using PmTool.Identity.CliLogin;
using PmTool.Identity.OAuth;
using PmTool.Identity.Persistence;
using PmTool.Identity.Sessions;

namespace PmTool.Identity;

public static class IdentityModule
{
    /// <summary>執行與遷移用同一個連線、不另外授權。本機開發與測試用。</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString) =>
        services.AddIdentityModule(new DatabaseConnections(connectionString));

    /// <summary>
    /// 註冊身分模組。所有服務都是單例、各自從工廠拿 DbContext：
    /// 初始化與就緒檢查是從根容器解析的，不能依賴 scoped 服務。
    /// </summary>
    /// <exception cref="ArgumentException">連線字串是空的，或執行的角色名稱不合 <see cref="RuntimeRoleGrants.RoleNamePattern"/>。</exception>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, DatabaseConnections connections)
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

        services.AddOptions();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher<PasswordCredential>, PasswordHasher<PasswordCredential>>();

        // 次數只在這裡定：新的雜湊用它，舊的雜湊在登入時比對成功之後換新（見 AuthenticationService）。
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = IdentityRules.PasswordHashIterations);
        services.TryAddSingleton<IRequestSource>(NullRequestSource.Instance);
        services.TryAddSingleton<AuditTrail>();
        services.TryAddSingleton<IAuditLog, AuditLog>();
        services.TryAddSingleton(provider => new AccountEvents(
            provider.GetServices<IAccountEventSink>(),
            provider.GetService<ILogger<AccountEvents>>() ?? NullLogger<AccountEvents>.Instance,
            AccountEvents.DefaultTimeout));
        services.TryAddSingleton(new IdentityDatabase(connections));
        services.AddDbContextFactory<IdentityDbContext>(options => IdentityDbContext.Configure(options, connections.Runtime));

        services.TryAddSingleton(new SessionCacheLimits());
        services.TryAddSingleton<SessionCache>();
        services.TryAddSingleton<AccountAdministration>();
        services.TryAddSingleton<AuthenticationService>();
        services.TryAddSingleton<IAuthenticationService>(provider => provider.GetRequiredService<AuthenticationService>());
        services.TryAddSingleton<ISessionService, SessionService>();
        services.TryAddSingleton<IAccessTokenService, AccessTokenService>();
        services.TryAddSingleton(new OAuthFetchSettings());
        services.TryAddSingleton<ClientMetadataFetcher>();
        services.TryAddSingleton<OAuthClientDirectory>();
        services.TryAddSingleton<OAuthAuthorization>();
        services.TryAddSingleton<OAuthTokenEndpoint>();
        services.TryAddSingleton<IOAuthService, OAuthService>();
        services.TryAddSingleton<CliLoginRedeemer>();
        services.TryAddSingleton<ICliLoginService, CliLoginService>();
        services.TryAddSingleton<IAccountService, AccountService>();
        services.TryAddSingleton<IPeopleDirectory, PeopleDirectory>();
        services.TryAddSingleton<IOrganizationClock>(provider => new OrganizationClock(
            provider.GetRequiredService<IDbContextFactory<IdentityDbContext>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<OrganizationClock>>() ?? NullLogger<OrganizationClock>.Instance));
        services.TryAddSingleton<IIdentityBootstrapper, IdentityBootstrapper>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModuleInitializer, IdentityMigrator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadinessProbe, IdentityReadinessProbe>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMigrationStatus, IdentityReadinessProbe>());

        return services;
    }
}
