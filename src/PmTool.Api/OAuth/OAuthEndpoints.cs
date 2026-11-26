namespace PmTool.Api.OAuth;

/// <summary>
/// Kumi 自己的 OAuth 2.1 授權伺服器的端點（給 MCP 用戶端用）。規格見 <c>internal/HANDOFF-mcp.md</c> 第 3、4 節。
/// 協定端點在 <see cref="OAuthProtocolEndpoints"/>，同意頁與「我的帳號」的端點在 <see cref="OAuthAccountEndpoints"/>。
/// </summary>
internal static class OAuthEndpoints
{
    public static IServiceCollection AddOAuthServer(this IServiceCollection services)
    {
        services.AddSingleton<OAuthServerResolver>();
        return services;
    }

    /// <summary>標準路徑上的端點（不在 /v1 底下、不用登入）：授權伺服器的中繼資料、token、動態註冊、撤銷。</summary>
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app) => OAuthProtocolEndpoints.Map(app);

    /// <summary>要登入的（只能用 Cookie）：同意頁的檢查、允許、拒絕，與「我的帳號」的授權清單。</summary>
    public static RouteGroupBuilder MapOAuthAccountEndpoints(this RouteGroupBuilder v1) => OAuthAccountEndpoints.Map(v1);
}
