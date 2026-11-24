using PmTool.Blueprint.Model;

namespace PmTool.Blueprint.Planning;

/// <summary>
/// 欄位從某個類型的欄位清單拿掉、欄位本身還在：這個類型的項目上的值要封存並拿掉，否則值留在項目上卻看不到、也清不掉。
/// 值是封存不是刪除，所以不需要變更集另外寫明，計畫直接列出來。
/// </summary>
internal static class FieldDetachRules
{
    public static MigrationOutcome Check(ProjectBlueprint? current, ChangeSetRequest changeSet) =>
        MigrationOutcome.Of(current is null ? [] : DetachedFields(current, changeSet.Blueprint));

    /// <summary>
    /// 兩版都有的類型，依新藍圖的類型順序、再依原本欄位清單的順序。欄位整個被刪除的不列：
    /// 那已經由欄位的刪除涵蓋，所有項目的值都會被封存，兩項都列的話同一個值會被封存兩次。
    /// </summary>
    private static IEnumerable<DataImpact> DetachedFields(ProjectBlueprint current, ProjectBlueprint next)
    {
        var currentPositions = KeyPositions.First(current.ItemTypes, type => type.Key);
        var nextPositions = KeyPositions.First(next.ItemTypes, type => type.Key);
        var definedFields = next.Fields.Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < next.ItemTypes.Count; index++)
        {
            var type = next.ItemTypes[index];
            if (nextPositions[type.Key] != index || !currentPositions.TryGetValue(type.Key, out var position))
            {
                continue;
            }

            var kept = type.Fields.ToHashSet(StringComparer.Ordinal);
            var detached = current.ItemTypes[position].Fields
                .Distinct(StringComparer.Ordinal)
                .Where(field => !kept.Contains(field) && definedFields.Contains(field));
            foreach (var field in detached)
            {
                yield return new FieldDetachImpact(type.Key, field);
            }
        }
    }
}
