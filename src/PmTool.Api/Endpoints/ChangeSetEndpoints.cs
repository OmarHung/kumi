using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Documents;
using PmTool.Api.Live;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Blueprint;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 修改藍圖：送出、列出、讀取、套用、拒絕變更集。權限、驗證與計畫都由工作引擎判斷，這裡只轉換格式。
/// </summary>
internal static class ChangeSetEndpoints
{
    // 編號不是正整數時路由就對不上，回的是和其他找不到的路徑一樣的 404，不必走到模組。
    private const string LogCategory = "PmTool.Api.Endpoints.ChangeSetEndpoints";

    private const string ChangeSetRoute = "/projects/{projectKey}/changesets/{number:long:min(1)}";

    public static RouteGroupBuilder MapChangeSetEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/projects/{projectKey}/changesets", ListAsync)
            .WithName("ListChangeSets")
            .RequireTokenScope(AccessTokenScope.BlueprintRead);
        v1.MapPost("/projects/{projectKey}/changesets", ProposeAsync)
            .WithName("ProposeChangeSet")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapPost("/projects/{projectKey}/changesets/preview", PreviewAsync)
            .WithName("PreviewChangeSet")
            .RequireTokenScope(AccessTokenScope.BlueprintRead);
        v1.MapGet(ChangeSetRoute, GetAsync)
            .WithName("GetChangeSet")
            .RequireTokenScope(AccessTokenScope.BlueprintRead);

        // 套用與拒絕沒有內文；帶了也不讀，和其他沒有內文的端點一樣。
        v1.MapPost(ChangeSetRoute + "/apply", ApplyAsync)
            .WithName("ApplyChangeSet")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        v1.MapPost(ChangeSetRoute + "/reject", RejectAsync)
            .WithName("RejectChangeSet")
            .RequireTokenScope(AccessTokenScope.BlueprintWrite);
        return v1;
    }

    private static async Task<IResult> ListAsync(
        string projectKey,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await changes.ListAsync(actor, projectKey, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async list => EndpointResults.Ok(
            await ChangeSetData.FromManyAsync(list, people, actor.OrganizationId, cancellationToken)));
    }

    /// <summary>
    /// 內文是變更集文件本身，媒體類型、編碼、上限與讀取期限都和 /v1/changesets/validate 一樣，
    /// 先驗證過的文件送出時不會被另一套規則擋下。
    /// 不占驗證端點同時處理的位置：那是給匿名流量的總量上限，共用的話匿名的請求就能把送出擋掉；
    /// 已登入的請求依人限制同時的數量（<see cref="ApiLimits.MaxConcurrentRequestsPerPerson"/>）。
    /// </summary>
    private static async Task<IResult> ProposeAsync(
        string projectKey,
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] IPeopleDirectory people,
        [FromServices] ValidationLoadLimits limits,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(request, limits, cancellationToken);
        if (document.Failure is { } documentFailure)
        {
            return documentFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await changes.ProposeAsync(actor, projectKey, document.Text, document.Format, cancellationToken);
        return await EndpointResults.OkOrFailAsync(result, async detail => EndpointResults.Created(
            LocationOf(projectKey, detail.ChangeSet.Number),
            await ChangeSetDetailData.FromAsync(detail, people, actor.OrganizationId, cancellationToken)));
    }

    /// <summary>
    /// 只算、不存。內文的規則和送出一樣，所以先預覽過的文件送出時不會被另一套規則擋下。
    /// 要 blueprint.read，用權杖時要 blueprint:read 的範圍。
    /// </summary>
    private static async Task<IResult> PreviewAsync(
        string projectKey,
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] ValidationLoadLimits limits,
        CancellationToken cancellationToken)
    {
        var document = await ReadDocumentAsync(request, limits, cancellationToken);
        if (document.Failure is { } documentFailure)
        {
            return documentFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await changes.PreviewAsync(actor, projectKey, document.Text, document.Format, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(ChangeSetPreviewData.From(result.Value));
    }

    private static async Task<DocumentRead> ReadDocumentAsync(
        HttpRequest request, ValidationLoadLimits limits, CancellationToken cancellationToken)
    {
        if (!DocumentMediaTypes.TryResolve(request.ContentType, out var format, out var mediaTypeFailure))
        {
            return new DocumentRead(string.Empty, format, mediaTypeFailure);
        }

        var body = await Utf8BodyReader.ReadDocumentAsync(request, limits.BodyReadTimeout, cancellationToken);
        return new DocumentRead(body.Text, format, body.Failure);
    }

    private readonly record struct DocumentRead(string Text, DocumentFormat Format, ApiFailure? Failure);

    private static Task<IResult> GetAsync(
        string projectKey,
        long number,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken) =>
        RespondAsync(principal, people, actor => changes.GetAsync(actor, projectKey, number, cancellationToken), cancellationToken);

    private static Task<IResult> ApplyAsync(
        string projectKey,
        long number,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] IPeopleDirectory people,
        [FromServices] IProjectService projects,
        [FromServices] ILiveNotifier live,
        [FromServices] ILoggerFactory loggers,
        CancellationToken cancellationToken) =>
        RespondAsync(principal, people, async actor =>
        {
            var applied = await changes.ApplyAsync(actor, projectKey, number, cancellationToken);
            if (applied.Failure is null)
            {
                await TellMembersAsync(projects, live, loggers.CreateLogger(LogCategory), actor, projectKey, cancellationToken);
            }

            return applied;
        }, cancellationToken);

    /// <summary>
    /// 角色的權限可能變了：成員的即時連線立刻重算。變更集已經套用了，這裡出任何問題都不能讓使用者看到失敗：
    /// 查不到成員或丟例外只記錄，不全站重算（每兩分鐘一次的全面檢查會補上）。
    /// </summary>
    private static async Task TellMembersAsync(
        IProjectService projects, ILiveNotifier live, ILogger logger, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        try
        {
            var project = await projects.GetAsync(actor, projectKey, cancellationToken);
            if (project.Failure is not null)
            {
                logger.LogWarning("變更集已套用，但取不到專案 {ProjectKey} 的成員，即時連線等定期檢查再更新權限。", projectKey);
                return;
            }

            live.AccessChanged([.. project.Value.Members.Select(m => m.PersonId)]);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "變更集已套用，但通知專案 {ProjectKey} 的成員重算即時連線失敗，等定期檢查再更新權限。", projectKey);
        }
    }

    private static Task<IResult> RejectAsync(
        string projectKey,
        long number,
        ClaimsPrincipal principal,
        [FromServices] IBlueprintChangeService changes,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken) =>
        RespondAsync(principal, people, actor => changes.RejectAsync(actor, projectKey, number, cancellationToken), cancellationToken);

    private static async Task<IResult> RespondAsync(
        ClaimsPrincipal principal,
        IPeopleDirectory people,
        Func<Actor, Task<Result<ChangeSetDetail>>> call,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await call(actor);
        return await EndpointResults.OkOrFailAsync(result, async detail => EndpointResults.Ok(
            await ChangeSetDetailData.FromAsync(detail, people, actor.OrganizationId, cancellationToken)));
    }

    private static string LocationOf(string projectKey, long number) =>
        $"/v1/projects/{Uri.EscapeDataString(projectKey)}/changesets/{number.ToString(CultureInfo.InvariantCulture)}";
}
