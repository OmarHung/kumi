using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.OAuth;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>「用 OpenRouter 登入」開始：要接的是組織的還是自己的。</summary>
internal sealed record OpenRouterLoginRequest(AiConnectionScope? Scope = null);

/// <summary>OpenRouter 導回來的授權碼與 state。</summary>
internal sealed record OpenRouterCompleteRequest(string Code, string State)
{
    // 授權碼與 state 都只能用一次、帶著 PKCE 的 verifier，不讓它出現在記錄或例外訊息裡。
    public override string ToString() => "OpenRouterCompleteRequest { Code = ***, State = *** }";
}

internal sealed record OpenRouterLoginStartData(string AuthorizeUrl);

internal sealed record OpenRouterLoginResultData(AiConnectionScope Scope, AiConnectionData Connection);

/// <summary>
/// 自己的 AI 帳號與用 OpenRouter 登入（internal/HANDOFF-ai-floating.md 第 4.4 節）。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 誰能做由 AI 模組判斷（組織允不允許、是不是管理員）；API 只負責認人與限流。
/// </summary>
internal static class PersonalAiEndpoints
{
    private const string ConnectionRoute = "/account/ai-connections/{connectionId:guid}";

    /// <summary>OpenRouter 導回來的網頁路由，接在這個部署的網址後面。</summary>
    internal const string CallbackPath = "/ai/openrouter/callback";

    public static RouteGroupBuilder MapPersonalAiEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/account/ai-connections", ListAsync).WithName("ListPersonalAiConnections");
        v1.MapDelete(ConnectionRoute, DeleteAsync).WithName("DeletePersonalAiConnection");

        // 這幾個會讓伺服器去連供應商（最久 20 秒），依人限流，和 AI 設定頁共用同一份額度。
        v1.MapPost("/account/ai-connections", CreateAsync)
            .WithName("CreatePersonalAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPut(ConnectionRoute, UpdateAsync)
            .WithName("UpdatePersonalAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost(ConnectionRoute + "/refresh", RefreshAsync)
            .WithName("RefreshPersonalAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost("/account/ai-models/discover", DiscoverAsync)
            .WithName("DiscoverPersonalAiModels")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost("/ai/openrouter/login", StartLoginAsync)
            .WithName("StartOpenRouterLogin")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost("/ai/openrouter/complete", CompleteLoginAsync)
            .WithName("CompleteOpenRouterLogin")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        return v1;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var list = await connections.ListAsync(actor, cancellationToken);
        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, list.Select(c => c.UpdatedBy), cancellationToken);
        return EndpointResults.Ok(list.Select(c => AiConnectionData.From(c, names)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        JsonBody<AiConnectionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await connections.CreateAsync(actor, request.ToInput(), cancellationToken);
        return await RespondAsync(result, actor, people, created: true, cancellationToken);
    }

    private static async Task<IResult> UpdateAsync(
        Guid connectionId,
        JsonBody<AiConnectionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await connections.UpdateAsync(actor, connectionId, request.ToInput(), cancellationToken);
        return await RespondAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> RefreshAsync(
        Guid connectionId,
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await connections.RefreshModelsAsync(actor, connectionId, cancellationToken);
        return await RespondAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> DeleteAsync(
        Guid connectionId,
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        CancellationToken cancellationToken)
    {
        var result = await connections.DeleteAsync(SessionPrincipal.Read(principal).ToActor(), connectionId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static async Task<IResult> DiscoverAsync(
        JsonBody<AiDiscoverRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPersonalAiConnectionService connections,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await connections.DiscoverModelsAsync(SessionPrincipal.Read(principal).ToActor(), request.ToInput(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(AiDiscoveredModelsData.From(result.Value));
    }

    private static async Task<IResult> StartLoginAsync(
        HttpContext context,
        JsonBody<OpenRouterLoginRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IOpenRouterLoginService login,
        [FromServices] OAuthServerResolver servers,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        // 缺 scope 不能當成組織的：那會讓一個漏了欄位的請求去走管理員才能做的流程。
        if (request.Scope is not { } scope)
        {
            return EndpointResults.Fail(new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "要說明接的是組織的還是自己的 AI 帳號。")
            {
                Details = [new FailureDetail(AiErrorCodes.FieldRequired, "scope", "要填 organization 或 personal。")],
            });
        }

        // 回呼網址一律由這個部署對外的網址組（有設 PublicHost 就不看 Host 標頭），不收用戶端給的。
        var callbackUrl = servers.Resolve(context.Request).Issuer + CallbackPath;
        var result = await login.StartAsync(SessionPrincipal.Read(principal).ToActor(), scope, callbackUrl, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(new OpenRouterLoginStartData(result.Value));
    }

    private static async Task<IResult> CompleteLoginAsync(
        JsonBody<OpenRouterCompleteRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IOpenRouterLoginService login,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await login.CompleteAsync(actor, request.Code, request.State, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var connection = result.Value.Connection;
        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [connection.UpdatedBy], cancellationToken);
        return EndpointResults.Created(
            $"/v1/account/ai-connections/{connection.Id}",
            new OpenRouterLoginResultData(result.Value.Scope, AiConnectionData.From(connection, names)));
    }

    private static async Task<IResult> RespondAsync(
        Result<AiConnection> result, Actor actor, IPeopleDirectory people, bool created, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var connection = result.Value;
        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [connection.UpdatedBy], cancellationToken);
        var data = AiConnectionData.From(connection, names);
        return created ? EndpointResults.Created($"/v1/account/ai-connections/{connection.Id}", data) : EndpointResults.Ok(data);
    }
}
