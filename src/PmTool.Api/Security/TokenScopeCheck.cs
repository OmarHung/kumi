using Microsoft.AspNetCore.Authorization;
using PmTool.Api.Responses;
using PmTool.Identity;

namespace PmTool.Api.Security;

/// <summary>
/// 用存取權杖呼叫需要登入的端點時，端點要宣告（<see cref="TokenScopeRequirement"/>）而且權杖有其中一個範圍，
/// 否則 403 <c>auth.insufficient_scope</c>。沒有宣告的端點一律不給權杖用。
/// 不需要登入的端點（登入、健康檢查、兩個驗證端點）不看權杖。
/// </summary>
internal static class TokenScopeCheck
{
    private const string WebOnlyMessage = "這個操作只能在網頁上做，存取權杖不能用。";

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
