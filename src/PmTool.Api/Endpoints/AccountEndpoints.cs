using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.RateLimiting;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <param name="RevokeAccessTokens">不給或 null 都當 true：改密碼預設連個人存取權杖一起撤銷。</param>
internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, bool? RevokeAccessTokens = null);

/// <param name="CurrentPassword">目前的密碼：沒有密碼的話，偷到 Cookie 的人可以一直把本人踢出去。</param>
internal sealed record EndOtherSessionsRequest(string CurrentPassword);

/// <param name="SignedOutSessions">因為這次操作而登出的其他工作階段數。</param>
internal sealed record SignedOutSessionsData(int SignedOutSessions);

/// <param name="SignedOutSessions">因為改了密碼而登出的其他工作階段數。</param>
/// <param name="RevokedAccessTokens">撤銷的個人存取權杖數。</param>
internal sealed record PasswordChangedData(int SignedOutSessions, int RevokedAccessTokens);

/// <summary>自己的帳號。用這個請求的工作階段權杖認人，身分模組才知道改完之後要留下哪一個工作階段。</summary>
internal static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder v1)
    {
        // 依人另外限流：拿著別人工作階段的人，不能用它無限次猜目前的密碼。不和登入共用依 IP 的額度，理由見 ApiLimits。
        v1.MapPut("/account/password", ChangePasswordAsync)
            .WithName("ChangeOwnPassword")
            .RequireRateLimiting(ApiRateLimiting.PasswordChangePolicy);

        // 一樣要目前的密碼、一樣依人限流；它是動作而不是刪除一個資源，所以是 POST。
        v1.MapPost("/account/other-sessions/sign-out", EndOtherSessionsAsync)
            .WithName("EndOtherSessions")
            .RequireRateLimiting(ApiRateLimiting.PasswordChangePolicy);
        return v1;
    }

    private static async Task<IResult> ChangePasswordAsync(
        HttpContext context,
        JsonBody<ChangePasswordRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAccountService accounts,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await accounts.ChangeOwnPasswordAsync(
            SessionPrincipal.ReadToken(principal), request.CurrentPassword, request.NewPassword,
            request.RevokeAccessTokens ?? true, cancellationToken);
        if (result.Failure is null)
        {
            // 目前這個工作階段已經換成新的：Cookie 要換上新的權杖，不然這個瀏覽器下一個請求就是 401。
            await SignInWithAsync(context, principal, result.Value.Session);
        }

        return result.Failure switch
        {
            null => EndpointResults.Ok(new PasswordChangedData(result.Value.SignedOutSessions, result.Value.RevokedAccessTokens)),

            // 確認登入之後、改密碼之前，這個工作階段被作廢了（別的裝置按了「登出其他裝置」、密碼被重設）。
            // 和一開始就沒登入一樣回 401，網頁才會回登入頁，而不是顯示「沒有權限」。
            { Code: IdentityErrorCodes.SessionEnded } => ApiFailure.AuthRequired.ToResult(),
            var failure => EndpointResults.Fail(failure),
        };
    }

    private static async Task<IResult> EndOtherSessionsAsync(
        HttpContext context,
        JsonBody<EndOtherSessionsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ISessionService sessions,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await sessions.EndOthersAsync(SessionPrincipal.ReadToken(principal), request.CurrentPassword ?? string.Empty, cancellationToken);
        if (result.Failure is null)
        {
            await SignInWithAsync(context, principal, result.Value.Session);
        }

        return result.Failure switch
        {
            null => EndpointResults.Ok(new SignedOutSessionsData(result.Value.SignedOutSessions)),

            // 確認登入之後，這個工作階段被作廢了：和沒登入一樣回 401。
            { Code: IdentityErrorCodes.SessionEnded } => ApiFailure.AuthRequired.ToResult(),
            var failure => EndpointResults.Fail(failure),
        };
    }

    /// <summary>
    /// 換上新的工作階段權杖，期限用伺服器給的絕對期限。同一個請求就算已經被標了要續發票證，
    /// 端點呼叫過登入之後 Cookie 處理常式不會再用舊的票證覆寫。
    /// </summary>
    private static Task SignInWithAsync(HttpContext context, ClaimsPrincipal principal, StartedSession session) =>
        context.SignInAsync(
            SessionPrincipal.Scheme,
            SessionPrincipal.Create(SessionPrincipal.Read(principal), session.Token),
            SessionPrincipal.NewProperties(session.ExpiresAt));
}
