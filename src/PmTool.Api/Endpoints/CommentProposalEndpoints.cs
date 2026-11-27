using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的留言建議：讀、套用、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限寫入。
/// 規格見 <c>internal/HANDOFF-ai-coverage.md</c> 第 5.7 節。
/// </summary>
internal static class CommentProposalEndpoints
{
    private const string ProposalRoute = "/ai/comment-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapCommentProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetCommentProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyCommentProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissCommentProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] ICommentProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：做到一半、使用者關掉頁面，已經寫入的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] ICommentProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] ICommentProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<CommentProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(CommentProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record CommentProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<CommentChangeData> Changes,
    ItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static CommentProposalData From(CommentProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Changes.Select(CommentChangeData.From)],
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record CommentChangeData(
    string ItemKey,
    Guid CommentId,
    CommentAction Action,
    string AuthorName,
    DateTimeOffset CommentedAt,
    string? Excerpt,
    string? Body,
    string? Error)
{
    public static CommentChangeData From(CommentChange change) => new(
        change.ItemKey, change.CommentId, change.Action, change.AuthorName, change.CommentedAt, change.Excerpt, change.Body, change.Error);
}
