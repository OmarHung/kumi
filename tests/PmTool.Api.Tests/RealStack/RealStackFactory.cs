using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PmTool.Api.Composition;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

internal sealed record RealStackOptions
{
    /// <summary>額外的設定值，例如遷移用的連線、啟動時不遷移、DataProtection 的憑證。</summary>
    public IReadOnlyDictionary<string, string?> Settings { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// 金鑰只放在記憶體裡。預設開著，和受測行為無關的測試不必管金鑰；
    /// 要測「金鑰存在資料庫、換容器還解得開」時關掉，改用 Program.cs 接上的存放方式。
    /// </summary>
    public bool UseEphemeralDataProtection { get; init; } = true;

    /// <summary>在 Program.cs 註冊完之後再加的服務，例如收集記錄。</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }
}

/// <summary>
/// 照 Program.cs 的接法啟動整個 API：真的身分模組、真的工作引擎、真的驗證器，連到測試用的 PostgreSQL。
/// 只換掉兩件和受測行為無關的事：金鑰只放在記憶體裡（可以用 <see cref="RealStackOptions"/> 關掉），web root 用暫存目錄。
/// </summary>
internal sealed class RealStackFactory(string connectionString, RealStackOptions? options = null) : WebApplicationFactory<Program>
{
    public const string OrganizationName = "Example 組織";
    public const string OwnerName = "Owner Example";
    public const string OwnerEmail = "owner@example.com";

    // 測試用的假密碼，只存在於這個測試的資料庫裡。
    public const string OwnerPassword = "real-stack-owner-0000";

    private readonly string _webRoot = ApiFactory.CreateTemporaryDirectory();
    private readonly string _attachmentDirectory = ApiFactory.CreateTemporaryDirectory();
    private readonly RealStackOptions _options = options ?? new RealStackOptions();

    // 主機建好時記下 TestServer：關閉之後要從它的請求管線找到限流中介層的端點限流器釋放（RateLimiterCleanup）。
    private TestServer? _server;

    /// <summary>附件的檔案放的資料夾（local 儲存），給要數儲存空間裡有幾個檔案的測試。</summary>
    public string AttachmentDirectory => _attachmentDirectory;

    /// <summary>資料庫的連線字串，給要直接動資料表的測試（例如模擬還原）。</summary>
    public string DatabaseConnectionString => connectionString;

    /// <summary>每個會改資料的請求都帶防偽標頭、會記住 Cookie 的用戶端，和網頁的行為一樣。</summary>
    public HttpClient CreateApiClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiRequests.CsrfHeader, "1");
        return client;
    }

    /// <summary>
    /// 背景處理（每兩秒把事件交給通知）在這裡關掉，由測試決定什麼時候處理，結果才不靠等待與運氣。
    /// 跑一輪，回傳交出去的事件數；0 也可能是出錯被記下來了，測試要檢查交出去的數量。
    /// </summary>
    public Task<int> RunNotificationsOnceAsync() =>
        new NotificationWorker(Services.GetRequiredService<IServiceScopeFactory>(), Services.GetRequiredService<ILogger<NotificationWorker>>())
            .RunOnceAsync(CancellationToken.None);

    /// <summary>寄信的背景工作跑一輪，回傳寄出的封數。和通知一樣，由測試決定什麼時候跑。</summary>
    public Task<int> RunEmailOnceAsync() =>
        new EmailWorker(Services.GetRequiredService<IServiceScopeFactory>(), Services.GetRequiredService<ILogger<EmailWorker>>())
            .RunOnceAsync(CancellationToken.None);

    /// <summary>即時推送讀事件的工作跑一輪（要打開 Live:Enabled）；回傳這一輪處理的事件數。第一輪只取起點，不推。</summary>
    public Task<int> RunLiveOnceAsync() => Services.GetRequiredService<LiveUpdateWorker>().RunOnceAsync(CancellationToken.None);

    /// <summary>重新檢查所有即時連線一輪；回傳被斷開的連線數。</summary>
    public Task<int> RunLiveRefresherOnceAsync() => Services.GetRequiredService<LiveConnectionRefresher>().RunOnceAsync(CancellationToken.None);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting(NotificationWorker.EnabledSetting, "false");
        builder.UseSetting(EmailWorker.EnabledSetting, "false");
        builder.UseSetting(AiMaintenanceWorker.EnabledSetting, "false");

        // 即時連線預設關著（和背景處理一樣）；要測它的測試用 Settings 打開，並自己決定什麼時候讀事件。
        builder.UseSetting(PmTool.Api.Live.LiveRegistration.EnabledSetting, "false");
        builder.UseSetting(PmTool.Api.Live.LiveUpdateWorker.EnabledSetting, "false");

        // 跨程序的訊號預設不開（每個測試類別一個主機，不需要多一條接收的連線）；要測的用 Settings 打開。
        builder.UseSetting(PmTool.Api.Cluster.ClusterRegistration.EnabledSetting, "false");
        builder.UseSetting("ConnectionStrings:Database", connectionString);
        builder.UseSetting("Attachments:Provider", "local");
        builder.UseSetting("Attachments:Directory", _attachmentDirectory);
        builder.UseSetting("Bootstrap:OrganizationName", OrganizationName);
        builder.UseSetting("Bootstrap:AdminName", OwnerName);
        builder.UseSetting("Bootstrap:AdminEmail", OwnerEmail);
        builder.UseSetting("Bootstrap:AdminPassword", OwnerPassword);
        builder.UseSetting(WebHostDefaults.WebRootKey, _webRoot);
        foreach (var (key, value) in _options.Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            if (_options.UseEphemeralDataProtection)
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            }

            services.AddSingleton<IStartupFilter, TestRemoteAddressStartupFilter>();
            _options.ConfigureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        _server = host.Services.GetService<IServer>() as TestServer;
        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _server is not null)
        {
            RateLimiterCleanup.DisposeEndpointLimiter(_server);
            _server = null;
        }

        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }

        if (disposing && Directory.Exists(_attachmentDirectory))
        {
            Directory.Delete(_attachmentDirectory, recursive: true);
        }
    }

    /// <summary>
    /// TestServer 的連線沒有遠端位址；要看「依來源位址」的行為時，請求帶 <see cref="ApiFactory.RemoteIpHeader"/> 指定。
    /// 沒帶標頭就維持沒有位址，和 Kestrel 走 Unix socket 的情形一樣。
    /// </summary>
    private sealed class TestRemoteAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ApiFactory.RemoteIpHeader, out var ip))
                {
                    context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip.ToString());
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
