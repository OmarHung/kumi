using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <param name="Blockers">現在套用不了的原因，依資料影響的順序，最後是項目數的上限。</param>
internal sealed record ChangeSetEvaluation(IReadOnlyList<PlannedImpact> Impacts, IReadOnlyList<FailureDetail> Blockers);

/// <summary>
/// 依目前的資料算每一項影響的數量（定義見 <see cref="PlannedImpact.Affected"/>）與擋住套用的原因。
/// 預覽與套用用同一套：套用時在鎖住專案之後再算一次，算出來的就是真的要動的資料。
/// </summary>
internal static class ImpactEvaluator
{
    public static async Task<ChangeSetEvaluation> EvaluateAsync(
        WorkDbContext db, MigrationScope scope, ChangeSetLimits limits, CancellationToken cancellationToken)
    {
        // 上限先算：超過就一定擋下，不必再逐一看每個值轉不轉得過去；那一步要讀每個有值的項目，筆數沒有上限。
        var touched = await scope.TouchedItems(db).LongCountAsync(cancellationToken);
        var withinLimit = touched <= limits.MaxItemsPerApply;
        var checks = new List<ImpactCheck>(scope.Impacts.Count);
        for (var index = 0; index < scope.Impacts.Count; index++)
        {
            checks.Add(await CheckAsync(db, scope, index, withinLimit, cancellationToken));
        }

        FailureDetail[] limit = withinLimit ? [] : [ChangeSetFailures.TooManyItems(touched, limits.MaxItemsPerApply)];
        return new ChangeSetEvaluation(
            [.. checks.Select(check => check.Impact)],
            [.. checks.Select(check => check.Blocker).OfType<FailureDetail>(), .. limit]);
    }

    /// <summary>已經決定的變更集：資料已經不是當時的樣子，數量一律是 0，也沒有擋住的原因。</summary>
    public static IReadOnlyList<PlannedImpact> Unevaluated(IReadOnlyList<DataImpact> impacts) =>
        [.. impacts.Select(impact => new PlannedImpact(impact, 0, BlocksApply: false))];

    /// <param name="scanValues">要不要逐一檢查轉換失敗就擋下的值；超過上限時不檢查。</param>
    private static Task<ImpactCheck> CheckAsync(
        WorkDbContext db, MigrationScope scope, int index, bool scanValues, CancellationToken cancellationToken) =>
        scope.Impacts[index] switch
        {
            StateMoveImpact move => StateMoveAsync(db, scope, move, cancellationToken),
            FieldDropImpact drop => FieldDropAsync(db, scope, drop, cancellationToken),
            FieldConversionImpact conversion => ConversionAsync(db, scope, index, conversion, scanValues, cancellationToken),
            FieldDetachImpact detach => FieldDetachAsync(db, scope, detach, cancellationToken),
            ItemTypeRemovalImpact removal => ItemTypeRemovalAsync(db, scope, index, removal, cancellationToken),
            ParentRemovalImpact removal => ParentRemovalAsync(db, scope, index, removal, cancellationToken),
            RoleRemovalImpact removal => RoleRemovalAsync(db, scope, index, removal, cancellationToken),
            var other => throw new InvalidOperationException($"不認得的資料影響 {other.GetType().Name}。"),
        };

    private static async Task<ImpactCheck> StateMoveAsync(WorkDbContext db, MigrationScope scope, StateMoveImpact move, CancellationToken cancellationToken)
    {
        var types = scope.TypesUsing(move.Workflow);
        var count = await scope.ItemsOf(db).LongCountAsync(i => types.Contains(i.Type) && i.State == move.From, cancellationToken);
        return ImpactCheck.Passing(move, count);
    }

    private static async Task<ImpactCheck> FieldDropAsync(WorkDbContext db, MigrationScope scope, FieldDropImpact drop, CancellationToken cancellationToken) =>
        ImpactCheck.Passing(drop, await WithValue(db, scope, drop.Field).LongCountAsync(cancellationToken));

    private static async Task<ImpactCheck> FieldDetachAsync(
        WorkDbContext db, MigrationScope scope, FieldDetachImpact detach, CancellationToken cancellationToken) =>
        ImpactCheck.Passing(
            detach, await WithValue(db, scope, detach.Field).LongCountAsync(i => i.Type == detach.ItemType, cancellationToken));

    private static async Task<ImpactCheck> ConversionAsync(
        WorkDbContext db, MigrationScope scope, int index, FieldConversionImpact conversion, bool scanValues, CancellationToken cancellationToken)
    {
        var count = await ToConvert(db, scope, conversion.Field).LongCountAsync(cancellationToken);
        if (conversion.OnFailure != ConversionFailurePolicy.Abort || count == 0 || !scanValues)
        {
            return ImpactCheck.Passing(conversion, count);
        }

        var from = scope.Current.Fields[conversion.Field];
        var to = scope.Next.Fields[conversion.Field];
        string[] failed = [.. (await ValuesToConvertAsync(db, scope, conversion.Field, cancellationToken))
            .Where(row => ValueConversions.Convert(from, to, row.Value) is null)
            .Select(row => ItemKeys.Format(scope.Project.Key, row.Number))];
        return failed.Length == 0
            ? ImpactCheck.Passing(conversion, count)
            : ImpactCheck.Blocking(conversion, count, ChangeSetFailures.ConversionFailed(index, to, conversion.To, failed));
    }

    /// <summary>
    /// 要轉換的值，依項目編號。只讀這個欄位的值：自訂欄位的 jsonb 裡可能有好幾個長文字，整份讀進來只為了取一個鍵。
    /// 條件和 <see cref="ToConvert"/> 一樣（包含排除刪除的項目）；EF 沒辦法從存成文字的 jsonb 取出一個鍵，所以寫成 SQL。
    /// </summary>
    private static async Task<(long Number, JsonElement Value)[]> ValuesToConvertAsync(
        WorkDbContext db, MigrationScope scope, string field, CancellationToken cancellationToken)
    {
        var detached = scope.TypesDetaching(field);
        var rows = await db.Database
            .SqlQuery<StoredValue>($"""
                SELECT number, (fields -> {field})::text AS value
                FROM work.items
                WHERE organization_id = {scope.Project.OrganizationId} AND project_id = {scope.Project.Id} AND deleted_at IS NULL
                  AND jsonb_exists(fields, {field}) AND NOT (type = ANY({detached}))
                ORDER BY number
                """)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.Number, JsonElement.Parse(row.Value)))];
    }

    private static async Task<ImpactCheck> ItemTypeRemovalAsync(
        WorkDbContext db, MigrationScope scope, int index, ItemTypeRemovalImpact removal, CancellationToken cancellationToken)
    {
        var count = await scope.ItemsOf(db).LongCountAsync(i => i.Type == removal.ItemType, cancellationToken);
        return count == 0
            ? ImpactCheck.Passing(removal, 0)
            : ImpactCheck.Blocking(removal, count, ChangeSetFailures.ItemsOfRemovedType(index, TypeName(scope, removal.ItemType), count));
    }

    private static async Task<ImpactCheck> ParentRemovalAsync(
        WorkDbContext db, MigrationScope scope, int index, ParentRemovalImpact removal, CancellationToken cancellationToken)
    {
        var parents = scope.ItemsOf(db).Where(p => p.Type == removal.Parent).Select(p => (Guid?)p.Id);
        var count = await scope.ItemsOf(db).LongCountAsync(i => i.Type == removal.ItemType && parents.Contains(i.ParentId), cancellationToken);
        return count == 0
            ? ImpactCheck.Passing(removal, 0)
            : ImpactCheck.Blocking(
                removal,
                count,
                ChangeSetFailures.ItemsUnderRemovedParent(index, TypeName(scope, removal.ItemType), TypeName(scope, removal.Parent), count));
    }

    private static async Task<ImpactCheck> RoleRemovalAsync(
        WorkDbContext db, MigrationScope scope, int index, RoleRemovalImpact removal, CancellationToken cancellationToken)
    {
        var count = await db.ProjectMembers.LongCountAsync(
            m => m.OrganizationId == scope.Project.OrganizationId && m.ProjectId == scope.Project.Id && m.Role == removal.Role,
            cancellationToken);
        var name = scope.Current.Roles.GetValueOrDefault(removal.Role)?.Name ?? removal.Role;
        return count == 0
            ? ImpactCheck.Passing(removal, 0)
            : ImpactCheck.Blocking(removal, count, ChangeSetFailures.MembersWithRemovedRole(index, name, count));
    }

    private static IQueryable<ItemEntity> WithValue(WorkDbContext db, MigrationScope scope, string field) =>
        scope.ItemsOf(db).Where(i => EF.Functions.JsonExists(i.Fields, field));

    /// <summary>要轉換的值：不再有這個欄位的類型的項目不算，它們的值是原樣封存。</summary>
    private static IQueryable<ItemEntity> ToConvert(WorkDbContext db, MigrationScope scope, string field)
    {
        var detached = scope.TypesDetaching(field);
        return WithValue(db, scope, field).Where(i => !detached.Contains(i.Type));
    }

    /// <summary>被拿掉的類型只在目前的藍圖裡；還在的類型用目前的名字，和使用者現在看到的一樣。</summary>
    private static string TypeName(MigrationScope scope, string itemType) =>
        scope.Current.ItemTypes.GetValueOrDefault(itemType)?.Name ?? itemType;

    private sealed record StoredValue(long Number, string Value);

    private sealed record ImpactCheck(PlannedImpact Impact, FailureDetail? Blocker)
    {
        public static ImpactCheck Passing(DataImpact impact, long affected) => new(new PlannedImpact(impact, affected, BlocksApply: false), null);

        public static ImpactCheck Blocking(DataImpact impact, long affected, FailureDetail blocker) =>
            new(new PlannedImpact(impact, affected, BlocksApply: true), blocker);
    }
}
