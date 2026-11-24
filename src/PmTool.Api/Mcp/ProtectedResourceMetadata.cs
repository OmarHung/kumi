using System.Text.Json.Serialization;
using PmTool.Api.OAuth;
using PmTool.Identity;

namespace PmTool.Api.Mcp;

/// <summary>受保護資源的中繼資料（RFC 9728）。屬性名稱是標準規定的，不跟 API 的命名走。</summary>
internal sealed record ProtectedResourceMetadata(
    [property: JsonPropertyName("resource")] string Resource,
    [property: JsonPropertyName("authorization_servers")] IReadOnlyList<string> AuthorizationServers,
    [property: JsonPropertyName("scopes_supported")] IReadOnlyList<string> ScopesSupported,
    [property: JsonPropertyName("bearer_methods_supported")] IReadOnlyList<string> BearerMethodsSupported,
    [property: JsonPropertyName("resource_name")] string ResourceName)
{
    public static ProtectedResourceMetadata For(string issuer, string resource) => new(
        resource,
        [issuer],
        [.. AccessTokenScopes.Grantable.Select(AccessTokenScopes.NameOf)],
        ["header"],
        McpInstructions.ProductName);

    public static IResult Respond(HttpContext context)
    {
        // 內容只依部署的網址而定，不含任何人的資料；快取的規則和授權伺服器的中繼資料一樣（沒設 PublicHost 時不給快取）。
        var resolver = context.RequestServices.GetRequiredService<OAuthServerResolver>();
        var server = resolver.Resolve(context.Request);
        context.Response.Headers.CacheControl = resolver.MetadataCacheControl;
        return Results.Json(For(server.Issuer, server.Resource));
    }
}
