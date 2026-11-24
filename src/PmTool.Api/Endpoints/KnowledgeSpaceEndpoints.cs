using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 知識庫的空間。全部只能用 Cookie：不宣告 <c>RequireTokenScope</c>，權杖一律 403（CLI 與 MCP 留到下一輪）。
/// 權限與規則都在知識庫模組，這裡只讀請求、換成回應的形狀。
/// </summary>
internal static class KnowledgeSpaceEndpoints
{
    public static RouteGroupBuilder MapKnowledgeSpaceEndpoints(this RouteGroupBuilder v1)
    {
        var spaces = v1.MapGroup("/knowledge/spaces");
        spaces.MapGet("", ListAsync).WithName("ListKnowledgeSpaces");
        spaces.MapPost("", CreateAsync).WithName("CreateKnowledgeSpace");
        spaces.MapGet("/{spaceKey}", GetAsync).WithName("GetKnowledgeSpace");
        spaces.MapPut("/{spaceKey}", UpdateAsync).WithName("UpdateKnowledgeSpace");
        spaces.MapPost("/{spaceKey}/archive", ArchiveAsync).WithName("ArchiveKnowledgeSpace");
        spaces.MapPost("/{spaceKey}/unarchive", UnarchiveAsync).WithName("UnarchiveKnowledgeSpace");
        return v1;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var list = await spaces.ListAsync(actor, cancellationToken);
        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, list.Select(s => s.CreatedBy), cancellationToken);
        return EndpointResults.Ok(list.Select(space => KnowledgeSpaceData.From(space, names)).ToArray());
    }

    private static async Task<IResult> CreateAsync(
        JsonBody<NewKnowledgeSpaceRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await spaces.CreateAsync(actor, request.ToNew(), cancellationToken);
        return await EndpointResults.OkOrFailAsync(
            result,
            async space => EndpointResults.Created(
                $"/v1/knowledge/spaces/{Uri.EscapeDataString(space.Key)}", await RespondAsync(space, people, actor, cancellationToken)));
    }

    private static async Task<IResult> GetAsync(
        string spaceKey,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await OkAsync(await spaces.GetAsync(actor, spaceKey, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> UpdateAsync(
        string spaceKey,
        JsonBody<KnowledgeSpaceSettingsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return await OkAsync(await spaces.UpdateAsync(actor, spaceKey, request.ToSettings(), cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> ArchiveAsync(
        string spaceKey,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await OkAsync(await spaces.SetArchivedAsync(actor, spaceKey, true, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> UnarchiveAsync(
        string spaceKey,
        ClaimsPrincipal principal,
        [FromServices] IKnowledgeSpaceService spaces,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await OkAsync(await spaces.SetArchivedAsync(actor, spaceKey, false, cancellationToken), people, actor, cancellationToken);
    }

    private static async Task<IResult> OkAsync(
        Result<SpaceSummary> result, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        await EndpointResults.OkOrFailAsync(
            result, async space => EndpointResults.Ok(await RespondAsync(space, people, actor, cancellationToken)));

    private static async Task<KnowledgeSpaceData> RespondAsync(
        SpaceSummary space, IPeopleDirectory people, Actor actor, CancellationToken cancellationToken) =>
        KnowledgeSpaceData.From(
            space, await PeopleNames.LookupAsync(people, actor.OrganizationId, [space.CreatedBy], cancellationToken));
}
