using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Json;
using PmTool.Api.Responses;
using PmTool.Api.Security;
using PmTool.Identity;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 主機 AI 代理的管理（組織擁有者，internal/HANDOFF-host-agent.md 第 5.1 節）。只能用網頁的 Cookie：不宣告權杖範圍，
/// 個人存取權杖與 OAuth 權杖一律 403；worker 的機器憑證與工具授權不是登入，打這裡是 401。
/// 「只有擁有者」由模組每次向身分模組重新確認（<see cref="AiErrorCodes.HostOwnerOnly"/>），API 不重複一份規則，也不看登入當時的角色。
/// </summary>
internal static class HostAgentAdminEndpoints
{
    private const string Root = "/ai/host";
    private const string AgentRoute = Root + "/agents/{agentId:guid}";
    private const string SourceRoute = Root + "/sources/{sourceId:guid}";

    public static RouteGroupBuilder MapHostAgentAdminEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Root, GetAsync).WithName("GetHostAgents");
        v1.MapPost(Root + "/agents", CreateAgentAsync).WithName("CreateHostAgent");
        v1.MapPatch(AgentRoute, UpdateAgentAsync).WithName("UpdateHostAgent");
        v1.MapPost(AgentRoute + "/pairing", ResetPairingAsync).WithName("ResetHostAgentPairing");
        v1.MapDelete(AgentRoute, RemoveAgentAsync).WithName("RemoveHostAgent");
        v1.MapPost(AgentRoute + "/engines/{engine}/login", StartLoginAsync).WithName("StartHostLogin");
        v1.MapPost(AgentRoute + "/engines/{engine}/logout", LogoutAsync).WithName("LogoutHostEngine");
        v1.MapDelete(Root + "/logins/{loginId:guid}", CancelLoginAsync).WithName("CancelHostLogin");
        v1.MapPost(Root + "/sources", CreateSourceAsync).WithName("CreateHostSource");
        v1.MapPut(SourceRoute, UpdateSourceAsync).WithName("UpdateHostSource");
        v1.MapDelete(SourceRoute, DeleteSourceAsync).WithName("DeleteHostSource");
        v1.MapGet(SourceRoute + "/usage", GetUsageAsync).WithName("GetHostSourceUsage");
        return v1;
    }

    private static Actor ActorOf(ClaimsPrincipal principal) => SessionPrincipal.Read(principal).ToActor();

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal, [FromServices] IHostAgentAdminService admin, [FromServices] IPeopleDirectory people, CancellationToken cancellationToken)
    {
        var actor = ActorOf(principal);
        var result = await admin.GetAsync(actor, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, HostAgentOverviewData.PeopleOf(result.Value), cancellationToken);
        return EndpointResults.Ok(HostAgentOverviewData.From(result.Value, names));
    }

    private static async Task<IResult> CreateAgentAsync(
        JsonBody<HostAgentRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = ActorOf(principal);
        var result = await admin.CreateAgentAsync(actor, request.ToInput(), cancellationToken);
        return await RespondPairingAsync(result, actor, people, created: true, cancellationToken);
    }

    private static async Task<IResult> UpdateAgentAsync(
        Guid agentId,
        JsonBody<HostAgentRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = ActorOf(principal);
        var result = await admin.UpdateAgentAsync(actor, agentId, request.ToInput(), cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, HostAgentData.PeopleOf(result.Value), cancellationToken);
        return EndpointResults.Ok(HostAgentData.From(result.Value, names));
    }

    // 內文可以是空的：不讀。
    private static async Task<IResult> ResetPairingAsync(
        Guid agentId,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = ActorOf(principal);
        var result = await admin.ResetPairingAsync(actor, agentId, cancellationToken);
        return await RespondPairingAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> RemoveAgentAsync(
        Guid agentId, ClaimsPrincipal principal, [FromServices] IHostAgentAdminService admin, CancellationToken cancellationToken) =>
        NoContent(await admin.RemoveAgentAsync(ActorOf(principal), agentId, cancellationToken));

    private static async Task<IResult> StartLoginAsync(
        Guid agentId,
        string engine,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!TryParseEngine(engine, out var kind))
        {
            return ApiFailure.NotFound.ToResult();
        }

        var actor = ActorOf(principal);
        var result = await admin.StartLoginAsync(actor, agentId, kind, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [result.Value.Login.StartedBy], cancellationToken);
        return EndpointResults.Ok(HostLoginStartData.From(result.Value, names));
    }

    private static async Task<IResult> LogoutAsync(
        Guid agentId, string engine, ClaimsPrincipal principal, [FromServices] IHostAgentAdminService admin, CancellationToken cancellationToken) =>
        TryParseEngine(engine, out var kind)
            ? NoContent(await admin.LogoutAsync(ActorOf(principal), agentId, kind, cancellationToken))
            : ApiFailure.NotFound.ToResult();

    private static async Task<IResult> CancelLoginAsync(
        Guid loginId, ClaimsPrincipal principal, [FromServices] IHostAgentAdminService admin, CancellationToken cancellationToken) =>
        NoContent(await admin.CancelLoginAsync(ActorOf(principal), loginId, cancellationToken));

    private static async Task<IResult> CreateSourceAsync(
        JsonBody<HostSourceRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = ActorOf(principal);
        var result = await admin.CreateSourceAsync(actor, request.ToInput(), cancellationToken);
        return await RespondSourceAsync(result, actor, people, created: true, cancellationToken);
    }

    private static async Task<IResult> UpdateSourceAsync(
        Guid sourceId,
        JsonBody<HostSourceRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var actor = ActorOf(principal);
        var result = await admin.UpdateSourceAsync(actor, sourceId, request.ToInput(), cancellationToken);
        return await RespondSourceAsync(result, actor, people, created: false, cancellationToken);
    }

    private static async Task<IResult> DeleteSourceAsync(
        Guid sourceId, ClaimsPrincipal principal, [FromServices] IHostAgentAdminService admin, CancellationToken cancellationToken) =>
        NoContent(await admin.DeleteSourceAsync(ActorOf(principal), sourceId, cancellationToken));

    private static async Task<IResult> GetUsageAsync(
        Guid sourceId,
        string? month,
        ClaimsPrincipal principal,
        [FromServices] IHostAgentAdminService admin,
        [FromServices] IPeopleDirectory people,
        CancellationToken cancellationToken)
    {
        var actor = ActorOf(principal);
        var result = await admin.GetUsageAsync(actor, sourceId, month, cancellationToken);
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, HostSourceUsageData.PeopleOf(result.Value), cancellationToken);
        return EndpointResults.Ok(HostSourceUsageData.From(result.Value, names));
    }

    /// <summary>路徑上的引擎只認 <c>claude_code</c> 與 <c>codex</c>；別的字（包括數字）不是引擎，當作沒有這個路徑。</summary>
    private static bool TryParseEngine(string text, out AiProviderKind kind) =>
        SnakeCaseEnums.TryParse(text, out kind) && kind is AiProviderKind.ClaudeCode or AiProviderKind.Codex;

    private static IResult NoContent(Result<Unit> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();

    private static async Task<IResult> RespondPairingAsync(
        Result<HostAgentPairing> result, Actor actor, IPeopleDirectory people, bool created, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, HostAgentData.PeopleOf(result.Value.Agent), cancellationToken);
        var data = HostAgentPairingData.From(result.Value, names);
        return created ? EndpointResults.Created($"{ApiRoutes.Prefix}/ai/host/agents/{result.Value.Agent.Id}", data) : EndpointResults.Ok(data);
    }

    private static async Task<IResult> RespondSourceAsync(
        Result<HostSource> result, Actor actor, IPeopleDirectory people, bool created, CancellationToken cancellationToken)
    {
        if (result.Failure is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var names = await PeopleNames.LookupAsync(people, actor.OrganizationId, [result.Value.UpdatedBy], cancellationToken);
        var data = HostSourceData.From(result.Value, names);
        return created ? EndpointResults.Created($"{ApiRoutes.Prefix}/ai/host/sources/{result.Value.Id}", data) : EndpointResults.Ok(data);
    }
}
