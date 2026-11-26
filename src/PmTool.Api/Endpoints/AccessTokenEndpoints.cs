using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

internal sealed record CreateAccessTokenRequest(string Name, IReadOnlyList<string> Scopes, int? ExpiresInDays);

/// <summary>權杖的資料。明文與雜湊都不在裡面，只有開頭幾個字讓人認得出是哪一把。</summary>
internal sealed record AccessTokenData(
    Guid TokenId,
    string Name,
    string DisplayPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    string? MachineName,
    string? CreatedFrom)
{
    public static AccessTokenData From(AccessTokenSummary token) => new(
        token.TokenId, token.Name, token.DisplayPrefix, [.. token.Scopes.Select(AccessTokenScopes.NameOf)],
        token.CreatedAt, token.LastUsedAt, token.ExpiresAt, token.MachineName, token.CreatedFrom);
}

/// <param name="Secret">權杖的明文，只在建立的這個回應裡出現一次。</param>
internal sealed record CreatedAccessTokenData(AccessTokenData Token, string Secret);

/// <summary>
/// 自己的存取權杖。管理權杖只能用 Cookie 登入做（沒有範圍的宣告）：拿到一把權杖的人不能再替自己開出更多權杖。
/// </summary>
internal static class AccessTokenEndpoints
{
    public static RouteGroupBuilder MapAccessTokenEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/account/tokens", ListAsync).WithName("ListAccessTokens");
        v1.MapPost("/account/tokens", CreateAsync).WithName("CreateAccessToken");

        // 路徑參數用字串收，自己解析：不是 GUID 時和「沒有這把權杖」一樣回 access_token.not_found，網頁只認一種 404。
        v1.MapDelete("/account/tokens/{tokenId}", RevokeAsync).WithName("RevokeAccessToken");

        // 一把權杖撤銷它自己（kumi auth logout --revoke）：CLI 手上只有權杖本身，清單與上面的撤銷只能用 Cookie。
        // 只會讓這一把失效，所以任何範圍都可以打（本機代理的 agent:run 也是）；字面的路徑比上面的參數優先。
        v1.MapDelete("/account/tokens/current", RevokeCurrentAsync).WithName("RevokeCurrentAccessToken").AllowAnyAccessToken();
        return v1;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ClaimsPrincipal principal, [FromServices] IAccessTokenService tokens, CancellationToken cancellationToken)
    {
        DisableCaching(context);
        var actor = SessionPrincipal.Read(principal).ToActor();
        var list = await tokens.ListAsync(actor, cancellationToken);
        return EndpointResults.Ok(list.Select(AccessTokenData.From).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        JsonBody<CreateAccessTokenRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAccessTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (ParseScopes(request.Scopes) is not { } scopes)
        {
            var unknownScope = ApiFailure.BadRequest with
            {
                Details = [new ApiErrorDetail(ApiErrorCodes.BadRequest, "scopes", "有不認得的範圍名稱。")],
            };
            return unknownScope.ToResult();
        }

        // 回應裡有只會出現一次的明文，不能留在瀏覽器或中間的快取。
        DisableCaching(context);
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await tokens.CreateAsync(actor, new NewAccessToken(request.Name, scopes, request.ExpiresInDays), cancellationToken);

        // 確認登入之後，發出要求的工作階段被作廢了（改密碼、別的程序登出）：和沒登入一樣回 401。
        return result.Failure switch
        {
            null => EndpointResults.Created(null, new CreatedAccessTokenData(AccessTokenData.From(result.Value.Token), result.Value.Secret)),
            { Code: IdentityErrorCodes.SessionEnded } => ApiFailure.AuthRequired.ToResult(),
            var failure => EndpointResults.Fail(failure),
        };
    }

    private static async Task<IResult> RevokeCurrentAsync(
        ClaimsPrincipal principal, [FromServices] IAccessTokenService tokens, CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();

        // 用 Cookie 登入的請求沒有「這一把」：和不存在的權杖一樣回 not_found，不撤銷任何東西。
        var tokenId = SessionPrincipal.TryReadAccessToken(principal)?.TokenId ?? Guid.Empty;
        var result = await tokens.RevokeAsync(actor, tokenId, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(AccessTokenData.From(result.Value));
    }

    private static async Task<IResult> RevokeAsync(
        string tokenId, ClaimsPrincipal principal, [FromServices] IAccessTokenService tokens, CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await tokens.RevokeAsync(actor, Guid.TryParse(tokenId, out var id) ? id : Guid.Empty, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(AccessTokenData.From(result.Value));
    }

    private static void DisableCaching(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    /// <summary>有不認得的名稱回 null。重複的名稱照樣交給身分模組判斷，兩層的規則才不會分岔。</summary>
    private static IReadOnlyList<AccessTokenScope>? ParseScopes(IReadOnlyList<string> names)
    {
        var scopes = new List<AccessTokenScope>(names.Count);
        foreach (var name in names)
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
