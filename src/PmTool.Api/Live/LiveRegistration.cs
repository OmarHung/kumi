using Microsoft.Extensions.DependencyInjection.Extensions;
using PmTool.Abstractions;
using PmTool.Api.Composition;
using PmTool.Notifications;

namespace PmTool.Api.Live;

/// <summary>即時更新的接線：SignalR 的 hub、讀事件流的背景工作、定期重新檢查連線，以及通知模組的觀察者。</summary>
public static class LiveRegistration
{
    public const string Route = "/v1/live";

    /// <summary>設定名稱。false 時不註冊 hub 與背景工作（測試與想關掉的部署用）；沒設定就是開著。</summary>
    public const string EnabledSetting = "Live:Enabled";

    // 網頁不會送任何東西給伺服器，連線握手之後的訊息幾乎只有心跳；上限壓低，免得拿它當送大封包的入口。
    private const int MaxReceiveBytes = 4 * 1024;

    internal static IServiceCollection AddLive(this IServiceCollection services, IConfiguration configuration)
    {
        if (!SettingValues.ReadFlag(configuration, EnabledSetting, defaultValue: true))
        {
            services.TryAddSingleton<ILiveNotifier, NullLiveNotifier>();
            return services;
        }

        services.AddSignalR(options => options.MaximumReceiveMessageSize = MaxReceiveBytes);
        services.AddSingleton<LiveConnections>();
        services.TryAddSingleton(new LiveOptions());
        services.AddSingleton<LiveSender>();
        services.AddSingleton<LiveGroupSync>();
        services.AddScoped<LiveGroupResolver>();
        services.AddSingleton<LiveConnectionRefresher>();
        services.AddHostedService(provider => provider.GetRequiredService<LiveConnectionRefresher>());
        services.AddSingleton<ILiveNotifier, HubLiveNotifier>();
        services.AddSingleton<INotificationObserver, LiveNotificationObserver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IClusterSignalHandler, LiveClusterSignals>());

        // 讀事件的工作永遠註冊（測試要自己叫它跑一輪），只有開關打開才在主機裡定時跑。
        services.AddSingleton<LiveUpdateWorker>();
        if (SettingValues.ReadFlag(configuration, LiveUpdateWorker.EnabledSetting, defaultValue: true))
        {
            services.AddHostedService(provider => provider.GetRequiredService<LiveUpdateWorker>());
        }

        return services;
    }

    /// <summary>
    /// 只登入的人（Cookie）連得上：<c>RequireAuthorization</c> 讓沒登入的 401；存取權杖因為沒有宣告範圍，
    /// 由範圍的檢查回 403（和其他沒宣告範圍的端點一樣）。negotiate 是 POST，所以一樣要防偽標頭。
    /// </summary>
    internal static IEndpointRouteBuilder MapLive(this IEndpointRouteBuilder app)
    {
        if (app.ServiceProvider.GetService<LiveConnections>() is null)
        {
            return app;
        }

        app.MapHub<LiveHub>(Route)
            .RequireAuthorization()
            .WithMetadata(LiveEndpointMarker.Instance);
        return app;
    }
}
