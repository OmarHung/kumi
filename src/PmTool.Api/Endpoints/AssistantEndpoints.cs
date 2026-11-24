using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 專案的 AI 助理：對話與送訊息（回應是 text/event-stream）。只能用 Cookie 登入：不宣告權杖範圍，個人存取權杖不能用。
/// 權限（看得到專案、blueprint.read、對話只有本人）都由 AI 模組判斷，API 不重複一份規則。
/// 規格見 <c>internal/HANDOFF-ai-assistant.md</c> 第 6.2 節。
/// </summary>
internal static class AssistantEndpoints
{
    private const string ConversationsRoute = "/projects/{projectKey}/assistant/conversations";
    private const string ConversationRoute = ConversationsRoute + "/{conversationId:guid}";

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
        return v1;
    }

    private static async Task<IResult> ListAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken)
    {
        var result = await assistant.ListAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(result.Value.Select(AssistantConversationData.From).ToList());
    }

    // 內文可以是空的或 {}：不讀。
    private static async Task<IResult> CreateAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IAssistantService assistant, CancellationToken cancellationToken)
    {
        var result = await assistant.CreateAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                $"{ApiRoutes.Prefix}/projects/{projectKey}/assistant/conversations/{result.Value.Id}",
                AssistantConversationData.From(result.Value));
    }

    private static async Task<IResult> GetAsync(
        string projectKey,
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
        string projectKey,
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
        string projectKey,
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

        var started = await assistant.StartAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, conversationId, request.ToInput(), http.RequestAborted);
        return started.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : new AssistantEventStream(started.Value, settings ?? AssistantStreamSettings.Default);
    }
}
