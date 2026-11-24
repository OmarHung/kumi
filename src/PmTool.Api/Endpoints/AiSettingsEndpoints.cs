using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.RateLimiting;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 設定（組織管理員）與成員能選的 AI。只能在網頁上用 Cookie 登入操作：不宣告權杖範圍，個人存取權杖不能用。
/// 誰能看、能改由 AI 模組判斷（<see cref="Actor"/> 帶著是不是組織管理員），API 不重複一份規則。
/// 規格見 <c>internal/HANDOFF-ai-assistant.md</c> 第 6.1 節。
/// </summary>
internal static class AiSettingsEndpoints
{
    private const string ConnectionRoute = "/ai/connections/{connectionId:guid}";

    public static RouteGroupBuilder MapAiSettingsEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/ai/settings", GetSettingsAsync).WithName("GetAiSettings");
        v1.MapPut("/ai/settings", SaveSettingsAsync).WithName("SaveAiSettings");
        v1.MapGet("/ai/options", GetOptionsAsync).WithName("GetAiOptions");
        v1.MapDelete(ConnectionRoute, DeleteConnectionAsync).WithName("DeleteAiConnection");

        // 這四個會讓伺服器去連供應商（最久 20 秒），依人限流。
        v1.MapPost("/ai/connections", CreateConnectionAsync)
            .WithName("CreateAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPut(ConnectionRoute, UpdateConnectionAsync)
            .WithName("UpdateAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost(ConnectionRoute + "/refresh", RefreshConnectionAsync)
            .WithName("RefreshAiConnection")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        v1.MapPost("/ai/models/discover", DiscoverModelsAsync)
            .WithName("DiscoverAiModels")
            .RequireRateLimiting(ApiRateLimiting.AiProviderCallPolicy);
        return v1;
    }

    private static async Task<IResult> GetSettingsAsync(
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        return await RespondSettingsAsync(await settings.GetAsync(actor, cancellationToken), actor, people, cancellationToken);
    }

    private static async Task<IResult> SaveSettingsAsync(
        JsonBody<AiSettingsRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var allowPersonal = false;
        if (request.AllowPersonalConnections is null)
        {
            var current = await settings.GetAsync(actor, cancellationToken);
            if (current.Failure is { } denied)
            {
                return EndpointResults.Fail(denied);
            }

            allowPersonal = current.Value.AllowPersonalConnections;
        }

        return await RespondSettingsAsync(
            await settings.SaveAsync(actor, request.ToInput(allowPersonal), cancellationToken), actor, people, cancellationToken);
    }

    private static async Task<IResult> GetOptionsAsync(
        ClaimsPrincipal principal, [FromServices] IAiOptionsService options, CancellationToken cancellationToken) =>
        EndpointResults.Ok(AiOptionsData.From(await options.GetAsync(SessionPrincipal.Read(principal).ToActor(), cancellationToken)));

    private static async Task<IResult> CreateConnectionAsync(
        JsonBody<AiConnectionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await settings.CreateConnectionAsync(actor, request.ToInput(), cancellationToken);
        return await RespondConnectionAsync(result, actor, people, created: true, cancellationToken);
    }

    private static async Task<IResult> UpdateConnectionAsync(
        Guid connectionId,
        JsonBody<AiConnectionRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await settings.UpdateConnectionAsync(actor, connectionId, request.ToInput(), cancellationToken);
        return await RespondConnectionAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> RefreshConnectionAsync(
        Guid connectionId,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = SessionPrincipal.Read(principal).ToActor();
        var result = await settings.RefreshModelsAsync(actor, connectionId, cancellationToken);
        return await RespondConnectionAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> DeleteConnectionAsync(
        Guid connectionId,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        CancellationToken cancellationToken)
    {
        var result = await settings.DeleteConnectionAsync(SessionPrincipal.Read(principal).ToActor(), connectionId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static async Task<IResult> DiscoverModelsAsync(
        JsonBody<AiDiscoverRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IAiSettingsService settings,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await settings.DiscoverModelsAsync(SessionPrincipal.Read(principal).ToActor(), request.ToInput(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(AiDiscoveredModelsData.From(result.Value));
    }

    private static async Task<IResult> RespondSettingsAsync(
        Result<AiSettings> result, Actor actor, IPeopleDirectory people, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, AiSettingsData.PeopleOf(result.Value), cancellationToken);
        return EndpointResults.Ok(AiSettingsData.From(result.Value, names));
    }

    private static async Task<IResult> RespondConnectionAsync(
        Result<AiConnection> result, Actor actor, IPeopleDirectory people, bool created, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var connection = result.Value;
        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [connection.UpdatedBy], cancellationToken);
        var data = AiConnectionData.From(connection, names);
        return created ? EndpointResults.Created($"/v1/ai/connections/{connection.Id}", data) : EndpointResults.Ok(data);
    }
}
