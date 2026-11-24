using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using PmTool.Api.OAuth;

namespace PmTool.Api.Mcp;

/// <summary>
/// MCP 伺服器（Streamable HTTP、無狀態，2026-07-28 版規格）與受保護資源的中繼資料（RFC 9728）。
/// 規格見 <c>internal/HANDOFF-mcp.md</c> 第 5 節。
/// </summary>
internal static class McpEndpoints
{
    private static readonly string[] UnsupportedMethods = [HttpMethods.Get, HttpMethods.Delete];

    public static IServiceCollection AddMcpServerEndpoint(this IServiceCollection services)
    {
        // 授權伺服器那邊也註冊；兩邊誰先加都可以，issuer 與 MCP 網址只有這一個算法。
        services.TryAddSingleton<OAuthServerResolver>();
        services.AddHttpContextAccessor();
        services.AddSingleton<McpToolRateLimiter>();
        services.AddSingleton<McpToolRunner>();

        // 無狀態：沒有 initialize 的握手、沒有 session，每個請求獨立，多個程序之間不必黏著。工具與權限都不放在 session 裡。
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = McpInstructions.ServerName, Version = VersionOfAssembly() };
                options.ServerInstructions = McpInstructions.Text;
            })
            .WithHttpTransport(transport => transport.Stateless = true)
            .WithListToolsHandler((request, cancellationToken) =>
                request.Services!.GetRequiredService<McpToolRunner>().ListAsync(request, cancellationToken))
            .WithCallToolHandler((request, cancellationToken) =>
                request.Services!.GetRequiredService<McpToolRunner>().CallAsync(request, cancellationToken));
        return services;
    }

    public static IEndpointRouteBuilder MapMcpEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapMcp(OAuthServerResolver.McpPath).AddEndpointFilter(McpRequestGuard.FilterAsync);

        // 無狀態沒有 GET（伺服器主動推播）與 DELETE（結束 session），SDK 不會對應這兩個方法；
        // 不自己對應的話，網頁的後備會對 GET /mcp 回 index.html（200），用戶端分不出這個端點不收 GET。
        app.MapMethods(OAuthServerResolver.McpPath, UnsupportedMethods, MethodNotAllowed);

        app.MapGet($"{McpRequestGuard.ProtectedResourcePath}{OAuthServerResolver.McpPath}", ProtectedResourceMetadata.Respond);
        app.MapGet(McpRequestGuard.ProtectedResourcePath, ProtectedResourceMetadata.Respond);
        return app;
    }

    private static IResult MethodNotAllowed(HttpContext context)
    {
        context.Response.Headers.Allow = HttpMethods.Post;
        return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private static string VersionOfAssembly()
    {
        var assembly = typeof(McpEndpoints).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // 建置時帶的是「版本+提交編號」，只留版本。
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
