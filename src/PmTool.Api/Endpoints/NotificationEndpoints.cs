using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Api.Json;
using PmTool.Api.Live;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 通知收件匣與關注。收件匣只列自己的；關注的項目先用工作引擎確認看得到（看不到就當作不存在），
/// 通知模組只管自己的資料。
/// </summary>
internal static class NotificationEndpoints
{
    private const string UnreadParameter = "unread";
    private const string CategoryParameter = "category";
    private const string SearchParameter = "q";

    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/notifications", ListAsync)
            .WithName("ListNotifications")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapGet("/notifications/unread-count", CountUnreadAsync)
            .WithName("CountUnreadNotifications")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost("/notifications/read", MarkReadAsync)
            .WithName("MarkNotificationsRead")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPost("/notifications/read-all", MarkAllReadAsync)
            .WithName("MarkAllNotificationsRead")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet("/items/{itemKey}/subscription", GetSubscriptionAsync)
            .WithName("GetItemSubscription")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPut("/items/{itemKey}/subscription", SetSubscriptionAsync)
            .WithName("SetItemSubscription")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        return v1;
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        [FromServices] INotificationInbox inbox,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var query = ParseQuery(request.Query);
        if (query.Failure is { } queryFailure)
        {
            return BadQuery(queryFailure);
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await inbox.ListAsync(actor, query.Value, cancellationToken);
        if (result.Failure is { } failure)
        {
            return BadQuery(failure);
        }

        var page = result.Value;
        var names = await PeopleNames.LookupAsync(
            people, actor.OrganizationId, page.Items.Select(item => item.ActorPersonId), cancellationToken);
        return EndpointResults.Ok(new NotificationPageData(
            [.. page.Items.Select(item => NotificationData.From(item, names))], page.NextCursor));
    }

    private static async Task<IResult> CountUnreadAsync(
        ClaimsPrincipal principal, [FromServices] INotificationInbox inbox, CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return EndpointResults.Ok(new UnreadCountData(await inbox.CountUnreadAsync(actor, cancellationToken)));
    }

    private static async Task<IResult> MarkReadAsync(
        JsonBody<MarkReadRequest> body,
        ClaimsPrincipal principal,
        [FromServices] INotificationInbox inbox,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        // 去重之後才算個數：同一個編號重複送不算多。
        Guid[] ids = [.. request.NotificationIds.Distinct()];
        if (ids.Length is < 1 or > NotificationRules.MaxMarkReadIds)
        {
            var invalid = ApiFailure.BadRequest with
            {
                Details = [new ApiErrorDetail(
                    ApiErrorCodes.BadRequest, "notificationIds", $"要給 1 到 {NotificationRules.MaxMarkReadIds} 個通知編號。")],
            };
            return invalid.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        return TellOtherTabs(live, actor, await inbox.MarkReadAsync(actor, ids, cancellationToken));
    }

    private static async Task<IResult> MarkAllReadAsync(
        ClaimsPrincipal principal,
        [FromServices] INotificationInbox inbox,
        [FromServices] ILiveNotifier live,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return TellOtherTabs(live, actor, await inbox.MarkAllReadAsync(actor, cancellationToken));
    }

    /// <summary>
    /// 有通知真的被標成已讀才推：自己的其他分頁的未讀數要跟著變。沒有變的請求不必吵醒它們。
    /// 推送不等：回應不能因為某條連線卡住而延遲，推送自己有期限、失敗只記錄；也不能用請求的取消權杖，回應一送出它就取消了。
    /// </summary>
    private static IResult TellOtherTabs(ILiveNotifier live, Actor actor, int updated)
    {
        if (updated > 0)
        {
            _ = live.InboxChangedAsync(actor.PersonId, CancellationToken.None);
        }

        return EndpointResults.Ok(new UpdatedCountData(updated));
    }

    private static async Task<IResult> GetSubscriptionAsync(
        string itemKey,
        ClaimsPrincipal principal,
        [FromServices] IWorkAccessQueries access,
        [FromServices] IItemSubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var item = await access.ResolveItemAsync(actor, itemKey, cancellationToken);
        return item.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(SubscriptionData.From(await subscriptions.GetAsync(actor, item.Value.ProjectId, item.Value.ItemId, cancellationToken)));
    }

    private static async Task<IResult> SetSubscriptionAsync(
        string itemKey,
        JsonBody<SubscriptionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IWorkAccessQueries access,
        [FromServices] IItemSubscriptionService subscriptions,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var item = await access.ResolveItemAsync(actor, itemKey, cancellationToken);
        if (item.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var reference = item.Value;
        var subscription = await subscriptions.SetAsync(actor, reference.ProjectId, reference.ItemId, request.Following, cancellationToken);
        return subscription.Failure is { } notMember
            ? EndpointResults.Fail(notMember)
            : EndpointResults.Ok(SubscriptionData.From(subscription.Value));
    }

    private static Result<NotificationQuery> ParseQuery(IQueryCollection query)
    {
        var unread = QueryParameters.Single(query, UnreadParameter).Then(value => value switch
        {
            null => new Parsed<bool>(false),
            "true" => new Parsed<bool>(true),
            "false" => new Parsed<bool>(false),
            _ => Parsed<bool>.Invalid(false, UnreadParameter, "unread 要是 true 或 false。"),
        });
        var limit = QueryParameters.LimitOf(query, NotificationRules.DefaultPageSize);
        var cursor = QueryParameters.Single(query, QueryParameters.Cursor);
        var category = QueryParameters.Single(query, CategoryParameter).Then(value => value switch
        {
            null => new Parsed<NotificationCategory>(NotificationCategory.All),
            "mentions" => new Parsed<NotificationCategory>(NotificationCategory.Mentions),
            "assigned" => new Parsed<NotificationCategory>(NotificationCategory.Assigned),
            _ => Parsed<NotificationCategory>.Invalid(NotificationCategory.All, CategoryParameter, "category 要是 mentions 或 assigned。"),
        });
        var search = QueryParameters.Single(query, SearchParameter);
        return QueryParameters.FailureOf(unread.Problem, limit.Problem, cursor.Problem, category.Problem, search.Problem) is { } failure
            ? failure
            : new NotificationQuery(unread.Value, cursor.Value, limit.Value) { Category = category.Value, Search = search.Value };
    }

    /// <summary>通知清單的查詢參數不正確一律回 400（合約），不用工作引擎清單的 422。</summary>
    private static IResult BadQuery(Failure failure) => (failure.ToApiFailure() with { StatusCode = StatusCodes.Status400BadRequest }).ToResult();
}
