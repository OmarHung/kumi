using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 主機 AI 代理的 worker 端點（<c>kumi host run</c>／<c>pair</c>／<c>login</c>，internal/HANDOFF-host-agent.md 第 5.2 節）。
/// 不在需要登入的那一組底下：身分是機器憑證（<c>Authorization: Bearer kha_…</c>），由 <see cref="HostCredentials"/> 驗證，
/// 不是任何人的登入，Cookie 與個人存取權杖在這裡一律沒用。配對是唯一不用憑證的（用配對碼換憑證）。
/// 防偽標頭不適用：這些端點只認明確放進標頭的憑證，瀏覽器不會自己帶。
/// </summary>
internal static class HostAgentWorkerEndpoints
{
    /// <summary>worker 的請求內文上限（位元組）：回報一批最多 200 則、每則文字最多 8000 字，加上 JSON 的結構。</summary>
    internal const int MaxBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapHostAgentWorkerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup($"{ApiRoutes.Prefix}/host-agent")
            .WithJsonBodyLimit(MaxBodyBytes)
            .ExemptFromCsrfCheck();

        group.MapPost("/pair", PairAsync)
            .WithName("HostAgentPair")
            .WithMetadata(new HostAgentEndpoint(RequiresCredential: false))
            .RequireRateLimiting(ApiRateLimiting.HostPairPolicy);

        var authenticated = group.MapGroup(string.Empty)
            .WithMetadata(new HostAgentEndpoint(RequiresCredential: true))
            .AddEndpointFilter(HostCredentials.RequireAgentAsync);
        authenticated.MapPost("/check-in", CheckInAsync).WithName("HostAgentCheckIn");
        authenticated.MapPost("/jobs/{jobId:guid}/events", ReportAsync).WithName("HostAgentReport");
        authenticated.MapPost("/jobs/{jobId:guid}/finish", FinishAsync).WithName("HostAgentFinish");
        authenticated.MapGet("/jobs/{jobId:guid}/attachments/{attachmentId:guid}", DownloadAttachmentAsync).WithName("HostAgentDownloadAttachment");
        authenticated.MapPost("/logins/claim", ClaimLoginAsync).WithName("HostAgentClaimLogin");
        authenticated.MapPost("/logins/{loginId:guid}/complete", CompleteLoginAsync).WithName("HostAgentCompleteLogin");
        return app;
    }

    private static HostAgentIdentity AgentOf(HttpContext http) =>
        HostCredentials.TryGetAgent(http) ?? throw new InvalidOperationException("worker 端點沒有驗證過的機器憑證。");

    private static async Task<IResult> PairAsync(
        JsonBody<HostPairRequest> body,
        HttpContext http,
        [FromServices] IHostAgentWorkerService workers,
        [FromServices] BearerFailureLimiter failures,
        CancellationToken cancellationToken)
    {
        // 在查資料庫之前先看這個來源猜錯了幾次：配對碼錯也算失敗。
        var source = HostCredentials.AgentFailureSource(http);
        if (failures.IsBlocked(source, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ApiFailure.RateLimited.ToResult();
        }

        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await workers.PairAsync(request.ToRequest(), cancellationToken);
        if (result.Failure is { } failure)
        {
            if (failure.Code == AiErrorCodes.HostPairingInvalid)
            {
                failures.RecordFailure(source);
            }

            return EndpointResults.Fail(failure);
        }

        return EndpointResults.Ok(HostPairData.From(result.Value));
    }

    private static async Task<IResult> CheckInAsync(
        JsonBody<HostCheckInRequest> body, HttpContext http, [FromServices] IHostAgentWorkerService workers, CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (request.Capacity is < 0 or > HostCheckInRequest.MaxCapacity)
        {
            return EndpointResults.Fail(new Failure(FailureKind.Invalid, AiErrorCodes.HostAgentInvalid, $"capacity 要在 0 到 {HostCheckInRequest.MaxCapacity} 之間。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "capacity", "數字不在範圍內。")],
            });
        }

        if (request.FindNullElement() is { } nullElement)
        {
            return EndpointResults.Fail(nullElement);
        }

        // 長輪詢：呼叫端斷線時（RequestAborted）模組立刻回，什麼都不領。
        var result = await workers.CheckInAsync(AgentOf(http), request.ToCheckIn(), request.Capacity, cancellationToken);
        if (result.Failure?.Code == AiErrorCodes.HostMachineMismatch)
        {
            // 對外和憑證不對一樣（401）：不告訴對方是哪一項不對。
            return ApiFailure.AuthRequired.ToResult();
        }

        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HostCheckInData.From(result.Value));
    }

    private static async Task<IResult> ReportAsync(
        Guid jobId,
        JsonBody<HostEventsRequest> body,
        HttpContext http,
        [FromServices] IHostAgentWorkerService workers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var converted = LocalAgentEventRequest.ConvertAll(request.Events);
        if (converted.Failure is { } invalid)
        {
            return EndpointResults.Fail(HostWorkerFailures.AsHostInvalid(invalid));
        }

        var events = converted.Value;
        var result = await workers.ReportAsync(AgentOf(http), jobId, request.Lease, request.FirstSeq, events, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HostAckData.From(result.Value));
    }

    private static async Task<IResult> FinishAsync(
        Guid jobId,
        JsonBody<HostFinishRequest> body,
        HttpContext http,
        [FromServices] IHostAgentWorkerService workers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await workers.FinishAsync(AgentOf(http), jobId, request.Lease, request.ToFinish(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    /// <summary>租約放在查詢字串（<c>?lease=</c>）：GET 沒有內文，其他端點的租約在 JSON 裡。</summary>
    private static async Task<IResult> DownloadAttachmentAsync(
        Guid jobId,
        Guid attachmentId,
        [FromQuery] int? lease,
        HttpContext http,
        [FromServices] IHostAgentWorkerService workers,
        CancellationToken cancellationToken)
    {
        if (lease is not { } jobLease)
        {
            return EndpointResults.Fail(new Failure(FailureKind.Invalid, AiErrorCodes.HostAgentInvalid, "要帶 lease（領工作時給的租約）。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "lease", "必填。")],
            });
        }

        var result = await workers.OpenJobAttachmentAsync(AgentOf(http), jobId, jobLease, attachmentId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : AttachmentDownload.StreamForAgent(http.Response, result.Value);
    }

    private static async Task<IResult> ClaimLoginAsync(
        JsonBody<HostClaimRequest> body, HttpContext http, [FromServices] IHostAgentWorkerService workers, CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await workers.ClaimLoginAsync(AgentOf(http), request.Code, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HostClaimData.From(result.Value));
    }

    private static async Task<IResult> CompleteLoginAsync(
        Guid loginId,
        JsonBody<HostCompleteRequest> body,
        HttpContext http,
        [FromServices] IHostAgentWorkerService workers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await workers.CompleteLoginAsync(AgentOf(http), loginId, request.ToReport(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(HostCompleteData.From(result.Value));
    }
}
