using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的專案建議：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 第 5.7 節。
/// </summary>
internal static class ProjectChangeProposalEndpoints
{
    private const string ProposalRoute = "/ai/project-change-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapProjectChangeProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetProjectChangeProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyProjectChangeProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissProjectChangeProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectChangeProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：做到一半、使用者關掉頁面，已經寫入的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectChangeProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectChangeProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ProjectChangeProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ProjectChangeProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。<c>description</c> 在 <c>changesDescription</c> 是 true 時才有意義（null 是清掉）。</summary>
internal sealed record ProjectChangeProposalData(
    Guid Id,
    string ProjectKey,
    string? Name,
    bool? Archived,
    bool ChangesDescription,
    string? Description,
    ProjectSnapshotData Before,
    string? Reason,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static ProjectChangeProposalData From(ProjectChangeProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        proposal.Name,
        proposal.Archived,
        proposal.ChangesDescription,
        proposal.Description,
        new ProjectSnapshotData(proposal.Before.Name, proposal.Before.Archived, proposal.Before.Description, proposal.Before.DescriptionUpdatedAt),
        proposal.Reason,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record ProjectSnapshotData(string Name, bool Archived, string? Description, DateTimeOffset? DescriptionUpdatedAt);
