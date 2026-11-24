using Anthropic.Models.Models;
using Google.GenAI;
using OpenAI;
using System.ClientModel.Primitives;

namespace PmTool.Ai.Providers;

internal sealed partial class AiProviderGateway
{
    // 設定頁要快點知道結果：不重試。
    private const int DiscoveryRetries = 0;

    public async Task<IReadOnlyList<AiDiscoveredModel>> DiscoverAsync(AiEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AiRules.DiscoveryTimeout);
        using var http = CreateHttpClient(endpoint, AiRules.DiscoveryTimeout);
        try
        {
            var models = endpoint.Kind switch
            {
                AiProviderKind.Anthropic => await DiscoverAnthropicAsync(endpoint, http, timeout.Token),
                AiProviderKind.Openai => await DiscoverOpenAiAsync(endpoint, http, timeout.Token, compatible: false),
                AiProviderKind.Google => await DiscoverGoogleAsync(endpoint, http, timeout.Token),
                AiProviderKind.Openrouter => await DiscoverOpenRouterAsync(endpoint, http, timeout.Token),
                _ => await DiscoverOpenAiAsync(endpoint, http, timeout.Token, compatible: true),
            };
            return AiModelCatalog.Normalize(models);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (NotFoundOnCompatibleEndpointException)
        {
            // 沒有 /models 的相容端點：回空的清單，管理員手動加模型。
            return [];
        }
        catch (Exception exception) when (exception is not AiProviderException)
        {
            var host = HostOf(endpoint);
            throw new AiProviderException(AiProviderFailures.Describe(exception, host, endpoint.ApiKey, endpoint.Personal), exception);
        }
    }

    private static string HostOf(AiEndpoint endpoint) => endpoint.Kind switch
    {
        AiProviderKind.Anthropic => "api.anthropic.com",
        AiProviderKind.Openai => "api.openai.com",
        AiProviderKind.Google => "generativelanguage.googleapis.com",
        AiProviderKind.Openrouter => "openrouter.ai",
        _ => Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "供應商",
    };

    private static async Task<List<AiDiscoveredModel>> DiscoverAnthropicAsync(
        AiEndpoint endpoint, HttpClient http, CancellationToken cancellationToken)
    {
        var client = CreateAnthropic(endpoint, http, DiscoveryRetries);
        var found = new List<AiDiscoveredModel>();
        var page = await client.Models.List(new ModelListParams { Limit = 1000 }, cancellationToken);
        while (true)
        {
            foreach (var model in page.Items)
            {
                var effort = model.Capabilities?.Effort;
                var efforts = AiEffortCatalog.ForAnthropic(
                    effort?.Low?.Supported ?? false,
                    effort?.Medium?.Supported ?? false,
                    effort?.High?.Supported ?? false,
                    effort?.Xhigh?.Supported ?? false,
                    effort?.Max?.Supported ?? false);
                found.Add(new AiDiscoveredModel(model.ID, model.DisplayName ?? model.ID, efforts));
            }

            // 上限：清單再長也不會無限讀下去。
            if (!page.HasNext() || found.Count > AiRules.MaxCatalogModels * 2)
            {
                return found;
            }

            page = await page.Next(cancellationToken);
        }
    }

    private static async Task<List<AiDiscoveredModel>> DiscoverOpenAiAsync(
        AiEndpoint endpoint, HttpClient http, CancellationToken cancellationToken, bool compatible)
    {
        var client = CreateOpenAi(endpoint, http, new System.ClientModel.Primitives.ClientRetryPolicy(DiscoveryRetries));
        try
        {
            var response = await client.GetOpenAIModelClient().GetModelsAsync(cancellationToken);
            return response.Value
                .Where(m => compatible || IsOpenAiChatModel(m.Id))
                .Select(m => new AiDiscoveredModel(
                    m.Id, m.Id, compatible ? [] : AiEffortCatalog.ForOpenAi(m.Id)))
                .ToList();
        }
        catch (System.ClientModel.ClientResultException exception) when (compatible && exception.Status == 404)
        {
            throw new NotFoundOnCompatibleEndpointException(exception);
        }
    }

    private static readonly string[] OpenAiChatPrefixes = ["gpt-", "chatgpt-", "o1", "o3", "o4"];

    private static readonly string[] OpenAiExcluded = ["audio", "realtime", "transcribe", "tts", "image", "search", "embedding", "moderation"];

    internal static bool IsOpenAiChatModel(string id) =>
        OpenAiChatPrefixes.Any(p => id.StartsWith(p, StringComparison.Ordinal))
        && !OpenAiExcluded.Any(x => id.Contains(x, StringComparison.Ordinal));

    private static async Task<List<AiDiscoveredModel>> DiscoverGoogleAsync(
        AiEndpoint endpoint, HttpClient http, CancellationToken cancellationToken)
    {
        var client = CreateGoogle(endpoint, http);
        var found = new List<AiDiscoveredModel>();
        var pager = await client.Models.ListAsync(cancellationToken: cancellationToken);
        await foreach (var model in pager.WithCancellation(cancellationToken))
        {
            if (model.Name is not { } name || model.SupportedActions?.Contains("generateContent") != true)
            {
                continue;
            }

            var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            found.Add(new AiDiscoveredModel(
                id, string.IsNullOrWhiteSpace(model.DisplayName) ? id : model.DisplayName, AiEffortCatalog.ForGoogle(id, model.Thinking ?? false)));
        }

        return found;
    }

    // 明確關掉 Vertex 與企業版、指定主機：GOOGLE_GENAI_USE_VERTEXAI、GOOGLE_CLOUD_PROJECT、GOOGLE_API_KEY、
    // GOOGLE_GEMINI_BASE_URL 這些環境變數不能把 client 切到別的服務或換掉金鑰。
    private static Client CreateGoogle(AiEndpoint endpoint, HttpClient http) =>
        new(
            enterprise: false,
            vertexAI: false,
            apiKey: RequireKey(endpoint),
            httpOptions: new Google.GenAI.Types.HttpOptions { BaseUrl = GoogleBaseUrl },
            clientOptions: new Google.GenAI.Types.ClientOptions { HttpClientFactory = () => http });

    private sealed class NotFoundOnCompatibleEndpointException(Exception inner) : Exception("沒有模型清單。", inner);
}
