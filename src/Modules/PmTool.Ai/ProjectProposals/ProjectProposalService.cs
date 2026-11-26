using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ProjectProposals;

/// <summary>
/// 新專案建議的讀取、建立專案、作廢（<see cref="IProjectProposalService"/>）。一定要是建立的人、同一個組織；別人的與不存在的都是
/// <see cref="AiErrorCodes.ProjectProposalNotFound"/>。建立與作廢在同一個 advisory lock 裡排隊：一份建議只會被決定一次。
/// </summary>
internal sealed class ProjectProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, TimeProvider time) : IProjectProposalService
{
    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ProjectProposalNotFound, "找不到這份新專案建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ProjectProposalClosed, "這份新專案建議已經建立或作廢了。");

    public async Task<Result<ProjectProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        return row is null ? NotFound : ProjectProposalMapper.ToProposal(row);
    }

    public async Task<Result<ProjectProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        if (row.Status != ProjectProposalRow.PendingStatus)
        {
            return Closed;
        }

        // 以套用的人的身分建立：不是管理員、代號被用掉都由工作引擎回失敗，建議維持待決定。
        var created = await projects.CreateAsync(
            actor, new NewProject(row.Key, row.Name, row.Template, row.IncludeExamples) { Description = row.Description }, cancellationToken);
        if (created.Failure is { } failure)
        {
            return failure;
        }

        return await DecideAsync(db, transaction, row, ProjectProposalRow.AppliedStatus, created.Value.Key);
    }

    public async Task<Result<ProjectProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        return row.Status != ProjectProposalRow.PendingStatus
            ? Closed
            : await DecideAsync(db, transaction, row, ProjectProposalRow.DismissedStatus, createdProjectKey: null);
    }

    private async Task<Result<ProjectProposal>> DecideAsync(
        AiDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        ProjectProposalRow row,
        string status,
        string? createdProjectKey)
    {
        var decided = row with { Status = status, DecidedAt = time.GetUtcNow(), CreatedProjectKey = createdProjectKey };

        // 專案已經建好了：存狀態不能被使用者關掉頁面而取消，不然同一份建議可以再按一次（會回 key_taken，但狀態是錯的）。
        await db.ProjectProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, decided.Status)
                .SetProperty(p => p.DecidedAt, decided.DecidedAt)
                .SetProperty(p => p.CreatedProjectKey, decided.CreatedProjectKey),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ProjectProposalMapper.ToProposal(decided);
    }

    private static Task<ProjectProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ProjectProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_project_proposal:" + proposalId}, 0))", cancellationToken);
}
