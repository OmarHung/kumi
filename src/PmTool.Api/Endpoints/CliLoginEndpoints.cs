using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.OAuth;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Ai;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>檢查與核准共用的請求內文（<c>approve</c> 多帶 <c>grantedScopes</c> 與 <c>expiresInDays</c>）。</summary>
internal sealed record CliLoginRequestBody(
    int Port,
    string State,
    string Challenge,
    string Machine,
    IReadOnlyList<string>? Tokens,
    IReadOnlyList<string>? Scopes,
    IReadOnlyList<string>? GrantedScopes = null,
    int? ExpiresInDays = null,
    string? ConfirmationCode = null);

internal sealed record CliLoginTokenRequest(string Code, string CodeVerifier, IReadOnlyList<string>? ReplaceTokens = null);

internal sealed record CliLoginCheckData(
    string MachineName,
    IReadOnlyList<string> Tokens,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AgentScopes,
    bool AgentAllowed,
    AuthorizationAccountData Account);

/// <param name="Cli">沒要或沒建的是 null。</param>
internal sealed record CliLoginTokensData(
    AuthorizationAccountData Account,
    CreatedAccessTokenData? Cli,
    CreatedAccessTokenData? Agent,
    bool AgentSkipped);

/// <summary>
/// 命令列（kumi）用瀏覽器登入。<c>check</c> 與 <c>approve</c> 在網頁的同意頁用，只能用 Cookie（沒有權杖範圍的宣告）：
/// 拿到一把權杖的人不能替自己開出更多權杖。<c>token</c> 是 kumi 自己兌換，不用登入、不用防偽標頭，靠一次性的碼與 PKCE。
/// 規格見 <c>internal/HANDOFF-setup-wizard.md</c> 第 3 節。
/// </summary>
internal static class CliLoginEndpoints
{
    public const string TokenRoute = ApiRoutes.Prefix + "/cli-login/token";

    private static readonly IReadOnlyList<string> AgentScopeNames = [.. CliLoginRules.AgentScopes.Select(AccessTokenScopes.NameOf)];

    public static RouteGroupBuilder MapCliLoginEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapPost("/cli-login/check", CheckAsync)
            .RequireRateLimiting(ApiRateLimiting.CliLoginCheckPolicy)
            .WithJsonBodyLimit(ApiLimits.MaxOAuthBodyBytes)
            .WithName("CheckCliLogin");
        v1.MapPost("/cli-login/approve", ApproveAsync)
            .RequireRateLimiting(ApiRateLimiting.CliLoginApprovePolicy)
            .WithJsonBodyLimit(ApiLimits.MaxOAuthBodyBytes)
            .WithName("ApproveCliLogin");
        return v1;
    }

    /// <summary>不用登入，所以不在 <see cref="ApiRoutes.MapSignedInEndpoints"/> 的那一組底下。</summary>
    public static IEndpointRouteBuilder MapCliLoginTokenEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost(TokenRoute, TokenAsync)
            .ExemptFromCsrfCheck()
            .RequireRateLimiting(ApiRateLimiting.CliLoginTokenPolicy)
            .WithJsonBodyLimit(ApiLimits.MaxOAuthBodyBytes)
            .WithName("RedeemCliLogin");
        return app;
    }

    private static async Task<IResult> CheckAsync(
        HttpContext context,
        JsonBody<CliLoginRequestBody> body,
        ClaimsPrincipal principal,
        [FromServices] ICliLoginService login,
        [FromServices] IAiOptionsService aiOptions,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (Parse(request) is not { } parameters)
        {
            return UnknownName.ToResult();
        }

        var user = SessionPrincipal.Read(principal);
        var actor = user.ToActor();
        var result = await login.CheckAsync(actor, parameters, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var options = await aiOptions.GetAsync(actor, cancellationToken);
        var found = result.Value;
        return EndpointResults.Ok(new CliLoginCheckData(
            found.MachineName,
            [.. found.Tokens.Select(TokenKindName)],
            [.. found.Scopes.Select(AccessTokenScopes.NameOf)],
            AgentScopeNames,
            options.CanAddPersonal,
            new AuthorizationAccountData(user.DisplayName, user.Email, user.OrganizationName)));
    }

    private static async Task<IResult> ApproveAsync(
        HttpContext context,
        JsonBody<CliLoginRequestBody> body,
        ClaimsPrincipal principal,
        [FromServices] ICliLoginService login,
        [FromServices] IAiOptionsService aiOptions,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (Parse(request) is not { } parameters || ParseScopes(request.GrantedScopes) is not { } granted)
        {
            return UnknownName.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var options = await aiOptions.GetAsync(actor, cancellationToken);
        var grant = new CliLoginGrant(granted, options.CanAddPersonal, request.ExpiresInDays, CliLoginOrigin.KeyOf(context.Connection.RemoteIpAddress))
        {
            ConfirmationCode = request.ConfirmationCode,
        };
        var result = await login.ApproveAsync(actor, parameters, grant, cancellationToken);

        // 確認登入之後，發出要求的工作階段被作廢了（改密碼、別的程序登出）：和沒登入一樣回 401。
        return result.Failure switch
        {
            null => EndpointResults.Ok(new RedirectData(result.Value.RedirectUri)),
            { Code: IdentityErrorCodes.SessionEnded } => ApiFailure.AuthRequired.ToResult(),
            var failure => EndpointResults.Fail(failure),
        };
    }

    private static async Task<IResult> TokenAsync(
        HttpContext context,
        JsonBody<CliLoginTokenRequest> body,
        [FromServices] ICliLoginService login,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var redemption = new CliLoginRedemption(request.Code ?? string.Empty, request.CodeVerifier ?? string.Empty, request.ReplaceTokens ?? [], CliLoginOrigin.KeyOf(context.Connection.RemoteIpAddress));
        var result = await login.RedeemAsync(redemption, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var tokens = result.Value;
        var account = new AuthorizationAccountData(tokens.Account.DisplayName, tokens.Account.Email, tokens.Account.OrganizationName);
        return EndpointResults.Ok(new CliLoginTokensData(account, Created(tokens.Cli), Created(tokens.Agent), tokens.AgentSkipped));
    }

    private static CreatedAccessTokenData? Created(CreatedAccessToken? created) =>
        created is null ? null : new CreatedAccessTokenData(AccessTokenData.From(created.Token), created.Secret);

    // 回應有只出現一次的明文（token）或一次性的碼（approve），不能留在瀏覽器或中間的快取。
    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    private static ApiFailure UnknownName => ApiFailure.BadRequest with
    {
        Details = [new ApiErrorDetail(ApiErrorCodes.BadRequest, "tokens", "有不認得的權杖種類或範圍名稱。")],
    };

    private static string TokenKindName(CliLoginTokenKind kind) => kind == CliLoginTokenKind.Agent ? "agent" : "cli";

    /// <summary>有不認得的名稱回 null；重複、空的等規則交給身分模組，兩層的規則才不會分岔。</summary>
    private static CliLoginParameters? Parse(CliLoginRequestBody request)
    {
        var kinds = new List<CliLoginTokenKind>();
        foreach (var name in request.Tokens ?? [])
        {
            switch (name)
            {
                case "cli":
                    kinds.Add(CliLoginTokenKind.Cli);
                    break;
                case "agent":
                    kinds.Add(CliLoginTokenKind.Agent);
                    break;
                default:
                    return null;
            }
        }

        return ParseScopes(request.Scopes) is { } scopes
            ? new CliLoginParameters(request.Port, request.State ?? string.Empty, request.Challenge ?? string.Empty, request.Machine ?? string.Empty, kinds, scopes)
            : null;
    }

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
