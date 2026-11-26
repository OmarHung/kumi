using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.MemberProposals;

/// <summary>
/// 成員建議的讀取、套用、作廢（<see cref="IMemberProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.MemberProposalNotFound"/>。成員的寫入是冪等的，所以套用不需要「建立中」的狀態，
/// 整個套用都在建議的 advisory lock 裡：同時按兩次，第二次等到鎖時已經是已套用。
/// </summary>
internal sealed class MemberProposalService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    TimeProvider time,
    ILogger<MemberProposalService> logger) : IMemberProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓套用停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.MemberProposalNotFound, "找不到這份成員建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.MemberProposalClosed, "這份成員建議已經套用或作廢。");

    public async Task<Result<MemberProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return MemberProposalMapper.ToProposal(row);
    }

    public async Task<Result<MemberProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != MemberProposalRow.PendingStatus)
        {
            return Closed;
        }

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var report = await ApplyChangesAsync(actor, row, timeout.Token);
        if (report.First is { } failure)
        {
            return failure;
        }

        var error = report.Reasons.Count == 0 ? null : AiText.Cut(PartlyFailed(report.Reasons), AiRules.MaxErrorLength);
        var decidedAt = time.GetUtcNow();
        await db.MemberProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, MemberProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return MemberProposalMapper.ToProposal(row with { Status = MemberProposalRow.AppliedStatus, DecidedAt = decidedAt, Error = error });
    }

    public async Task<Result<MemberProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != MemberProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.MemberProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, MemberProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return MemberProposalMapper.ToProposal(row with { Status = MemberProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    // 第一筆就失敗：什麼都沒改，回傳工作引擎的失敗；之後的失敗記下原因、繼續做下一筆。
    private async Task<ApplyReport> ApplyChangesAsync(Actor actor, MemberProposalRow row, CancellationToken cancellationToken)
    {
        var changes = MemberProposalMapper.ReadChanges(row.Changes);
        var reasons = new List<(string Name, string Reason)>();
        var anyApplied = false;
        foreach (var change in changes)
        {
            var result = await ApplyOneAsync(actor, row.ProjectKey, change, anyApplied, cancellationToken);
            if (result.Failure is { } failure)
            {
                if (!anyApplied)
                {
                    return new ApplyReport(failure, reasons);
                }

                reasons.Add((change.DisplayName, failure.Message));
                continue;
            }

            anyApplied = true;
        }

        return new ApplyReport(null, reasons);
    }

    // 已經有成員改過之後丟的例外不能讓整個請求變成 500、建議維持待決定（再按一次會重複套用已經改過的）：當作那一筆失敗。
    private async Task<Result<ProjectMember>> ApplyOneAsync(
        Actor actor, string projectKey, MemberChange change, bool somethingDone, CancellationToken cancellationToken)
    {
        try
        {
            return change.Role is { } role
                ? await projects.SetMemberAsync(actor, projectKey, change.PersonId, role, cancellationToken)
                : await projects.RemoveMemberAsync(actor, projectKey, change.PersonId, cancellationToken);
        }
        catch (Exception exception) when (somethingDone && exception is not OutOfMemoryException)
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用成員建議時有一筆丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    private static string PartlyFailed(IReadOnlyList<(string Name, string Reason)> reasons) =>
        "有幾筆沒有套用：" + string.Join("；", reasons.Select(r => $"{MemberManagers.SafeName(r.Name)}（{r.Reason}）")) + "。其餘已經套用。";

    // 專案已經看不到：建議也當作不存在。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, MemberProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<MemberProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.MemberProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_member_proposal:" + proposalId}, 0))", cancellationToken);

    private sealed record ApplyReport(Failure? First, IReadOnlyList<(string Name, string Reason)> Reasons);
}
