using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的批次修改建議（一次改好幾個項目）：讀、套用（全部套用）、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限逐筆寫入。
/// 規格見 <c>internal/HANDOFF-ai-revise.md</c> 第 6 節。
/// </summary>
internal static class ItemBatchProposalEndpoints
{
    private const string ProposalRoute = "/ai/item-batch-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapItemBatchProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetItemBatchProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyItemBatchProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissItemBatchProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemBatchProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：改到一半、使用者關掉頁面，已經改的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemBatchProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemBatchProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ItemBatchProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ItemBatchProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record ItemBatchProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<ItemBatchEntryData> Items,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static ItemBatchProposalData From(ItemBatchProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Items.Select(ItemBatchEntryData.From)],
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record ItemBatchEntryData(
    string ItemKey, string ItemTitle, IReadOnlyList<ItemFieldChangeData> Changes, string? Comment, CommentReplyTargetData? ReplyTo, string? Error)
{
    public static ItemBatchEntryData From(ItemBatchEntry entry) => new(
        entry.ItemKey,
        entry.ItemTitle,
        [.. entry.Changes.Select(change => new ItemFieldChangeData(change.Field, change.Value))],
        entry.Comment,
        CommentReplyTargetData.From(entry.ReplyTo),
        entry.Error);
}
