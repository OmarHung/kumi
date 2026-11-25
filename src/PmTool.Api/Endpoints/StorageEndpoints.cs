using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Security;
using PmTool.Storage;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 儲存空間的用量。只能用 Cookie 登入讀（沒有宣告權杖的範圍）；誰能讀由 <see cref="IStorageQuota"/> 判斷，只有組織管理員。
/// </summary>
internal static class StorageEndpoints
{
    public const string Route = "/storage-usage";

    public static RouteGroupBuilder MapStorageEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Route, ReadAsync).WithName("ReadStorageUsage");
        return v1;
    }

    private static async Task<IResult> ReadAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IStorageQuota quota,
        CancellationToken cancellationToken)
    {
        // 用量只有管理員要看，也不該被瀏覽器或中間的代理留著。
        context.Response.Headers.CacheControl = "no-store";
        var result = await quota.ReportAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(StorageUsageData.From(result.Value));
    }
}
