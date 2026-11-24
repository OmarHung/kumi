using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Json;
using PmTool.Api.Security;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

internal sealed record ItemProgressData(int Total, int Done, int Canceled, decimal? EstimateTotal, decimal? EstimateDone)
{
    public static ItemProgressData From(ItemProgress progress) =>
        new(progress.Total, progress.Done, progress.Canceled, progress.EstimateTotal, progress.EstimateDone);
}

internal sealed record CycleData(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ItemProgressData Progress)
{
    public static CycleData From(Cycle cycle) =>
        new(cycle.Id, cycle.Name, cycle.StartDate, cycle.EndDate, ItemProgressData.From(cycle.Progress));
}

internal sealed record CycleRequest(string Name, DateOnly StartDate, DateOnly EndDate)
{
    public CycleDraft ToDraft() => new(Name, StartDate, EndDate);
}

internal sealed record MilestoneData(Guid Id, string Name, DateOnly DueDate, string? Description, ItemProgressData Progress)
{
    public static MilestoneData From(Milestone milestone) =>
        new(milestone.Id, milestone.Name, milestone.DueDate, milestone.Description, ItemProgressData.From(milestone.Progress));
}

/// <summary>沒給說明和給 null 一樣：沒有說明。</summary>
internal sealed record MilestoneRequest(string Name, DateOnly DueDate, string? Description = null)
{
    public MilestoneDraft ToDraft() => new(Name, DueDate, Description);
}

/// <summary>
/// 週期與里程碑。讀宣告 items:read，寫宣告 items:write：它們是項目規劃的一部分，沒有另外開範圍。
/// 「目前的週期」不在這裡判斷：伺服器不知道使用者的時區，網頁拿日期範圍自己算。
/// </summary>
internal static class PlanningEndpoints
{
    private const string Cycles = "/projects/{projectKey}/cycles";
    private const string Milestones = "/projects/{projectKey}/milestones";

    public static RouteGroupBuilder MapPlanningEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(Cycles, ListCyclesAsync).WithName("ListCycles").RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost(Cycles, CreateCycleAsync).WithName("CreateCycle").RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPut(Cycles + "/{cycleId:guid}", UpdateCycleAsync).WithName("UpdateCycle").RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapDelete(Cycles + "/{cycleId:guid}", DeleteCycleAsync).WithName("DeleteCycle").RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapGet(Milestones, ListMilestonesAsync).WithName("ListMilestones").RequireTokenScope(AccessTokenScope.ItemsRead);
        v1.MapPost(Milestones, CreateMilestoneAsync).WithName("CreateMilestone").RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapPut(Milestones + "/{milestoneId:guid}", UpdateMilestoneAsync)
            .WithName("UpdateMilestone")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        v1.MapDelete(Milestones + "/{milestoneId:guid}", DeleteMilestoneAsync)
            .WithName("DeleteMilestone")
            .RequireTokenScope(AccessTokenScope.ItemsWrite);
        return v1;
    }

    private static async Task<IResult> ListCyclesAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IPlanningService planning, CancellationToken cancellationToken)
    {
        var result = await planning.ListCyclesAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(result.Value.Select(CycleData.From).ToArray());
    }

    private static async Task<IResult> CreateCycleAsync(
        string projectKey,
        JsonBody<CycleRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await planning.CreateCycleAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                $"/v1/projects/{Uri.EscapeDataString(projectKey)}/cycles/{result.Value.Id}", CycleData.From(result.Value));
    }

    private static async Task<IResult> UpdateCycleAsync(
        string projectKey,
        Guid cycleId,
        JsonBody<CycleRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await planning.UpdateCycleAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, cycleId, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(CycleData.From(result.Value));
    }

    private static async Task<IResult> DeleteCycleAsync(
        string projectKey,
        Guid cycleId,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        var result = await planning.DeleteCycleAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cycleId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }

    private static async Task<IResult> ListMilestonesAsync(
        string projectKey, ClaimsPrincipal principal, [FromServices] IPlanningService planning, CancellationToken cancellationToken)
    {
        var result = await planning.ListMilestonesAsync(SessionPrincipal.Read(principal).ToActor(), projectKey, cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Ok(result.Value.Select(MilestoneData.From).ToArray());
    }

    private static async Task<IResult> CreateMilestoneAsync(
        string projectKey,
        JsonBody<MilestoneRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await planning.CreateMilestoneAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure
            ? EndpointResults.Fail(failure)
            : EndpointResults.Created(
                $"/v1/projects/{Uri.EscapeDataString(projectKey)}/milestones/{result.Value.Id}", MilestoneData.From(result.Value));
    }

    private static async Task<IResult> UpdateMilestoneAsync(
        string projectKey,
        Guid milestoneId,
        JsonBody<MilestoneRequest> body,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        if (!body.TryGetValue(out var request, out var bodyFailure))
        {
            return bodyFailure.ToResult();
        }

        var result = await planning.UpdateMilestoneAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, milestoneId, request.ToDraft(), cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(MilestoneData.From(result.Value));
    }

    private static async Task<IResult> DeleteMilestoneAsync(
        string projectKey,
        Guid milestoneId,
        ClaimsPrincipal principal,
        [FromServices] IPlanningService planning,
        CancellationToken cancellationToken)
    {
        var result = await planning.DeleteMilestoneAsync(
            SessionPrincipal.Read(principal).ToActor(), projectKey, milestoneId, cancellationToken);
        return result.Failure is { } failure ? EndpointResults.Fail(failure) : Results.NoContent();
    }
}
