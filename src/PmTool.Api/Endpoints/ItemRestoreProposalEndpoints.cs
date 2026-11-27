using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的復原項目建議：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 第 5.7 節。
/// </summary>
internal static class ItemRestoreProposalEndpoints
{
    private const string ProposalRoute = "/ai/item-restore-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapItemRestoreProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetItemRestoreProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyItemRestoreProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissItemRestoreProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemRestoreProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：做到一半、使用者關掉頁面，已經寫入的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemRestoreProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemRestoreProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ItemRestoreProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ItemRestoreProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null，<c>restoredKeys</c> 沒有是 <c>[]</c>。</summary>
internal sealed record ItemRestoreProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<ItemRestoreTargetData> Items,
    string? Reason,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<string> RestoredKeys,
    string? Error)
{
    public static ItemRestoreProposalData From(ItemRestoreProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Items.Select(item => new ItemRestoreTargetData(item.ItemKey, item.Title, item.DeletedAt))],
        proposal.Reason,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.RestoredKeys,
        proposal.Error);
}

internal sealed record ItemRestoreTargetData(string ItemKey, string Title, DateTimeOffset DeletedAt);
