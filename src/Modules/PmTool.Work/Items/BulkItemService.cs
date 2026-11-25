using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 批次操作：組合 <see cref="IWorkItemService"/> 的單一項目方法，每一筆各自走單一項目的規則（權限、流程、上下層），
/// 各自一個交易、留自己的活動與事件。這裡只負責請求的檢查、整批的權限、順序與重複，沒有另一套規則。
/// </summary>
internal sealed class BulkItemService(IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, IWorkItemService items) : IBulkItemService
{
    public async Task<Result<BulkUpdateResult>> UpdateAsync(
        Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(update);
        var invalid = BulkRequestRules.Check(update);
        if (invalid is not null)
        {
            return invalid;
        }

        var permission = update.State.HasValue ? WorkPermissions.ItemTransition : WorkPermissions.ItemUpdate;
        var batch = await PrepareAsync(actor, projectKey, update.Keys, permission, cancellationToken);
        if (batch.Failure is { } failure)
        {
            return failure;
        }

        var patch = BulkRequestRules.ToPatch(update);
        var results = batch.Value.InitialResults<WorkItem>();
        foreach (var key in batch.Value.Valid)
        {
            results[key] = update.State.HasValue
                ? await TransitionOrKeepAsync(actor, key, update.State.Value, cancellationToken)
                : await items.UpdateAsync(actor, key, patch, cancellationToken);
        }

        var (updated, failed) = batch.Value.Split(results);
        return new BulkUpdateResult(updated, failed);
    }

    /// <summary>
    /// 已經是目標狀態的算成功、不留活動：批次的「值沒變也算成功」（合約）。單一項目的改狀態照舊回「已經是…」，
    /// 那是使用者對一個項目的明確操作；批次是對一整群下指令，其中幾個本來就在那個狀態不是錯。
    /// 先照常轉換、被擋下才回頭看現在的狀態：不必為每一筆多讀一次，也不會因為先讀後轉而用到舊的狀態。
    /// </summary>
    private async Task<Result<WorkItem>> TransitionOrKeepAsync(
        Actor actor, string key, string state, CancellationToken cancellationToken)
    {
        var transitioned = await items.TransitionAsync(actor, key, state, cancellationToken);
        if (transitioned.Failure?.Code != WorkErrorCodes.TransitionNotAllowed)
        {
            return transitioned;
        }

        var current = await items.GetAsync(actor, key, cancellationToken);
        return current.Failure is null && current.Value.State == state ? current : transitioned;
    }

    public async Task<Result<BulkDeleteResult>> DeleteAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var invalid = BulkRequestRules.CheckKeys(itemKeys);
        if (invalid is not null)
        {
            return invalid;
        }

        var prepared = await PrepareAsync(actor, projectKey, itemKeys, WorkPermissions.ItemDelete, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var batch = prepared.Value;
        var alreadyDeleted = await DeletedAtAsync(actor, batch, cancellationToken);
        var parents = await ParentsAsync(actor, batch, cancellationToken);
        var results = await batch.RunAsync(
            key => alreadyDeleted.TryGetValue(key, out var at)
                ? Task.FromResult<Result<DeletedWorkItem>>(new DeletedWorkItem(key, at))
                : items.DeleteAsync(actor, key, cancellationToken),
            WaitsForChildren,
            batch.Sorted(parents, parentsFirst: false));
        var (deleted, failed) = batch.Split(results);
        return new BulkDeleteResult(deleted, failed);
    }

    public async Task<Result<BulkRestoreResult>> RestoreAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var invalid = BulkRequestRules.CheckKeys(itemKeys);
        if (invalid is not null)
        {
            return invalid;
        }

        var prepared = await PrepareAsync(actor, projectKey, itemKeys, WorkPermissions.ItemDelete, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var batch = prepared.Value;
        var parents = await ParentsAsync(actor, batch, cancellationToken);
        var results = await batch.RunAsync(
            key => items.RestoreAsync(actor, key, cancellationToken),
            (key, error, inBatch) => error.Code == WorkErrorCodes.ItemParentDeleted
                && parents.TryGetValue(key, out var parent) && inBatch.Contains(parent),
            batch.Sorted(parents, parentsFirst: true));
        var (restored, failed) = batch.Split(results);
        return new BulkRestoreResult(restored, failed);
    }

    // 還有下層沒刪、而且其中有（每個一筆 Details）在這一批裡還沒做完：先放著，等它們刪掉再回頭；
    // 回頭時失敗的原因只剩不在這一批的下層，訊息才不會列出已經刪掉的。
    private static bool WaitsForChildren(string key, Failure error, IReadOnlySet<string> inBatch) =>
        error.Code == WorkErrorCodes.ItemHasChildren
        && error.Details.Any(detail => detail.Path.StartsWith(ChildItems.PathPrefix, StringComparison.Ordinal)
            && inBatch.Contains(detail.Path[ChildItems.PathPrefix.Length..]));

    /// <summary>先確認專案看得到、有這件事要的權限，沒有就整批不做；通過了才整理編號。</summary>
    private async Task<Result<BulkBatch>> PrepareAsync(
        Actor actor, string projectKey, IReadOnlyList<string> keys, string permission, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        return access.Can(permission)
            ? BulkBatch.Of(access.Project, keys, access.Items)
            : access.Missing(permission);
    }

    /// <summary>這一批裡已經刪除的項目與它原本的刪除時刻：再刪一次算成功，時刻不變。</summary>
    private async Task<Dictionary<string, DateTimeOffset>> DeletedAtAsync(Actor actor, BulkBatch batch, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var numbers = batch.ValidNumbers;
        var rows = await batch.Scope.Apply(db.Items.AsNoTracking())
            .Where(i => i.OrganizationId == actor.OrganizationId && i.ProjectId == batch.ProjectId && numbers.Contains(i.Number) && i.DeletedAt != null)
            .Select(i => new { i.Number, DeletedAt = i.DeletedAt!.Value })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => ItemKeys.Format(batch.ProjectKey, row.Number), row => row.DeletedAt, StringComparer.Ordinal);
    }

    /// <summary>這一批裡每個項目的上層編號（有上層的）：用來排順序，上層也在這一批、還沒做完時就等它。</summary>
    private async Task<Dictionary<string, string>> ParentsAsync(Actor actor, BulkBatch batch, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var numbers = batch.ValidNumbers;
        var rows = await batch.Scope.Apply(db.Items.AsNoTracking())
            .Where(i => i.OrganizationId == actor.OrganizationId && i.ProjectId == batch.ProjectId && numbers.Contains(i.Number) && i.ParentId != null)
            .Select(i => new
            {
                i.Number,
                Parent = db.Items.Where(p => p.OrganizationId == i.OrganizationId && p.Id == i.ParentId).Select(p => p.Number).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            row => ItemKeys.Format(batch.ProjectKey, row.Number), row => ItemKeys.Format(batch.ProjectKey, row.Parent), StringComparer.Ordinal);
    }
}
