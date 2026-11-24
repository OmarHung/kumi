using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>復原刪除、已刪除的清單與批次操作（PM-4a）。單一項目的規則在工作引擎，批次是把同一套規則一筆一筆套用。</summary>
internal static class ItemRecoveryEndpoints
{
    public static RouteGroupBuilder MapItemRecoveryEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/projects/{projectKey}/items/deleted", ListDeletedAsync)
            .WithName("ListDeletedItems")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/items/{itemKey}/restore", RestoreItemAsync)
            .WithName("RestoreItem")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/projects/{projectKey}/items/bulk-update", BulkUpdateAsync)
            .WithName("BulkUpdateItems")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/projects/{projectKey}/items/bulk-delete", BulkDeleteAsync)
            .WithName("BulkDeleteItems")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/projects/{projectKey}/items/bulk-restore", BulkRestoreAsync)
            .WithName("BulkRestoreItems")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        return v1;
    }

    private static async Task<IResult> ListDeletedAsync(
        string projectKey,
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var limit = QueryParameters.LimitOf(request.Query, DeletedItemQuery.DefaultLimit);
        var cursor = QueryParameters.Single(request.Query, QueryParameters.Cursor);
        if (QueryParameters.FailureOf(limit.Problem, cursor.Problem) is { } queryFailure)
        {
            return EndpointResults.Fail(queryFailure);
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.ListDeletedAsync(
            actor, projectKey, new DeletedItemQuery { Limit = limit.Value, Cursor = cursor.Value }, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async page =>
        {
            var names = await PeopleNames.LookupAsync(
                people, actor.OrganizationId, page.Items.Select(i => i.DeletedBy).OfType<Guid>(), cancellationToken);
            return EndpointResults.Ok(new DeletedItemPageData([.. page.Items.Select(i => DeletedItemSummaryData.From(i, names))], page.NextCursor));
        });
    }

    private static async Task<IResult> RestoreItemAsync(
        string itemKey,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.RestoreAsync(actor, itemKey, cancellationToken);
        return await ItemEndpoints.RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> BulkUpdateAsync(
        string projectKey,
        JsonBody<BulkUpdateRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IBulkItemService bulk,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await bulk.UpdateAsync(actor, projectKey, request.ToUpdate(), cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async done => EndpointResults.Ok(new BulkUpdateData(
            await ItemData.FromManyAsync(done.Updated, people, actor.OrganizationId, cancellationToken),
            BulkFailureData.FromMany(done.Failed))));
    }

    private static async Task<IResult> BulkDeleteAsync(
        string projectKey,
        JsonBody<BulkKeysRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IBulkItemService bulk,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await bulk.DeleteAsync(actor, projectKey, request.Keys ?? [], cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new BulkDeleteData(
                [.. result.Value.Deleted.Select(d => new DeletedItemData(d.Key, d.DeletedAt))],
                BulkFailureData.FromMany(result.Value.Failed)));
    }

    private static async Task<IResult> BulkRestoreAsync(
        string projectKey,
        JsonBody<BulkKeysRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IBulkItemService bulk,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await bulk.RestoreAsync(actor, projectKey, request.Keys ?? [], cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async done => EndpointResults.Ok(new BulkRestoreData(
            await ItemData.FromManyAsync(done.Restored, people, actor.OrganizationId, cancellationToken),
            BulkFailureData.FromMany(done.Failed))));
    }
}
