using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

internal sealed record SignInRequest(string Email, string Password);

/// <summary>回給用戶端的使用者。帳號編號只給伺服器用，不放進來。</summary>
internal sealed record UserData(
    Guid PersonId, string DisplayName, string Email, Guid OrganizationId, string OrganizationName, OrganizationRole Role)
{
    public static UserData From(AuthenticatedUser user) => new(
        user.PersonId, user.DisplayName, user.Email, user.OrganizationId, user.OrganizationName, user.Role);
}

/// <param name="Scopes">
/// 用存取權杖呼叫時是那把權杖的範圍（kumi 用它分辨存的是命令列的權杖還是本機代理的，見 internal/HANDOFF-setup-wizard.md 第 8 節）；
/// 網頁的工作階段沒有範圍的限制，是 null。
/// </param>
internal sealed record SessionData(UserData User, IReadOnlyList<string>? Scopes = null);

internal static class SessionEndpoints
{
    public const string Route = "/v1/session";

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Route, SignInAsync).WithName("SignIn").AllowAnonymous().RequireRateLimiting(ApiRateLimiting.SignInPolicy);
        app.MapGet(Route, GetSession).WithName("GetSession").RequireAuthorization().AllowAnyAccessToken();

        // 不需要登入：工作階段已經過期的網頁按登出，結果一樣是登出了，不必讓它看到錯誤。
        app.MapDelete(Route, SignOutAsync).WithName("SignOut").AllowAnonymous();
        return app;
    }

    private static async Task<IResult> SignInAsync(
        JsonBody<SignInRequest> body,
        HttpContext context,
        [FromServices] ISessionService sessions,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var failure))
        {
            return failure.ToResult();
        }

        // 驗證密碼與開工作階段在身分模組裡是同一件事：驗證的同時密碼被重設，就不會多出一個工作階段。
        if (await sessions.SignInAsync(request.Email, request.Password, cancellationToken) is not { } signedIn)
        {
            return ApiFailure.InvalidCredentials.ToResult();
        }

        // 這個瀏覽器原本的工作階段會被新的 Cookie 蓋掉，再也用不到，順手作廢，不讓它留到閒置期限。
        if (SessionPrincipal.TryReadToken(context.User) is { } previous)
        {
            await sessions.EndAsync(previous, cancellationToken);
        }

        var (user, session) = signedIn;
        await context.SignInAsync(
            SessionPrincipal.Scheme, SessionPrincipal.Create(user, session.Token), SessionPrincipal.NewProperties(session.ExpiresAt));
        return Results.Ok(ApiEnvelope.Ok(new SessionData(UserData.From(user))));
    }

    private static IResult GetSession(ClaimsPrincipal principal) =>
        Results.Ok(ApiEnvelope.Ok(new SessionData(
            UserData.From(SessionPrincipal.Read(principal)),
            SessionPrincipal.TryReadAccessToken(principal) is { } token ? [.. token.Scopes.Select(AccessTokenScopes.NameOf)] : null)));

    /// <summary>
    /// 先在伺服器端作廢，再請瀏覽器刪掉 Cookie：有人事先複製了這份 Cookie 也用不了。
    /// 作廢不跟著請求取消：用戶端送出登出就斷線（例如馬上關掉分頁）時，回應送不到、Cookie 還留著，
    /// 伺服器端更要作廢，否則他以為登出了，其實還登入著。
    /// 已經失效的工作階段在確認登入時就被擋下，這裡讀不到權杖，也沒有東西要作廢。
    /// </summary>
    private static async Task<IResult> SignOutAsync(HttpContext context, [FromServices] ISessionService sessions)
    {
        if (SessionPrincipal.TryReadToken(context.User) is { } token)
        {
            await sessions.EndAsync(token, CancellationToken.None);
        }

        await context.SignOutAsync(SessionPrincipal.Scheme);
        return Results.Ok(ApiEnvelope.Ok<object?>(null));
    }
}
