using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Live;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的成員建議（加入、改角色、移出專案）：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-assign.md</c> 第 8.1 節。
/// </summary>
internal static class MemberProposalEndpoints
{
    private const string ProposalRoute = "/ai/member-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapMemberProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetMemberProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyMemberProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissMemberProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IMemberProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：寫到一半、使用者關掉頁面，已經改的與建議的狀態都要存下來。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IMemberProposalService proposals, [FromServices] ILiveNotifier live)
    {
        var result = await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None);
        if (result.Failure is null)
        {
            // 部分成功也算：已經改的人，連線的權限都要重算（和專案的成員端點一樣）。
            live.AccessChanged([.. result.Value.Changes.Select(change => change.PersonId).Distinct()]);
        }

        return Render(result);
    }

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IMemberProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<MemberProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(MemberProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record MemberProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<MemberChangeData> Changes,
    MemberProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static MemberProposalData From(MemberProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Changes.Select(MemberChangeData.From)],
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record MemberChangeData(
    Guid PersonId, string DisplayName, string? Role, string? RoleName, string? PreviousRole, string? PreviousRoleName)
{
    public static MemberChangeData From(MemberChange change) =>
        new(change.PersonId, change.DisplayName, change.Role, change.RoleName, change.PreviousRole, change.PreviousRoleName);
}
