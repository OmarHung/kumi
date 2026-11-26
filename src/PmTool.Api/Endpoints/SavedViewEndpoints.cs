using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 條件的 JSON 形狀，和網址上的篩選同一套名稱；搜尋文字叫 q。每個都是選填，請求沒寫的當成沒設，回應沒設的是 null。
/// </summary>
internal sealed record SavedViewQueryData(
    string? Type = null,
    string? State = null,
    string? Assignee = null,
    string? Q = null,
    string? Sort = null,
    string? Due = null,
    string? Priority = null,
    string? Cycle = null,
    string? Milestone = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? Group = null,
    string? Category = null,
    string? Project = null,
    string? CreatedBy = null,
    string? Mine = null,
    bool? Open = null,
    bool? Stale = null)
{
    public static SavedViewQueryData From(SavedViewQuery query) => new(
        query.Type, query.State, query.Assignee, query.Search, query.Sort, query.Due, query.Priority, query.Cycle, query.Milestone,
        query.Fields, query.Group, query.Category, query.Project, query.CreatedBy, query.Mine, query.Open, query.Stale);

    public SavedViewQuery ToQuery() => new(Type, State, Assignee, Q, Sort, Due, Priority, Cycle, Milestone, Fields, Group, Category, Project, CreatedBy, Mine, Open, Stale);
}

/// <summary>新增與整個換掉用的內文。</summary>
internal sealed record SavedViewRequest(string Name, SavedViewLayout Layout, SavedViewQueryData Query)
{
    public SavedViewDraft ToDraft() => new(Name, Layout, Query.ToQuery());
}

internal sealed record SavedViewData(
    Guid Id, string Name, SavedViewLayout Layout, SavedViewQueryData Query, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static SavedViewData From(SavedView view) => new(
        view.Id, view.Name, view.Layout, SavedViewQueryData.From(view.Query), view.CreatedAt, view.UpdatedAt);
}

/// <summary>
/// 個人的檢視。刻意不宣告權杖的範圍：沒有宣告的端點一律不給權杖用，檢視是網頁上的個人偏好，
/// 命令列與自動化用不到，也就不必替它們多開一個可以改使用者設定的範圍。
/// </summary>
internal static class SavedViewEndpoints
{
    private const string Collection = "/projects/{projectKey}/saved-views";

    public static RouteGroupBuilder MapSavedViewEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Collection, ListAsync).WithName("ListSavedViews");
        v1.MapPost(Collection, CreateAsync).WithName("CreateSavedView");
        v1.MapPut(Collection + "/{viewId:guid}", ReplaceAsync).WithName("ReplaceSavedView");
        v1.MapDelete(Collection + "/{viewId:guid}", DeleteAsync).WithName("DeleteSavedView");
        return v1;
    }

    private static async Task<IResult> ListAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] ISavedViewService views, CancellationToken cancellationToken)
    {
        var result = await views.ListAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(result.Value.Select(SavedViewData.From).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        string projectKey,
        JsonBody<SavedViewRequest> body,
        ClaimsPrincipal principal,
        [FromServices] ISavedViewService views,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await views.CreateAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                $"/v1/projects/{Uri.EscapeDataString(projectKey)}/saved-views/{result.Value.Id}", SavedViewData.From(result.Value));
    }

    private static async Task<IResult> ReplaceAsync(
        string projectKey,
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

        var result = await views.ReplaceAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, viewId, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(SavedViewData.From(result.Value));
    }

    private static async Task<IResult> DeleteAsync(
        string projectKey,
        Guid viewId,
        ClaimsPrincipal principal,
        [FromServices] ISavedViewService views,
        CancellationToken cancellationToken)
    {
        var result = await views.DeleteAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, viewId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }
}
