using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的刪除項目建議：讀、套用（刪除）、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限軟刪除。
/// 規格見 <c>internal/HANDOFF-ai-revise.md</c> 第 6 節。
/// </summary>
internal static class ItemDeletionProposalEndpoints
{
    private const string ProposalRoute = "/ai/item-deletion-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapItemDeletionProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetItemDeletionProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyItemDeletionProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissItemDeletionProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemDeletionProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：刪到一半、使用者關掉頁面，已經刪的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemDeletionProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemDeletionProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ItemDeletionProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ItemDeletionProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null，<c>deletedKeys</c> 沒有是 <c>[]</c>。</summary>
internal sealed record ItemDeletionProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<ItemDeletionTargetData> Items,
    string? Reason,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    IReadOnlyList<string> DeletedKeys,
    string? Error)
{
    public static ItemDeletionProposalData From(ItemDeletionProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Items.Select(item => new ItemDeletionTargetData(item.ItemKey, item.Title))],
        proposal.Reason,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.DeletedKeys,
        proposal.Error);
}

internal sealed record ItemDeletionTargetData(string ItemKey, string Title);
