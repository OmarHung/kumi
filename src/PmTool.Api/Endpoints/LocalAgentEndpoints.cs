using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 本機代理（<c>kumi agent start</c>）的端點與「我的帳號」的電腦清單（internal/HANDOFF-local-agent.md 第 4.4 節）。
/// 本機代理的三個端點只收存取權杖（範圍 <c>agent:run</c>），Cookie 的工作階段回 403：本機代理的身分要清楚，
/// 網頁自己不該代它領工作或回報。電腦清單是網頁的，只能用 Cookie（不宣告範圍，權杖不能用）。
/// </summary>
internal static class LocalAgentEndpoints
{
    /// <summary>本機代理的請求內文上限（位元組）：回報一批最多 200 則、每則文字最多 8000 字，加上 JSON 的結構。</summary>
    internal const int MaxBodyBytes = 1024 * 1024;

    private const string CookieNotAllowedMessage =
        "這個端點只給本機代理用（kumi agent start），要用範圍 agent:run 的存取權杖，不能用網頁的登入。";

    public static RouteGroupBuilder MapLocalAgentEndpoints(this RouteGroupBuilder v1)
    {
        var agent = v1.MapGroup("/agent")
            .RequireTokenScope(AccessTokenScope.AgentRun)
            .RequireRateLimiting(ApiRateLimiting.LocalAgentPolicy)
            .WithJsonBodyLimit(MaxBodyBytes)
            .AddEndpointFilter(RequireTokenAsync);
        agent.MapPost("/check-in", CheckInAsync).WithName("LocalAgentCheckIn");
        agent.MapPost("/jobs/{jobId:guid}/events", ReportAsync).WithName("LocalAgentReport");
        agent.MapPost("/jobs/{jobId:guid}/finish", FinishAsync).WithName("LocalAgentFinish");
        agent.MapGet("/jobs/{jobId:guid}/attachments/{attachmentId:guid}", DownloadAttachmentAsync).WithName("LocalAgentDownloadAttachment");

        v1.MapGet("/account/local-agents", ListAsync).WithName("ListLocalAgents");
        v1.MapDelete("/account/local-agents/{machineId:guid}", RemoveAsync).WithName("RemoveLocalAgent");
        return v1;
    }

    private static async ValueTask<object?> RequireTokenAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (SessionPrincipal.TryReadAccessToken(context.HttpContext.User) is null)
        {
            return ApiFailure.InsufficientScope(CookieNotAllowedMessage).ToResult();
        }

        return await next(context);
    }

    private static async Task<IResult> CheckInAsync(
        JsonBody<LocalAgentCheckInRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ILocalAgentService agents,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (request.Capacity is < 0 or > LocalAgentCheckInRequest.MaxCapacity)
        {
            return EndpointResults.Fail(new Failure(FailureKind.Invalid, AiErrorCodes.AgentInvalid, $"capacity 要在 0 到 {LocalAgentCheckInRequest.MaxCapacity} 之間。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "capacity", "數字不在範圍內。")],
            });
        }

        if (request.FindNullElement() is { } nullElement)
        {
            return EndpointResults.Fail(nullElement);
        }

        // 長輪詢：呼叫端斷線時（RequestAborted）模組立刻回，什麼都不領。
        var result = await agents.CheckInAsync(
            SessionPrincipal.Read(principal).ToActor(), request.ToCheckIn(), request.Capacity, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(LocalAgentCheckInData.From(result.Value));
    }

    private static async Task<IResult> ReportAsync(
        Guid jobId,
        JsonBody<LocalAgentEventsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ILocalAgentService agents,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var converted = LocalAgentEventRequest.ConvertAll(request.Events);
        if (converted.Failure is { } invalid)
        {
            return EndpointResults.Fail(invalid);
        }

        var events = converted.Value;
        var result = await agents.ReportAsync(SessionPrincipal.Read(principal).ToActor(), jobId, events, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new LocalAgentAckData(result.Value.Cancelled));
    }

    private static async Task<IResult> FinishAsync(
        Guid jobId,
        JsonBody<LocalAgentFinishRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ILocalAgentService agents,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await agents.FinishAsync(SessionPrincipal.Read(principal).ToActor(), jobId, request.ToFinish(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static async Task<IResult> DownloadAttachmentAsync(
        Guid jobId,
        Guid attachmentId,
        HttpContext http,
        ClaimsPrincipal principal,
        [FromServices] ILocalAgentService agents,
        CancellationToken cancellationToken)
    {
        var result = await agents.OpenJobAttachmentAsync(SessionPrincipal.Read(principal).ToActor(), jobId, attachmentId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : AttachmentDownload.StreamForAgent(http.Response, result.Value);
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, [FromServices] ILocalAgentDirectory directory, CancellationToken cancellationToken)
    {
        var machines = await directory.ListAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(machines.Select(LocalAgentMachineData.From).ToList());
    }

    private static async Task<IResult> RemoveAsync(
        Guid machineId, ClaimsPrincipal principal, [FromServices] ILocalAgentDirectory directory, CancellationToken cancellationToken)
    {
        var result = await directory.RemoveAsync(SessionPrincipal.Read(principal).ToActor(), machineId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }
}
