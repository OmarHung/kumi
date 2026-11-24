using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 知識庫的頁面：樹、讀寫、搬移、刪除與復原、垃圾桶、限制。全部只能用 Cookie（不宣告 <c>RequireTokenScope</c>）。
/// <c>{number}</c> 不是正整數時和找不到頁面同一個回答（404 page_not_found）。
/// </summary>
internal static class KnowledgePageEndpoints
{
    private const string SpaceRoute = "/knowledge/spaces/{spaceKey}";

    public static RouteGroupBuilder MapKnowledgePageEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(SpaceRoute + "/tree", GetTreeAsync).WithName("GetKnowledgeTree");
        v1.MapGet(SpaceRoute + "/trash", ListTrashAsync).WithName("ListKnowledgeTrash");
        v1.MapPost(SpaceRoute + "/pages", CreateAsync).WithName("CreateKnowledgePage");
        v1.MapGet(SpaceRoute + "/pages/{number}", GetAsync).WithName("GetKnowledgePage");
        v1.MapPatch(SpaceRoute + "/pages/{number}", UpdateAsync).WithName("UpdateKnowledgePage");
        v1.MapPost(SpaceRoute + "/pages/{number}/move", MoveAsync).WithName("MoveKnowledgePage");
        v1.MapDelete(SpaceRoute + "/pages/{number}", DeleteAsync).WithName("DeleteKnowledgePage");
        v1.MapPost(SpaceRoute + "/pages/{number}/restore", RestoreAsync).WithName("RestoreKnowledgePage");
        v1.MapGet(SpaceRoute + "/pages/{number}/restriction", GetRestrictionAsync).WithName("GetKnowledgePageRestriction");
        v1.MapPut(SpaceRoute + "/pages/{number}/restriction", SetRestrictionAsync).WithName("SetKnowledgePageRestriction");
        return v1;
    }

    private static async Task<IResult> GetTreeAsync(
        string spaceKey,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await TreeAsync(await pages.GetTreeAsync(actor, spaceKey, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> ListTrashAsync(
        string spaceKey,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await pages.ListTrashAsync(actor, spaceKey, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async entries => EndpointResults.Ok(
            await KnowledgeTrashEntryData.FromManyAsync(entries, people, actor.OrganizationId, cancellationToken)));
    }

    private static async Task<IResult> CreateAsync(
        string spaceKey,
        JsonBody<NewKnowledgePageRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await pages.CreateAsync(actor, spaceKey, request.ToNew(), cancellationToken);
        return await EndpointResults.OkOrFailAsync(
            result,
            async page => EndpointResults.Created(
                $"/v1/knowledge/spaces/{Uri.EscapeDataString(page.Space.Key)}/pages/{page.Number}",
                await KnowledgePageData.FromAsync(page, people, actor.OrganizationId, cancellationToken)));
    }

    private static async Task<IResult> GetAsync(
        string spaceKey,
        string number,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await PageAsync(await pages.GetAsync(actor, spaceKey, pageNumber, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> UpdateAsync(
        string spaceKey,
        string number,
        JsonBody<PatchKnowledgePageRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        if (request.ExpectedRevision is not { } expectedRevision)
        {
            return EndpointResults.Fail(KnowledgeRoutes.RevisionRequired());
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var changes = new PageChanges(request.Title, request.Body, expectedRevision);
        return await PageAsync(await pages.UpdateAsync(actor, spaceKey, pageNumber, changes, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> MoveAsync(
        string spaceKey,
        string number,
        JsonBody<KnowledgePageMoveRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await TreeAsync(await pages.MoveAsync(actor, spaceKey, pageNumber, request.ToMove(), cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> DeleteAsync(
        string spaceKey,
        string number,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        CancellationToken cancellationToken)
    {
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await pages.DeleteAsync(actor, spaceKey, pageNumber, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(KnowledgePageDeletionData.From(result.Value));
    }

    private static async Task<IResult> RestoreAsync(
        string spaceKey,
        string number,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await PageAsync(await pages.RestoreAsync(actor, spaceKey, pageNumber, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> GetRestrictionAsync(
        string spaceKey,
        string number,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await RestrictionAsync(await pages.GetRestrictionAsync(actor, spaceKey, pageNumber, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> SetRestrictionAsync(
        string spaceKey,
        string number,
        JsonBody<KnowledgeRestrictionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgePageService pages,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        if (!KnowledgeRoutes.TryParseNumber(number, out var pageNumber))
        {
            return EndpointResults.Fail(KnowledgeRoutes.PageNotFound(number));
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await pages.SetRestrictionAsync(actor, spaceKey, pageNumber, request.People, cancellationToken);
        return await RestrictionAsync(result, people, actor, cancellationToken);
    }

    private static async Task<IResult> TreeAsync(
        Result<PageTree> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        await EndpointResults.OkOrFailAsync(result, async tree => EndpointResults.Ok(
            await KnowledgePageTreeData.FromAsync(tree, people, actor.OrganizationId, cancellationToken)));

    private static async Task<IResult> PageAsync(
        Result<PageDetail> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        await EndpointResults.OkOrFailAsync(result, async page => EndpointResults.Ok(
            await KnowledgePageData.FromAsync(page, people, actor.OrganizationId, cancellationToken)));

    private static async Task<IResult> RestrictionAsync(
        Result<PageRestriction> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        await EndpointResults.OkOrFailAsync(result, async restriction => EndpointResults.Ok(
            await KnowledgePageRestrictionData.FromAsync(restriction, people, actor.OrganizationId, cancellationToken)));
}
