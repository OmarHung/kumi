using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI;
using PmTool.Http;

namespace PmTool.Ai.Providers;

/// <summary>
/// 真的連到供應商的 <see cref="IAiProviderGateway"/>：四種供應商都用自己的 <see cref="HttpClient"/>，
/// 所以位址檢查（相容端點）、不跟隨轉址、逾時都在這一層，SDK 只負責講各家的格式。
/// </summary>
internal sealed partial class AiProviderGateway : IAiProviderGateway, IDisposable
{
    // 相容端點沒有金鑰時 SDK 仍要一個 credential；送出去的 Authorization 標頭是這個佔位字串，自架的服務不看它。
    internal const string NoKeyPlaceholder = "no-key";

    private readonly HttpMessageHandler _fixedHost;
    private readonly HttpMessageHandler _compatible;
    private readonly HttpMessageHandler _compatibleStrict;
    private readonly HttpMessageHandler _openRouter;

    /// <param name="handlerOverride">測試用：所有供應商都改走這個 handler（接住 SDK 送出的請求）。</param>
    public AiProviderGateway(AiModuleOptions options, HttpMessageHandler? handlerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _fixedHost = handlerOverride ?? PublicAddressGuard.CreateHandler(options.AllowPrivateEndpoints, guarded: false);
        _compatible = handlerOverride ?? PublicAddressGuard.CreateHandler(options.AllowPrivateEndpoints, guarded: true);
        _compatibleStrict = handlerOverride ?? PublicAddressGuard.CreateHandler(allowPrivateEndpoints: false, guarded: true);
        _openRouter = new OpenRouterHandler(_fixedHost, options.PublicUrl);
        OwnsHandlers = handlerOverride is null;
    }

    private bool OwnsHandlers { get; }

    public IChatClient CreateChatClient(AiEndpoint endpoint, string modelId)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        // 串流的回覆可能很久，由呼叫端的取消與 ReplyTimeout 管，不靠 HttpClient 的逾時。
        var http = CreateHttpClient(endpoint, Timeout.InfiniteTimeSpan);
        return endpoint.Kind switch
        {
            AiProviderKind.Anthropic => CreateAnthropic(endpoint, http, retries: null).AsIChatClient(modelId, AiRules.MaxOutputTokens),
            AiProviderKind.Google => CreateGoogle(endpoint, http).AsIChatClient(modelId),
            AiProviderKind.Openrouter => CreateOpenAi(endpoint, http, retry: null).GetChatClient(modelId).AsIChatClient(),
            _ => CreateOpenAi(endpoint, http, retry: null).GetChatClient(modelId).AsIChatClient(),
        };
    }

    public void Dispose()
    {
        if (OwnsHandlers)
        {
            _fixedHost.Dispose();
            _compatible.Dispose();
            _compatibleStrict.Dispose();
        }
    }

    private HttpClient CreateHttpClient(AiEndpoint endpoint, TimeSpan timeout) =>
        new(HandlerFor(endpoint), disposeHandler: false) { Timeout = timeout };

    private HttpMessageHandler HandlerFor(AiEndpoint endpoint) => endpoint.Kind switch
    {
        AiProviderKind.OpenaiCompatible => endpoint.Personal ? _compatibleStrict : _compatible,
        AiProviderKind.Openrouter => _openRouter,
        _ => _fixedHost,
    };

    // SDK 沒指定就會去讀環境變數（ANTHROPIC_BASE_URL、ANTHROPIC_AUTH_TOKEN 等）：主機的環境剛好設了，
    // 組織的金鑰就會送到別的主機。所以主機與金鑰一律明確指定，不留給環境決定。
    internal const string AnthropicBaseUrl = "https://api.anthropic.com";

    internal const string OpenAiBaseUrl = "https://api.openai.com/v1";

    internal const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    internal const string GoogleBaseUrl = "https://generativelanguage.googleapis.com/";

    private static AnthropicClient CreateAnthropic(AiEndpoint endpoint, HttpClient http, int? retries) =>
        new(new Anthropic.Core.ClientOptions
        {
            ApiKey = RequireKey(endpoint),
            BaseUrl = AnthropicBaseUrl,
            HttpClient = http,
            MaxRetries = retries,
        });

    private static OpenAIClient CreateOpenAi(AiEndpoint endpoint, HttpClient http, ClientRetryPolicy? retry)
    {
        var options = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };
        if (retry is not null)
        {
            options.RetryPolicy = retry;
        }

        options.Endpoint = new Uri(endpoint.Kind switch
        {
            AiProviderKind.OpenaiCompatible => endpoint.BaseUrl ?? throw new InvalidOperationException("相容端點沒有基底網址。"),
            AiProviderKind.Openrouter => OpenRouterBaseUrl,
            _ => OpenAiBaseUrl,
        });

        var key = endpoint.Kind == AiProviderKind.OpenaiCompatible ? endpoint.ApiKey ?? NoKeyPlaceholder : RequireKey(endpoint);
        return new OpenAIClient(new ApiKeyCredential(key), options);
    }

    private static string RequireKey(AiEndpoint endpoint) =>
        string.IsNullOrEmpty(endpoint.ApiKey) ? throw new AiProviderException("沒有金鑰。") : endpoint.ApiKey;
}
