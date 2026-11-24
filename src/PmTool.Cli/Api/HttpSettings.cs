namespace PmTool.Cli.Api;

/// <summary>真正連網路用的 HttpClient 設定（測試用假的處理器，不經過這裡）。</summary>
internal static class HttpSettings
{
    /// <summary>一般請求的時限。由 ApiClient 對每個請求自己計時；HttpClient 本身不設整體時限（見 <see cref="UploadTimeout"/>）。</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(100);

    /// <summary>
    /// HttpClient.Timeout 把傳送請求本文的時間也算進去，25 MiB 的檔案在慢的上行（低於約 2 Mbps）
    /// 會在傳完之前就逾時。所以 HttpClient 設成不限，改由 ApiClient 依請求決定：一般請求照 <see cref="Timeout"/>，
    /// 上傳用 <see cref="UploadTimeout"/>。
    /// </summary>
    public static readonly TimeSpan ClientTimeout = System.Threading.Timeout.InfiniteTimeSpan;

    private const long SlowestUploadBytesPerSecond = 64 * 1024;

    /// <summary>上傳的時限：一般時限加上「以每秒 64 KiB 傳完檔案」要的時間；25 MiB 約 8 分鐘。還是要有上限，連線卡死時才會放棄。</summary>
    public static TimeSpan UploadTimeout(long bytes) =>
        Timeout + TimeSpan.FromSeconds(Math.Max(bytes, 0) / (double)SlowestUploadBytesPerSecond);

    /// <summary>
    /// 不自動跟著轉址：每個請求都帶著權杖，被轉去別的網址時不該一起帶過去；
    /// 轉址也不是權杖無效，要讓使用者看到伺服器轉去哪裡（見 ApiClient）。
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new() { AllowAutoRedirect = false };
}
