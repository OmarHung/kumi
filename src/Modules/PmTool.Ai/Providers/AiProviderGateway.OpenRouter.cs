using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PmTool.Ai.Providers;

internal sealed partial class AiProviderGateway
{
    private const string OpenRouterOrigin = "https://openrouter.ai/api/v1";

    /// <summary>
    /// OpenRouter 的模型清單不用金鑰也讀得到，所以先打一個要金鑰的端點（<c>GET /key</c>，金鑰不對回 401）確認金鑰有效，
    /// 再讀只留支援工具呼叫的清單。
    /// </summary>
    private static async Task<List<AiDiscoveredModel>> DiscoverOpenRouterAsync(
        AiEndpoint endpoint, HttpClient http, CancellationToken cancellationToken)
    {
        var key = RequireKey(endpoint);
        using (await GetAsync(http, "/key", key, cancellationToken))
        {
        }

        using var response = await GetAsync(http, "/models?supported_parameters=tools", key, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new AiProviderException("OpenRouter 回的模型清單格式不認得。");
        }

        return [.. data.EnumerateArray().Select(ReadOpenRouterModel).OfType<AiDiscoveredModel>()];
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, string path, string key, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OpenRouterOrigin + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // 本文帶進例外：說明裡會取其中的 error.message，並遮掉像金鑰的片段。
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException(text, null, status);
    }

    // 沒有 tools 的模型不能用（助理靠工具呼叫）。清單已經用 supported_parameters 篩過，這裡再看一次，不信任遠端一定照做。
    private static AiDiscoveredModel? ReadOpenRouterModel(JsonElement model)
    {
        if (model.ValueKind != JsonValueKind.Object || StringOf(model, "id") is not { } id || id.Contains('#', StringComparison.Ordinal))
        {
            return null;
        }

        if (model.TryGetProperty("supported_parameters", out var parameters)
            && parameters.ValueKind == JsonValueKind.Array
            && !parameters.EnumerateArray().Any(p => p.ValueKind == JsonValueKind.String && p.GetString() == "tools"))
        {
            return null;
        }

        var name = StringOf(model, "name") ?? id;
        return new AiDiscoveredModel(id, name, ReadOpenRouterEfforts(model));
    }

    private static IReadOnlyList<AiEffort> ReadOpenRouterEfforts(JsonElement model)
    {
        if (!model.TryGetProperty("reasoning", out var reasoning) || reasoning.ValueKind != JsonValueKind.Object)
        {
            return AiEffortCatalog.ForOpenRouter(hasReasoning: false, null, mandatory: false);
        }

        IEnumerable<string>? supported = null;
        if (reasoning.TryGetProperty("supported_efforts", out var efforts) && efforts.ValueKind == JsonValueKind.Array)
        {
            supported = efforts.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
        }

        var mandatory = reasoning.TryGetProperty("mandatory", out var flag) && flag.ValueKind == JsonValueKind.True;
        return AiEffortCatalog.ForOpenRouter(hasReasoning: true, supported, mandatory);
    }

    private static string? StringOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}
