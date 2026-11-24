using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

internal static class ItemEndpoints
{
    private const int DefaultSearchLimit = 8;

    public static RouteGroupBuilder MapItemEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/projects/{projectKey}/items", ListItemsAsync)
            .WithName("ListItems")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/projects/{projectKey}/items", CreateItemAsync)
            .WithName("CreateItem")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet("/items/{itemKey}", GetItemAsync)
            .WithName("GetItem")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPatch("/items/{itemKey}", UpdateItemAsync)
            .WithName("UpdateItem")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapDelete("/items/{itemKey}", DeleteItemAsync)
            .WithName("DeleteItem")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/items/{itemKey}/transitions", TransitionItemAsync)
            .WithName("TransitionItem")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/items/{itemKey}/type", ChangeItemTypeAsync)
            .WithName("ChangeItemType")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet("/my-work", ListMyWorkAsync)
            .WithName("ListMyWork")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapGet("/search/items", SearchItemsAsync)
            .WithName("SearchItems")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> ListItemsAsync(
        string projectKey,
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var query = ItemQueryParser.Parse(request.Query, actor.PersonId);
        if (query.Failure is { } queryFailure)
        {
            return EndpointResults.Fail(queryFailure);
        }

        var result = await items.ListAsync(actor, projectKey, query.Value, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async page => EndpointResults.Ok(new ItemPageData(
            await ItemData.FromManyAsync(page.Items, people, actor.OrganizationId, cancellationToken), page.NextCursor)));
    }

    private static async Task<IResult> CreateItemAsync(
        string projectKey,
        JsonBody<CreateItemRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return ItemRequestPaths.UseModuleNames(bodyFailure).ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.CreateAsync(actor, projectKey, request.ToNewWorkItem(), cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async item => EndpointResults.Created(
            LocationOf(item), await SingleAsync(item, people, actor, cancellationToken)));
    }

    private static async Task<IResult> GetItemAsync(
        string itemKey,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.GetAsync(actor, itemKey, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> UpdateItemAsync(
        string itemKey,
        JsonBody<PatchItemRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return ItemRequestPaths.UseModuleNames(bodyFailure).ToResult();
        }

        // 型別寫成不可為 null 的字串，但 JSON 的 null 照樣進得來；標題不能清掉，所以在這裡擋。
        if (request.Title is { HasValue: true, Value: null })
        {
            return NullNotAllowed("title").ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.UpdateAsync(actor, itemKey, request.ToPatch(), cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> TransitionItemAsync(
        string itemKey,
        JsonBody<TransitionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.TransitionAsync(actor, itemKey, request.To, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> DeleteItemAsync(
        string itemKey,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.DeleteAsync(actor, itemKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new DeletedItemData(result.Value.Key, result.Value.DeletedAt));
    }

    private static async Task<IResult> ChangeItemTypeAsync(
        string itemKey,
        JsonBody<ChangeTypeRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();

        // 合約的參數不可為 null，但模組自己會把 null 當成不存在的類型回 422；這裡照原樣交過去。
        var result = await items.ChangeTypeAsync(actor, itemKey, request.Type!, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> ListMyWorkAsync(
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var mine = await items.ListAssignedToMeAsync(actor, cancellationToken);
        return EndpointResults.Ok(new MyWorkData(
            await ItemData.FromManyAsync(mine, people, actor.OrganizationId, cancellationToken)));
    }

    private static async Task<IResult> SearchItemsAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkItemService items,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var (text, limit, failure) = QueryParameters.SearchOf(request.Query, DefaultSearchLimit);
        if (failure is not null)
        {
            return EndpointResults.Fail(failure);
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await items.SearchAsync(actor, text!, limit, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async found => EndpointResults.Ok(new SearchItemsData(
            await ItemData.FromManyAsync(found, people, actor.OrganizationId, cancellationToken))));
    }

    internal static Task<IResult> RespondAsync(
        Result<WorkItem> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        EndpointResults.OkOrFailAsync(result, async item =>
            EndpointResults.Ok(await SingleAsync(item, people, actor, cancellationToken)));

    private static async Task<ItemDetailData> SingleAsync(
        WorkItem item, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        new((await ItemData.FromManyAsync([item], people, actor.OrganizationId, cancellationToken))[0], item.Description);

    private static string LocationOf(WorkItem item) => $"/v1/items/{Uri.EscapeDataString(item.Key)}";

    private static ApiFailure NullNotAllowed(string path) => ApiFailure.BadRequest with
    {
        Details = [new ApiErrorDetail(ApiErrorCodes.BadRequest, path, "這個屬性不能是 null。")],
    };
}
