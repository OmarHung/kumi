using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的修改建議：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-items.md</c> 第 4.4 節。
/// </summary>
internal static class ItemProposalEndpoints
{
    private const string ProposalRoute = "/ai/item-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapItemProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetItemProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyItemProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissItemProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：項目改到一半、使用者關掉頁面，建議的狀態還是要存下來，不然同一份會被再套用一次。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IItemProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ItemProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ItemProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record ItemProposalData(
    Guid Id,
    string ProjectKey,
    string ItemKey,
    string ItemTitle,
    IReadOnlyList<ItemFieldChangeData> Changes,
    string? Comment,
    CommentReplyTargetData? ReplyTo,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static ItemProposalData From(ItemProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        proposal.ItemKey,
        proposal.ItemTitle,
        [.. proposal.Changes.Select(change => new ItemFieldChangeData(change.Field, change.Value))],
        proposal.Comment,
        CommentReplyTargetData.From(proposal.ReplyTo),
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record ItemFieldChangeData(string Field, JsonElement Value);

/// <summary>留言回覆的是哪一則（建立建議時的作者與摘錄）；新的一則是 null。修改建議與批次建議共用。</summary>
internal sealed record CommentReplyTargetData(Guid CommentId, string AuthorName, string Excerpt)
{
    public static CommentReplyTargetData? From(CommentReplyTarget? target) =>
        target is null ? null : new(target.CommentId, target.AuthorName, target.Excerpt);
}
