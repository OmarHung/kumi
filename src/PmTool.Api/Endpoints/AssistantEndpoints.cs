using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理：專案的對話（<c>/projects/{key}/assistant/…</c>）與不選專案的組織對話（<c>/assistant/…</c>，傳給模組的專案代號是 null），
/// 兩組的形狀完全一樣；對話與送訊息（回應是 text/event-stream）。只能用 Cookie 登入：不宣告權杖範圍，個人存取權杖不能用。
/// 權限（看得到專案、blueprint.read、對話只有本人）都由 AI 模組判斷，API 不重複一份規則。
/// 規格見 <c>internal/HANDOFF-ai-assistant.md</c> 第 6.2 節。
/// </summary>
internal static class AssistantEndpoints
{
    private const string ConversationsRoute = "/projects/{projectKey}/assistant/conversations";
    private const string ConversationRoute = ConversationsRoute + "/{conversationId:guid}";
    private const string OrganizationConversationsRoute = "/assistant/conversations";
    private const string OrganizationConversationRoute = OrganizationConversationsRoute + "/{conversationId:guid}";

    public static RouteGroupBuilder MapAssistantEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ConversationsRoute, ListAsync).WithName("ListAssistantConversations");
        v1.MapPost(ConversationsRoute, CreateAsync).WithName("CreateAssistantConversation");
        v1.MapGet(ConversationRoute, GetAsync).WithName("GetAssistantConversation");
        v1.MapDelete(ConversationRoute, DeleteAsync).WithName("DeleteAssistantConversation");

        // 每一則都會讓伺服器去呼叫模型（最久五分鐘、要錢），依人限流。
        v1.MapPost(ConversationRoute + "/messages", SendAsync)
            .WithName("SendAssistantMessage")
            .RequireRateLimiting(ApiRateLimiting.AssistantMessagePolicy);

        v1.MapGet(OrganizationConversationsRoute, ListOrganizationAsync).WithName("ListOrganizationAssistantConversations");
        v1.MapPost(OrganizationConversationsRoute, CreateOrganizationAsync).WithName("CreateOrganizationAssistantConversation");
        v1.MapGet(OrganizationConversationRoute, GetOrganizationAsync).WithName("GetOrganizationAssistantConversation");
        v1.MapDelete(OrganizationConversationRoute, DeleteOrganizationAsync).WithName("DeleteOrganizationAssistantConversation");
        v1.MapPost(OrganizationConversationRoute + "/messages", SendOrganizationAsync)
            .WithName("SendOrganizationAssistantMessage")
            .RequireRateLimiting(ApiRateLimiting.AssistantMessagePolicy);
        return v1;
    }

    private static Task<IResult> ListOrganizationAsync(
        ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken) =>
        ListAsync(null, principal, assistant, cancellationToken);

    private static Task<IResult> CreateOrganizationAsync(
        ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken) =>
        CreateAsync(null, principal, assistant, cancellationToken);

    private static Task<IResult> GetOrganizationAsync(
        Guid conversationId, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken) =>
        GetAsync(null, conversationId, principal, assistant, cancellationToken);

    private static Task<IResult> DeleteOrganizationAsync(
        Guid conversationId, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken) =>
        DeleteAsync(null, conversationId, principal, assistant, cancellationToken);

    private static Task<IResult> SendOrganizationAsync(
        Guid conversationId,
        JsonBody<AssistantMessageRequest> body,
        ClaimsPrincipal principal,
        HttpContext http,
        [FromServices] IAssistantService assistant,
        [FromServices] AssistantStreamSettings? settings) =>
        SendAsync(null, conversationId, body, principal, http, assistant, settings);

    private static async Task<IResult> ListAsync(
        string? projectKey, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken)
    {
        var result = await assistant.ListAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(result.Value.Select(AssistantConversationData.From).ToList());
    }

    // 內文可以是空的或 {}：不讀。
    private static async Task<IResult> CreateAsync(
        string? projectKey, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken)
    {
        var result = await assistant.CreateAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                projectKey is null
                    ? $"{ApiRoutes.Prefix}{OrganizationConversationsRoute}/{result.Value.Id}"
                    : $"{ApiRoutes.Prefix}/projects/{projectKey}/assistant/conversations/{result.Value.Id}",
                AssistantConversationData.From(result.Value));
    }

    private static async Task<IResult> GetAsync(
        string? projectKey,
        Guid conversationId,
        ClaimsPrincipal principal,
        [FromServices] IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var result = await assistant.GetAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, conversationId, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(AssistantConversationDetailData.From(result.Value));
    }

    private static async Task<IResult> DeleteAsync(
        string? projectKey,
        Guid conversationId,
        ClaimsPrincipal principal,
        [FromServices] IAssistantService assistant,
        CancellationToken cancellationToken)
    {
        var result = await assistant.DeleteAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, conversationId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    /// <summary>
    /// 通過所有檢查之前的失敗是一般的 JSON 錯誤；通過之後回應是事件串流。
    /// 讀的 CancellationToken 是 <c>HttpContext.RequestAborted</c>：使用者按停止、關掉頁面就中斷，回覆存成已停止。
    /// </summary>
    private static async Task<IResult> SendAsync(
        string? projectKey,
        Guid conversationId,
        JsonBody<AssistantMessageRequest> body,
        ClaimsPrincipal principal,
        HttpContext http,
        [FromServices] IAssistantService assistant,
        [FromServices] AssistantStreamSettings? settings)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        // 組織對話沒有專案：選項目沒有意義，在這裡就擋下（模組也會擋）。附件照收（組織對話自己上傳的）。
        if (projectKey is null && request.ItemKey is not null)
        {
            return EndpointResults.Fail(new Failure(
                FailureKind.Invalid, AiErrorCodes.OrganizationChatUnsupported, "不選專案的對話不能指定項目。"));
        }

        var started = await assistant.StartAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, conversationId, request.ToInput(), http.RequestAborted);
        return started.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : new AssistantEventStream(started.Value, settings ?? AssistantStreamSettings.Default);
    }
}
