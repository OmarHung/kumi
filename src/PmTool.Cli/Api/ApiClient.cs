using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Blueprint;

namespace PmTool.Cli.Api;

/// <summary>
/// 呼叫伺服器的 /v1：帶權杖、拆信封。成功回 data；失敗丟 <see cref="CliException"/>，
/// 結束代碼與訊息在這裡統一決定，各個指令不必各寫一套。
/// </summary>
internal sealed class ApiClient(HttpClient http, string server, string token)
{
    private const string Sha256Header = "X-Attachment-Sha256";
    /// <summary>伺服器回了 3xx（例如要求 https 的部署會 308）；不是伺服器的錯誤代碼，是這個用戶端自己標的。</summary>
    internal const string RedirectCode = "client.redirect";
    private const string ApplyBlockedCode = "changeset.apply_blocked";

    /// <summary>
    /// 被限流（429）時最多重送幾次。伺服器依人每分鐘有額度（固定的時間窗），等到下一個窗就有；
    /// monday 匯入一次要送幾千個請求，超過額度是常態，不能因此整個失敗。限流在處理請求之前就擋下，重送不會重複寫入。
    /// </summary>
    private const int MaxRateLimitRetries = 10;

    /// <summary>一般回應最大讀多少：伺服器出錯或被代理換掉時，不能被一個無限大的回應塞爆記憶體。</summary>
    internal const int MaxResponseBytes = 4 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders = new Dictionary<string, string>();

    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

    /// <summary>伺服器說要等更久時最多等一分鐘（時間窗就是一分鐘），再問一次。</summary>
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    public string Server { get; } = server;

    /// <summary>每個請求都要多帶的標頭（主機 worker 的機器編號 <c>X-Kumi-Machine</c>）；null 是沒有。</summary>
    internal IReadOnlyDictionary<string, string>? ExtraHeaders { get; init; }

    /// <summary>一般請求的時限；可以換掉，測試才不必真的等 100 秒。</summary>
    internal TimeSpan RequestTimeout { get; init; } = HttpSettings.Timeout;

    /// <summary>上傳的時限，依檔案大小；可以換掉，理由同上。</summary>
    internal Func<long, TimeSpan> UploadTimeout { get; init; } = HttpSettings.UploadTimeout;

    /// <summary>被限流時等待；可以換掉，測試才不必真的等。</summary>
    internal Func<TimeSpan, CancellationToken, Task> Wait { get; init; } = Task.Delay;

    public Task<JsonElement> GetAsync(string path, IEnumerable<KeyValuePair<string, string>>? query, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, path, query, NoContent, cancellationToken);

    public Task<JsonElement> PostAsync(string path, JsonNode? body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, path, null, body is null ? NoContent : () => JsonContent(body), cancellationToken);

    public Task<JsonElement> DeleteAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, path, null, NoContent, cancellationToken);

    public Task<JsonElement> PutAsync(string path, JsonNode body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, path, null, () => JsonContent(body), cancellationToken);

    public Task<JsonElement> PatchAsync(string path, JsonNode body, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, path, null, () => JsonContent(body), cancellationToken);

    /// <summary>送出整份文件（變更集）：照原文送，伺服器回報的行號才對得上使用者的檔案。</summary>
    public Task<JsonElement> PostDocumentAsync(string path, string document, DocumentFormat format, CancellationToken cancellationToken)
    {
        var mediaType = format == DocumentFormat.Json ? "application/json" : "application/yaml";
        return SendAsync(HttpMethod.Post, path, null, () => new StringContent(document, new UTF8Encoding(false), mediaType), cancellationToken);
    }

    /// <summary>
    /// 上傳一個檔案（multipart/form-data，欄位 file）。內容用串流送，不整個讀進記憶體；
    /// 串流由這裡負責關掉。
    /// </summary>
    public Task<JsonElement> PostFileAsync(string path, Stream content, string fileName, string contentType, CancellationToken cancellationToken) =>
        PostFormFileAsync(path, [], content, fileName, contentType, cancellationToken);

    /// <summary>
    /// 上傳一個檔案，前面帶文字欄位（歷史匯入：伺服器要求欄位照順序來，檔案放最後，才能不讀內容就認出已經匯入過的）。
    /// 文字欄位依給的順序送出；串流由這裡負責關掉。
    /// </summary>
    public async Task<JsonElement> PostFormFileAsync(
        string path, IReadOnlyList<(string Name, string Value)> fields, Stream content, string fileName, string contentType,
        CancellationToken cancellationToken)
    {
        using var owned = content;
        // 被限流時要整份重送：串流倒回開頭，包一層不會被 HttpContent 關掉的；不能倒回的串流只送一次。
        var start = content.CanSeek ? content.Position : 0;
        HttpContent MakeForm()
        {
            if (content.CanSeek)
            {
                content.Position = start;
            }

            var form = new MultipartFormDataContent();
            foreach (var (name, value) in fields)
            {
                form.Add(new StringContent(value, new UTF8Encoding(false)), name);
            }

            var part = new StreamContent(new KeepOpenStream(content));
            part.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(part, "file", fileName);
            return form;
        }

        return await SendAsync(
            HttpMethod.Post, path, null, MakeForm, cancellationToken, UploadTimeout(content.CanSeek ? content.Length : 0), canRetry: content.CanSeek);
    }

    /// <summary>
    /// 下載檔案：只讀到回應標頭就回來，內容由呼叫端自己從串流讀。失敗（不是 2xx）時照一般的方式丟 <see cref="CliException"/>。
    /// </summary>
    public async Task<DownloadStream> OpenDownloadAsync(string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, null));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("kumi-cli");
        HttpResponseMessage? response = null;
        // 時限只管到收齊回應標頭；內容可以很大，邊收邊寫，不設整體時限。
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerTimeout.CancelAfter(RequestTimeout);
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw RedirectFailure(request.RequestUri!, response.Headers.Location);
            }

            if (!response.IsSuccessStatusCode)
            {
                Unwrap(response.StatusCode, await response.Content.ReadAsStringAsync(headerTimeout.Token));
                throw Failure(response.StatusCode, null);
            }

            var headers = response.Content.Headers;
            var name = headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName?.Trim('"');
            var hash = response.Headers.TryGetValues(Sha256Header, out var values) ? values.FirstOrDefault()?.Trim().ToLowerInvariant() : null;
            return new DownloadStream(response, await response.Content.ReadAsStreamAsync(cancellationToken), name, headers.ContentLength,
                hash is { Length: 64 } && hash.All(Uri.IsHexDigit) ? hash : null);
        }
        catch (HttpRequestException exception)
        {
            response?.Dispose();
            throw new CliException(ExitCodes.Error, $"連不上伺服器 {Server}：{exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            response?.Dispose();
            throw new CliException(ExitCodes.Error, $"伺服器 {Server} 沒有在時限內回應。");
        }
        catch
        {
            response?.Dispose();
            throw;
        }
        finally
        {
            request.Dispose();
        }
    }

    private static readonly Func<HttpContent?> NoContent = () => null;

    private static StringContent JsonContent(JsonNode body) =>
        new(body.ToJsonString(), new UTF8Encoding(false), "application/json");

    private async Task<JsonElement> SendAsync(
        HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? query, Func<HttpContent?> makeContent,
        CancellationToken cancellationToken, TimeSpan? timeout = null, bool canRetry = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            var (status, body, redirect, retryAfter, uri) = await SendOnceAsync(method, path, query, makeContent(), cancellationToken, timeout);
            if (status == HttpStatusCode.TooManyRequests && canRetry && attempt < MaxRateLimitRetries)
            {
                await Wait(RetryDelay(retryAfter), cancellationToken);
                continue;
            }

            if ((int)status is >= 300 and < 400)
            {
                throw RedirectFailure(uri, redirect);
            }

            return Unwrap(status, body);
        }
    }

    private static TimeSpan RetryDelay(TimeSpan? retryAfter) =>
        retryAfter is { } wait && wait > TimeSpan.Zero ? (wait > MaxRetryAfter ? MaxRetryAfter : wait) : DefaultRetryAfter;

    private async Task<(HttpStatusCode Status, string Body, Uri? Redirect, TimeSpan? RetryAfter, Uri Uri)> SendOnceAsync(
        HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? query, HttpContent? content,
        CancellationToken outerToken, TimeSpan? timeout)
    {
        // 自己計時（HttpClient 不設整體時限）：逾時取消的是這個 source，不是呼叫端的 token，下面才分得出兩種取消。
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        timeoutSource.CancelAfter(timeout ?? RequestTimeout);
        var cancellationToken = timeoutSource.Token;
        using var request = new HttpRequestMessage(method, BuildUri(path, query)) { Content = content };
        if (token.Length > 0)
        {
            // 沒有憑證的請求（配對）不帶 Authorization 標頭。
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("kumi-cli");
        foreach (var (name, value) in ExtraHeaders ?? EmptyHeaders)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var body = await ReadLimitedAsync(response.Content, cancellationToken);
            var retryAfter = response.Headers.RetryAfter is { } header
                ? header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null)
                : null;
            return (response.StatusCode, body, response.Headers.Location, retryAfter, request.RequestUri!);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(ExitCodes.Error, $"連不上伺服器 {Server}：{exception.Message}");
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            throw new CliException(ExitCodes.Error, $"伺服器 {Server} 沒有在時限內回應。");
        }
    }

    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxResponseBytes)
        {
            throw TooLarge();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static CliException TooLarge() =>
        new(ExitCodes.Error, $"伺服器的回應超過 {MaxResponseBytes / (1024 * 1024)} MB，不像是正常的 API 回應，已經放棄。");

    /// <summary>
    /// 不跟著轉址：權杖是給這個伺服器的，轉去別的網址就不該帶著它；也不能把轉址當成權杖無效。
    /// 只印網址的開頭到路徑，網址上的帳密與查詢字串不印。
    /// </summary>
    private CliException RedirectFailure(Uri requested, Uri? location)
    {
        if (location is null)
        {
            return new CliException(ExitCodes.Error, $"伺服器 {Server} 回了轉址，但沒有說轉去哪裡。請確認伺服器網址。", code: RedirectCode);
        }

        var target = location.IsAbsoluteUri ? location : new Uri(requested, location);
        var shown = target.GetLeftPart(UriPartial.Path);
        return new CliException(ExitCodes.Error, $"伺服器轉址到 {shown}，請改用那個網址（例如 --server 或 {Settings.Credentials.ServerVariable}）。", code: RedirectCode);
    }

    private Uri BuildUri(string path, IEnumerable<KeyValuePair<string, string>>? query)
    {
        var builder = new StringBuilder(Server).Append("/v1/").Append(path);
        var separator = '?';
        foreach (var (name, value) in query ?? [])
        {
            builder.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return new Uri(builder.ToString());
    }

    private static JsonElement Unwrap(HttpStatusCode status, string body)
    {
        JsonElement? envelope = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            envelope = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // 不是 API 的信封（例如反向代理的錯誤頁），照狀態碼說明。
        }

        var succeeded = (int)status is >= 200 and < 300;
        if (envelope is { } found && found.Get("success") is not null)
        {
            if (found.Flag("success") && succeeded)
            {
                return found.Get("data") ?? default;
            }
        }
        else if (succeeded)
        {
            // 2xx 卻不是信封（例如 204 沒有內文）：照狀態碼，是成功。
            return default;
        }

        throw Failure(status, envelope?.Get("error"));
    }

    private static CliException Failure(HttpStatusCode status, JsonElement? error)
    {
        var code = error?.Text("code");
        var serverMessage = error?.Text("message");
        var details = (error?.List("details") ?? [])
            .Select(detail => (Path: detail.Text("path") ?? string.Empty, Message: detail.Text("message") ?? string.Empty))
            .ToArray();

        var message = status switch
        {
            HttpStatusCode.Unauthorized =>
                "權杖無效、已過期或已撤銷。請到網頁「我的帳號 → 存取權杖」重新建立一把，再執行 kumi auth login。",
            _ when serverMessage is not null => serverMessage,
            _ => $"伺服器回了 HTTP {(int)status}。",
        };
        return new CliException(ExitCodeOf(status, code), message, details, code, (int)status);
    }

    /// <summary>422 與 400（內容不對）是 2；套用被資料擋住是 3；其他都是 1。</summary>
    private static int ExitCodeOf(HttpStatusCode status, string? code) => code == ApplyBlockedCode
        ? ExitCodes.NeedsDecision
        : status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType
            or HttpStatusCode.RequestEntityTooLarge
            ? ExitCodes.Invalid
            : ExitCodes.Error;
}

/// <summary>下載中的回應：標頭已經讀完，內容還在串流裡。用完要 Dispose。</summary>
internal sealed class DownloadStream(HttpResponseMessage response, Stream content, string? fileName, long? length, string? sha256) : IDisposable
{
    public Stream Content { get; } = content;

    /// <summary>伺服器在 Content-Disposition 給的檔名（沒整理過，不能直接當路徑用）。</summary>
    public string? FileName { get; } = fileName;

    public long? Length { get; } = length;

    /// <summary>伺服器在 X-Attachment-Sha256 標頭給的雜湊（小寫十六進位）；舊的伺服器沒有。</summary>
    public string? Sha256 { get; } = sha256;

    public void Dispose()
    {
        Content.Dispose();
        response.Dispose();
    }
}
