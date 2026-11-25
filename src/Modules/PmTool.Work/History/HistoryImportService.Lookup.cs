using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

internal sealed partial class HistoryImportService
{
    public async Task<Result<IReadOnlyList<HistoryRef>>> LookupAsync(Actor actor, HistoryLookup lookup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(lookup);
        if (CheckLookup(lookup) is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 查詢只給匯入工具用：只回有 project.manage 的專案，和匯入的寫入一致；非組織管理員一個都沒有就是沒有權限。
        var readable = await ReadableProjects.ManageableIdsAsync(db, cache, actor, cancellationToken);
        if (readable.Count == 0 && !actor.IsOrganizationAdmin)
        {
            return WorkFailures.Forbidden("你沒有任何專案的管理權限，不能查詢匯入紀錄。");
        }

        var found = await HistoryRefs.FindAsync(db, actor.OrganizationId, lookup.Source, lookup.Kind, lookup.ExternalIds, cancellationToken);
        List<HistoryRef> visible =
        [
            .. lookup.ExternalIds.Distinct(StringComparer.Ordinal)
                .Where(id => found.TryGetValue(id, out var row) && readable.Contains(row.ProjectId))
                .Select(id => found[id])
                .Select(row => new HistoryRef(row.ExternalId, lookup.Kind, row.ProjectKey, row.ItemKey, row.TargetId)),
        ];
        return visible;
    }

    private static Failure? CheckLookup(HistoryLookup lookup)
    {
        if (HistoryRules.CheckSource(lookup.Source) is { } source)
        {
            return source;
        }

        if (!Enum.IsDefined(lookup.Kind))
        {
            return WorkFailures.Invalid(WorkErrorCodes.HistoryInvalid, "kind", "不認得的種類。");
        }

        if (lookup.ExternalIds is null || lookup.ExternalIds.Count > HistoryLookup.MaxIds)
        {
            return WorkFailures.Invalid(
                WorkErrorCodes.HistoryInvalid, "externalIds", $"一次最多查 {HistoryLookup.MaxIds} 個外部編號，這次有 {lookup.ExternalIds?.Count ?? 0} 個。");
        }

        for (var index = 0; index < lookup.ExternalIds.Count; index++)
        {
            if (HistoryRules.ExternalIdProblem(lookup.ExternalIds[index]) is { } problem)
            {
                return WorkFailures.Invalid(WorkErrorCodes.HistoryInvalid, $"externalIds/{index}", problem);
            }
        }

        return null;
    }
}
