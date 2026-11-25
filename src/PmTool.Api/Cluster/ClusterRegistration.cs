using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Api.Composition;

namespace PmTool.Api.Cluster;

/// <summary>跨程序訊號的設定。<paramref name="Enabled"/> 是「要不要經過資料庫送給別的程序」；關掉時只有這個程序裡的接收端收得到。</summary>
public sealed record ClusterSignalOptions(bool Enabled);

/// <summary>
/// 跨程序訊號的接線：匯流排（先本機分送，啟用時再經過資料庫送給別的程序）與接收的連線。
/// 關掉（或沒有連線字串）時只有本機分送，單一程序的部署與測試照樣正常。
/// </summary>
public static class ClusterRegistration
{
    /// <summary>設定名稱。沒設定就是開著；單一程序的部署與測試可以關掉。</summary>
    public const string EnabledSetting = "Cluster:Signals:Enabled";

    public static IServiceCollection AddClusterSignals(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabled = SettingValues.ReadFlag(configuration, EnabledSetting, defaultValue: true);
        services.TryAddSingleton(new ClusterSignalOptions(enabled));

        // 身分模組先放了什麼都不做的版本，這裡換成匯流排。
        services.AddSingleton<ClusterSignalBus>();
        services.Replace(ServiceDescriptor.Singleton<IClusterSignals>(provider => provider.GetRequiredService<ClusterSignalBus>()));
        services.AddHostedService(provider => provider.GetRequiredService<ClusterSignalBus>());

        var connectionString = configuration.GetConnectionString(ModuleRegistration.DatabaseConnectionName);
        if (enabled && !string.IsNullOrWhiteSpace(connectionString))
        {
            // 資料來源由容器釋放；發送用連線池，接收用專用的一條（Pooling 關掉）。
            services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
            services.AddSingleton(ClusterSignalConnection.From(connectionString));
            services.AddSingleton<ClusterSignalListener>();
            services.AddHostedService(provider => provider.GetRequiredService<ClusterSignalListener>());
        }

        return services;
    }
}
