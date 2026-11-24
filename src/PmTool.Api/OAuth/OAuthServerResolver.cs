using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.OAuth;

/// <summary>
/// 這個部署對外的 issuer 與 MCP 端點的網址（<see cref="OAuthServer"/>）。授權伺服器的中繼資料、受保護資源的中繼資料、
/// 發權杖時綁的 resource 都用這一份，才不會有對不上的網址（用戶端會拒絕 issuer 不一致的中繼資料）。
/// 設了 <c>Security:PublicHost</c> 就一律用 <c>https://{PublicHost}</c>：Host 標頭是用戶端自己填的，不能拿它決定 issuer；
/// 沒設時（本機的 http://localhost:8080）才用請求本身的 scheme 與 host。
/// </summary>
internal sealed class OAuthServerResolver(TransportSecurity transport)
{
    /// <summary>MCP 端點的路徑。受保護資源的中繼資料在 <c>/.well-known/oauth-protected-resource/mcp</c>。</summary>
    public const string McpPath = "/mcp";

    /// <summary>
    /// 兩個中繼資料端點（授權伺服器、受保護資源）的 Cache-Control。設了 PublicHost，內容只隨部署的設定變，
    /// 公開快取五分鐘；沒設的時候 issuer 是從 Host 標頭組的，有人送假的 Host，前面的快取就會把假的 issuer 存起來給別人，所以不給快取。
    /// </summary>
    public string MetadataCacheControl => transport.PublicHost is null ? "no-store" : "public, max-age=300";

    public OAuthServer Resolve(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var issuer = transport.PublicHost is { } host
            ? $"https://{host}"
            : $"{request.Scheme}://{request.Host.Value}";
        return new OAuthServer(issuer, issuer + McpPath);
    }
}
