using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的週期與里程碑建議：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 第 5.7 節。
/// </summary>
internal static class PlanningProposalEndpoints
{
    private const string ProposalRoute = "/ai/planning-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapPlanningProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetPlanningProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyPlanningProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissPlanningProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IPlanningProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：做到一半、使用者關掉頁面，已經寫入的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IPlanningProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IPlanningProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<PlanningProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(PlanningProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applying、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record PlanningProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<PlanningChangeData> Changes,
    string? Reason,
    NewItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static PlanningProposalData From(PlanningProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Changes.Select(PlanningChangeData.From)],
        proposal.Reason,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record PlanningChangeData(
    PlanningTarget Target,
    PlanningAction Action,
    Guid? Id,
    PlanningValuesData? Before,
    PlanningValuesData? After,
    int? ItemCount,
    string? Error)
{
    public static PlanningChangeData From(PlanningChange change) => new(
        change.Target, change.Action, change.Id, PlanningValuesData.From(change.Before), PlanningValuesData.From(change.After),
        change.ItemCount, change.Error);
}

/// <summary>日期是 <c>yyyy-MM-dd</c>。</summary>
internal sealed record PlanningValuesData(string Name, DateOnly? StartDate, DateOnly? EndDate, DateOnly? DueDate, string? Description)
{
    public static PlanningValuesData? From(PlanningValues? values) =>
        values is null ? null : new(values.Name, values.StartDate, values.EndDate, values.DueDate, values.Description);
}
