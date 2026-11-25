using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PmTool.Http;

namespace PmTool.Identity.OAuth;

/// <summary>抓文件用的 HTTP 設定。獨立出來，測試才換得掉 handler 與逾時，不必真的連網路、等五秒。</summary>
internal sealed record OAuthFetchSettings
{
    /// <summary>null 是正式用的：只連公開位址、不跟隨轉址、不走 proxy（<see cref="PublicAddressGuard"/>）。</summary>
    public HttpMessageHandler? Handler { get; init; }

    public TimeSpan Timeout { get; init; } = OAuthRules.MetadataFetchTimeout;
}

/// <summary>一次抓取的結果。</summary>
internal sealed record FetchOutcome
{
    /// <summary>抓到而且驗過了。</summary>
    public ClientMetadataDocument? Document { get; init; }

    /// <summary>對方回的 Cache-Control 的存活時間，沒有或不能快取是 null。</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>對方確定不合規定（網址不行、內容不對、太大）：給人看的原因。重試也不會好，不能用舊的快取。</summary>
    public string? Rejected { get; init; }

    /// <summary>暫時抓不到（逾時、連不上、對方 5xx）：可以先用舊的快取。</summary>
    public bool Unavailable { get; init; }
}

/// <summary>
/// 去抓 Client ID Metadata Document。網址是別人填的，伺服器替他連出去：只能連公開的位址
/// （連線當下檢查解析出來的位址，防 DNS rebinding）、不跟隨轉址、限時、限大小。
/// 「指向內網」與「解析不到、連不上、沒回應」對外是同一種結果（暫時抓不到），登入的人不能靠回應的差別探測內網有哪些主機名稱；
/// 兩者的差別只寫在記錄裡。
/// </summary>
internal sealed partial class ClientMetadataFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;

    public ClientMetadataFetcher(OAuthFetchSettings settings, ILogger<ClientMetadataFetcher>? logger = null)
    {
        _timeout = settings.Timeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _http = new HttpClient(settings.Handler ?? PublicAddressGuard.CreateHandler(allowPrivateEndpoints: false, guarded: true), disposeHandler: true)
        {
            // 逾時由每次抓取自己的取消權杖控制，才分得出是逾時還是呼叫端取消。
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    public async Task<FetchOutcome> FetchAsync(Uri url, string clientId, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            return await ReadAsync(response, clientId, _logger, limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogTimedOut(_logger, url.Host);
            return new FetchOutcome { Unavailable = true };
        }
        catch (HttpRequestException exception)
        {
            if (IsBlocked(exception))
            {
                LogBlocked(_logger, url.Host);
            }
            else
            {
                LogFailed(_logger, url.Host, exception.GetType().Name);
            }

            return new FetchOutcome { Unavailable = true };
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Client ID Metadata Document 的主機 {Host} 解析到內網或本機的位址，沒有連線。")]
    private static partial void LogBlocked(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "抓 Client ID Metadata Document 失敗（主機 {Host}，{ErrorType}）：解析不到、連不上，或對方沒回應。")]
    private static partial void LogFailed(ILogger logger, string host, string errorType);

    [LoggerMessage(Level = LogLevel.Information, Message = "抓 Client ID Metadata Document 逾時（主機 {Host}）。")]
    private static partial void LogTimedOut(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client ID Metadata Document 的網址回應 {Status}，不是 200。")]
    private static partial void LogUnexpectedStatus(ILogger logger, int status);

    public void Dispose() => _http.Dispose();

    private static async Task<FetchOutcome> ReadAsync(HttpResponseMessage response, string clientId, ILogger logger, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode >= 500)
        {
            return new FetchOutcome { Unavailable = true };
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            LogUnexpectedStatus(logger, (int)response.StatusCode);
            return new FetchOutcome { Rejected = "抓不到用戶端的資料：網址要直接回 200 的 JSON（不跟隨轉址）。" };
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
        {
            return new FetchOutcome { Rejected = "用戶端的資料網址要回 application/json。" };
        }

        var body = await ReadLimitedAsync(response, cancellationToken);
        if (body is null)
        {
            return new FetchOutcome { Rejected = "用戶端的資料太大。" };
        }

        return ClientMetadataDocument.TryParse(body, clientId, out var document, out var error)
            ? new FetchOutcome { Document = document, MaxAge = MaxAgeOf(response) }
            : new FetchOutcome { Rejected = error };
    }

    /// <summary>讀到上限就停，不把對方送的整份內容都收進記憶體。太大回 null。</summary>
    private static async Task<string?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > OAuthRules.MaxMetadataBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > OAuthRules.MaxMetadataBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static TimeSpan? MaxAgeOf(HttpResponseMessage response)
    {
        var control = response.Headers.CacheControl;
        return control is null || control.NoStore ? null : control.MaxAge;
    }

    private static bool IsBlocked(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is BlockedAddressException)
            {
                return true;
            }
        }

        return false;
    }
}
