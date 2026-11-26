using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PmTool.Ai;
using PmTool.Api.Live;
using PmTool.Api.Security;

namespace PmTool.Api.Endpoints;

/// <summary>
/// AI 助理的新專案建議（不選專案的組織對話提出的）：讀、建立專案、作廢。只能用 Cookie：不宣告權杖範圍，個人存取權杖不能用。
/// 建立的人才看得到、能決定（AI 模組判斷）；「建立專案」是以登入的人自己的身分、照工作引擎的權限建立（只有組織管理員建得成）。
/// 規格見 <c>internal/HANDOFF-org-assistant.md</c> 第 4 節。
/// </summary>
internal static class ProjectProposalEndpoints
{
    private const string ProposalRoute = "/ai/project-proposals/{proposalId:guid}";

    public static RouteGroupBuilder MapProjectProposalEndpoints(this RouteGroupBuilder v1)
    {
        v1.MapGet(ProposalRoute, GetAsync).WithName("GetProjectProposal");
        v1.MapPost(ProposalRoute + "/apply", ApplyAsync).WithName("ApplyProjectProposal");
        v1.MapPost(ProposalRoute + "/dismiss", DismissAsync).WithName("DismissProjectProposal");
        return v1;
    }

    private static async Task<IResult> GetAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.GetAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    // 建立不跟著請求取消：專案建到一半、使用者關掉頁面，建議的狀態還是要存下來，不然同一份會被再建立一次。
    private static async Task<IResult> ApplyAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectProposalService proposals, [FromServices] ILiveNotifier live)
    {
        var result = await proposals.ApplyAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, CancellationToken.None);
        if (result.Failure is null)
        {
            // 和 POST /v1/projects 一樣：管理員看得到所有專案，所有人的連線都要重算。
            live.AccessChanged(null);
        }

        return Render(result);
    }

    private static async Task<IResult> DismissAsync(
        Guid proposalId, ClaimsPrincipal principal, [FromServices] IProjectProposalService proposals, CancellationToken cancellationToken) =>
        Render(await proposals.DismissAsync(SessionPrincipal.Read(principal).ToActor(), proposalId, cancellationToken));

    private static IResult Render(PmTool.Abstractions.Result<ProjectProposal> result) =>
        result.Failure is { } failure ? EndpointResults.Fail(failure) : EndpointResults.Ok(ProjectProposalData.From(result.Value));
}

/// <summary>建議的形狀。<c>status</c> 是 pending、applied、dismissed；沒有的值寫出 null。</summary>
internal sealed record ProjectProposalData(
    Guid Id,
    string Key,
    string Name,
    string Template,
    string TemplateName,
    bool IncludeExamples,
    string? Description,
    ProjectProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? CreatedProjectKey)
{
    public static ProjectProposalData From(ProjectProposal proposal) => new(
        proposal.Id,
        proposal.Key,
        proposal.Name,
        proposal.Template,
        proposal.TemplateName,
        proposal.IncludeExamples,
        proposal.Description,
        proposal.Status,
        proposal.CreatedAt,
        proposal.DecidedAt,
        proposal.CreatedProjectKey);
}
