using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PmTool.Api.Live;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.RateLimiting;

internal static class ApiRateLimiting
{
    /// <summary>
    /// 登入與改自己的密碼另外套用的限制，依來源 IP 計算，擋住猜密碼。改密碼要先輸入目前的密碼，
    /// 拿著別人的工作階段的人也能拿它猜，所以和登入共用同一份額度，換一個端點不會多出額度。
    /// </summary>
    public const string SignInPolicy = "sign-in";

    /// <summary>網頁回報錯誤的端點另外套用的限制，依人計算（沒登入的依來源 IP，反正會被擋下）。</summary>
    public const string ClientErrorPolicy = "client-errors";

    /// <summary>改自己的密碼另外套用的限制，依人計算（見 <see cref="ApiLimits.PasswordChangePermitsPerWindow"/>）。</summary>
    public const string PasswordChangePolicy = "password-change";

    /// <summary>寄測試信另外套用的限制，依人計算（見 <see cref="ApiLimits.EmailTestPermitsPerWindow"/>）。</summary>
    public const string EmailTestPolicy = "email-test";

    /// <summary>AI 設定頁的「讀取模型」與存連線（會連到供應商）另外套用的限制，依人計算（<see cref="ApiLimits.AiProviderCallPermitsPerWindow"/>）。</summary>
    public const string AiProviderCallPolicy = "ai-provider-call";

    /// <summary>送訊息給 AI 助理另外套用的限制，依人計算（<see cref="ApiLimits.AssistantMessagePermitsPerWindow"/>）。</summary>
    public const string AssistantMessagePolicy = "assistant-message";

    /// <summary>AI 助理附件的上傳另外套用的限制，依人計算（<see cref="ApiLimits.AssistantAttachmentUploadPermitsPerWindow"/>）。</summary>
    public const string AssistantAttachmentUploadPolicy = "assistant-attachment-upload";

    /// <summary>本機代理的三個端點（登記的長輪詢、回報、結束）另外套用的限制，依人計算（<see cref="ApiLimits.LocalAgentPermitsPerWindow"/>）。</summary>
    public const string LocalAgentPolicy = "local-agent";

    /// <summary>主機代理的配對，依來源 IP 計算（<see cref="ApiLimits.HostPairPermitsPerWindow"/>）。其他 worker 端點依代理算，在全域的限流裡。</summary>
    public const string HostPairPolicy = "host-pair";

    /// <summary>OAuth 的 token 與撤銷端點，依來源 IP 計算（<see cref="ApiLimits.OAuthTokenPermitsPerWindow"/>）。</summary>
    public const string OAuthTokenPolicy = "oauth-token";

    /// <summary>OAuth 動態註冊，依來源 IP 計算，每小時 <see cref="ApiLimits.OAuthRegisterPermitsPerHour"/> 次。</summary>
    public const string OAuthRegisterPolicy = "oauth-register";

    /// <summary>同意頁按「允許」，依人計算（<see cref="ApiLimits.OAuthApprovePermitsPerWindow"/>）。</summary>
    public const string OAuthApprovePolicy = "oauth-approve";

    /// <summary>同意頁的 check 與 deny（check 會替使用者連出去抓用戶端的文件），依人計算，兩個共用一份（<see cref="ApiLimits.OAuthCheckPermitsPerWindow"/>）。</summary>
    public const string OAuthCheckPolicy = "oauth-check";

    /// <summary>命令列瀏覽器登入的 check，依人計算（<see cref="ApiLimits.CliLoginCheckPermitsPerWindow"/>）。</summary>
    public const string CliLoginCheckPolicy = "cli-login-check";

    /// <summary>命令列瀏覽器登入按「允許」，依人計算（<see cref="ApiLimits.CliLoginApprovePermitsPerWindow"/>）：每一次都會多一個一次性的碼。</summary>
    public const string CliLoginApprovePolicy = "cli-login-approve";

    /// <summary>命令列瀏覽器登入的兌換，不用登入，依來源 IP 計算（<see cref="ApiLimits.CliLoginTokenPermitsPerWindow"/>）：擋住猜碼。</summary>
    public const string CliLoginTokenPolicy = "cli-login-token";

    /// <summary>匯入（試算表與歷史）：每人同時最多 <see cref="ApiLimits.MaxConcurrentImportsPerPerson"/> 個，在讀內文之前就擋。</summary>
    public const string ImportPolicy = "import";

    /// <summary>kumi 執行檔的下載，依人計算（<see cref="ApiLimits.CliDownloadPermitsPerWindow"/>）。</summary>
    public const string CliDownloadPolicy = "cli-download";

    /// <summary>附件的下載，項目與知識庫共用一份，依人計算（<see cref="ApiLimits.AttachmentDownloadPermitsPerWindow"/>）。</summary>
    public const string AttachmentDownloadPolicy = "attachment-download";

    private static readonly TimeSpan OAuthRegisterWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// 兩個驗證端點另外套用的限制：每個呼叫者同時最多 <see cref="ApiLimits.MaxConcurrentValidationsPerCaller"/> 個，
    /// 不管有沒有登入。全域限流給已登入的人的同時請求數剛好等於全部的驗證位置，只靠它的話一個帳號就占得滿。
    /// </summary>
    public const string ValidationPolicy = "validation";

    // TestServer、Unix socket 這類情況拿不到來源 IP，全部算進同一份額度，寧可擋多也不要完全不擋。
    private const string UnknownClientPartition = "unknown-client";

    private const string LiveConnectionPartition = "live-connection";

    private const int Ipv6AddressBytes = 16;
    private const int Ipv6ClientPrefixBytes = 8;
    private const int BitsPerByte = 8;

    // 同時進行的請求數沒有「視窗還剩多久」可以回報，請用戶端一秒後再試。
    private const string DefaultRetryAfterSeconds = "1";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds);

    /// <summary>
    /// 全域限流：固定視窗內的次數，加上同時進行的請求數。只算次數不夠：固定視窗允許在視窗一開始就把整個額度連發出去，
    /// 每個請求再把內文拖到期限，一個來源就能把所有處理位置占住大半分鐘。
    /// 已登入的請求依人計算，沒登入的依來源 IP（IPv6 以 /64 為一個來源）；所以這個中介軟體要排在確認登入之後。
    /// 來源 IP 是 <c>Connection.RemoteIpAddress</c>。預設就是連線本身，不信任 X-Forwarded-For；放在反向代理後面時，
    /// 設了 ForwardedHeaders:*（見 <see cref="ProxySettings"/>）才會換成信任的代理轉送的用戶端 IP，
    /// 讀轉送標頭的中介軟體排在限流之前。沒設的話所有人看起來都是代理的 IP，共用同一份額度。
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddSingleton<BearerFailureLimiter>().AddSingleton<GlobalLimiters>();
        services.AddOptions<RateLimiterOptions>().Configure<GlobalLimiters>((options, limiters) => options.GlobalLimiter = limiters.Chained);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SignInPolicy, SignInAttempts);
            options.AddPolicy(ValidationPolicy, ValidationsPerCaller);
            options.AddPolicy(ClientErrorPolicy, ClientErrorsPerCaller);
            options.AddPolicy(PasswordChangePolicy, PasswordChangesPerCaller);
            options.AddPolicy(EmailTestPolicy, EmailTestsPerCaller);
            options.AddPolicy(AiProviderCallPolicy, AiProviderCallsPerCaller);
            options.AddPolicy(AssistantMessagePolicy, AssistantMessagesPerCaller);
            options.AddPolicy(AssistantAttachmentUploadPolicy, AssistantAttachmentUploadsPerCaller);
            options.AddPolicy(LocalAgentPolicy, LocalAgentCallsPerCaller);
            options.AddPolicy(HostPairPolicy, HostPairPerAddress);
            options.AddPolicy(OAuthTokenPolicy, OAuthTokenPerAddress);
            options.AddPolicy(OAuthRegisterPolicy, OAuthRegisterPerAddress);
            options.AddPolicy(OAuthApprovePolicy, OAuthApprovesPerCaller);
            options.AddPolicy(OAuthCheckPolicy, OAuthChecksPerCaller);
            options.AddPolicy(CliLoginCheckPolicy, CliLoginChecksPerCaller);
            options.AddPolicy(CliLoginApprovePolicy, CliLoginApprovesPerCaller);
            options.AddPolicy(CliLoginTokenPolicy, CliLoginTokenPerAddress);
            options.AddPolicy(ImportPolicy, ImportsPerCaller);
            options.AddPolicy(CliDownloadPolicy, CliDownloadsPerCaller);
            options.AddPolicy(AttachmentDownloadPolicy, AttachmentDownloadsPerCaller);
            options.OnRejected = WriteRejectionAsync;
        });
    }

    /// <summary>
    /// 全域限流的兩個分區限流器。每個分區限流器都有一個每 0.1 秒跑一次的計時器，<c>CreateChained</c> 回傳的那一個
    /// 釋放時又不會釋放裡面的；交給容器，主機關閉時才會一起釋放、計時器停掉。沒停的計時器還會抓著開始時的執行內容，
    /// 連帶整個主機都放不掉（API 的測試一輪開兩千多個主機，記憶體與計時器越積越多）。
    /// </summary>
    internal sealed class GlobalLimiters : IDisposable
    {
        private readonly PartitionedRateLimiter<HttpContext> _requests = PartitionedRateLimiter.Create<HttpContext, string>(RequestsPerWindow);

        private readonly PartitionedRateLimiter<HttpContext> _concurrent = PartitionedRateLimiter.Create<HttpContext, string>(ConcurrentRequests);

        public GlobalLimiters() => Chained = PartitionedRateLimiter.CreateChained(_requests, _concurrent);

        public PartitionedRateLimiter<HttpContext> Chained { get; }

        public void Dispose()
        {
            Chained.Dispose();
            _requests.Dispose();
            _concurrent.Dispose();
        }
    }

    private static RateLimitPartition<string> RequestsPerWindow(HttpContext context)
    {
        var client = Identify(context);
        return RateLimitPartition.GetFixedWindowLimiter(client.Key, _ => FixedWindow(client.PermitsPerWindow));
    }

    // 即時連線一開就是好幾分鐘到好幾小時（WebSocket 或長輪詢的請求一直在進行中），算進同時請求數的話，
    // 開著幾個分頁就把這個人的 16 個位置占光，他自己的一般請求全部 429。連線另外有每人 20 條的上限（LiveConnections）；
    // 每個視窗的次數照算（negotiate 與每次重連都是一個請求），反覆重連的人還是會被擋住。
    private static RateLimitPartition<string> ConcurrentRequests(HttpContext context)
    {
        if (LiveEndpointMarker.IsLive(context))
        {
            return RateLimitPartition.GetNoLimiter(LiveConnectionPartition);
        }

        var client = Identify(context);
        return Concurrency(client.Key, client.MaxConcurrentRequests);
    }

    private static RateLimitPartition<string> ValidationsPerCaller(HttpContext context) =>
        Concurrency(Identify(context).Key, ApiLimits.MaxConcurrentValidationsPerCaller);

    private static RateLimitPartition<string> ClientErrorsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.ClientErrorPermitsPerWindow));

    // 端點要登入，所以一定認得出是誰；萬一認不出來，照來源 IP 算，寧可擋多也不要不擋。
    private static RateLimitPartition<string> PasswordChangesPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.PasswordChangePermitsPerWindow));

    private static RateLimitPartition<string> EmailTestsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.EmailTestPermitsPerWindow));

    private static RateLimitPartition<string> AiProviderCallsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.AiProviderCallPermitsPerWindow));

    private static RateLimitPartition<string> AssistantMessagesPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.AssistantMessagePermitsPerWindow));

    private static RateLimitPartition<string> AssistantAttachmentUploadsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(Identify(context).Key, _ => FixedWindow(ApiLimits.AssistantAttachmentUploadPermitsPerWindow));

    // 鍵加前綴，不和網頁的額度混在一起：本機代理忙的時候，網頁上的操作不會因此被擋。
    private static RateLimitPartition<string> LocalAgentCallsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "local-agent:" + Identify(context).Key, _ => FixedWindow(ApiLimits.LocalAgentPermitsPerWindow));

    private static RateLimitPartition<string> HostPairPerAddress(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "host-pair:" + AddressKeyOf(context.Connection.RemoteIpAddress), _ => FixedWindow(ApiLimits.HostPairPermitsPerWindow));

    // 這兩個端點不用登入，依來源 IP 算；鍵加前綴，不和登入的額度混在一起。
    private static RateLimitPartition<string> OAuthTokenPerAddress(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "oauth-token:" + AddressKeyOf(context.Connection.RemoteIpAddress), _ => FixedWindow(ApiLimits.OAuthTokenPermitsPerWindow));

    private static RateLimitPartition<string> OAuthRegisterPerAddress(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "oauth-register:" + AddressKeyOf(context.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ApiLimits.OAuthRegisterPermitsPerHour,
                Window = OAuthRegisterWindow,
                QueueLimit = 0,
                AutoReplenishment = true,
            });

    private static RateLimitPartition<string> OAuthApprovesPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "oauth-approve:" + Identify(context).Key, _ => FixedWindow(ApiLimits.OAuthApprovePermitsPerWindow));

    private static RateLimitPartition<string> OAuthChecksPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "oauth-check:" + Identify(context).Key, _ => FixedWindow(ApiLimits.OAuthCheckPermitsPerWindow));

    private static RateLimitPartition<string> CliLoginChecksPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "cli-login-check:" + Identify(context).Key, _ => FixedWindow(ApiLimits.CliLoginCheckPermitsPerWindow));

    private static RateLimitPartition<string> CliLoginApprovesPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "cli-login-approve:" + Identify(context).Key, _ => FixedWindow(ApiLimits.CliLoginApprovePermitsPerWindow));

    private static RateLimitPartition<string> CliLoginTokenPerAddress(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "cli-login-token:" + AddressKeyOf(context.Connection.RemoteIpAddress), _ => FixedWindow(ApiLimits.CliLoginTokenPermitsPerWindow));

    private static RateLimitPartition<string> ImportsPerCaller(HttpContext context) =>
        Concurrency("import:" + Identify(context).Key, ApiLimits.MaxConcurrentImportsPerPerson);

    private static RateLimitPartition<string> CliDownloadsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "cli-download:" + Identify(context).Key, _ => FixedWindow(ApiLimits.CliDownloadPermitsPerWindow));

    private static RateLimitPartition<string> AttachmentDownloadsPerCaller(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            "attachment-download:" + Identify(context).Key, _ => FixedWindow(ApiLimits.AttachmentDownloadPermitsPerWindow));

    private static RateLimitPartition<string> Concurrency(string key, int permits) =>
        RateLimitPartition.GetConcurrencyLimiter(key, _ => new ConcurrencyLimiterOptions { PermitLimit = permits, QueueLimit = 0 });

    // 已經登入的人再登入一次也照來源 IP 算：猜密碼的人不必先登入。
    private static RateLimitPartition<string> SignInAttempts(HttpContext context) =>
        RateLimitPartition.GetFixedWindowLimiter(
            AddressKeyOf(context.Connection.RemoteIpAddress),
            _ => FixedWindow(ApiLimits.SignInPermitsPerWindow));

    private static FixedWindowRateLimiterOptions FixedWindow(int permits) => new()
    {
        PermitLimit = permits,
        Window = Window,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    // 機器憑證與工具授權（HostCredentials）驗證過的請求依代理、依工作算；各自的鍵前綴不和人的、位址的混在一起。
    private static RateLimitClient Identify(HttpContext context)
    {
        if (HostCredentials.TryGetAgent(context) is { } agent)
        {
            return new RateLimitClient(
                $"host-agent:{agent.AgentId}", ApiLimits.HostAgentPermitsPerWindow, ApiLimits.MaxConcurrentHostAgentRequests);
        }

        if (HostCredentials.TryGetGrant(context) is { } grant)
        {
            return new RateLimitClient(
                $"host-job:{grant.JobId}", ApiLimits.SignedInPermitsPerWindow, ApiLimits.MaxConcurrentRequestsPerPerson);
        }

        return SessionPrincipal.TryRead(context.User) is { } user
            ? new RateLimitClient(
                $"person:{user.PersonId}", ApiLimits.SignedInPermitsPerWindow, ApiLimits.MaxConcurrentRequestsPerPerson)
            : new RateLimitClient(
                $"address:{AddressKeyOf(context.Connection.RemoteIpAddress)}",
                ApiLimits.RateLimitPermitsPerWindow,
                ApiLimits.MaxConcurrentRequestsPerClient);
    }

    internal static string AddressKeyOf(IPAddress? address)
    {
        if (address is null)
        {
            return UnknownClientPartition;
        }

        // 雙協定的通訊端會把 IPv4 用戶端回報成 ::ffff:a.b.c.d，要和直接用 IPv4 連進來的算同一個來源。
        var client = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (client.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return client.ToString();
        }

        // 一個 IPv6 用戶通常拿到一整個 /64，位址可以隨便換。以完整位址分開計算的話，換一個位址就有新的額度，
        // 每個新位址還會讓限流器多記一份狀態；所以同一個 /64 算同一個來源。
        Span<byte> bytes = stackalloc byte[Ipv6AddressBytes];
        client.TryWriteBytes(bytes, out _);
        bytes[Ipv6ClientPrefixBytes..].Clear();
        return $"{new IPAddress(bytes)}/{Ipv6ClientPrefixBytes * BitsPerByte}";
    }

    private static ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        context.HttpContext.Response.Headers.RetryAfter =
            context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
                : DefaultRetryAfterSeconds;

        return new ValueTask(ApiFailure.RateLimited.WriteAsync(context.HttpContext.Response, cancellationToken));
    }

    /// <summary>一個額度屬於誰，以及它的大小。鍵帶著前綴，人的編號和位址不會撞在一起。</summary>
    private sealed record RateLimitClient(string Key, int PermitsPerWindow, int MaxConcurrentRequests);
}
