using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Http;

namespace PmTool.Ai.Providers;

/// <summary>用 OpenRouter 的授權碼換金鑰。測試先放假的，就不會真的連出去。</summary>
internal interface IOpenRouterAuthClient
{
    /// <summary>
    /// 換到金鑰（<c>sk-or-v1-…</c>）。失敗丟 <see cref="AiProviderException"/>，訊息是給人看的原因（繁體中文），不含金鑰、授權碼與 verifier。
    /// </summary>
    Task<string> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken);
}

/// <summary>
/// 真的連到 <c>https://openrouter.ai/api/v1/auth/keys</c>（PKCE 的第二步）。網址寫死，和供應商一樣不讀環境變數、
/// 不走相容端點的位址檢查；不跟隨轉址、逾時同讀模型清單。
/// </summary>
internal sealed class OpenRouterAuthClient : IOpenRouterAuthClient, IDisposable
{
    internal const string ExchangeUrl = "https://openrouter.ai/api/v1/auth/keys";

    private readonly HttpMessageHandler _handler;
    private readonly bool _ownsHandler;

    /// <param name="handlerOverride">測試用：改走這個 handler。</param>
    public OpenRouterAuthClient(AiModuleOptions options, HttpMessageHandler? handlerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _handler = handlerOverride ?? PublicAddressGuard.CreateHandler(options.AllowPrivateEndpoints, guarded: false);
        _ownsHandler = handlerOverride is null;
    }

    public async Task<string> ExchangeCodeAsync(string code, string verifier, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AiRules.DiscoveryTimeout);
        using var http = new HttpClient(_handler, disposeHandler: false) { Timeout = AiRules.DiscoveryTimeout };
        try
        {
            using var response = await http.PostAsJsonAsync(
                ExchangeUrl, new { code, code_verifier = verifier, code_challenge_method = "S256" }, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return response.IsSuccessStatusCode ? KeyOf(body) : throw new AiProviderException(Describe(response.StatusCode, body));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new AiProviderException("OpenRouter 太久沒有回應。");
        }
        catch (HttpRequestException)
        {
            throw new AiProviderException("連不上 openrouter.ai。");
        }
    }

    public void Dispose()
    {
        if (_ownsHandler)
        {
            _handler.Dispose();
        }
    }

    private static string KeyOf(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject { } node && node["key"] is JsonValue value && value.TryGetValue<string>(out var key)
                && !string.IsNullOrWhiteSpace(key))
            {
                return key;
            }
        }
        catch (JsonException)
        {
            // 下面統一說「回的內容不認得」。
        }

        throw new AiProviderException("OpenRouter 回的內容不認得，沒有拿到金鑰。");
    }

    // 說明只用狀態與錯誤訊息裡的關鍵字決定，不把整段本文放進來。
    private static string Describe(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Forbidden when body.Contains("expired", StringComparison.OrdinalIgnoreCase) =>
            "授權碼已經過期（10 分鐘內要完成），請重新登入。",
        HttpStatusCode.Forbidden => "授權碼或驗證碼不對（授權碼只能用一次），請重新登入。",
        HttpStatusCode.BadRequest => "OpenRouter 不接受這次的登入請求，請重新登入。",
        >= HttpStatusCode.InternalServerError => "OpenRouter 那邊出錯了，稍後再試。",
        _ => $"OpenRouter 回了 {(int)status}。",
    };
}
