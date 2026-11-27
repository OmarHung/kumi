using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemRevisions;

/// <summary>
/// 批次修改建議的讀取、套用、作廢（<see cref="IItemBatchProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.ItemBatchProposalNotFound"/>。整個套用都在建議的 advisory lock 裡：
/// 同時按兩次，第二次等到鎖時已經是已套用。每一筆的步驟和單一項目的建議同一份（<see cref="ItemProposalApplier"/>）。
/// </summary>
internal sealed class ItemBatchProposalService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    ItemProposalApplier applier,
    TimeProvider time,
    ILogger<ItemBatchProposalService> logger) : IItemBatchProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓套用停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(90);

    private const string TimedOutMessage = "逾時沒有套用";

    private static readonly Failure TimeoutFailure =
        new(FailureKind.Conflict, AiErrorCodes.ApplyTimedOut, "套用逾時，已經完成的不會重做，可以再按一次。");

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ItemBatchProposalNotFound, "找不到這份批次修改建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ItemBatchProposalClosed, "這份批次修改建議已經套用或作廢了。");

    public async Task<Result<ItemBatchProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return ItemBatchMapper.ToProposal(row);
    }

    public async Task<Result<ItemBatchProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ItemBatchProposalRow.PendingStatus)
        {
            return Closed;
        }

        using var timeout = new CancellationTokenSource(ApplyTimeout);
        var outcome = await ApplyEntriesAsync(actor, row, timeout.Token);
        if (outcome.Failure is { } failure)
        {
            return failure;
        }

        var (entries, summary) = (outcome.Value.Entries, outcome.Value.Summary);
        var itemsJson = ItemBatchMapper.Write(entries);
        var error = summary is null ? null : AiText.Cut(summary, AiRules.MaxErrorLength);
        var decidedAt = time.GetUtcNow();
        await db.ItemBatchProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, ItemBatchProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Items, itemsJson)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemBatchMapper.ToProposal(
            row with { Status = ItemBatchProposalRow.AppliedStatus, DecidedAt = decidedAt, Items = itemsJson, Error = error });
    }

    public async Task<Result<ItemBatchProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ItemBatchProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.ItemBatchProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ItemBatchProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemBatchMapper.ToProposal(row with { Status = ItemBatchProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    // 每一筆的失敗都是「什麼都沒改」（套用器只在第一個步驟失敗時回傳失敗）：全部都失敗才維持待決定、回傳第一筆的失敗；
    // 只要有一筆有進展就整份標成已套用，沒成功的各自記在那一筆。逾時時剩下的每一筆寫「逾時沒有套用」。
    private async Task<Result<BatchOutcome>> ApplyEntriesAsync(
        Actor actor, ItemBatchProposalRow row, CancellationToken cancellationToken)
    {
        var entries = ItemBatchMapper.Read(row.Items);
        var results = new List<ItemBatchMapper.StoredEntry>(entries.Count);
        var problems = new List<string>();
        Failure? firstFailure = null;
        var anySucceeded = false;
        var timedOut = false;
        foreach (var entry in entries)
        {
            if (timedOut)
            {
                results.Add(entry with { Error = TimedOutMessage });
                problems.Add($"{entry.ItemKey}（{TimedOutMessage}）");
                continue;
            }

            Result<string> outcome;
            try
            {
                outcome = await ApplyOneAsync(actor, entry, anySucceeded, cancellationToken);
            }
            // 這裡的 token 只有逾時會取消（不跟著請求），所以任何取消都當作逾時。
            catch (OperationCanceledException)
            {
                timedOut = true;
                firstFailure ??= TimeoutFailure;
                results.Add(entry with { Error = TimedOutMessage });
                problems.Add($"{entry.ItemKey}（{TimedOutMessage}）");
                continue;
            }

            if (outcome.Failure is { } failure)
            {
                firstFailure ??= failure;
                results.Add(entry with { Error = failure.Message });
                problems.Add($"{entry.ItemKey}（{failure.Message}）");
                continue;
            }

            anySucceeded = true;
            var partial = outcome.Value.Length == 0 ? null : outcome.Value;
            results.Add(entry with { Error = partial });
            if (partial is not null)
            {
                problems.Add($"{entry.ItemKey}（{partial}）");
            }
        }

        if (!anySucceeded && firstFailure is not null)
        {
            return firstFailure;
        }

        var summary = problems.Count == 0 ? null : "有幾筆沒有完全套用：" + string.Join("；", problems) + "。其餘已經套用。";
        return new BatchOutcome(results, summary);
    }

    // 前面已經有項目改過之後，這一筆丟的例外不能讓整個請求變成 500、建議維持待決定（再按一次會把前面的重做）：當作這一筆失敗。
    // 逾時的取消例外交給呼叫端處理。
    private async Task<Result<string>> ApplyOneAsync(
        Actor actor, ItemBatchMapper.StoredEntry entry, bool somethingDone, CancellationToken cancellationToken)
    {
        try
        {
            return await applier.ApplyAsync(
                actor, entry.ItemKey, ItemBatchMapper.ChangesOf(entry), entry.Comment, entry.ExpectedDescriptionRevision, cancellationToken);
        }
        catch (Exception exception) when (somethingDone && exception is not (OutOfMemoryException or OperationCanceledException))
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用批次修改建議時有一筆丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    // 專案已經看不到：建議也當作不存在，不再把項目的標題與修改內容給他看。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, ItemBatchProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<ItemBatchProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ItemBatchProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_item_batch_proposal:" + proposalId}, 0))", cancellationToken);

    private sealed record BatchOutcome(IReadOnlyList<ItemBatchMapper.StoredEntry> Entries, string? Summary);
}
