using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.RateLimiting;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

internal sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <param name="SignedOutSessions">因為這次操作而登出的其他工作階段數。</param>
internal sealed record SignedOutSessionsData(int SignedOutSessions);

/// <summary>自己的帳號。用這個請求的工作階段權杖認人，身分模組才知道改完之後要留下哪一個工作階段。</summary>
internal static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder v1)
    {
        // 依人另外限流：拿著別人工作階段的人，不能用它無限次猜目前的密碼。不和登入共用依 IP 的額度，理由見 ApiLimits。
        v1.MapPut("/account/password", ChangePasswordAsync)
            .WithName("ChangeOwnPassword")
            .RequireRateLimiting(ApiRateLimiting.PasswordChangePolicy);
        v1.MapDelete("/account/other-sessions", EndOtherSessionsAsync).WithName("EndOtherSessions");
        return v1;
    }

    private static async Task<IResult> ChangePasswordAsync(
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
            SessionPrincipal.ReadToken(principal), request.CurrentPassword, request.NewPassword, cancellationToken);
        return result.Failure switch
        {
            null => EndpointResults.Ok(new SignedOutSessionsData(result.Value.SignedOutSessions)),

            // 確認登入之後、改密碼之前，這個工作階段被作廢了（別的裝置按了「登出其他裝置」、密碼被重設）。
            // 和一開始就沒登入一樣回 401，網頁才會回登入頁，而不是顯示「沒有權限」。
            { Code: IdentityErrorCodes.SessionEnded } => ApiFailure.AuthRequired.ToResult(),
            var failure => EndpointResults.Fail(failure),
        };
    }

    private static async Task<IResult> EndOtherSessionsAsync(
        ClaimsPrincipal principal, [FromServices] ISessionService sessions, CancellationToken cancellationToken)
    {
        var ended = await sessions.EndOthersAsync(SessionPrincipal.ReadToken(principal), cancellationToken);
        return EndpointResults.Ok(new SignedOutSessionsData(ended));
    }
}
