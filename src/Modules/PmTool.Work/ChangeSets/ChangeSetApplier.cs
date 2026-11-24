using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PmTool.Abstractions;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Access;
using PmTool.Work.Activities;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 核准並套用一份變更集，全部在一個交易裡：鎖住專案、重新計畫與檢查、依編號鎖住要動的項目、搬移、
/// 存新的藍圖版本、把其他等待核准的變更集標成過期。任何一步丟出例外，交易退回，什麼都沒改。
/// </summary>
internal sealed class ChangeSetApplier(
    IDbContextFactory<WorkDbContext> contexts,
    BlueprintCache cache,
    ChangeSetReader reader,
    IBlueprintValidator validator,
    ChangeSetLimits limits,
    TimeProvider time)
{
    public async Task<Result<ChangeSetDetail>> ApplyAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        try
        {
            return await ApplyInTransactionAsync(db, actor, projectKey, number, cancellationToken);
        }
        catch (Exception exception) when (PostgresErrors.IsLockTimeout(exception))
        {
            // 例外離開交易的範圍時交易已經退回，什麼都沒改。
            return ChangeSetFailures.Busy(number);
        }
    }

    private async Task<Result<ChangeSetDetail>> ApplyInTransactionAsync(
        WorkDbContext db, Actor actor, string projectKey, long number, CancellationToken cancellationToken)
    {
        // 專案那一列鎖到交易結束：進行中的項目寫入（它們先拿共用鎖）做完才輪到這裡，之後的寫入等這裡提交，
        // 再讀到新的藍圖。鎖住之後才讀專案與藍圖，讀到的版本就是要套用的對象。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LimitLockWaitAsync(db, cancellationToken);
        await ProjectLocks.ExclusiveAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.BlueprintApply))
        {
            return access.Missing(WorkPermissions.BlueprintApply);
        }

        if (!access.ReadsAllItems)
        {
            return ChangeSetFailures.NeedsAllItems();
        }

        var entity = await ChangeSetStore.LockAsync(db, access.Project, number, cancellationToken);
        if (entity is null)
        {
            return ChangeSetFailures.NotFound(number);
        }

        if (await RefuseAsync(db, transaction, access, entity, cancellationToken) is { } refused)
        {
            return refused;
        }

        var request = reader.Read(entity);
        var scope = ChangeSetReader.Scope(access.Project, access.Schema, request, Plan(access, entity, request));
        var evaluation = await reader.EvaluateAsync(db, scope, cancellationToken);
        if (evaluation.Blockers.Count > 0)
        {
            return ChangeSetFailures.ApplyBlocked(number, evaluation.Blockers);
        }

        var (version, schema) = await MigrateAsync(db, actor, entity, request, scope, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache.Remember(access.Project.Id, version, schema);
        var applied = (await ChangeSetStore.FindAsync(db, access.Project, number, cancellationToken))!;
        return await reader.DescribeAsync(db, access, applied, cancellationToken);
    }

    /// <summary>
    /// 等鎖最多等 <see cref="ChangeSetLimits.ApplyLockTimeout"/>。等 FOR UPDATE 的交易擋不住之後才來的共用鎖，
    /// 項目一直有人在改的話，套用會一直排不到，而它排著的時候專案也不能有新的寫入。
    /// 只在這個交易裡有效（set_config 的第三個參數），連線還回連線池之後不影響別的請求。
    /// </summary>
    private Task LimitLockWaitAsync(WorkDbContext db, CancellationToken cancellationToken) =>
        ProjectLocks.LimitWaitAsync(db, limits.ApplyLockTimeout, cancellationToken);

    /// <summary>不是等待核准，或基準版本已經過時。</summary>
    private async Task<Failure?> RefuseAsync(
        WorkDbContext db, IDbContextTransaction transaction, ProjectAccess access, ChangeSetEntity entity, CancellationToken cancellationToken)
    {
        var status = ChangeSetRecords.ParseStatus(entity.Status);
        if (status != ChangeSetStatus.Pending)
        {
            return ChangeSetFailures.NotPending(entity.Number, status);
        }

        var current = access.Project.CurrentBlueprintVersion;
        if (entity.BaseVersion == current)
        {
            return null;
        }

        // 過期是事實，要留下來，失敗也一樣。交易裡到這裡只有這一筆修改，直接提交再回失敗，
        // 不必另開連線；專案那一列還鎖著，標的當下不會有別的套用插進來。
        await ChangeSetStore.DecideAsync(db, entity, ChangeSetRecords.Expired, null, WorkClock.Now(time), null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ChangeSetFailures.Outdated(entity.Number, entity.BaseVersion, current);
    }

    /// <summary>基準版本就是目前的版本，計畫一定和送出時一樣；算出問題表示計畫的規則改了，不能照舊的結果搬資料。</summary>
    private BlueprintPlan Plan(ProjectAccess access, ChangeSetEntity entity, ChangeSetRequest request)
    {
        var plan = reader.Plan(access.Schema, access.Project.CurrentBlueprintVersion, request);
        return plan.Report.IsValid
            ? plan
            : throw new InvalidOperationException(
                $"變更集 {entity.Id} 對照第 {entity.BaseVersion} 版算不出計畫：{string.Join("；", plan.Report.Issues.Take(3).Select(i => i.Code))}");
    }

    private async Task<(long Version, ProjectSchema Schema)> MigrateAsync(
        WorkDbContext db, Actor actor, ChangeSetEntity entity, ChangeSetRequest request, MigrationScope scope, CancellationToken cancellationToken)
    {
        var items = await LockTouchedItemsAsync(db, scope, cancellationToken);

        // 時間在鎖住項目之後才讀，和一般的修改一樣：活動依時間分頁，時間的先後要和提交的先後一致。
        var now = WorkClock.Now(time);
        var version = scope.Project.CurrentBlueprintVersion + 1;
        var (document, schema) = PrepareBlueprint(request.Blueprint, entity);
        var migrations = items.Select(item => ItemMigrator.Migrate(item, scope, now)).ToArray();
        await WriteItemsAsync(db, scope.Project.OrganizationId, migrations, cancellationToken);
        db.ArchivedFieldValues.AddRange(migrations.SelectMany(m => m.Archived.Select(value => Archive(m.Before, value, entity.Id, now))));
        db.ItemActivities.AddRange(migrations.Where(m => m.IsRecorded).Select(m => ActivityRecords.Migrated(
            actor, m.Before, now, version, m.StateMoved ? m.Before.State : null, m.StateMoved ? m.After.State : null, m.Changes)));
        db.BlueprintVersions.Add(new BlueprintVersionEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = scope.Project.OrganizationId,
            ProjectId = scope.Project.Id,
            Version = version,
            Document = document,
            CreatedBy = actor.PersonId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await db.Projects
            .Where(p => p.OrganizationId == scope.Project.OrganizationId && p.Id == scope.Project.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.CurrentBlueprintVersion, version), cancellationToken);
        await ChangeSetStore.DecideAsync(db, entity, ChangeSetRecords.Applied, actor.PersonId, now, version, cancellationToken);
        await ChangeSetStore.ExpireOthersAsync(db, scope.Project, entity.Id, now, cancellationToken);
        return (version, schema);
    }

    /// <summary>
    /// 依編號順序鎖住要動的項目，和其他動到多個項目的操作用同一個順序，不會互相卡死。
    /// 專案那一列已經鎖住，項目的寫入（包含刪除）都進不來，先查出要動哪些、再鎖，中間不會有變化；
    /// 刪除的項目在查的時候就排除了，鎖的時候再排除一次，條件和查的時候一致。
    /// 只讀搬移用得到的欄位：最多幾千個項目，每個的說明都可能很長。
    /// </summary>
    private static async Task<List<MigratingItem>> LockTouchedItemsAsync(WorkDbContext db, MigrationScope scope, CancellationToken cancellationToken)
    {
        var ids = await scope.TouchedItems(db).OrderBy(i => i.Number).Select(i => i.Id).ToArrayAsync(cancellationToken);
        if (ids.Length == 0)
        {
            return [];
        }

        return await db.Database
            .SqlQuery<MigratingItem>($"""
                SELECT id, organization_id, project_id, type, state, state_category, fields, updated_at
                FROM work.items
                WHERE organization_id = {scope.Project.OrganizationId} AND project_id = {scope.Project.Id} AND id = ANY({ids})
                  AND deleted_at IS NULL
                ORDER BY number
                FOR NO KEY UPDATE
                """)
            .ToListAsync(cancellationToken);
    }

    /// <summary>一個陳述式寫完所有被動到的項目：一次最多幾千個，逐一更新要來回幾千次。</summary>
    private static async Task WriteItemsAsync(WorkDbContext db, Guid organizationId, IReadOnlyList<ItemMigration> migrations, CancellationToken cancellationToken)
    {
        if (migrations.Count == 0)
        {
            return;
        }

        var ids = migrations.Select(m => m.After.Id).ToArray();
        var states = migrations.Select(m => m.After.State).ToArray();
        var categories = migrations.Select(m => m.After.StateCategory).ToArray();
        var fields = migrations.Select(m => m.After.Fields).ToArray();
        var updatedAt = migrations.Select(m => m.After.UpdatedAt).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE work.items AS i
            SET state = u.state, state_category = u.state_category, fields = u.fields::jsonb, updated_at = u.updated_at
            FROM unnest({ids}, {states}, {categories}, {fields}, {updatedAt}) AS u(id, state, state_category, fields, updated_at)
            WHERE i.organization_id = {organizationId} AND i.id = u.id
            """,
            cancellationToken);
    }

    /// <summary>和開專案時一樣：寫成文件後照讀回來的方式再驗證一次，存進去的每一版之後都要讀得回來。</summary>
    private (string Document, ProjectSchema Schema) PrepareBlueprint(ProjectBlueprint blueprint, ChangeSetEntity entity)
    {
        var document = BlueprintSerializer.ToJson(blueprint);
        var readBack = validator.ValidateBlueprint(document, DocumentFormat.Json);
        return readBack.Blueprint is { } stored
            ? (document, ProjectSchema.From(stored))
            : throw new InvalidOperationException(
                $"變更集 {entity.Id} 的藍圖寫成文件後讀不回來：{string.Join("；", readBack.Report.Issues.Take(3).Select(i => $"{i.Path} {i.Code}"))}");
    }

    private ArchivedFieldValueEntity Archive(MigratingItem item, ArchivedValue value, Guid changeSetId, DateTimeOffset now) => new()
    {
        Id = WorkClock.NewId(time),
        OrganizationId = item.OrganizationId,
        ProjectId = item.ProjectId,
        ItemId = item.Id,
        Field = value.Field,
        Value = value.Value.GetRawText(),
        ChangeSetId = changeSetId,
        ArchivedAt = now,
    };
}
