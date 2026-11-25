using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>測 /mcp 的共用步驟：發 OAuth 授權發出的權杖、用 SDK 的用戶端連線與呼叫工具、送沒有經過用戶端的原始請求。</summary>
internal static class McpTesting
{
    public const string Route = "/mcp";

    /// <summary>本機代理的請求帶的工作編號（<c>X-Kumi-Job</c>）。假的額度不看它，只記下來。</summary>
    public static readonly Guid JobId = Guid.Parse("77777777-7777-7777-7777-777777777771");

    /// <summary>OAuth 授權能發的全部範圍（沒有 agent:run：它只給本機代理的個人存取權杖，而且一定要帶專案標頭）。</summary>
    public static readonly AccessTokenScope[] AllScopes = [.. AccessTokenScopes.Grantable];

    /// <summary>OAuth 授權發出的權杖：有授權編號，只能用在 /mcp。</summary>
    public static string GrantToken(ApiFactory factory, AuthenticatedUser user, params AccessTokenScope[] scopes) =>
        factory.Modules.AccessTokens.IssueForGrant(user, Guid.NewGuid(), scopes);

    /// <summary>用 SDK 的用戶端連到 /mcp，帶 Bearer 權杖。用完要 DisposeAsync。</summary>
    public static async Task<McpClient> ConnectAsync(ApiFactory factory, string secret, string? project = null, Guid? job = null, bool sendJob = true)
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {secret}" };
        if (project is not null)
        {
            headers["X-Kumi-Project"] = project;
            if (sendJob)
            {
                headers["X-Kumi-Job"] = (job ?? JobId).ToString();
            }
        }

        var http = factory.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, Route),
                AdditionalHeaders = headers,
            },
            http,
            loggerFactory: null,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>呼叫工具，回傳是不是錯誤與結果的 JSON（工具的結果一律是一段 JSON 文字）。</summary>
    public static async Task<(bool IsError, JsonNode Body)> CallAsync(
        McpClient client, string tool, object? arguments = null)
    {
        var result = await client.CallToolAsync(tool, ToDictionary(arguments));
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        return (result.IsError ?? false, JsonNode.Parse(text)!);
    }

    /// <summary>不經過 SDK 用戶端，直接送一個 JSON-RPC 的 POST：測入口的檢查（登入、Origin）用。</summary>
    public static async Task<HttpResponseMessage> PostRawAsync(
        HttpClient http, string? secret = null, string? origin = null, string method = "tools/list", int padding = 0, string? project = null)
    {
        // padding：在 JSON 後面補空白，把內文撐到需要的大小（測內文上限用）。
        using var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"" + method + "\",\"params\":{}}" + new string(' ', padding),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (secret is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (project is not null)
        {
            request.Headers.Add("X-Kumi-Project", project);
        }

        return await http.SendAsync(request);
    }

    private static Dictionary<string, object?>? ToDictionary(object? arguments) =>
        arguments is null
            ? null
            : JsonSerializer.SerializeToElement(arguments).EnumerateObject()
                .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
}
