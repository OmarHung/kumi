using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.CommentProposals;

/// <summary>
/// 留言建議的讀取、套用、作廢（<see cref="ICommentProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.CommentProposalNotFound"/>。編輯、刪除、復原本來就是冪等的，
/// 所以套用只在建議的 advisory lock 裡排隊，不需要「套用中」的狀態。
/// </summary>
internal sealed class CommentProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, IItemActivityService activity, TimeProvider time) : ICommentProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓寫入停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.CommentProposalNotFound, "找不到這份留言建議。");

    private static readonly Failure TimedOut =
        new(FailureKind.Conflict, AiErrorCodes.ApplyTimedOut, "套用逾時，已經完成的不會重做，可以再按一次。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.CommentProposalClosed, "這份留言建議已經套用或作廢了。");

    public async Task<Result<CommentProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return CommentProposalMapper.ToProposal(row);
    }

    public async Task<Result<CommentProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        if (row.Status != CommentProposalRow.PendingStatus)
        {
            return Closed;
        }

        var changes = CommentProposalMapper.ReadChanges(row.Changes);
        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var results = new List<CommentChange>(changes.Count);
        Failure? first = null;
        try
        {
            foreach (var change in changes)
            {
                var failure = await WriteAsync(actor, change, timeout.Token);
                first ??= failure;
                results.Add(change with { Error = failure is null ? null : AiText.Cut(failure.Message, AiRules.MaxErrorLength) });
            }
        }
        catch (OperationCanceledException)
        {
            // 這個 token 只有逾時會取消（不跟著請求）。三種寫入都冪等：維持待決定，再按一次只會重做還沒成功的。
            return TimedOut;
        }

        // 一筆都沒成功：什麼都沒改，維持待決定，回傳第一筆的失敗。
        if (results.All(r => r.Error is not null) && first is not null)
        {
            return first;
        }

        var failed = results.Count(r => r.Error is not null);
        var error = failed == 0 ? null : $"有 {failed} 筆沒成功，其餘已經寫入。";
        var written = CommentProposalMapper.WriteChanges(results);
        var decidedAt = time.GetUtcNow();
        await db.CommentProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, CommentProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Changes, written)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return CommentProposalMapper.ToProposal(
            row with { Status = CommentProposalRow.AppliedStatus, DecidedAt = decidedAt, Changes = written, Error = error });
    }

    public async Task<Result<CommentProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        if (row.Status != CommentProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.CommentProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, CommentProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return CommentProposalMapper.ToProposal(row with { Status = CommentProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    private async Task<Failure?> WriteAsync(Actor actor, CommentChange change, CancellationToken cancellationToken)
    {
        var result = change.Action switch
        {
            CommentAction.Edit => await activity.EditCommentAsync(actor, change.ItemKey, change.CommentId, change.Body ?? string.Empty, cancellationToken),
            CommentAction.Delete => await activity.DeleteCommentAsync(actor, change.ItemKey, change.CommentId, cancellationToken),
            _ => await activity.RestoreCommentAsync(actor, change.ItemKey, change.CommentId, cancellationToken),
        };
        return result.Failure;
    }

    // 專案已經看不到：建議也當作不存在，不再把留言的摘錄給他看。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, CommentProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<CommentProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.CommentProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_comment_proposal:" + proposalId}, 0))", cancellationToken);
}
