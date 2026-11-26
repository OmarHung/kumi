using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的新增項目建議：讀、套用（全部建立）、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能套用（AI 模組判斷）；套用是以登入的人自己的身分、照工作引擎的權限依序建立。
/// 規格見 <c>internal/HANDOFF-ai-new-items.md</c> 第 5.1 節。
/// </summary>
internal static class NewItemProposalEndpoints
{
    private const string ProposalRoute = "/ai/new-item-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapNewItemProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetNewItemProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyNewItemProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissNewItemProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] INewItemProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 套用不跟著請求取消：建立到一半、使用者關掉頁面，已經建好的與建議的狀態都要存下來（模組自己也不跟著取消，這裡是第二道）。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] INewItemProposalService proposals) =>
        Render(await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None));

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] INewItemProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<NewItemProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(NewItemProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applying、applied、dismissed；沒有的值寫出 null，<c>fields</c> 沒有是 <c>{}</c>。</summary>
internal sealed record NewItemProposalData(
    Guid Id,
    string ProjectKey,
    IReadOnlyList<NewItemDraftData> Items,
    IReadOnlyList<string?> CreatedKeys,
    NewItemProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Error)
{
    public static NewItemProposalData From(NewItemProposal proposal) => new(
        proposal.Id,
        proposal.ProjectKey,
        [.. proposal.Items.Select(NewItemDraftData.From)],
        proposal.CreatedKeys,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.Error);
}

internal sealed record NewItemDraftData(
    string? Ref,
    string Type,
    string Title,
    string? Description,
    Guid? AssigneeId,
    string? Priority,
    string? StartDate,
    string? DueDate,
    decimal? Estimate,
    string? ParentKey,
    string? ParentRef,
    IReadOnlyDictionary<string, JsonElement> Fields)
{
    public static NewItemDraftData From(NewItemDraft item) => new(
        item.Ref, item.Type, item.Title, item.Description, item.AssigneeId, item.Priority, item.StartDate, item.DueDate,
        item.Estimate, item.ParentKey, item.ParentRef, item.Fields);
}
