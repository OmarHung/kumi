using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Endpoints;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.OAuth;

/// <summary>
/// 要登入的 OAuth 端點。一律只能用 Cookie（沒有權杖範圍的宣告）：拿到一把權杖的人不能再替自己授權更多應用。
/// 失敗統一是 422 <c>oauth.request_invalid</c>，<c>error.data</c> 放 OAuth 的錯誤代碼與要導回去的網址。
/// </summary>
internal static class OAuthAccountEndpoints
{
    public static RouteGroupBuilder Map(RouteGroupBuilder v1)
    {
        v1.MapPost("/oauth/authorization/check", CheckAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthCheckPolicy)
            .WithName("CheckOAuthAuthorization");
        v1.MapPost("/oauth/authorization/approve", ApproveAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthApprovePolicy)
            .WithName("ApproveOAuthAuthorization");
        v1.MapPost("/oauth/authorization/deny", DenyAsync)
            .RequireRateLimiting(ApiRateLimiting.OAuthCheckPolicy)
            .WithName("DenyOAuthAuthorization");
        v1.MapGet("/account/oauth-grants", ListGrantsAsync).WithName("ListOAuthGrants");

        // 路徑參數用字串收，自己解析：不是 GUID 時和「沒有這筆授權」一樣回 oauth.grant_not_found。
        v1.MapDelete("/account/oauth-grants/{grantId}", RevokeGrantAsync).WithName("RevokeOAuthGrant");
        return v1;
    }

    private static async Task<IResult> CheckAsync(
        HttpContext context,
        JsonBody<AuthorizeRequestBody> body,
        ClaimsPrincipal principal,
        [FromServices] IOAuthService oauth,
        [FromServices] OAuthServerResolver servers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await oauth.CheckAuthorizationAsync(servers.Resolve(context.Request), request.ToParameters(), cancellationToken);
        if (result.Failure is { } failure)
        {
            return ToResult(failure);
        }

        var user = SessionPrincipal.Read(principal);
        var found = result.Value;
        return EndpointResults.Ok(new AuthorizationCheckData(
            found.ClientId,
            found.ClientName,
            found.ClientHost,
            found.RedirectHost,
            found.RedirectIsLoopback,
            [.. found.RequestedScopes.Select(AccessTokenScopes.NameOf)],
            new AuthorizationAccountData(user.DisplayName, user.Email, user.OrganizationName)));
    }

    private static async Task<IResult> ApproveAsync(
        HttpContext context,
        JsonBody<ApproveRequestBody> body,
        ClaimsPrincipal principal,
        [FromServices] IOAuthService oauth,
        [FromServices] OAuthServerResolver servers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (ParseScopes(request.Scopes) is not { } scopes)
        {
            return ToResult(new OAuthFailure(OAuthError.InvalidScope, "有不認得的範圍名稱。"));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await oauth.ApproveAsync(servers.Resolve(context.Request), actor, request.ToParameters(), scopes, cancellationToken);
        return result.Failure is { } failure ? ToResult(failure) : EndpointResults.Ok(new RedirectData(result.Value));
    }

    private static async Task<IResult> DenyAsync(
        HttpContext context,
        JsonBody<AuthorizeRequestBody> body,
        [FromServices] IOAuthService oauth,
        [FromServices] OAuthServerResolver servers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await oauth.DenyAsync(servers.Resolve(context.Request), request.ToParameters(), cancellationToken);
        return result.Failure is { } failure ? ToResult(failure) : EndpointResults.Ok(new RedirectData(result.Value));
    }

    private static async Task<IResult> ListGrantsAsync(
        HttpContext context, ClaimsPrincipal principal, [FromServices] IOAuthService oauth, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var actor = SessionPrincipal.Read(principal).ToActor();
        var grants = await oauth.ListGrantsAsync(actor, cancellationToken);
        return EndpointResults.Ok(grants.Select(OAuthGrantData.From).ToArray());
    }

    private static async Task<IResult> RevokeGrantAsync(
        string grantId, ClaimsPrincipal principal, [FromServices] IOAuthService oauth, CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await oauth.RevokeGrantAsync(actor, Guid.TryParse(grantId, out var id) ? id : Guid.Empty, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static IResult ToResult(OAuthFailure failure) => (ApiFailure.OAuthRequestInvalid(failure)).ToResult();

    /// <summary>沒給範圍時是空清單（由模組判斷「至少一個」）；有不認得的名稱回 null。</summary>
    private static IReadOnlyList<AccessTokenScope>? ParseScopes(IReadOnlyList<string>? names)
    {
        var scopes = new List<AccessTokenScope>();
        foreach (var name in names ?? [])
        {
            if (!AccessTokenScopes.TryParse(name, out var scope))
            {
                return null;
            }

            scopes.Add(scope);
        }

        return scopes;
    }
}
