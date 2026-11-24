using PmTool.Ai;
using PmTool.Api.OAuth;

namespace PmTool.Api.Composition;

/// <summary>
/// 這個部署的 MCP 網址，依目前的請求組：設了 <c>Security:PublicHost</c> 就用它（不看 Host 標頭），沒設時（本機 http://localhost:8080）用請求本身。
/// 和 OAuth 發權杖時綁的 resource 同一個算法（<see cref="OAuthServerResolver"/>），本機的引擎拿到的網址和權杖的對象才會一致。
/// </summary>
internal sealed class RequestMcpUrlProvider(IHttpContextAccessor accessor, OAuthServerResolver resolver) : IMcpUrlProvider
{
    public string? GetMcpUrl() => accessor.HttpContext is { } context ? resolver.Resolve(context.Request).Resource : null;
}
