using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>項目的活動紀錄與留言。看不看得到、能不能留言由工作引擎判斷，這裡只轉換格式。</summary>
internal static class ItemActivityEndpoints
{
    public static RouteGroupBuilder MapItemActivityEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/items/{itemKey}/activity", ListActivityAsync)
            .WithName("ListItemActivity")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/items/{itemKey}/comments", AddCommentAsync)
            .WithName("AddItemComment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPatch("/items/{itemKey}/comments/{commentId}", EditCommentAsync)
            .WithName("EditItemComment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapDelete("/items/{itemKey}/comments/{commentId}", DeleteCommentAsync)
            .WithName("DeleteItemComment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/items/{itemKey}/comments/{commentId}/restore", RestoreCommentAsync)
            .WithName("RestoreItemComment")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet("/items/{itemKey}/comments/{commentId}/revisions", ListRevisionsAsync)
            .WithName("ListItemCommentRevisions")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> ListActivityAsync(
        string itemKey,
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var query = ActivityQueryParser.Parse(request.Query);
        if (query.Failure is { } queryFailure)
        {
            return EndpointResults.Fail(queryFailure);
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.ListAsync(actor, itemKey, query.Value, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async page => EndpointResults.Ok(new ActivityPageData(
            await ActivityData.FromManyAsync(page.Items, people, actor.OrganizationId, cancellationToken), page.NextCursor)));
    }

    /// <summary>
    /// 回 201 但不帶 Location：一則留言沒有自己的網址，它出現在項目的活動紀錄裡。
    /// </summary>
    private static async Task<IResult> AddCommentAsync(
        string itemKey,
        JsonBody<CommentRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.AddCommentAsync(actor, itemKey, request.Body, request.ReplyTo, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async comment => EndpointResults.Created(
            null, (await ActivityData.FromManyAsync([comment], people, actor.OrganizationId, cancellationToken))[0]));
    }

    private static async Task<IResult> EditCommentAsync(
        string itemKey,
        string commentId,
        JsonBody<CommentEditRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(commentId, "D", out var id))
        {
            return EndpointResults.Fail(CommentNotFound());
        }

        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.EditCommentAsync(actor, itemKey, id, request.Body, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    /// <summary>回 200 加刪除之後的活動，不是 204：網頁拿它換掉快取裡的那一筆。</summary>
    private static async Task<IResult> DeleteCommentAsync(
        string itemKey,
        string commentId,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(commentId, "D", out var id))
        {
            return EndpointResults.Fail(CommentNotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.DeleteCommentAsync(actor, itemKey, id, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> RestoreCommentAsync(
        string itemKey,
        string commentId,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(commentId, "D", out var id))
        {
            return EndpointResults.Fail(CommentNotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.RestoreCommentAsync(actor, itemKey, id, cancellationToken);
        return await RespondAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> ListRevisionsAsync(
        string itemKey,
        string commentId,
        ClaimsPrincipal principal,
        [FromServices] IItemActivityService activity,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(commentId, "D", out var id))
        {
            return EndpointResults.Fail(CommentNotFound());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await activity.ListCommentRevisionsAsync(actor, itemKey, id, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new CommentRevisionListData([.. result.Value.Select(CommentRevisionData.From)]));
    }

    private static async Task<IResult> RespondAsync(
        Result<ItemActivity> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        await EndpointResults.OkOrFailAsync(result, async comment => EndpointResults.Ok(
            (await ActivityData.FromManyAsync([comment], people, actor.OrganizationId, cancellationToken))[0]));

    private static Failure CommentNotFound() => new(FailureKind.NotFound, WorkErrorCodes.CommentNotFound, "找不到這則留言。");
}
