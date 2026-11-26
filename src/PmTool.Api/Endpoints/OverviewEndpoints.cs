using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Api.Security;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

internal sealed record OverviewStateData(string Key, string Name, StateCategory Category, int Count)
{
    public static OverviewStateData From(OverviewStateCount state) => new(state.Key, state.Name, state.Category, state.Count);
}

internal sealed record OverviewCycleData(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ItemProgressData Progress)
{
    public static OverviewCycleData? From(OverviewCycle? cycle) =>
        cycle is null ? null : new(cycle.Id, cycle.Name, cycle.StartDate, cycle.EndDate, ItemProgressData.From(cycle.Progress));
}

internal sealed record OverviewMilestoneData(Guid Id, string Name, DateOnly DueDate, ItemProgressData Progress)
{
    public static OverviewMilestoneData? From(OverviewMilestone? milestone) =>
        milestone is null ? null : new(milestone.Id, milestone.Name, milestone.DueDate, ItemProgressData.From(milestone.Progress));
}

internal sealed record ProjectOverviewData(
    string ProjectKey,
    string Name,
    bool OwnItemsOnly,
    IReadOnlyList<OverviewStateData> States,
    int Open,
    int Overdue,
    int DueSoon,
    int Unassigned,
    int Stale,
    int DoneRecently,
    OverviewCycleData? CurrentCycle,
    OverviewMilestoneData? NextMilestone,
    DateTimeOffset? LastUpdatedAt)
{
    public static ProjectOverviewData From(ProjectOverview overview) => new(
        overview.ProjectKey,
        overview.Name,
        overview.OwnItemsOnly,
        [.. overview.States.Select(OverviewStateData.From)],
        overview.Open,
        overview.Overdue,
        overview.DueSoon,
        overview.Unassigned,
        overview.Stale,
        overview.DoneRecently,
        OverviewCycleData.From(overview.CurrentCycle),
        OverviewMilestoneData.From(overview.NextMilestone),
        overview.LastUpdatedAt);
}

/// <summary>專案總覽：只讀，範圍與數字由模組依呼叫者看得到的項目算，權杖不會多開放任何東西。</summary>
internal static class OverviewEndpoints
{
    private const string ManagedScope = "managed";
    private const string VisibleScope = "visible";

    public static RouteGroupBuilder MapOverviewEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet("/overview/projects", ListProjectsAsync)
            .WithName("ListProjectOverviews")
            .RequireTokenScope(AccessTokenScope.ItemsRead);
        return v1;
    }

    private static async Task<IResult> ListProjectsAsync(
        HttpRequest request, ClaimsPrincipal principal, [FromServices] IProjectOverviewService overview, CancellationToken cancellationToken)
    {
        var scope = QueryParameters.Single(request.Query, "scope").Then(ParseScope);
        if (QueryParameters.FailureOf(scope.Problem) is { } failure)
        {
            return EndpointResults.Fail(failure);
        }

        var projects = await overview.ListAsync(SessionPrincipal.Read(principal).ToActor(), scope.Value, cancellationToken);
        return EndpointResults.Ok(projects.Select(ProjectOverviewData.From).ToArray());
    }

    // 沒給是只看我管理的：總覽的預設畫面。
    private static Parsed<ProjectOverviewScope> ParseScope(string? value) => value switch
    {
        null or ManagedScope => new Parsed<ProjectOverviewScope>(ProjectOverviewScope.Managed),
        VisibleScope => new Parsed<ProjectOverviewScope>(ProjectOverviewScope.Visible),
        _ => Parsed<ProjectOverviewScope>.Invalid(ProjectOverviewScope.Managed, "scope", $"範圍要是 {ManagedScope} 或 {VisibleScope}。"),
    };
}
