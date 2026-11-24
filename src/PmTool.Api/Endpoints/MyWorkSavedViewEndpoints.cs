using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 「我的工作」的檢視：不屬於任何專案。內文、回應與錯誤代碼和專案的檢視一樣（沒有專案找不到的錯誤）。
/// 和專案的檢視一樣刻意不宣告權杖的範圍：這是網頁上的個人偏好，權杖用不到。
/// </summary>
internal static class MyWorkSavedViewEndpoints
{
    private const string Collection = "/my-work/saved-views";

    public static RouteGroupBuilder MapMyWorkSavedViewEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Collection, ListAsync).WithName("ListMyWorkSavedViews");
        v1.MapPost(Collection, CreateAsync).WithName("CreateMyWorkSavedView");
        v1.MapPut(Collection + "/{viewId:guid}", ReplaceAsync).WithName("ReplaceMyWorkSavedView");
        v1.MapDelete(Collection + "/{viewId:guid}", DeleteAsync).WithName("DeleteMyWorkSavedView");
        return v1;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, [FromServices] ISavedViewService views, CancellationToken cancellationToken)
    {
        var listed = await views.ListMyWorkAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(listed.Select(SavedViewData.From).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        JsonBody<SavedViewRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ISavedViewService views,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await views.CreateMyWorkAsync(SessionPrincipal.Read(principal).ToActor(), request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created($"/v1{Collection}/{result.Value.Id}", SavedViewData.From(result.Value));
    }

    private static async Task<IResult> ReplaceAsync(
        Guid viewId,
        JsonBody<SavedViewRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ISavedViewService views,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await views.ReplaceMyWorkAsync(SessionPrincipal.Read(principal).ToActor(), viewId, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(SavedViewData.From(result.Value));
    }

    private static async Task<IResult> DeleteAsync(
        Guid viewId, ClaimsPrincipal principal, [FromServices] ISavedViewService views, CancellationToken cancellationToken)
    {
        var result = await views.DeleteMyWorkAsync(SessionPrincipal.Read(principal).ToActor(), viewId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }
}
