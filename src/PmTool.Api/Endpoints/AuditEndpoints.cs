using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 稽核紀錄。只能用 Cookie 登入讀（沒有宣告權杖的範圍）；誰能讀由身分模組判斷，只有組織的擁有者。
/// </summary>
internal static class AuditEndpoints
{
    public const string Route = "/audit-events";

    private const string PersonParameter = "person";

    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Route, ListAsync).WithName("ListAuditEvents");
        return v1;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ClaimsPrincipal principal,
        [FromServices] IAuditLog audit,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        // 紀錄裡有人的 Email 與來源位址：不讓瀏覽器或中間的代理留著。
        context.Response.Headers.CacheControl = "no-store";
        var user = SessionPrincipal.Read(principal);
        if (ParseQuery(context.Request.Query) is not { } query)
        {
            // 權限先於參數：不是擁有者的人不管送什麼都是 403。判斷權限的是模組，所以拿一個必定不合的查詢去問，
            // 回 403 就照實回，其餘才回參數的錯。
            var probe = await audit.ListAsync(user.ToActor(), new AuditQuery(Limit: 0), cancellationToken);
            return EndpointResults.Fail(probe.Failure is { Kind: FailureKind.Forbidden } forbidden ? forbidden : InvalidQuery(context.Request.Query));
        }

        var result = await audit.ListAsync(user.ToActor(), query, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var page = result.Value;
        var ids = page.Items.SelectMany(entry => new[] { entry.ActorPersonId, entry.TargetPersonId }).OfType<Guid>();
        var names = await PeopleNames.LookupAsync(people, user.OrganizationId, ids, cancellationToken);
        return EndpointResults.Ok(new AuditPageData([.. page.Items.Select(entry => AuditEntryData.From(entry, names))], page.NextCursor));
    }

    private static AuditQuery? ParseQuery(IQueryCollection query)
    {
        var limit = QueryParameters.LimitOf(query, AuditRules.DefaultLimit);
        var cursor = QueryParameters.Single(query, QueryParameters.Cursor);
        var person = QueryParameters.Single(query, PersonParameter);
        if (limit.Problem is not null || cursor.Problem is not null || person.Problem is not null)
        {
            return null;
        }

        if (person.Value is null)
        {
            return new AuditQuery(cursor.Value, limit.Value);
        }

        return Guid.TryParse(person.Value, out var personId) ? new AuditQuery(cursor.Value, limit.Value, personId) : null;
    }

    /// <summary>參數的格式不對（不是整數、不是編號、給了兩次）。範圍與游標的內容由模組判斷，用同一個代碼。</summary>
    private static Failure InvalidQuery(IQueryCollection query)
    {
        var limit = QueryParameters.LimitOf(query, AuditRules.DefaultLimit).Problem;
        var cursor = QueryParameters.Single(query, QueryParameters.Cursor).Problem;
        var person = QueryParameters.Single(query, PersonParameter).Problem;
        var found = new[] { limit, cursor, person }.OfType<FailureDetail>().ToList();
        if (found.Count == 0)
        {
            found.Add(new FailureDetail(IdentityErrorCodes.AuditQueryInvalid, PersonParameter, "person 要是人員編號。"));
        }

        return new Failure(FailureKind.Invalid, IdentityErrorCodes.AuditQueryInvalid, "查詢參數不正確。")
        {
            Details = [.. found.Select(d => d with { Code = IdentityErrorCodes.AuditQueryInvalid })],
        };
    }
}
