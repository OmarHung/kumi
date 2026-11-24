using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>一種項目類型在呼叫者看得到的專案裡的樣子；跨專案的「類型」篩選送的是 key。</summary>
internal sealed record ItemTypeData(string Key, IReadOnlyList<string> Names, IReadOnlyList<string> ProjectKeys)
{
    public static ItemTypeData From(ItemTypeSummary type) => new(type.Key, type.Names, type.ProjectKeys);
}

/// <summary>
/// 跨專案的項目清單與類型。範圍是呼叫者看得到的專案，權杖也只能讀（和專案的清單同一個範圍）：
/// 能讀哪些專案由模組判斷，權杖不會多開放任何東西。
/// </summary>
internal static class CrossProjectEndpoints
{
    public static RouteGroupBuilder MapCrossProjectEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/items", ListItemsAsync)
            .WithName("ListItemsAcrossProjects")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapGet("/item-types", ListItemTypesAsync)
            .WithName("ListItemTypes")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> ListItemsAsync(
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

        // project 可以重複給；個數與字元由模組檢查，規則只有一份。
        var scope = new CrossProjectScope(QueryParameters.NonEmpty(request.Query["project"]));
        var result = await items.ListAcrossProjectsAsync(actor, scope, query.Value, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async page => EndpointResults.Ok(new ItemPageData(
            await ItemData.FromManyAsync(page.Items, people, actor.OrganizationId, cancellationToken), page.NextCursor)));
    }

    private static async Task<IResult> ListItemTypesAsync(
        ClaimsPrincipal principal, [FromServices] IWorkItemService items, CancellationToken cancellationToken)
    {
        var types = await items.ListItemTypesAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken);
        return EndpointResults.Ok(types.Select(ItemTypeData.From).ToArray());
    }
}
