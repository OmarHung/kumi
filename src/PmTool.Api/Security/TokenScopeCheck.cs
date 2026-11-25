using Microsoft.AspNetCore.Authorization;
using PmTool.Api.Responses;
using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 用存取權杖呼叫需要登入的端點時，端點要宣告（<see cref="TokenScopeRequirement"/>）而且權杖有其中一個範圍，
/// 否則 403 <c>auth.insufficient_scope</c>。沒有宣告的端點一律不給權杖用。
/// 不需要登入的端點（登入、健康檢查、兩個驗證端點）不看權杖。
/// <para>
/// 帶 <c>agent:run</c> 的權杖（本機代理的）是例外中的例外：它會交給成員電腦上的 AI 引擎，所以只能打宣告了
/// <see cref="AccessTokenScope.AgentRun"/> 的端點（<c>/v1/agent/*</c>）與任何權杖都可以的端點；
/// 其他端點就算權杖另外帶的範圍符合宣告也一律拒絕。引擎要做的事只走 <c>/mcp</c>，那裡另有工具清單的上限。
/// </para>
/// </summary>
internal static class TokenScopeCheck
{
    private const string WebOnlyMessage = "這個操作只能在網頁上做，存取權杖不能用。";

    private const string AgentTokenMessage = "這把權杖是給本機代理用的，只能領工作與連 MCP；其他操作請用命令列的權杖。";

    public static IApplicationBuilder UseTokenScopeCheck(this IApplicationBuilder app) => app.Use(CheckAsync);

    private static Task CheckAsync(HttpContext context, RequestDelegate next)
    {
        if (SessionPrincipal.TryReadAccessToken(context.User) is not { } token
            || context.GetEndpoint() is not { } endpoint
            || !RequiresSignIn(endpoint))
        {
            return next(context);
        }

        var requirement = endpoint.Metadata.GetMetadata<TokenScopeRequirement>();
        if (requirement is null)
        {
            return Deny(context, WebOnlyMessage);
        }

        if (token.Scopes.Contains(AccessTokenScope.AgentRun) && !requirement.AllowsAnyToken && !requirement.AnyOf.Contains(AccessTokenScope.AgentRun))
        {
            return Deny(context, AgentTokenMessage);
        }

        return requirement.AllowsAnyToken || requirement.AnyOf.Any(token.Scopes.Contains)
            ? next(context)
            : Deny(context, MessageFor(requirement));
    }

    private static bool RequiresSignIn(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAuthorizeData>() is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;

    private static string MessageFor(TokenScopeRequirement requirement) =>
        requirement.AnyOf.Count == 1
            ? $"這個操作需要權杖有 {AccessTokenScopes.NameOf(requirement.AnyOf[0])} 範圍。"
            : $"這個操作需要權杖有 {string.Join(" 或 ", requirement.AnyOf.Select(AccessTokenScopes.NameOf))} 其中一個範圍。";

    private static Task Deny(HttpContext context, string message) =>
        ApiFailure.InsufficientScope(message).WriteAsync(context.Response, context.RequestAborted);
}
