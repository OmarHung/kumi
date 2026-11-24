using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 試算表匯入：全部或全部不。先對專案取共用鎖再讀藍圖（和新增一樣，套用中的變更集會先做完），
/// 每一列照新增項目的規則檢查，全部合格才在同一個交易裡照順序建立；任何一列不合格就一個都不建立。
/// </summary>
internal sealed class ItemImportService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time) : IItemImportService
{
    public async Task<Result<ItemImportResult>> ImportAsync(
        Actor actor, string projectKey, ItemImport import, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(import);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ItemCreate))
        {
            return access.Missing(WorkPermissions.ItemCreate);
        }

        if (ItemImportRules.CheckRequest(import) is { } invalidRequest)
        {
            return invalidRequest;
        }

        if (!access.Can(WorkPermissions.ProjectManage) && ItemImportRules.NeedsManage(access, import))
        {
            return ItemImportRules.NeedsManageFailure();
        }

        var rows = new List<CheckedImportRow>(import.Rows.Count);
        var problems = new List<FailureDetail>();
        for (var index = 0; index < import.Rows.Count; index++)
        {
            var (row, rowProblems) = await ItemImportRules.CheckRowAsync(db, access, import.Rows[index], index, cancellationToken);
            problems.AddRange(rowProblems);
            if (row is not null)
            {
                rows.Add(row);
            }
        }

        if (problems.Count > 0)
        {
            return ItemImportRules.ToFailure(problems);
        }

        if (import.DryRun)
        {
            return new ItemImportResult(DryRun: true, rows.Count, []);
        }

        var keys = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            var item = await ItemCreation.InsertAsync(
                db, time, actor, access, row.ItemType, row.Changes, row.State, imported: true, cancellationToken);
            keys.Add(ItemKeys.Format(access.Project.Key, item.Number));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ItemImportResult(DryRun: false, keys.Count, keys);
    }
}
