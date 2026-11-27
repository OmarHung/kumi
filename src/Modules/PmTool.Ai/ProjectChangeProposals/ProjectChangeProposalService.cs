using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ProjectChangeProposals;

/// <summary>
/// 改專案建議的讀取、套用、作廢（<see cref="IProjectChangeProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.ProjectChangeProposalNotFound"/>。三個寫入都是冪等的，所以套用不需要「建立中」的狀態，
/// 整個套用都在建議的 advisory lock 裡：同時按兩次，第二次等到鎖時已經是已套用。
/// </summary>
internal sealed class ProjectChangeProposalService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IProjectDescriptionService descriptions,
    TimeProvider time,
    ILogger<ProjectChangeProposalService> logger) : IProjectChangeProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓套用停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ProjectChangeProposalNotFound, "找不到這份改專案的建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ProjectChangeProposalClosed, "這份改專案的建議已經套用或作廢。");

    private static readonly Failure Conflict =
        new(FailureKind.Conflict, AiErrorCodes.ProjectChangeConflict, "專案說明在建議提出之後被別人改過了，沒有套用任何東西；請重新讀一次專案說明再提出建議。");

    public async Task<Result<ProjectChangeProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return ProjectChangeMapper.ToProposal(row);
    }

    public async Task<Result<ProjectChangeProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ProjectChangeProposalRow.PendingStatus)
        {
            return Closed;
        }

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var report = await ApplyStepsAsync(actor, row, timeout.Token);
        if (report.First is { } failure)
        {
            return failure;
        }

        var error = report.Reasons.Count == 0 ? null : AiText.Cut(PartlyFailed(report.Reasons), AiRules.MaxErrorLength);
        var decidedAt = time.GetUtcNow();
        await db.ProjectChangeProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, ProjectChangeProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ProjectChangeMapper.ToProposal(row with { Status = ProjectChangeProposalRow.AppliedStatus, DecidedAt = decidedAt, Error = error });
    }

    public async Task<Result<ProjectChangeProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ProjectChangeProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.ProjectChangeProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ProjectChangeProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ProjectChangeMapper.ToProposal(row with { Status = ProjectChangeProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    // 順序：說明 → 名稱 → 封存。說明先比對版本：別人改過就整份不做。
    // 第一個步驟就失敗：什麼都沒改，回傳工作引擎的失敗；之後的失敗記下原因、繼續做下一步。
    private async Task<ApplyReport> ApplyStepsAsync(Actor actor, ProjectChangeProposalRow row, CancellationToken cancellationToken)
    {
        var before = ProjectChangeMapper.ReadBefore(row.Before);
        if (row.ChangesDescription && await HasDescriptionChangedAsync(actor, row.ProjectKey, before, cancellationToken) is { } check)
        {
            return new ApplyReport(check, []);
        }

        var steps = new List<(string Name, Func<CancellationToken, Task<Result<Unit>>> Run)>();
        if (row.ChangesDescription)
        {
            steps.Add(("改專案說明", async token => Drop(await descriptions.SetAsync(actor, row.ProjectKey, row.Description, token))));
        }

        if (row.Name is { } name)
        {
            steps.Add(("改名稱", async token => Drop(await projects.RenameAsync(actor, row.ProjectKey, name, token))));
        }

        if (row.Archived is { } archived)
        {
            steps.Add((archived ? "封存" : "取消封存", async token => archived
                ? Drop(await projects.ArchiveAsync(actor, row.ProjectKey, token))
                : Drop(await projects.UnarchiveAsync(actor, row.ProjectKey, token))));
        }

        return await RunStepsAsync(steps, cancellationToken);
    }

    private async Task<ApplyReport> RunStepsAsync(
        IReadOnlyList<(string Name, Func<CancellationToken, Task<Result<Unit>>> Run)> steps, CancellationToken cancellationToken)
    {
        var reasons = new List<(string Name, string Reason)>();
        var anyApplied = false;
        foreach (var (name, run) in steps)
        {
            var result = await RunOneAsync(run, anyApplied, cancellationToken);
            if (result.Failure is { } failure)
            {
                if (!anyApplied)
                {
                    return new ApplyReport(failure, reasons);
                }

                reasons.Add((name, failure.Message));
                continue;
            }

            anyApplied = true;
        }

        return new ApplyReport(null, reasons);
    }

    // 已經有步驟改過之後丟的例外不能讓整個請求變成 500、建議維持待決定：當作那一步失敗。
    private async Task<Result<Unit>> RunOneAsync(Func<CancellationToken, Task<Result<Unit>>> run, bool somethingDone, CancellationToken cancellationToken)
    {
        try
        {
            return await run(cancellationToken);
        }
        catch (Exception exception) when (somethingDone && exception is not OutOfMemoryException)
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用改專案的建議時有一步丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    private async Task<Failure?> HasDescriptionChangedAsync(
        Actor actor, string projectKey, ProjectSnapshot before, CancellationToken cancellationToken)
    {
        var current = await descriptions.GetAsync(actor, projectKey, cancellationToken);
        if (current.Failure is { } failure)
        {
            return failure;
        }

        return current.Value.UpdatedAt?.UtcTicks == before.DescriptionUpdatedAt?.UtcTicks ? null : Conflict;
    }

    private static Result<Unit> Drop<T>(Result<T> result) => result.Failure is { } failure ? failure : Unit.Value;

    private static string PartlyFailed(IReadOnlyList<(string Name, string Reason)> reasons) =>
        "有步驟沒有套用：" + string.Join("；", reasons.Select(r => $"{r.Name}（{r.Reason}）")) + "。其餘已經套用。";

    // 專案已經看不到：建議也當作不存在。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, ProjectChangeProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<ProjectChangeProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ProjectChangeProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_project_change_proposal:" + proposalId}, 0))", cancellationToken);

    private sealed record ApplyReport(Failure? First, IReadOnlyList<(string Name, string Reason)> Reasons);
}
