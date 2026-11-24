namespace PmTool.Ai;

/// <summary>
/// 這個部署的 MCP 網址（<c>{issuer}/mcp</c>）。本機的引擎要連它讀專案的設定、提出變更集。
/// 網址由 API 層依部署的對外網址組（和 OpenRouter 的回呼網址一樣，不讓模組自己猜 Host 標頭）；
/// 在請求的範圍裡解析，所以沒設 <c>Security:PublicHost</c> 的本機部署能從請求本身組出網址。
/// </summary>
public interface IMcpUrlProvider
{
    /// <summary>網址；組不出來（沒設對外網址、也不在請求裡）是 null，本機代理的回覆會因此送不出去。</summary>
    string? GetMcpUrl();
}

/// <summary>模組自己的預設：用 <see cref="AiModuleOptions.PublicUrl"/>。組合根換成依請求組的那一個。</summary>
internal sealed class ConfiguredMcpUrlProvider(AiModuleOptions options) : IMcpUrlProvider
{
    public string? GetMcpUrl() => options.PublicUrl is { Length: > 0 } url ? url.TrimEnd('/') + "/mcp" : null;
}
