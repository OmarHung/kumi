using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PmTool.Ai.Providers;

/// <summary>
/// 只給 OpenRouter 的請求用，補 OpenAI 的 SDK 做不到的三件事（internal/HANDOFF-ai-floating.md 第 4.2 節）：
/// 帶上 OpenRouter 要的標頭；把附在模型代號後面的思考深度（<see cref="WithEffort"/>）改寫成 OpenRouter 自己的 <c>reasoning: {effort}</c>
/// （SDK 的 ChatCompletionOptions 不能直接加任意欄位，而且只有改寫才送得出 <c>max</c> 這種 OpenAI 沒有的等級）；
/// 串流中途的 <c>{"error": …}</c>（HTTP 已經是 200，SDK 不會當錯誤）變成失敗。
/// </summary>
internal sealed class OpenRouterHandler(HttpMessageHandler inner, string? referer) : DelegatingHandler(inner)
{
    internal const string AppTitle = "Kumi";

    private const string EffortMarker = "#effort=";

    private const string ChatPath = "/chat/completions";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.IsNullOrEmpty(referer))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", referer);
        }

        request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", AppTitle);
        if (request.Method == HttpMethod.Post && request.Content is not null
            && request.RequestUri?.AbsolutePath.EndsWith(ChatPath, StringComparison.Ordinal) == true)
        {
            await RewriteBodyAsync(request, cancellationToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            await WrapStreamAsync(response, cancellationToken);
        }

        return response;
    }

    /// <summary>把思考深度附在模型代號後面，交給這個 handler 改寫成 <c>reasoning: {effort}</c>。</summary>
    internal static string WithEffort(string modelId, string effort) => modelId + EffortMarker + effort;

    private static async Task RewriteBodyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (!TryRewrite(text, out var rewritten))
        {
            return;
        }

        request.Content = new StringContent(rewritten, Encoding.UTF8, "application/json");
    }

    internal static bool TryRewrite(string json, out string rewritten)
    {
        rewritten = json;
        JsonObject? body;
        try
        {
            body = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }

        if (body is null)
        {
            return false;
        }

        if (body["model"]?.GetValue<string>() is { } model && model.Contains(EffortMarker, StringComparison.Ordinal))
        {
            // 用最後一個：萬一模型代號本身有 # 也不會被誤讀（代號擋 # 是第一道防線）。
            var at = model.LastIndexOf(EffortMarker, StringComparison.Ordinal);
            body["model"] = model[..at];
            body["reasoning"] = new JsonObject { ["effort"] = model[(at + EffortMarker.Length)..] };
        }

        // 新的 SDK 送 max_completion_tokens；OpenRouter 文件上的名字是 max_tokens，兩個都收，這裡照文件寫。
        if (body.Remove("max_completion_tokens", out var maxTokens) && maxTokens is not null)
        {
            body["max_tokens"] = maxTokens;
        }

        rewritten = body.ToJsonString();
        return true;
    }

    private static async Task WrapStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var original = response.Content;
        var stream = await original.ReadAsStreamAsync(cancellationToken);
        var wrapped = new StreamContent(new OpenRouterErrorStream(stream));
        foreach (var header in original.Headers)
        {
            wrapped.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = wrapped;
    }
}
