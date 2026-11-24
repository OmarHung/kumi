using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday;

/// <summary>
/// monday 的 GraphQL API。權杖只放在請求標頭：不寫進任何檔案、不印出，錯誤訊息裡出現也一律遮掉。
/// 限流照 monday 的規則處理（internal/HANDOFF-monday-import.md 第 4 節）：每個請求都問 complexity，剩的不夠下一次就等到重置；
/// 被擋（HTTP 429、ComplexityException、Rate limit）就照對方說的秒數或指數退避重試。
/// </summary>
internal sealed class MondayGraphQlClient
{
    public const string DefaultUrl = "https://api.monday.com/v2";
    public const string ApiVersion = "2026-10";
    public const string TokenVariable = "MONDAY_API_TOKEN";
    public const string UrlVariable = "MONDAY_API_URL";

    /// <summary>重試的次數上限（不含第一次）。</summary>
    internal const int MaxRetries = 5;

    private const string ComplexitySelection = "complexity { before after query reset_in_x_seconds }";
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);
    private static readonly string[] RetryableCodes =
    [
        "ComplexityException", "COMPLEXITY_BUDGET_EXHAUSTED", "RATE_LIMIT_EXCEEDED", "IP_RATE_LIMIT_EXCEEDED",
        "maxConcurrencyExceeded", "DAILY_LIMIT_EXCEEDED", "DAILY_REQUEST_LIMIT_EXCEEDED",
    ];

    private readonly HttpClient _http;
    private readonly Uri _url;
    private readonly string _token;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private TimeSpan _budgetWait = TimeSpan.Zero;

    public MondayGraphQlClient(HttpClient http, string url, string token, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http;
        _token = token;
        _url = ParseUrl(url);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>一個請求的時限；可以換掉，測試才不必真的等。</summary>
    internal TimeSpan RequestTimeout { get; init; } = HttpSettings.Timeout;

    public static MondayGraphQlClient FromEnvironment(CliContext context, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var token = context.GetEnvironmentVariable(TokenVariable)?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            throw new CliException(ExitCodes.Error,
                $"沒有設定 {TokenVariable}。到 monday 的「Profile → Developers → My access tokens」複製個人 API 權杖，"
                + $"然後在終端機執行 export {TokenVariable}=…（不要寫進檔案，也不要貼進指令列）。");
        }

        var url = context.GetEnvironmentVariable(UrlVariable);
        return new MondayGraphQlClient(context.Http, string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim(), token, delay);
    }

    /// <summary>
    /// 執行一個查詢，回傳回應的 data（拿掉 complexity）。<paramref name="selection"/> 是大括號裡的欄位；
    /// 值一律走變數，不拼進查詢字串。
    /// </summary>
    public async Task<JsonObject> QueryAsync(string declarations, string selection, JsonObject? variables, CancellationToken cancellationToken)
    {
        var declared = declarations.Length == 0 ? string.Empty : $"({declarations})";
        var query = $"query{declared} {{ {ComplexitySelection} {selection} }}";
        for (var attempt = 0; ; attempt++)
        {
            await WaitForBudgetAsync(cancellationToken);
            var outcome = await SendOnceAsync(query, variables, attempt, cancellationToken);
            if (outcome.Data is { } data)
            {
                return data;
            }

            if (attempt >= MaxRetries)
            {
                throw new CliException(ExitCodes.Error, $"monday 一直在限流，重試 {MaxRetries} 次還是被擋。等幾分鐘再執行一次。");
            }

            await _delay(outcome.RetryAfter, cancellationToken);
        }
    }

    private static Uri ParseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || !(parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback)))
        {
            // 權杖在標頭裡：不是 https 的網址（本機測試除外）會把它明文送出去。
            throw new CliException(ExitCodes.Invalid, $"{UrlVariable} 要是 https 開頭的網址（本機測試才可以用 http）。");
        }

        return parsed;
    }

    private async Task WaitForBudgetAsync(CancellationToken cancellationToken)
    {
        if (_budgetWait > TimeSpan.Zero)
        {
            var wait = _budgetWait;
            _budgetWait = TimeSpan.Zero;
            await _delay(wait, cancellationToken);
        }
    }

    private async Task<Outcome> SendOnceAsync(string query, JsonObject? variables, int attempt, CancellationToken outerToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        timeout.CancelAfter(RequestTimeout);
        var body = new JsonObject { ["query"] = query, ["variables"] = variables?.DeepClone() };
        using var request = new HttpRequestMessage(HttpMethod.Post, _url)
        {
            Content = new StringContent(body.ToJsonString(), new UTF8Encoding(false), "application/json"),
        };

        // 權杖不帶 Bearer：monday 的個人權杖直接放在 Authorization。
        request.Headers.TryAddWithoutValidation("Authorization", _token);
        request.Headers.TryAddWithoutValidation("API-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("kumi-cli");
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            return Interpret(response, text, attempt);
        }
        catch (HttpRequestException exception)
        {
            throw new CliException(ExitCodes.Error, Scrub($"連不上 monday（{_url.Host}）：{exception.Message}"));
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            throw new CliException(ExitCodes.Error, $"monday（{_url.Host}）沒有在時限內回應。");
        }
    }

    private Outcome Interpret(HttpResponseMessage response, string text, int attempt)
    {
        var status = response.StatusCode;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw Unauthorized();
        }

        if (status == HttpStatusCode.TooManyRequests)
        {
            return Retry(RetryAfterHeader(response) ?? Backoff(attempt));
        }

        if (status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
        {
            return Retry(RetryAfterHeader(response) ?? Backoff(attempt));
        }

        var json = TryParse(text);
        if (json is null)
        {
            throw new CliException(ExitCodes.Error, $"monday 回的不是 JSON（HTTP {(int)status}）。");
        }

        if (FindFailure(json) is { } failure)
        {
            return Judge(failure, attempt);
        }

        if (!status.IsSuccess())
        {
            throw new CliException(ExitCodes.Error, $"monday 回了 HTTP {(int)status}。");
        }

        if (json["data"] is not JsonObject data)
        {
            throw new CliException(ExitCodes.Error, "monday 的回應裡沒有 data。");
        }

        // 取下來再回傳：呼叫端會把它放進別的 JSON 樹，節點不能同時掛在兩棵樹上。
        json.AsObject().Remove("data");
        RememberBudget(data);
        data.Remove("complexity");
        return new Outcome(data, TimeSpan.Zero);
    }

    private Outcome Judge(Failure failure, int attempt)
    {
        if (failure.Code is "UserUnauthorizedException" or "Unauthorized" || failure.Message.Contains("Not Authenticated", StringComparison.OrdinalIgnoreCase))
        {
            throw Unauthorized();
        }

        var limited = RetryableCodes.Contains(failure.Code, StringComparer.OrdinalIgnoreCase)
            || failure.Message.Contains("Rate limit", StringComparison.OrdinalIgnoreCase)
            || failure.Message.Contains("Complexity budget", StringComparison.OrdinalIgnoreCase);
        if (limited)
        {
            return Retry(failure.RetryIn is { } seconds ? TimeSpan.FromSeconds(seconds) : Backoff(attempt));
        }

        throw new CliException(ExitCodes.Error, Scrub($"monday 回報錯誤：{failure.Message}"));
    }

    /// <summary>剩下的額度比剛才那個請求用的還少，下一個請求就等到重置（多等一秒，免得剛好差一點）。</summary>
    private void RememberBudget(JsonObject data)
    {
        var complexity = data["complexity"];
        var after = MondayJson.Long(MondayJson.Get(complexity, "after"));
        var cost = MondayJson.Long(MondayJson.Get(complexity, "query"));
        var reset = MondayJson.Long(MondayJson.Get(complexity, "reset_in_x_seconds"));
        if (after is { } left && cost is { } used && left < used && reset is { } seconds)
        {
            _budgetWait = Min(TimeSpan.FromSeconds(seconds + 1), MaxWait);
        }
    }

    private static CliException Unauthorized() =>
        new(ExitCodes.Error, $"monday 不接受這把權杖（無效、已撤銷，或沒有權限）。到 Profile → Developers 重新取得，再設定 {TokenVariable}。");

    private static Outcome Retry(TimeSpan wait) => new(null, Min(wait, MaxWait));

    private static TimeSpan Backoff(int attempt) => Min(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), MaxBackoff);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private static TimeSpan? RetryAfterHeader(HttpResponseMessage response) =>
        response.Headers.RetryAfter is { } header
            ? Max(header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null))
            : null;

    /// <summary>過去的日期算出來是負的等待時間；夾到 0。</summary>
    private static TimeSpan? Max(TimeSpan? wait) => wait is { } value && value < TimeSpan.Zero ? TimeSpan.Zero : wait;

    private static JsonNode? TryParse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Failure? FindFailure(JsonNode json)
    {
        if (json["errors"] is JsonArray { Count: > 0 } errors)
        {
            var first = errors[0];
            var messages = string.Join("；", errors.Select(error => MondayJson.Text(error?["message"])).Where(message => message is not null));
            var code = MondayJson.Text(first?["extensions"]?["code"]) ?? string.Empty;
            return new Failure(code, messages.Length == 0 ? "沒有說明" : messages, MondayJson.Long(first?["extensions"]?["retry_in_seconds"]));
        }

        // 舊格式的錯誤：{ "error_code": …, "error_message": … }。
        if (MondayJson.Text(json["error_message"]) is { } message)
        {
            return new Failure(MondayJson.Text(json["error_code"]) ?? string.Empty, message, MondayJson.Long(json["extensions"]?["retry_in_seconds"]));
        }

        return null;
    }

    private string Scrub(string text) => text.Replace(_token, "***", StringComparison.Ordinal);

    private sealed record Failure(string Code, string Message, long? RetryIn);

    private sealed record Outcome(JsonObject? Data, TimeSpan RetryAfter);
}

internal static class HttpStatusExtensions
{
    public static bool IsSuccess(this HttpStatusCode status) => (int)status is >= 200 and < 300;
}
